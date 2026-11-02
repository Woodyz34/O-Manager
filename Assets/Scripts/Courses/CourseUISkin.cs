using TMPro;
using UnityEngine;

// Sprites and font for the course pages, set up by the editor (Editor/CourseSetup.cs) in Resources/UI/CourseUISkin.
[CreateAssetMenu(menuName = "O-Manager/Course UI Skin")]
public class CourseUISkin : ScriptableObject
{
    public Sprite rounded;
    public Sprite pill;
    public Sprite background;
    public TMP_FontAsset font;
}
