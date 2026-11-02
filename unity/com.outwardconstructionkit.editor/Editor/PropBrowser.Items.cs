using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>The Items tab, which lists the game's own items rather than what rips happen to hold.</summary>
internal partial class PropBrowser
{
    private List<ItemTools.Entry> m_visibleItems = new List<ItemTools.Entry>();

    /// <summary>Items whose name or number matches, listed by name.</summary>
    public static List<ItemTools.Entry> ItemsFor(IEnumerable<ItemTools.Entry> catalog, string category, string search)
    {
        return catalog.Where(i => (category == null || i.Type == category) && (Matches(i.Name, search) || Matches(i.PrefabName, search) || Matches(i.Id.ToString(), search)))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Id).ToList();
    }

    private void UpdateItemCategories()
    {
        m_categories = new[] { ALL_CATEGORIES }.Concat(ItemTools.Catalog().Select(i => i.Type).Distinct().OrderBy(c => c)).ToArray();
        m_categoryIndex = 0;
    }

    private void FilterItems()
    {
        string category = m_categoryIndex > 0 && m_categoryIndex < m_categories.Length ? m_categories[m_categoryIndex] : null;
        m_visibleItems = ItemsFor(ItemTools.Catalog(), category, m_search);
    }

    private void DrawItemsTab()
    {
        if (ItemTools.Catalog().Count == 0)
        {
            KitStyles.HelpBox("The list of items comes from the game. Start it once with the kit installed, then come back here.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Launch Game"))
                    m_status = GameLauncher.Launch();
                if (GUILayout.Button("Check Again"))
                {
                    UpdateItemCategories();
                    FilterItems();
                }
            }
            return;
        }

        EditorGUILayout.LabelField($"{m_visibleItems.Count} found. Click Add for a marker, which becomes a real item players can pick up.", KitStyles.Note);
        DrawRows(m_visibleItems.Count, i =>
        {
            ItemTools.Entry item = m_visibleItems[i];
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(ROW_HEIGHT)))
            {
                GUILayout.Label(new GUIContent(item.Name, item.PrefabName));
                GUILayout.FlexibleSpace();
                GUILayout.Label(item.Id.ToString(), KitStyles.Note, GUILayout.Width(60));
                GUILayout.Label(item.Type, KitStyles.Note, GUILayout.Width(100));
                if (GUILayout.Button(new GUIContent("Add", "Adds an orange marker at the middle of your view. In game it becomes this real item, and each character can pick it up once until the region resets."), GUILayout.Width(44)))
                    AddItem(item);
            }
        });
    }

    private void AddItem(ItemTools.Entry item)
    {
        SceneView view = SceneView.lastActiveSceneView;
        Vector3 spot = view != null ? view.pivot : Vector3.zero;
        Vector3 ground;
        Selection.activeGameObject = ItemTools.AddMarker(item, TryFindGround(new Ray(spot + Vector3.up * 50f, Vector3.down), out ground) ? ground : spot);
        m_status = item.Name + " added. Move the orange marker to where the item should lie.";
    }
}
