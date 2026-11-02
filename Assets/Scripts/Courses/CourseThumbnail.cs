using System.Collections.Generic;
using UnityEngine;

// Small pictures of courses for menus: the part of the map the course uses, cut out of the map's thumbnail at
// card size, with the course drawn on it in purple. Kept small (about 1 MB each) and only the most recent ones
// are kept, so a long community list can not use up the memory.
public static class CourseThumbnail
{
    public const float CardAspect = 368f / 300f;
    const int Width = 552;   // 1.5 x the card picture, sharp on a 1080p screen
    const int Height = 450;
    const float Scale = Width / 368f;
    const int MaxCached = 60;

    static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
    static readonly LinkedList<string> recent = new LinkedList<string>();

    public static Texture2D Get(CourseData course)
    {
        MapDefinition map = MapDefinition.Find(course.mapId);
        MapBackground background = map != null ? map.GetBackground(course.background) : null;
        if (background == null)
        {
            return null;
        }
        string key = course.id + "|" + course.background + "|" + course.ToCompact();
        if (cache.TryGetValue(key, out Texture2D cached) && cached != null)
        {
            recent.Remove(key);
            recent.AddFirst(key);
            return cached;
        }
        Texture2D source = Resources.Load<Texture2D>(background.thumbnailPath);
        if (source == null || !source.isReadable)
        {
            return null;
        }

        Rect area = CourseArea(course, CardAspect);
        Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[Width * Height];
        for (int y = 0; y < Height; y++)
        {
            float v = area.yMin + (y + 0.5f) / Height * area.height;
            for (int x = 0; x < Width; x++)
            {
                float u = area.xMin + (x + 0.5f) / Width * area.width;
                pixels[y * Width + x] = source.GetPixelBilinear(u, v);
            }
        }
        texture.SetPixels32(pixels);
        // Also on the old pictures with a printed course: the bold copy lands on the thin print and stays readable when small
        DrawCourse(texture, map, background, course, area);
        texture.Apply();

        cache[key] = texture;
        recent.AddFirst(key);
        while (recent.Count > MaxCached)
        {
            string oldest = recent.Last.Value;
            recent.RemoveLast();
            if (cache.TryGetValue(oldest, out Texture2D old) && old != null) Object.Destroy(old);
            cache.Remove(oldest);
        }
        return texture;
    }

    // The part of the picture around the course (0..1 UV rect), widened to the given width/height ratio
    public static Rect CourseArea(CourseData course, float aspect)
    {
        MapDefinition map = MapDefinition.Find(course.mapId);
        MapBackground background = map != null ? map.GetBackground(course.background) : null;
        if (background == null || background.pixelSize.x <= 0)
        {
            return new Rect(0, 0, 1, 1);
        }
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (CoursePoint point in course.AllPoints())
        {
            Vector2 uv = background.WorldToUV(map.ToWorld(point));
            min = Vector2.Min(min, uv);
            max = Vector2.Max(max, uv);
        }
        // Work in pixels so the aspect ratio is right, with a margin around the course
        Vector2 size = background.pixelSize;
        Vector2 pMin = Vector2.Scale(min, size), pMax = Vector2.Scale(max, size);
        Vector2 center = (pMin + pMax) / 2f;
        Vector2 extent = (pMax - pMin) * 0.6f + Vector2.one * size.x * 0.03f;
        if (extent.x / extent.y < aspect) extent.x = extent.y * aspect; else extent.y = extent.x / aspect;
        extent = Vector2.Min(extent, size / 2f);
        if (extent.x / extent.y < aspect) extent.y = extent.x / aspect; else extent.x = extent.y * aspect;
        center.x = Mathf.Clamp(center.x, extent.x, size.x - extent.x);
        center.y = Mathf.Clamp(center.y, extent.y, size.y - extent.y);
        return new Rect((center.x - extent.x) / size.x, (center.y - extent.y) / size.y, extent.x * 2f / size.x, extent.y * 2f / size.y);
    }

    static void DrawCourse(Texture2D texture, MapDefinition map, MapBackground background, CourseData course, Rect area)
    {
        List<CoursePoint> points = course.AllPoints();
        List<Vector2> pixels = new List<Vector2>();
        foreach (CoursePoint point in points)
        {
            Vector2 uv = background.WorldToUV(map.ToWorld(point));
            pixels.Add(new Vector2((uv.x - area.xMin) / area.width * texture.width, (uv.y - area.yMin) / area.height * texture.height));
        }
        // Marks as on the big map, but never thinner or smaller than reads well on the card
        float pixelsPerMeter = MapDefinition.WorldUnitsPerMeter * background.pixelsPerUnit * background.scale * texture.width / (area.width * background.pixelSize.x);
        float radius = Mathf.Max(37f * pixelsPerMeter, 9f * Scale);
        float width = Mathf.Max(4.5f * pixelsPerMeter, 2.4f * Scale);
        Color32 purple = CourseRenderer.CoursePurple;

        for (int i = 0; i < pixels.Count; i++)
        {
            if (i > 0)
            {
                Vector2 direction = (pixels[i] - pixels[i - 1]).normalized;
                float fromGap = i - 1 == 0 ? radius * 1.1f : radius;
                Vector2 a = pixels[i - 1] + direction * fromGap;
                Vector2 b = pixels[i] - direction * radius;
                if (Vector2.Dot(b - a, direction) > 0f)
                {
                    DrawLine(texture, a, b, width, purple);
                }
            }
            if (i == 0)
            {
                Vector2 toward = pixels.Count > 1 ? (pixels[1] - pixels[0]).normalized : Vector2.up;
                float angle = Mathf.Atan2(toward.y, toward.x);
                Vector2[] corners = new Vector2[3];
                for (int k = 0; k < 3; k++)
                {
                    float c = angle + k * Mathf.PI * 2f / 3f;
                    corners[k] = pixels[0] + new Vector2(Mathf.Cos(c), Mathf.Sin(c)) * radius * 1.3f;
                }
                for (int k = 0; k < 3; k++)
                {
                    DrawLine(texture, corners[k], corners[(k + 1) % 3], width, purple);
                }
            }
            else
            {
                DrawRing(texture, pixels[i], radius, width, purple);
                if (i == pixels.Count - 1)
                {
                    DrawRing(texture, pixels[i], radius * 0.7f, width, purple);
                }
            }
        }
    }

    static void DrawLine(Texture2D texture, Vector2 a, Vector2 b, float width, Color32 color)
    {
        float half = width / 2f;
        int minX = Mathf.FloorToInt(Mathf.Min(a.x, b.x) - half), maxX = Mathf.CeilToInt(Mathf.Max(a.x, b.x) + half);
        int minY = Mathf.FloorToInt(Mathf.Min(a.y, b.y) - half), maxY = Mathf.CeilToInt(Mathf.Max(a.y, b.y) + half);
        Vector2 ab = b - a;
        float lengthSquared = Mathf.Max(ab.sqrMagnitude, 0.0001f);
        for (int y = Mathf.Max(minY, 0); y <= Mathf.Min(maxY, texture.height - 1); y++)
        {
            for (int x = Mathf.Max(minX, 0); x <= Mathf.Min(maxX, texture.width - 1); x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSquared);
                if ((a + ab * t - p).sqrMagnitude <= half * half)
                {
                    texture.SetPixel(x, y, color);
                }
            }
        }
    }

    static void DrawRing(Texture2D texture, Vector2 center, float radius, float width, Color32 color)
    {
        float inner = radius - width / 2f, outer = radius + width / 2f;
        for (int y = Mathf.Max(Mathf.FloorToInt(center.y - outer), 0); y <= Mathf.Min(Mathf.CeilToInt(center.y + outer), texture.height - 1); y++)
        {
            for (int x = Mathf.Max(Mathf.FloorToInt(center.x - outer), 0); x <= Mathf.Min(Mathf.CeilToInt(center.x + outer), texture.width - 1); x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                if (distance >= inner && distance <= outer)
                {
                    texture.SetPixel(x, y, color);
                }
            }
        }
    }
}
