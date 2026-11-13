using System;
using System.Collections.Generic;
using MapImport;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The "Terrain look" pop-up of Import map and Edit map: what the ground is made of, which trees grow there and how
// much undergrowth there is (see MapImport.TerrainLook). The 3D view of a TV control builds its world from it.
// The player can also describe the terrain in their own words; that text is saved with the map for later.
public class TerrainLookPanel : MonoBehaviour
{
    TerrainLook look;
    Action<TerrainLook> onDone;
    readonly List<(Button button, string value)> groundButtons = new List<(Button, string)>();
    readonly List<(Button button, string value)> forestButtons = new List<(Button, string)>();
    Slider undergrowth;
    TextMeshProUGUI undergrowthText;
    TMP_InputField textField;

    public static TerrainLookPanel Open(Transform canvas, TerrainLook current, Action<TerrainLook> onDone)
    {
        var dim = CourseUI.Panel("Terrain look", canvas, new Color(0.08f, 0.14f, 0.1f, 0.7f), false);
        CourseUI.Stretch(dim.rectTransform);
        TerrainLookPanel panel = dim.gameObject.AddComponent<TerrainLookPanel>();
        panel.look = current != null ? current.Safe() : new TerrainLook();
        panel.onDone = onDone;
        panel.Build(dim.transform);
        return panel;
    }

    // One line for the import and edit pop-ups
    public static string Describe(TerrainLook l)
    {
        return (l ?? new TerrainLook()).Safe().Describe();
    }

    void Build(Transform root)
    {
        Image card = CourseUI.CardPanel("Card", root);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180, 820));
        CourseUI.Column(card.gameObject, 10, new RectOffset(36, 36, 28, 28));

        CourseUI.Size(CourseUI.Text("Title", card.transform, "Terrain look", 34, CourseUI.Ink, FontStyles.Bold), 42);
        TextMeshProUGUI info = CourseUI.Text("Info", card.transform,
            "What the terrain looks like in the 3D view of a TV control. The map file says where the forest, the marshes and " +
            "the stones are; here you say what the ground is made of and which trees grow there. It changes nothing about how the runners run.",
            19, CourseUI.InkMuted);
        info.textWrappingMode = TextWrappingModes.Normal;
        CourseUI.Size(info, 56);

        CourseUI.Size(CourseUI.Text("Ground title", card.transform, "Ground in the forest", 22, CourseUI.Ink, FontStyles.Bold), 30);
        Choices(card.transform, "Ground 1", new[] { TerrainLook.Moss, TerrainLook.Heather, TerrainLook.Grass, TerrainLook.Lichen }, groundButtons, true);
        Choices(card.transform, "Ground 2", new[] { TerrainLook.Hard, TerrainLook.Sand, TerrainLook.Stone }, groundButtons, true);

        CourseUI.Size(CourseUI.Text("Forest title", card.transform, "Trees", 22, CourseUI.Ink, FontStyles.Bold), 30);
        Choices(card.transform, "Forest", TerrainLook.Forests, forestButtons, false);

        RectTransform row = CourseUI.Rect("Undergrowth", card.transform);
        CourseUI.Row(row.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = false;
        CourseUI.Size(row, 48);
        TextMeshProUGUI label = CourseUI.Text("Name", row, "Undergrowth", 22, CourseUI.Ink, FontStyles.Bold);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        CourseUI.Size(label, -1, 170);
        TextMeshProUGUI left = CourseUI.Text("Low end", row, "Clean forest floor", 17, CourseUI.InkMuted);
        left.alignment = TextAlignmentOptions.MidlineRight;
        CourseUI.Size(left, -1, 200);
        undergrowth = CourseUI.SliderControl("Slider", row, 0f, 1f, look.undergrowth, false, v =>
        {
            if (Mathf.Abs(v - 0.5f) < 0.025f) v = 0.5f;
            look.undergrowth = v;
            Refresh();
        });
        CourseUI.Size(undergrowth, 40, -1, 1);
        TextMeshProUGUI right = CourseUI.Text("High end", row, "Bushes and brush everywhere", 17, CourseUI.InkMuted);
        right.alignment = TextAlignmentOptions.MidlineLeft;
        CourseUI.Size(right, -1, 230);
        undergrowthText = CourseUI.Text("Value", row, "", 20, CourseUI.Forest, FontStyles.Bold);
        undergrowthText.alignment = TextAlignmentOptions.MidlineRight;
        CourseUI.Size(undergrowthText, -1, 100);

        CourseUI.Size(CourseUI.Text("Text title", card.transform, "Describe the terrain in your own words (saved with the map)", 22, CourseUI.Ink, FontStyles.Bold), 30);
        textField = CourseUI.Input("Text", card.transform, "For example: flat pine heath with sandy ground and a lot of heather, few stones",
            TerrainLook.MaxTextLength, null);
        CourseUI.Size(textField, 110);
        textField.lineType = TMP_InputField.LineType.MultiLineSubmit;
        if (textField.textComponent != null)
        {
            textField.textComponent.textWrappingMode = TextWrappingModes.Normal;
            textField.textComponent.alignment = TextAlignmentOptions.TopLeft;
            textField.textComponent.fontSize = 20;
        }
        if (textField.placeholder is TextMeshProUGUI placeholder)
        {
            placeholder.textWrappingMode = TextWrappingModes.Normal;
            placeholder.alignment = TextAlignmentOptions.TopLeft;
            placeholder.fontSize = 20;
        }
        textField.SetTextWithoutNotify(look.text ?? "");

        RectTransform spacer = CourseUI.Rect("Spacer", card.transform);
        spacer.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;

        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Row(buttons.gameObject, 12, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(buttons, 58);
        CourseUI.Button("Normal", buttons, "All normal", CourseUI.ButtonKind.Light, () =>
        {
            look = new TerrainLook { text = textField.text };
            Refresh();
        }, 22);
        CourseUI.Button("Cancel", buttons, "Cancel", CourseUI.ButtonKind.Light, () => Destroy(gameObject), 22);
        CourseUI.Button("Done", buttons, "Done", CourseUI.ButtonKind.Primary, Done, 22);

        Refresh();
    }

    void Choices(Transform parent, string name, string[] values, List<(Button, string)> into, bool ground)
    {
        RectTransform row = CourseUI.Rect(name, parent);
        CourseUI.Row(row.gameObject, 10, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Size(row, 50);
        foreach (string value in values)
        {
            string chosen = value;
            Button button = CourseUI.Button(value, row, ground ? TerrainLook.GroundName(value) : TerrainLook.ForestName(value),
                CourseUI.ButtonKind.Light, () =>
                {
                    if (ground) look.ground = chosen;
                    else look.forest = chosen;
                    Refresh();
                }, 19);
            into.Add((button, value));
        }
    }

    void Refresh()
    {
        foreach (var (button, value) in groundButtons)
            CourseUI.SetButtonColor(button, value == look.ground ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        foreach (var (button, value) in forestButtons)
            CourseUI.SetButtonColor(button, value == look.forest ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        if (Mathf.Abs(undergrowth.value - look.undergrowth) > 0.0001f) undergrowth.SetValueWithoutNotify(look.undergrowth);
        undergrowthText.text = look.undergrowth < 0.25f ? "Little" : look.undergrowth > 0.75f ? "A lot" : "Normal";
        undergrowthText.color = Mathf.Abs(look.undergrowth - 0.5f) >= 0.005f ? CourseUI.Accent : CourseUI.Forest;
    }

    void Done()
    {
        look.text = textField.text ?? "";
        onDone?.Invoke(look.Safe());
        Destroy(gameObject);
    }
}
