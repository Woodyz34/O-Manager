using System.Collections.Generic;
using UnityEngine;

public class SpriteAlignerAuto : MonoBehaviour
{
    [Header("Sprite som ska flyttas")]
    public SpriteRenderer spriteRenderer;
    private PathfindingManager PathManager;

    private bool align = false;
    public List<Transform> KontrollerPåGrid;


    private void Update()
    {
        if (align && Input.GetKeyDown(KeyCode.F))
        {
            Debug.Log("trycker ner f");
            if(KontrollerPåGrid.Count > 0)
            {
                Debug.Log("Gör sprite align");
                SpriteAligner();
            }
        }
    }
    public void DoSpriteAlign(PathfindingManager gridManager)
    {
        PathManager = gridManager;
        align = true;
    }
    public void SpriteAligner()
    {
        if (spriteRenderer == null || PathManager.controlPoints.Count == 0 || PathManager == null)
        {
            Debug.LogError("SpriteRenderer, controlPoints eller gridManager saknas!");
            return;
        }

        // Om det finns fler targetPoints än controlPoints, ta bara så många som finns
        if (KontrollerPåGrid.Count > PathManager.controlPoints.Count)
            KontrollerPåGrid = KontrollerPåGrid.GetRange(0, PathManager.controlPoints.Count);

        // 2. Justera sprite:en
        SnapSprite(PathManager.controlPoints, KontrollerPåGrid);
    }

    void SnapSprite(List<Transform> controls, List<Transform> targets)
    {
        int n = controls.Count;

        // 1. Centroid för kontroller (lokal space)
        Vector2 centroidLocal = GetCentroidLocal(controls);

        // 2. Centroid för targets (världspositioner)
        Vector2 centroidTarget = GetCentroid(targets);

        // 3. Flytta sprite till matchning
        Vector2 translation = centroidTarget - (Vector2)spriteRenderer.transform.TransformPoint(centroidLocal);
        spriteRenderer.transform.position += (Vector3)translation;

        // 4. Rotera & skala
        float angle, scale;
        ComputeOptimalRotationAndScale(controls, targets, out angle, out scale);

        spriteRenderer.transform.Rotate(Vector3.forward, angle);
        spriteRenderer.transform.localScale *= scale;
    }

    Vector2 GetCentroidLocal(List<Transform> points)
    {
        Vector2 sum = Vector2.zero;
        foreach (var t in points)
            sum += (Vector2)t.localPosition; // OBS! lokala positioner
        return sum / points.Count;
    }

    Vector2 GetCentroid(List<Transform> points)
    {
        Vector2 sum = Vector2.zero;
        foreach (var t in points)
            sum += (Vector2)t.position; // världskoordinater
        return sum / points.Count;
    }


    void ComputeOptimalRotationAndScale(List<Transform> currentPoints, List<Transform> targetPoints, out float angleDeg, out float scale)
    {
        int n = currentPoints.Count;

        Vector2[] X = new Vector2[n];
        Vector2[] Y = new Vector2[n];

        Vector2 centroidCurrent = Vector2.zero;
        Vector2 centroidTarget = GetCentroid(targetPoints);

        for (int i = 0; i < n; i++)
        {
            X[i] = currentPoints[i].position;
            centroidCurrent += X[i];
            Y[i] = targetPoints[i].position;
        }
        centroidCurrent /= n;

        for (int i = 0; i < n; i++)
        {
            X[i] -= centroidCurrent;
            Y[i] -= centroidTarget;
        }

        float numerator = 0f;
        float denominator = 0f;
        float sumX2 = 0f;
        float sumY2 = 0f;

        for (int i = 0; i < n; i++)
        {
            numerator += X[i].x * Y[i].y - X[i].y * Y[i].x;
            denominator += X[i].x * Y[i].x + X[i].y * Y[i].y;
            sumX2 += X[i].sqrMagnitude;
            sumY2 += Y[i].sqrMagnitude;
        }

        angleDeg = Mathf.Atan2(numerator, denominator) * Mathf.Rad2Deg;
        scale = Mathf.Sqrt(sumY2 / sumX2);
    }
}
