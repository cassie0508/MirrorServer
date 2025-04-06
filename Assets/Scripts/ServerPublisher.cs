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

public class ServerPublisher : MonoBehaviour
{
    [Header("Network Settings")]
    [SerializeField] private string port = "55555";
    private PublisherSocket dataPubSocket;


    [Header("For Debugging")]
    [SerializeField] private Texture2D ColorImage;
    [SerializeField] private Texture2D resizedColorImage;

    private Device _Device;

    private void Start()
    {
        InitializeSocket();
        StartCoroutine(CameraCaptureMirror());
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

    private IEnumerator CameraCaptureMirror()
    {
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

        _Device.StartCameras(configuration);

        // For debugging: Set up textures
        if (!SetupTextures(ref ColorImage, ref resizedColorImage))
        {
            Debug.LogError("CameraCapture(): Something went wrong while setting up camera textures");
            yield break;
        }

        int[] sizeArray = new int[2] { resizedColorImage.width, resizedColorImage.height };
        Debug.Log($"Send size width {resizedColorImage.width} and {resizedColorImage.height}");
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

                DownsampleTexture(ref ColorImage, ref resizedColorImage, resizedColorImage.width, resizedColorImage.height);
                byte[] resizedColorData = resizedColorImage.GetRawTextureData();

                PublishData("Frame", resizedColorData);
            }

            //yield return new WaitForSeconds(0.2f); //5 frames per second
            yield return null;
        }
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
        //Graphics.Blit(originalTexture, rt,new Vector2(1, -1), Vector2.zero);
        Graphics.Blit(originalTexture, rt, new Vector2(0.5f, -0.5f), new Vector2(0.25f, 0.75f));

        // Read pixels from the render texture into the new Texture2D
        //resizedTexture.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
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
                Debug.Log($"Sending topic and data length: {topic}, {data.Length}");
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