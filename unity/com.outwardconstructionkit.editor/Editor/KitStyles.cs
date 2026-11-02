using UnityEditor;
using UnityEngine;

/// <summary>The kit's small text, at a size that can be read comfortably, since Unity's own small styles are tiny.</summary>
internal static class KitStyles
{
    private const int NOTE_FONT_SIZE = 12;

    private static GUIStyle s_note;
    private static GUIStyle s_wrappedNote;
    private static GUIStyle s_helpBox;

    public static GUIStyle Note
    {
        get { return s_note ?? (s_note = new GUIStyle(EditorStyles.label) { fontSize = NOTE_FONT_SIZE }); }
    }

    public static GUIStyle WrappedNote
    {
        get { return s_wrappedNote ?? (s_wrappedNote = new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = NOTE_FONT_SIZE }); }
    }

    public static void HelpBox(string message, MessageType type)
    {
        if (s_helpBox == null)
            s_helpBox = new GUIStyle(EditorStyles.helpBox) { fontSize = NOTE_FONT_SIZE, wordWrap = true };
        GUILayout.Label(new GUIContent(message, Icon(type)), s_helpBox);
    }

    private static Texture Icon(MessageType type)
    {
        switch (type)
        {
            case MessageType.Info: return EditorGUIUtility.IconContent("console.infoicon.sml").image;
            case MessageType.Warning: return EditorGUIUtility.IconContent("console.warnicon.sml").image;
            case MessageType.Error: return EditorGUIUtility.IconContent("console.erroricon.sml").image;
            default: return null;
        }
    }
}
