using System.Collections.Generic;
using AssetsTools.NET;

// How Unity itself describes script fields, worked out by comparing its real descriptions with generated ones.
internal static class TreeRules
{
    private static readonly Dictionary<string, (string name, int version)> s_builtIn = new Dictionary<string, (string, int)>
    {
        { "Vector2", ("Vector2f", 1) },
        { "Vector3", ("Vector3f", 1) },
        { "Vector4", ("Vector4f", 1) },
        { "Quaternion", ("Quaternionf", 1) },
        { "Color", ("ColorRGBA", 1) },
        { "Color32", ("ColorRGBA", 2) },
        { "Rect", ("Rectf", 2) },
        { "Bounds", ("AABB", 1) },
        { "LayerMask", ("BitField", 2) },
        { "AnimationCurve", ("AnimationCurve", 2) },
        { "Keyframe", ("Keyframe", 3) },
        { "Gradient", ("Gradient", 2) },
    };

    public static (string type, int version) TypeOf(AssetTypeTemplateField field)
    {
        return s_builtIn.TryGetValue(field.Type, out var known) ? known : (field.Type, 1);
    }

    // Arrays of simple values and strings align after themselves. Arrays of objects and pointers don't.
    public static bool Aligned(AssetTypeTemplateField field)
    {
        if (!field.IsArray)
        {
            // A vector of bytes also carries the alignment on the vector itself.
            if (field.Type == "vector" && field.Children.Count == 1 && field.Children[0].Children.Count == 2)
            {
                string element = field.Children[0].Children[1].Type;
                return element == "UInt8" || element == "SInt8" || element == "char" || element == "bool";
            }
            return field.IsAligned;
        }

        var data = field.Children[1];
        return data.Type == "string" || data.Type == "Keyframe" || data.ValueType != AssetValueType.None;
    }
}
