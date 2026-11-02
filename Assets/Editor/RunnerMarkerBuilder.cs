using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UI = ModernUIBuilder;

// Adds the ring, the coloured copy of the circle and the name tag to the runner prefab (NetworkPlayerPF),
// and hooks them up to RunnerMarker. Run it again from Tools > O-Manager > Apply Runner Marker
// after changing sizes or colours here.
public static class RunnerMarkerBuilder
{
    const string PlayerPrefab = "Assets/Prefabs/Network/NetworkPlayerPF.prefab";
    const string RingPath = "Assets/UI/Game/runner_ring.png";

    // The ring picture is 256 pixels wide and its outer edge is 124 pixels from the middle
    const float RingOuterPixels = 124f;
    // Outer edge of the ring, measured in circle diameters from the middle (the circle itself reaches 0.5)
    const float RingOuterRadius = 0.70f;

    const float NameFontSize = 64f;

    [MenuItem("Tools/O-Manager/Apply Runner Marker (ring and name tag on the runner)")]
    public static void ApplyMenu()
    {
        Debug.Log(Apply());
    }

    public static string Apply()
    {
        UI.log = new StringBuilder();
        if (EditorApplication.isPlaying)
        {
            return "Aborted: exit Play mode first.";
        }
        UI.LoadAssets();
        Sprite ringSprite = ImportRing();
        if (ringSprite == null)
        {
            return "Aborted: " + RingPath + " is missing.";
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
        try
        {
            Build(root, ringSprite);
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
            UI.log.AppendLine("runner prefab saved");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        AssetDatabase.SaveAssets();
        return UI.log.ToString();
    }

    static void Build(GameObject root, Sprite ringSprite)
    {
        SpriteRenderer circle = root.GetComponent<SpriteRenderer>();
        TrailRenderer trail = root.GetComponent<TrailRenderer>();
        LineRenderer route = root.GetComponent<LineRenderer>();
        NetworkPlayer player = root.GetComponent<NetworkPlayer>();

        // The copy of the circle and the ring are drawn above the route line the player has drawn,
        // so the line no longer covers the runner. The name goes above everything.
        int above = Mathf.Max(circle.sortingOrder, route != null ? route.sortingOrder : 0) + 1;

        Transform marker = Plain(root.transform, "Marker");
        marker.localPosition = new Vector3(0, 0, -0.1f);
        marker.localRotation = Quaternion.identity;
        marker.localScale = Vector3.one;

        SpriteRenderer dot = UI.GetOrAdd<SpriteRenderer>(Plain(marker, "Dot").gameObject);
        dot.sprite = circle.sprite;
        dot.sharedMaterial = circle.sharedMaterial;
        dot.color = circle.color;
        dot.sortingLayerID = circle.sortingLayerID;
        dot.sortingOrder = above;

        SpriteRenderer ring = UI.GetOrAdd<SpriteRenderer>(Plain(marker, "Ring").gameObject);
        ring.sprite = ringSprite;
        ring.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
        ring.color = Color.white;
        ring.sortingLayerID = circle.sortingLayerID;
        ring.sortingOrder = above + 1;
        ring.transform.localPosition = new Vector3(0, 0, -0.05f);
        float ringScale = RingOuterRadius / (RingOuterPixels / ringSprite.pixelsPerUnit);
        ring.transform.localScale = new Vector3(ringScale, ringScale, 1f);

        // Name tag: a dark pill with a light rim, so the name reads on every colour of the map
        TextMeshProUGUI text = player.playerNickName;
        Canvas canvas = text.GetComponentInParent<Canvas>(true);
        canvas.sortingLayerID = circle.sortingLayerID;
        canvas.sortingOrder = above + 2;

        Transform tag = UI.Child(canvas.transform, "Tag");
        RectTransform tagRect = (RectTransform)tag;
        tagRect.anchorMin = tagRect.anchorMax = tagRect.pivot = new Vector2(0.5f, 0.5f);
        tagRect.anchoredPosition = Vector2.zero;
        tagRect.localScale = Vector3.one;
        // The background is a child that fills the tag. On the tag itself its rounded ends would
        // count as a minimum size and make the tag taller than the name needs.
        Image onTag = tag.GetComponent<Image>();
        if (onTag != null) Object.DestroyImmediate(onTag);
        Outline rimOnTag = tag.GetComponent<Outline>();
        if (rimOnTag != null) Object.DestroyImmediate(rimOnTag);
        Transform back = UI.Child(tag, "Background");
        back.SetAsFirstSibling();
        UI.Stretch((RectTransform)back, 0, 0, 0, 0);
        UI.GetOrAdd<LayoutElement>(back.gameObject).ignoreLayout = true;
        Image background = UI.GetOrAdd<Image>(back.gameObject);
        background.sprite = UI.pill;
        background.type = Image.Type.Sliced;
        background.pixelsPerUnitMultiplier = 1.4f;
        background.color = new Color(UI.Ink.r, UI.Ink.g, UI.Ink.b, 0.95f);
        background.raycastTarget = false;
        Outline rim = UI.GetOrAdd<Outline>(back.gameObject);
        rim.effectColor = new Color(UI.Cream.r, UI.Cream.g, UI.Cream.b, 0.9f);
        rim.effectDistance = new Vector2(3f, -3f);
        rim.useGraphicAlpha = true;
        HorizontalLayoutGroup row = UI.GetOrAdd<HorizontalLayoutGroup>(tag.gameObject);
        row.padding = new RectOffset(34, 34, 8, 8);
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        ContentSizeFitter fit = UI.GetOrAdd<ContentSizeFitter>(tag.gameObject);
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        text.transform.SetParent(tag, false);
        text.rectTransform.localScale = Vector3.one;
        UI.StyleText(text, NameFontSize, UI.Cream, FontStyles.Bold, TextAlignmentOptions.Center);
        text.margin = Vector4.zero;
        text.raycastTarget = false;
        LayoutRebuilder.ForceRebuildLayoutImmediate(tagRect);

        RunnerMarker runnerMarker = UI.GetOrAdd<RunnerMarker>(root);
        runnerMarker.circle = circle;
        runnerMarker.marker = marker;
        runnerMarker.dot = dot;
        runnerMarker.ring = ring;
        runnerMarker.nameCanvas = (RectTransform)canvas.transform;
        runnerMarker.nameTag = tagRect;
        runnerMarker.nameText = text;
        runnerMarker.trail = trail;
        UI.log.AppendLine("marker built: order " + above + ", ring scale " + ringScale + ", tag " + tagRect.rect.size);
    }

    static Sprite ImportRing()
    {
        AssetDatabase.ImportAsset(RingPath);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(RingPath);
        if (importer == null)
        {
            return null;
        }
        if (importer.textureType != TextureImporterType.Sprite || importer.spritePixelsPerUnit != 256 || !importer.mipmapEnabled)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 256;
            importer.mipmapEnabled = true;       // the ring is often drawn very small
            importer.filterMode = FilterMode.Trilinear;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(RingPath);
    }

    // A child without UI layout, made once and reused when the builder runs again
    static Transform Plain(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            GameObject created = new GameObject(name);
            created.layer = parent.gameObject.layer;
            child = created.transform;
            child.SetParent(parent, false);
        }
        return child;
    }
}
