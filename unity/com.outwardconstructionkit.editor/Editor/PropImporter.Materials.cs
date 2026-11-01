using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Stand-in materials for the editor, and faithful copies of vanilla ones for embedded props.
internal static partial class PropImporter
{
    private static readonly Regex s_cutoutNames = new Regex("leaf|leaves|plant|bush|tree|grass|foliage|fern|vine", RegexOptions.IgnoreCase);
    private static readonly string[] s_normalSlots = { "_BumpMap", "_NormTex", "_NormalMap" };

    /// <summary>
    /// An exact copy of a vanilla material, which keeps every property and texture but uses a placeholder
    /// shader. The placeholder has the real shader's name, so the plugin can swap the real one in by name.
    /// Used only for embedded props, because it copies many more textures than the display material does.
    /// </summary>
    public static Material GetFaithfulMaterial(Material displayMaterial)
    {
        string guid = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(displayMaterial))?.userData;
        string file = string.IsNullOrEmpty(guid) ? null : RipFile(guid);
        if (file == null)
            return displayMaterial;

        string existing = AssetDatabase.GUIDToAssetPath(guid);
        if (!string.IsNullOrEmpty(existing) && AssetDatabase.LoadAssetAtPath<Material>(existing) != null)
            return AssetDatabase.LoadAssetAtPath<Material>(existing);

        string folder = LIBRARY + "/_Embedded";
        EnsureFolder(folder + "/Textures");
        EnsureFolder(folder + "/Shaders");

        foreach (KeyValuePair<string, string> slot in ReadTextureSlots(file))
            CopyTextureIfNeeded(slot.Value, folder, Regex.IsMatch(slot.Key, "Norm|Bump", RegexOptions.IgnoreCase));

        string shaderGuid = File.ReadLines(file).Where(l => l.Contains("m_Shader:")).Select(l => Regex.Match(l, @"guid: (\w+)")).Where(m => m.Success).Select(m => m.Groups[1].Value).FirstOrDefault();
        if (shaderGuid != null && string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(shaderGuid)) && RipFile(shaderGuid) != null)
            CopyWithMeta(RipFile(shaderGuid), folder + "/Shaders");

        return AssetDatabase.LoadAssetAtPath<Material>(CopyWithMeta(file, folder));
    }

    internal static Material GetMaterial(string guid, string folder)
    {
        string file = RipFile(guid);
        if (file == null)
            return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat"); // Unity's own, as with the shapes

        string path = folder + "/Materials/" + Safe(Path.GetFileNameWithoutExtension(file)) + "_" + guid.Substring(0, 8) + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            // Materials imported before this link existed need it too, or embedding can't find their originals.
            AssetImporter existingImporter = AssetImporter.GetAtPath(path);
            if (existingImporter.userData != guid)
            {
                existingImporter.userData = guid;
                AssetDatabase.WriteImportSettingsIfDirty(path);
            }
            if (IsOutOfDate(existing))
            {
                Configure(existing, file, folder);
                EditorUtility.SetDirty(existing);
            }
            return existing;
        }

        var material = new Material(Shader.Find(STAND_IN_SHADER)) { name = Path.GetFileNameWithoutExtension(file) };
        Configure(material, file, folder);

        AssetDatabase.CreateAsset(material, path);
        // Remembers which vanilla material this stands for, in case its prop gets embedded.
        AssetImporter.GetAtPath(path).userData = guid;
        AssetDatabase.WriteImportSettingsIfDirty(path);
        return material;
    }

    private static void Configure(Material material, string file, string folder)
    {
        material.shader = Shader.Find(STAND_IN_SHADER);
        Dictionary<string, string> textures = ReadTextureSlots(file);

        string main;
        if (textures.TryGetValue("_MainTex", out main))
            material.SetTexture("_MainTex", CopyTexture(main, folder, false));

        foreach (string slot in s_normalSlots)
        {
            string normal;
            if (textures.TryGetValue(slot, out normal))
            {
                material.SetTexture("_BumpMap", CopyTexture(normal, folder, true));
                break;
            }
        }

        Color color;
        if (TryReadColor(file, out color))
            material.color = color;

        // Draw both sides where the game does. 2 is back-face culling, which is the usual default.
        material.SetFloat("_Cull", ReadFloat(file, "_Cull", 2f));
        material.SetFloat("_NormalStrength", ReadFloat(file, "_NormStr", 1f));
        material.SetFloat("_Glossiness", ReadFloat(file, "_Glossiness", 0.1f));

        // Fine surface detail is the main visible difference from the game. It takes extra texture imports,
        // so only Detailed mode brings it in.
        bool detailed = !OckSettings.FastImport;
        string detail;
        material.SetFloat("_DetailStrength", detailed && textures.ContainsKey("_DetTex") ? 1f : 0f);
        material.SetTexture("_DetTex", detailed && textures.TryGetValue("_DetTex", out detail) ? CopyTexture(detail, folder, false) : null);
        material.SetTexture("_DetNormTex", detailed && textures.TryGetValue("_DetNormTex", out detail) ? CopyTexture(detail, folder, true) : null);
        material.SetFloat("_DetailNormalStrength", ReadFloat(file, "_DetNormStr", 0.35f));
        Vector4 tiling;
        material.SetVector("_DetailTiling", TryReadVector(file, "_DetTiling", out tiling) && tiling.x > 0f && tiling.y > 0f ? tiling : new Vector4(1f, 1f, 0f, 0f));

        // Alpha means cut-out holes on foliage, but other things on other materials, so this needs both
        // the game not to mark it opaque and a name that looks like foliage.
        string keywords = File.ReadLines(file).FirstOrDefault(l => l.Contains("m_ShaderKeywords:")) ?? "";
        bool cutout = !keywords.Contains("_OPAQUE") && s_cutoutNames.IsMatch(material.name);
        material.SetFloat("_Cutoff", cutout ? 0.4f : -0.01f);
        material.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "Opaque");
        material.renderQueue = (int)(cutout ? UnityEngine.Rendering.RenderQueue.AlphaTest : UnityEngine.Rendering.RenderQueue.Geometry);
    }

    private static bool TryReadVector(string materialFile, string property, out Vector4 value)
    {
        value = Vector4.zero;
        foreach (string line in File.ReadLines(materialFile))
        {
            Match match = Regex.Match(line, @"^\s+" + property + @": \{r: ([^,]+), g: ([^,]+), b: ([^,]+), a: ([^}]+)\}");
            if (match.Success)
            {
                var ci = CultureInfo.InvariantCulture;
                value = new Vector4(float.Parse(match.Groups[1].Value, ci), float.Parse(match.Groups[2].Value, ci), float.Parse(match.Groups[3].Value, ci), float.Parse(match.Groups[4].Value, ci));
                return true;
            }
        }
        return false;
    }

    private static float ReadFloat(string materialFile, string property, float fallback)
    {
        string prefix = property + ":";
        foreach (string line in File.ReadLines(materialFile))
        {
            string trimmed = line.Trim();
            float value;
            if (trimmed.StartsWith(prefix) && float.TryParse(trimmed.Substring(prefix.Length).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return value;
        }
        return fallback;
    }

    /// <summary>Brings already-imported stand-ins up to date with how materials are made now.</summary>
    public static void RefreshLibraryMaterials()
    {
        if (Shader.Find(STAND_IN_SHADER) == null)
            return; // not imported yet, so this runs again the next time the window opens

        foreach (string assetGuid in AssetDatabase.FindAssets("t:Material", new[] { LIBRARY }))
        {
            string path = AssetDatabase.GUIDToAssetPath(assetGuid);
            if (path.Contains("/_Embedded/"))
                continue;
            string ripGuid = AssetImporter.GetAtPath(path).userData;
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            string file = string.IsNullOrEmpty(ripGuid) ? null : RipFile(ripGuid);
            if (material == null || file == null || !IsOutOfDate(material))
                continue;
            Configure(material, file, Path.GetDirectoryName(Path.GetDirectoryName(path)).Replace('\\', '/'));
            EditorUtility.SetDirty(material);
        }
        AssetDatabase.SaveAssets();
    }

    private static bool IsOutOfDate(Material material)
    {
        if (material.shader.name != STAND_IN_SHADER)
            return true;
        // A material with detail in Fast mode, or without its detail texture in Detailed mode, is from the other mode.
        bool hasDetail = material.GetFloat("_DetailStrength") > 0f;
        return hasDetail == OckSettings.FastImport || material.GetVector("_DetailTiling").x <= 0f;
    }

    private static Dictionary<string, string> ReadTextureSlots(string materialFile)
    {
        var slots = new Dictionary<string, string>();
        string name = null;
        foreach (string line in File.ReadLines(materialFile))
        {
            Match slot = Regex.Match(line, @"^\s{6}(_\w+):\s*$");
            if (slot.Success)
            {
                name = slot.Groups[1].Value;
                continue;
            }
            if (name != null && line.Contains("m_Texture:"))
            {
                Match guid = Regex.Match(line, @"guid: (\w+)");
                if (guid.Success)
                    slots[name] = guid.Groups[1].Value;
                name = null;
            }
        }
        return slots;
    }

    private static bool TryReadColor(string materialFile, out Color color)
    {
        color = Color.white;
        foreach (string line in File.ReadLines(materialFile))
        {
            Match match = Regex.Match(line, @"^\s+_Color: \{r: ([^,]+), g: ([^,]+), b: ([^,]+), a: ([^}]+)\}");
            if (match.Success)
            {
                var ci = CultureInfo.InvariantCulture;
                color = new Color(float.Parse(match.Groups[1].Value, ci), float.Parse(match.Groups[2].Value, ci), float.Parse(match.Groups[3].Value, ci), float.Parse(match.Groups[4].Value, ci));
                return true;
            }
        }
        return false;
    }
}
