using System.Collections.Generic;
using System.IO;
using System.Text;
using AssetsTools.NET;

namespace OutwardConstructionKit.Extractor
{
    /// <summary>
    /// Writes a script's fields as Unity's own type tree, from a template generated from the game's code. The game
    /// refuses a bundle whose description isn't right, so these rules were worked out by comparing Unity's real
    /// descriptions with generated ones, field by field.
    /// </summary>
    internal class ScriptDescriber
    {
        private const uint ALIGN_FLAG = 0x4000;
        private const uint ROOT_FLAG = 0x8000;
        private const uint COMMON_STRING = 0x80000000;

        private struct BuiltInType
        {
            public string Name;
            public ushort Version;

            public BuiltInType(string name, ushort version)
            {
                Name = name;
                Version = version;
            }
        }

        private static readonly Dictionary<string, BuiltInType> s_builtIn = new Dictionary<string, BuiltInType>
        {
            { "Vector2", new BuiltInType("Vector2f", 1) },
            { "Vector3", new BuiltInType("Vector3f", 1) },
            { "Vector4", new BuiltInType("Vector4f", 1) },
            { "Quaternion", new BuiltInType("Quaternionf", 1) },
            { "Color", new BuiltInType("ColorRGBA", 1) },
            { "Color32", new BuiltInType("ColorRGBA", 2) },
            { "Rect", new BuiltInType("Rectf", 2) },
            { "Bounds", new BuiltInType("AABB", 1) },
            { "LayerMask", new BuiltInType("BitField", 2) },
            { "AnimationCurve", new BuiltInType("AnimationCurve", 2) },
            { "Keyframe", new BuiltInType("Keyframe", 3) },
            { "Gradient", new BuiltInType("Gradient", 2) },
        };

        // Unity keeps common names in a shared table, marked by a high bit, so the same offsets are used here.
        private readonly Dictionary<string, uint> m_commonStrings = new Dictionary<string, uint>();

        public ScriptDescriber(IEnumerable<TypeTreeType> knownTypes)
        {
            foreach (TypeTreeType known in knownTypes)
                foreach (TypeTreeNode node in known.Nodes)
                {
                    if ((node.TypeStrOffset & COMMON_STRING) != 0)
                        m_commonStrings[node.GetTypeString(known.StringBufferBytes, null)] = node.TypeStrOffset;
                    if ((node.NameStrOffset & COMMON_STRING) != 0)
                        m_commonStrings[node.GetNameString(known.StringBufferBytes, null)] = node.NameStrOffset;
                }
        }

        public void Describe(AssetTypeTemplateField fields, TypeTreeType type)
        {
            var nodes = new List<TypeTreeNode>();
            var strings = new MemoryStream();
            var local = new Dictionary<string, uint>();
            Flatten(fields, 0, nodes, strings, local);
            type.StringBufferBytes = strings.ToArray();
            type.Nodes = nodes;
        }

        private int Flatten(AssetTypeTemplateField field, byte level, List<TypeTreeNode> nodes, MemoryStream strings, Dictionary<string, uint> local)
        {
            BuiltInType builtIn;
            string typeName = s_builtIn.TryGetValue(field.Type, out builtIn) ? builtIn.Name : field.Type;
            var node = new TypeTreeNode
            {
                Version = s_builtIn.ContainsKey(field.Type) ? builtIn.Version : (ushort)1,
                Level = level,
                TypeFlags = (TypeTreeNodeFlags)(field.IsArray ? 1 : 0),
                TypeStrOffset = AddString(typeName, strings, local),
                NameStrOffset = AddString(field.Name, strings, local),
                Index = (uint)nodes.Count,
                MetaFlags = (IsAligned(field) ? ALIGN_FLAG : 0u) | (level == 0 ? ROOT_FLAG : 0u),
            };
            nodes.Add(node);

            int size = SizeOf(field.ValueType);
            bool sumChildren = size < 0 && !field.IsArray && field.Type != "string" && field.Children.Count > 0;
            if (sumChildren)
                size = 0;
            foreach (AssetTypeTemplateField child in field.Children)
            {
                int childSize = Flatten(child, (byte)(level + 1), nodes, strings, local);
                if (sumChildren)
                    size = size < 0 || childSize < 0 ? -1 : size + childSize;
            }

            node.ByteSize = field.IsArray || field.Type == "string" ? -1 : size;
            return node.ByteSize;
        }

        private uint AddString(string text, MemoryStream strings, Dictionary<string, uint> local)
        {
            uint offset;
            if (m_commonStrings.TryGetValue(text, out offset) || local.TryGetValue(text, out offset))
                return offset;

            offset = (uint)strings.Position;
            local[text] = offset;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            strings.Write(bytes, 0, bytes.Length);
            strings.WriteByte(0);
            return offset;
        }

        private static int SizeOf(AssetValueType type)
        {
            switch (type)
            {
                case AssetValueType.Bool:
                case AssetValueType.Int8:
                case AssetValueType.UInt8:
                    return 1;
                case AssetValueType.Int16:
                case AssetValueType.UInt16:
                    return 2;
                case AssetValueType.Int32:
                case AssetValueType.UInt32:
                case AssetValueType.Float:
                    return 4;
                case AssetValueType.Int64:
                case AssetValueType.UInt64:
                case AssetValueType.Double:
                    return 8;
                default:
                    return -1;
            }
        }

        // Arrays of simple values and strings align after themselves, and arrays of objects and pointers don't.
        // A vector of bytes carries the alignment on the vector as well.
        private static bool IsAligned(AssetTypeTemplateField field)
        {
            if (!field.IsArray)
            {
                if (field.Type == "vector" && field.Children.Count == 1 && field.Children[0].Children.Count == 2)
                {
                    string element = field.Children[0].Children[1].Type;
                    return element == "UInt8" || element == "SInt8" || element == "char" || element == "bool";
                }
                return field.IsAligned;
            }

            AssetTypeTemplateField data = field.Children[1];
            return data.Type == "string" || data.Type == "Keyframe" || data.ValueType != AssetValueType.None;
        }
    }
}
