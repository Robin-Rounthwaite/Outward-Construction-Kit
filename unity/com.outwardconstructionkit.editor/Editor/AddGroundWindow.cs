using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Asks what ground to start with before making it.</summary>
internal class AddGroundWindow : EditorWindow
{
    private static readonly int[] s_sizes = { 100, 200, 400, 800 };
    private static readonly string[] s_sizeLabels = { "Small: 100 by 100 m", "Medium: 200 by 200 m", "Large: 400 by 400 m", "Very large: 800 by 800 m" };
    private const int DEFAULT_SIZE_INDEX = 1;

    private List<KeyValuePair<string, string>> m_textures = new List<KeyValuePair<string, string>>();
    private string[] m_labels = new string[0];
    private int m_texture;
    private int m_size = DEFAULT_SIZE_INDEX;
    private Action m_added;

    public static void Open(Action added)
    {
        var window = GetWindow<AddGroundWindow>(true, "Add Ground");
        window.minSize = window.maxSize = new Vector2(420f, 150f);
        window.m_added = added;
        window.ReadTextures();
    }

    // Texture name to the scene it comes from, the first location offering it.
    private void ReadTextures()
    {
        var found = new Dictionary<string, string>();
        foreach (KeyValuePair<string, string> scene in OckSettings.RippedScenes())
        {
            string root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(scene.Value)));
            foreach (string texture in GroundTools.GroundTexturesIn(root))
                if (!found.ContainsKey(texture))
                    found[texture] = scene.Key;
        }
        m_textures = found.OrderBy(t => t.Key).ToList();
        m_labels = m_textures.Select(t => t.Key.Replace("tex_env_", "")).ToArray();
        m_texture = Mathf.Max(0, m_textures.FindIndex(t => t.Key == GroundTools.DEFAULT_START_TEXTURE));
    }

    private void OnGUI()
    {
        if (m_textures.Count == 0)
        {
            KitStyles.HelpBox("No ground textures found. Rip a scene first, with Construction Kit > Rip a Scene.", MessageType.Warning);
            return;
        }
        m_texture = EditorGUILayout.Popup(new GUIContent("Starting ground", "Covers the whole terrain to begin with. You can paint others over it."), m_texture, m_labels);
        m_size = EditorGUILayout.Popup(new GUIContent("Size", "Bigger ground takes longer to export, because enemies need paths worked out across all of it."), m_size, s_sizeLabels);
        EditorGUILayout.LabelField("This replaces the starter room's floor and walls.", KitStyles.WrappedNote);

        GUILayout.FlexibleSpace();
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Add Ground", "Makes the ground and selects it, ready to sculpt and paint."), GUILayout.Width(100)))
            {
                KeyValuePair<string, string> texture = m_textures[m_texture];
                Terrain ground = GroundTools.AddGround(s_sizes[m_size], texture.Value, texture.Key);
                if (ground != null)
                {
                    Selection.activeGameObject = ground.gameObject;
                    m_added?.Invoke();
                }
                Close();
                GUIUtility.ExitGUI();
            }
        }
    }
}
