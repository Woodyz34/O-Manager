using UnityEngine;

// Keeps a background sprite centered on the camera and scaled to cover the whole view.
[RequireComponent(typeof(SpriteRenderer))]
public class CameraBackdrop : MonoBehaviour
{
    public Camera targetCamera;
    public float depth = 20f;

    SpriteRenderer spriteRenderer;

    void LateUpdate()
    {
        Fit();
    }

    public void Fit()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null || !cam.orthographic)
        {
            return;
        }
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }
        if (spriteRenderer.sprite == null)
        {
            return;
        }

        float viewHeight = cam.orthographicSize * 2f;
        float viewWidth = viewHeight * cam.aspect;
        Vector2 spriteSize = spriteRenderer.sprite.bounds.size;
        float scale = Mathf.Max(viewWidth / spriteSize.x, viewHeight / spriteSize.y);

        transform.localScale = new Vector3(scale, scale, 1f);
        Vector3 camPos = cam.transform.position;
        transform.position = new Vector3(camPos.x, camPos.y, camPos.z + depth);
    }
}
