using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraMovement : MonoBehaviour
{

    [SerializeField]
    private Camera cam;

    [SerializeField]
    private float zoomStep, minCamSize, maxCamSize;

    [SerializeField]
    private SpriteRenderer mapRenderer;
    private float mapMinX, mapMaxX, mapMinY, mapMaxY;

    private Vector3 dragOrigin;


    public Transform target;
    Vector3 velocity = Vector3.zero;

    [Range(0, 1)]
    public float smoothTime;

    public Vector3 positionOffset;




    public bool cameraFollow = false;





    public void CameraFollow()
    {
        if (cameraFollow == true)
        {
            cameraFollow = false;
        }
        else if (cameraFollow == false)
        {
            cameraFollow = true;
        }
    }



    private void Awake()
    {
        mapMinX = mapRenderer.transform.position.x - mapRenderer.bounds.size.x;
        mapMaxX = mapRenderer.transform.position.x + mapRenderer.bounds.size.x;

        mapMinY = mapRenderer.transform.position.y - mapRenderer.bounds.size.y;
        mapMaxY = mapRenderer.transform.position.y + mapRenderer.bounds.size.y;
    }


    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        PanCamera();
        if (Input.GetAxis("Mouse ScrollWheel") > 0f) { ZoomIn(); }
        if (Input.GetAxis("Mouse ScrollWheel") < 0f) { ZoomOut(); }

        if (cameraFollow == true)
        {
            Vector3 targetPosition = target.position + positionOffset;
            targetPosition = new Vector3(Mathf.Clamp(targetPosition.x, mapMinX, mapMaxX), Mathf.Clamp(targetPosition.y, mapMinY, mapMaxY), -10);
            transform.position = ClampCamera(Vector3.SmoothDamp(transform.position, targetPosition, ref velocity, smoothTime));
        }
    }


    private void PanCamera()
    {

        if (Input.GetMouseButtonDown(0))
            dragOrigin = cam.ScreenToWorldPoint(Input.mousePosition);

        if (Input.GetMouseButton(0))
        {
            Vector3 difference = dragOrigin - cam.ScreenToWorldPoint(Input.mousePosition);



            cam.transform.position = ClampCamera(cam.transform.position + difference);

        }


    }

    private void ZoomIn()
    {
        float newSize = cam.orthographicSize - zoomStep;

        cam.orthographicSize = Mathf.Clamp(newSize, minCamSize, Mathf.Min(maxCamSize, (mapRenderer.bounds.size.x / 2f) / cam.aspect));
        cam.transform.position = ClampCamera(cam.transform.position);
    }

    private void ZoomOut()
    {
        float newSize = cam.orthographicSize + zoomStep;
        cam.orthographicSize = Mathf.Clamp(newSize, minCamSize, Mathf.Min(maxCamSize, (mapRenderer.bounds.size.x / 2f) / cam.aspect));
        cam.transform.position = ClampCamera(cam.transform.position);
    }

    private Vector3 ClampCamera(Vector3 targetPosition)
    {
        float camHeight = cam.orthographicSize;
        float camWidth = cam.orthographicSize * cam.aspect;

        float minX = mapMinX + camWidth;
        float maxX = mapMaxX - camWidth;
        float minY = mapMinY + camHeight;
        float maxY = mapMaxY - camHeight;

        float newX = Mathf.Clamp(targetPosition.x, minX, maxX);
        float newY = Mathf.Clamp(targetPosition.y, minY, maxY);

        return new Vector3(newX, newY, targetPosition.z);
    }
}