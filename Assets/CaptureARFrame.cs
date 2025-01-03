using UnityEngine;

public class CaptureARFrame : MonoBehaviour
{
    public Texture2D image; // ���ڴ洢ÿһ֡�ĺϳ�ͼ��
    public Camera arCamera;

    private RenderTexture renderTexture;

    void Start()
    {
        if (arCamera == null)
        {
            Debug.LogError("No Camera component found on this GameObject.");
            return;
        }

        // ��ʼ�� RenderTexture �� Texture2D
        InitializeRenderTexture();
    }

    void Update()
    {
        if (arCamera != null)
        {
            if (Screen.width != renderTexture.width || Screen.height != renderTexture.height)
            {
                // �����Ļ��С�仯�����³�ʼ�� RenderTexture �� Texture2D
                InitializeRenderTexture();
            }

            CaptureFrame();
        }
    }

    private void InitializeRenderTexture()
    {
        // ������� RenderTexture���ͷžɵ�
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }

        // �����µ� RenderTexture
        renderTexture = new RenderTexture(Screen.width, Screen.height, 24);
        arCamera.targetTexture = renderTexture;

        // ��ʼ�� Texture2D
        if (image != null)
        {
            Destroy(image);
        }
        image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
    }

    private void CaptureFrame()
    {
        // ����ǰ֡��Ⱦ�� RenderTexture
        RenderTexture.active = renderTexture;
        arCamera.targetTexture = renderTexture;
        arCamera.Render();

        // �� RenderTexture �����ݶ�ȡ�� Texture2D
        image.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        image.Apply();

        // ���� RenderTexture �����������Ⱦ����Ļ
        arCamera.targetTexture = null;
        RenderTexture.active = null;
    }


    void OnDestroy()
    {
        // �ͷ���Դ
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
