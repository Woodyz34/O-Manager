using Fusion;
using System;
using System.Collections.Generic;
using UnityEngine;

public class BodyHandler : MonoBehaviour
{
    public SpriteRenderer cirkel;       // Dra in din cirkels MeshRenderer här
    public TrailRenderer svans;    // Dra in din LineRenderer här
    public LineRenderer draw;

    private List<Material> cirkelMaterials = new List<Material>();
    private List<Material> svansMaterials = new List<Material>();
    private List<Material> drawMaterials = new List<Material>();

    private int currentIndex = 0;

    private void Awake()
    {
        // Läs in alla material automatiskt
        cirkelMaterials.AddRange(Resources.LoadAll<Material>("Materials/CirkelMaterial"));
        svansMaterials.AddRange(Resources.LoadAll<Material>("Materials/SvansMaterial"));
        drawMaterials.AddRange(Resources.LoadAll<Material>("Materials/DrawMaterial"));

        Debug.Log($"Laddade {cirkelMaterials.Count} cirkel-material och {svansMaterials.Count} svans-material.");
    }

    private void Start()
    {
        ApplyAppearance(currentIndex);
    }

    public void ApplyAppearance(int index)
    {
        if (cirkelMaterials.Count == 0 || svansMaterials.Count == 0)
        {
            Debug.LogWarning("Inga material hittades i Resources!");
            return;
        }

        index = Mathf.Clamp(index, 0, Mathf.Min(cirkelMaterials.Count, svansMaterials.Count) - 1);

        cirkel.material = cirkelMaterials[index];
        svans.material = svansMaterials[index];
        draw.material = drawMaterials[index];

        currentIndex = index;
    }

    // The player's own colour on the dot, the trail and the drawn line
    public void ApplyColor(Color color)
    {
        if (cirkelMaterials.Count == 0) return;
        cirkel.material = new Material(cirkelMaterials[0]) { color = color };
        svans.material = new Material(svansMaterials[0]) { color = color };
        Color line = color;
        line.a = drawMaterials.Count > 0 ? drawMaterials[0].color.a : 1f;
        if (drawMaterials.Count > 0) draw.material = new Material(drawMaterials[0]) { color = line };
    }

    public void NextAppearance()
    {
        currentIndex++;
        if (currentIndex >= cirkelMaterials.Count)
            currentIndex = 0;

        ApplyAppearance(currentIndex);
        var player = GetComponent<NetworkPlayer>();
        if (player.Object.HasInputAuthority)
        {
            player.RPC_SetAppearance(currentIndex);
        }
    }


}
