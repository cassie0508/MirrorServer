using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using Microsoft.Azure.Kinect.Sensor;
using NetMQ;
using NetMQ.Sockets;
using System.Linq;
using UnityEngine.Playables;
using PubSub;
using UnityEngine.UI;
using Vuforia;

public class OldPublisher : MonoBehaviour
{
    [Header("Network Settings")]
    [SerializeField] private string port = "55555";
    private PublisherSocket dataPubSocket;

    [Header("For Debugging")]
    [SerializeField] private Texture2D ColorImage;
    private Texture2D resizedColorImage;

    [Header("Virtual Overlay Settings")]
    [SerializeField] private RenderTexture virtualOverlay; // Virtual guidance overlay
    private Material mergeMaterial;

    [Header("AR Camera Settings")]
    [SerializeField] private GameObject arCamera; // Reference to the Vuforia AR Camera
    [SerializeField] private GameObject imageTarget; // Reference to the Image Target
    
    [Header("Mode Selection")]
    [SerializeField] private Mode selectedMode;
    private enum Mode { Calibration, Play }

    private Matrix4x4 kinectToImageTargetMatrix;

    private Device _Device;

    private void Start()
    {
        InitializeSocket();

        // Initialize merge material
        mergeMaterial = new Material(Shader.Find("Shaders/ViewMerge"));

        if (selectedMode == Mode.Calibration)
        {
            StartCoroutine(DetectImageTargetAndSwitch());
        }
        else if (selectedMode == Mode.Play)
        {
            StartCoroutine(CameraCaptureMirror());
        }
    }

    private void InitializeSocket()
    {
        try
        {
            AsyncIO.ForceDotNet.Force();
            dataPubSocket = new PublisherSocket();
            dataPubSocket.Options.SendHighWatermark = 10;

            dataPubSocket.Bind($"tcp://*:{port}");
            Debug.Log("Successfully bound socket port " + port);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to bind socket: {ex.Message}");
        }
    }

    private IEnumerator DetectImageTargetAndSwitch()
    {
        // Wait until the Image Target is detected
        ObserverBehaviour targetObserver = imageTarget.GetComponent<ObserverBehaviour>();
        if (targetObserver == null)
        {
            Debug.LogError("Image Target does not have ObserverBehaviour attached.");
            yield break;
        }

        // Wait for Image Target to be tracked
        while (targetObserver.TargetStatus.Status != Status.TRACKED)
        {
            yield return null;
        }

        Debug.Log("Image Target detected. Calculating matrix...");
        // Retrieve the relative matrix of Kinect to Image Target
        kinectToImageTargetMatrix = GetKinectToImageTargetMatrix(targetObserver);


        byte[] matrixData = Matrix4x4ToByteArray(kinectToImageTargetMatrix);
        PublishData("Calibration", matrixData);

        // Start the original logic
        StartCoroutine(CameraCaptureMirror());
    }

    private Matrix4x4 GetKinectToImageTargetMatrix(ObserverBehaviour targetObserver)
    {
        // Retrieve the transformation matrix from AR Camera
        Transform arTransform = arCamera.transform;
        Matrix4x4 arCameraMatrix = Matrix4x4.TRS(
            arTransform.position,
            arTransform.rotation,
            Vector3.one
        );

        // Retrieve the transformation matrix from Image Target
        Transform targetTransform = targetObserver.transform;
        Matrix4x4 targetMatrix = Matrix4x4.TRS(
            targetTransform.position,
            targetTransform.rotation,
            Vector3.one
        );

        // Calculate the relative matrix (Kinect to Image Target)
        Matrix4x4 kinectToImageTargetMatrix = targetMatrix * arCameraMatrix.inverse;

        return kinectToImageTargetMatrix;
    }


    private byte[] Matrix4x4ToByteArray(Matrix4x4 matrix)
    {
        float[] matrixFloats = new float[16];
        for (int i = 0; i < 16; i++)
        {
            matrixFloats[i] = matrix[i];
        }

        byte[] byteArray = new byte[matrixFloats.Length * sizeof(float)];
        Buffer.BlockCopy(matrixFloats, 0, byteArray, 0, byteArray.Length);
        return byteArray;
    }

    private IEnumerator CameraCaptureMirror()
    {
        // Disable AR Camera
        DisableARCamera();

        Debug.Log("Start to send kinect data");
        if (Device.GetInstalledCount() == 0)
        {
            Debug.LogError("No Kinect Device Found");
            yield break;
        }

        try
        {
            _Device = Device.Open();
        }
        catch (AzureKinectOpenDeviceException ex)
        {
            Debug.LogError($"Failed to open Azure Kinect device: {ex.Message}");
            yield break;
        }

        var configuration = new DeviceConfiguration
        {
            ColorFormat = ImageFormat.ColorBGRA32,
            ColorResolution = ColorResolution.R1080p,
            DepthMode = DepthMode.NFOV_2x2Binned,
            SynchronizedImagesOnly = true,
            CameraFPS = FPS.FPS30
        };

        try
        {
            _Device.StartCameras(configuration);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to start camera: {ex.Message}");
            yield break;
        }

        // For debugging: Set up textures
        if (!SetupTextures(ref ColorImage, ref resizedColorImage))
        {
            Debug.LogError("CameraCapture(): Something went wrong while setting up camera textures");
            yield break;
        }


        int[] sizeArray = new int[2] { resizedColorImage.width, resizedColorImage.height };
        byte[] sizeData = new byte[sizeArray.Length * sizeof(int)];
        Buffer.BlockCopy(sizeArray, 0, sizeData, 0, sizeData.Length);
        PublishData("Size", sizeData);

        /* Publish Frame Data */
        while (true)
        {
            using (var capture = _Device.GetCapture())
            {
                byte[] colorData = capture.Color.Memory.ToArray();

                ColorImage.LoadRawTextureData(colorData);
                ColorImage.Apply();

                RenderTexture rt = new RenderTexture(ColorImage.width, ColorImage.height, 24);
                Graphics.Blit(ColorImage, rt, mergeMaterial);

                mergeMaterial.mainTexture = virtualOverlay; // Virtual guidance overlay
                mergeMaterial.SetTexture("_RealContentTex", ColorImage); // Real content

                Graphics.Blit(rt, rt, mergeMaterial);

                Texture2D mergedTexture = new Texture2D(rt.width, rt.height, TextureFormat.BGRA32, false);
                RenderTexture.active = rt;
                mergedTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                mergedTexture.Apply();

                DownsampleTexture(ref mergedTexture, ref resizedColorImage, resizedColorImage.width, resizedColorImage.height);
                byte[] resizedColorData = resizedColorImage.GetRawTextureData();

                PublishData("Frame", resizedColorData);
            }


            //yield return new WaitForSeconds(0.2f); //5 frames per second
            yield return null;
        }
    }

    private void DisableARCamera()
    {
        Debug.Log("Disabling AR Camera...");
        if (arCamera != null) arCamera.SetActive(false);
    }

    private bool SetupTextures(ref Texture2D Color, ref Texture2D resizedColor)
    {
        try
        {
            using (var capture = _Device.GetCapture())
            {
                if (Color == null)
                    Color = new Texture2D(capture.Color.WidthPixels, capture.Color.HeightPixels, TextureFormat.BGRA32, false);
                if (resizedColor == null)
                    resizedColor = new Texture2D(capture.Color.WidthPixels / 4, capture.Color.HeightPixels / 4, TextureFormat.BGRA32, false);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"An error occurred " + ex.Message);
            return false;
        }
        return true;
    }

    public void DownsampleTexture(ref Texture2D originalTexture, ref Texture2D resizedTexture, int targetWidth, int targetHeight)
    {
        RenderTexture rt = new RenderTexture(targetWidth, targetHeight, 24);
        RenderTexture.active = rt;

        // Copy original texture to the render texture
        Graphics.Blit(originalTexture, rt);

        // Read pixels from the render texture into the new Texture2D
        resizedTexture.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        resizedTexture.Apply();

        // Clean up
        RenderTexture.active = null;
        rt.Release();
    }


    private void PublishData(string topic, byte[] data)
    {
        if (dataPubSocket != null)
        {
            try
            {
                if (topic == "Frame")
                {
                    long timestamp = DateTime.UtcNow.Ticks;
                    byte[] timestampBytes = BitConverter.GetBytes(timestamp);

                    byte[] message = new byte[timestampBytes.Length + data.Length];
                    Buffer.BlockCopy(timestampBytes, 0, message, 0, timestampBytes.Length);
                    Buffer.BlockCopy(data, 0, message, timestampBytes.Length, data.Length);
                    dataPubSocket.SendMoreFrame(topic).SendFrame(message);
                }
                else
                {
                    dataPubSocket.SendMoreFrame(topic).SendFrame(data);
                }
            }
            catch (NetMQ.TerminatingException)
            {
                Debug.LogWarning("Context was terminated. Reinitializing socket.");
                InitializeSocket();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to publish data: {ex.Message}");
            }
        }
    }

    private void OnDestroy()
    {
        Debug.Log("Closing socket on port " + port);
        dataPubSocket.Dispose();
        NetMQConfig.Cleanup(false);
        dataPubSocket = null;


        StopAllCoroutines();
        Task.WaitAny(Task.Delay(1000));

        if (_Device != null)
        {
            _Device.StopCameras();
            _Device.Dispose();
        }
    }
}