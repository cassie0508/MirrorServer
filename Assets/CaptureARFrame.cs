using UnityEngine;

public class CaptureARFrame : MonoBehaviour
{
    public Texture2D image; // 用于存储每一帧的合成图像
    public Camera arCamera;

    private RenderTexture renderTexture;

    void Start()
    {
        if (arCamera == null)
        {
            Debug.LogError("No Camera component found on this GameObject.");
            return;
        }

        // 初始化 RenderTexture 和 Texture2D
        InitializeRenderTexture();
    }

    void Update()
    {
        if (arCamera != null)
        {
            if (Screen.width != renderTexture.width || Screen.height != renderTexture.height)
            {
                // 如果屏幕大小变化，重新初始化 RenderTexture 和 Texture2D
                InitializeRenderTexture();
            }

            CaptureFrame();
        }
    }

    private void InitializeRenderTexture()
    {
        // 如果已有 RenderTexture，释放旧的
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }

        // 创建新的 RenderTexture
        renderTexture = new RenderTexture(Screen.width, Screen.height, 24);
        arCamera.targetTexture = renderTexture;

        // 初始化 Texture2D
        if (image != null)
        {
            Destroy(image);
        }
        image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
    }

    private void CaptureFrame()
    {
        // 将当前帧渲染到 RenderTexture
        RenderTexture.active = renderTexture;
        arCamera.targetTexture = renderTexture;
        arCamera.Render();

        // 将 RenderTexture 的数据读取到 Texture2D
        image.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        image.Apply();

        // 重置 RenderTexture 以允许相机渲染到屏幕
        arCamera.targetTexture = null;
        RenderTexture.active = null;
    }


    void OnDestroy()
    {
        // 释放资源
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }

        if (image != null)
        {
            Destroy(image);
        }
    }
}
