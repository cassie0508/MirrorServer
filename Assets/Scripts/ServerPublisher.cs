using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vuforia;

public class ServerPublisher : MonoBehaviour
{
    [Header("Network Settings")]
    [SerializeField] private string port = "55555";
    private PublisherSocket dataPubSocket;

    [Header("AR Camera Settings")]
    [SerializeField] private Camera arCamera;
    [SerializeField] private GameObject imageTarget;

    [Header("For Debugging")]
    [SerializeField] private Texture2D colorImage;
    private Texture2D resizedColorImage;

    private RenderTexture renderTexture;

    private float sizeBroadcastInterval = 3.0f;
    private float lastSizeBroadcastTime = -4f;

    void Start()
    {
        if (arCamera == null)
        {
            Debug.LogError("AR Camera is not set.");
            return;
        }

        InitializeSocket();

        InitializeTexture();

        // Wait until image target is tracked
        ObserverBehaviour targetObserver = imageTarget.GetComponent<ObserverBehaviour>();
        while (targetObserver.TargetStatus.Status == Status.TRACKED)
        {
            Debug.Log("Marker is tracked. Start to publish data.");
            break;
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

    private void InitializeTexture()
    {
        // TODO: Consider using _Camera.sensorSize.x instead of Screen
        // TODO: RGB24 or RGB32
        renderTexture = new RenderTexture(Screen.width, Screen.height, 24);
        arCamera.targetTexture = renderTexture;

        colorImage = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        resizedColorImage = new Texture2D(Screen.width / 2, Screen.height / 2, TextureFormat.RGB24, false);
    }

    void Update()
    {
        // Publish size data every 5 seconds
        if (Time.time - lastSizeBroadcastTime > sizeBroadcastInterval && Time.time < 20)
        {
            int[] sizeArray = new int[2] { resizedColorImage.width, resizedColorImage.height };
            byte[] sizeData = new byte[sizeArray.Length * sizeof(int)];
            Buffer.BlockCopy(sizeArray, 0, sizeData, 0, sizeData.Length);
            PublishData("Size", sizeData);

            lastSizeBroadcastTime = Time.time;
            Debug.Log("Periodic size broadcast.");
        } 

        // Render current frame on RenderTexture
        RenderTexture.active = renderTexture;
        arCamera.targetTexture = renderTexture;
        arCamera.Render();

        // Read RenderTexture data to colorImage
        colorImage.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        colorImage.Apply();

        // Publish colorImage data for every frame
        DownsampleTexture(ref colorImage, ref resizedColorImage, resizedColorImage.width, resizedColorImage.height);
        byte[] resizedColorData = resizedColorImage.GetRawTextureData();
        PublishData("Frame", resizedColorData);

        // Reset RenderTexture
        arCamera.targetTexture = null;
        RenderTexture.active = null;
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
                // TODO: Can be removed after test
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
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to publish data: {ex.Message}");
            }
        }
    }

    void OnDestroy()
    {
        Debug.Log("Closing socket on port " + port);
        dataPubSocket.Dispose();
        NetMQConfig.Cleanup(false);
        dataPubSocket = null;

        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }

        if (colorImage != null)
        {
            Destroy(colorImage);
        }
    }
}
