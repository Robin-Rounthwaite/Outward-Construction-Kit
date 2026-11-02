using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Reading a scene's text takes seconds, and everything read is lost on each code reload, so it's kept in a file too.
/// The file only counts for the same scene file and the same build of this code, since either could change what's read.
/// </summary>
internal partial class RipScene
{
    // Every field this writes. A field added without being written would quietly come back empty, so a difference
    // here turns the cache off, and the self-test says so.
    private const string CACHED_FIELDS =
        "ColliderInfo:Center,ClassId,Convex,Direction,Height,IsTrigger,MeshGuid,Radius,Size;"
        + "Node:Active,Children,Colliders,FirstSubMesh,HasLodGroup,HasRenderer,Id,IsCharacter,IsInteractive,IsItem,IsWaterPlane,IsWaterSource,MaterialGuids,MeshFileId,MeshGuid,Name,Parent,ParentNode,Position,RendererEnabled,RootOrder,Rotation,Scale,SkinnedEnabled,SkinnedMaterialGuids,SkinnedMeshFileId,SkinnedMeshGuid,SubMeshCount;"
        + "Prop:BaseName,Batched,Category,InstanceCount,IsEnemy,IsItem,Node,Scene;"
        + "RipScene:HasWater,HasWaterSource,Props,Roots,SceneName";

    public static bool CacheCoversEveryField
    {
        get { return CACHED_FIELDS == string.Join(";", new[] { typeof(ColliderInfo), typeof(Node), typeof(Prop), typeof(RipScene) }.Select(FieldsOf).ToArray()); }
    }

    private static string FieldsOf(Type type)
    {
        IEnumerable<string> names = type.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !f.IsNotSerialized).Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal);
        return type.Name + ":" + string.Join(",", names.ToArray());
    }

    /// <summary>Which version of a scene file this is: its path, size and date.</summary>
    public static string StampOf(string file)
    {
        var info = new FileInfo(file);
        return file + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
    }

    /// <summary>Like Load, but uses what's kept in the folder if it's still good, and keeps what it reads there.</summary>
    public static RipScene LoadCached(string sceneName, string file, string cacheFolder)
    {
        if (!CacheCoversEveryField)
            return Load(sceneName, file);

        string cache = Path.Combine(cacheFolder, sceneName + ".bin");
        string stamp = StampOf(file) + "|" + typeof(RipScene).Module.ModuleVersionId;
        if (File.Exists(cache))
        {
            try
            {
                using (var reader = new BinaryReader(new BufferedStream(File.OpenRead(cache), 1 << 16)))
                    if (reader.ReadString() == stamp)
                        return new CacheReader(reader).Read(sceneName);
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is ArgumentException || e is UnauthorizedAccessException)
            {
                // A damaged cache only means reading the scene again.
            }
        }

        RipScene scene = Load(sceneName, file);
        try
        {
            Directory.CreateDirectory(cacheFolder);
            string spare = cache + ".tmp";
            using (var writer = new BinaryWriter(new BufferedStream(File.Create(spare), 1 << 16)))
            {
                writer.Write(stamp);
                new CacheWriter(writer).Write(scene);
            }
            if (File.Exists(cache))
                File.Delete(cache);
            File.Move(spare, cache);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            // Not being able to keep it only means reading it again next time.
        }
        return scene;
    }

    // Names and GUIDs repeat thousands of times, so each is written once and then by number.
    private sealed class CacheWriter
    {
        private readonly BinaryWriter m_out;
        private readonly Dictionary<string, int> m_strings = new Dictionary<string, int>();
        private readonly Dictionary<Node, int> m_nodes = new Dictionary<Node, int>();

        public CacheWriter(BinaryWriter writer)
        {
            m_out = writer;
        }

        public void Write(RipScene scene)
        {
            m_out.Write(scene.HasWater);
            m_out.Write(scene.HasWaterSource);
            m_out.Write(scene.Roots.Count);
            foreach (Node root in scene.Roots)
                WriteNode(root);
            m_out.Write(scene.Props.Count);
            foreach (Prop prop in scene.Props)
            {
                m_out.Write(m_nodes[prop.Node]);
                Text(prop.BaseName);
                Text(prop.Category);
                m_out.Write(prop.InstanceCount);
                m_out.Write(prop.Batched);
                m_out.Write(prop.IsEnemy);
                m_out.Write(prop.IsItem);
            }
        }

        private void WriteNode(Node node)
        {
            m_nodes[node] = m_nodes.Count;
            m_out.Write(node.Id);
            m_out.Write(node.Parent);
            Text(node.Name);
            m_out.Write(node.Active);
            m_out.Write(node.RootOrder);
            Vector(node.Position);
            m_out.Write(node.Rotation.x);
            m_out.Write(node.Rotation.y);
            m_out.Write(node.Rotation.z);
            m_out.Write(node.Rotation.w);
            Vector(node.Scale);
            m_out.Write(node.HasLodGroup);
            m_out.Write(node.IsCharacter);
            m_out.Write(node.IsInteractive);
            m_out.Write(node.IsItem);
            m_out.Write(node.IsWaterPlane);
            m_out.Write(node.IsWaterSource);
            Text(node.MeshGuid);
            m_out.Write(node.MeshFileId);
            m_out.Write(node.RendererEnabled);
            m_out.Write(node.HasRenderer);
            Texts(node.MaterialGuids);
            m_out.Write(node.FirstSubMesh);
            m_out.Write(node.SubMeshCount);
            Text(node.SkinnedMeshGuid);
            m_out.Write(node.SkinnedMeshFileId);
            m_out.Write(node.SkinnedEnabled);
            Texts(node.SkinnedMaterialGuids);
            m_out.Write(node.Colliders.Count);
            foreach (ColliderInfo collider in node.Colliders)
            {
                Text(collider.ClassId);
                m_out.Write(collider.IsTrigger);
                Vector(collider.Center);
                Vector(collider.Size);
                m_out.Write(collider.Radius);
                m_out.Write(collider.Height);
                m_out.Write(collider.Direction);
                m_out.Write(collider.Convex);
                Text(collider.MeshGuid);
            }
            m_out.Write(node.Children.Count);
            foreach (Node child in node.Children)
                WriteNode(child);
        }

        private void Vector(Vector3 v)
        {
            m_out.Write(v.x);
            m_out.Write(v.y);
            m_out.Write(v.z);
        }

        private void Texts(List<string> texts)
        {
            m_out.Write(texts.Count);
            foreach (string text in texts)
                Text(text);
        }

        // 0 is nothing, -1 is a new string that follows, and anything else is one seen before.
        private void Text(string text)
        {
            int number;
            if (text == null)
                m_out.Write(0);
            else if (m_strings.TryGetValue(text, out number))
                m_out.Write(number);
            else
            {
                m_strings[text] = m_strings.Count + 1;
                m_out.Write(-1);
                m_out.Write(text);
            }
        }
    }

    private sealed class CacheReader
    {
        private readonly BinaryReader m_in;
        private readonly List<string> m_strings = new List<string> { null };
        private readonly List<Node> m_nodes = new List<Node>();

        public CacheReader(BinaryReader reader)
        {
            m_in = reader;
        }

        public RipScene Read(string sceneName)
        {
            var scene = new RipScene { SceneName = sceneName };
            scene.HasWater = m_in.ReadBoolean();
            scene.HasWaterSource = m_in.ReadBoolean();
            int roots = m_in.ReadInt32();
            for (int i = 0; i < roots; i++)
                scene.Roots.Add(ReadNode(null));
            int props = m_in.ReadInt32();
            for (int i = 0; i < props; i++)
            {
                scene.Props.Add(new Prop
                {
                    Node = m_nodes[m_in.ReadInt32()],
                    BaseName = Text(),
                    Category = Text(),
                    InstanceCount = m_in.ReadInt32(),
                    Batched = m_in.ReadBoolean(),
                    IsEnemy = m_in.ReadBoolean(),
                    IsItem = m_in.ReadBoolean(),
                    Scene = scene,
                });
            }
            return scene;
        }

        private Node ReadNode(Node parent)
        {
            var node = new Node { ParentNode = parent };
            m_nodes.Add(node);
            node.Id = m_in.ReadInt64();
            node.Parent = m_in.ReadInt64();
            node.Name = Text();
            node.Active = m_in.ReadBoolean();
            node.RootOrder = m_in.ReadInt32();
            node.Position = Vector();
            node.Rotation = new Quaternion(m_in.ReadSingle(), m_in.ReadSingle(), m_in.ReadSingle(), m_in.ReadSingle());
            node.Scale = Vector();
            node.HasLodGroup = m_in.ReadBoolean();
            node.IsCharacter = m_in.ReadBoolean();
            node.IsInteractive = m_in.ReadBoolean();
            node.IsItem = m_in.ReadBoolean();
            node.IsWaterPlane = m_in.ReadBoolean();
            node.IsWaterSource = m_in.ReadBoolean();
            node.MeshGuid = Text();
            node.MeshFileId = m_in.ReadInt64();
            node.RendererEnabled = m_in.ReadBoolean();
            node.HasRenderer = m_in.ReadBoolean();
            Texts(node.MaterialGuids);
            node.FirstSubMesh = m_in.ReadInt32();
            node.SubMeshCount = m_in.ReadInt32();
            node.SkinnedMeshGuid = Text();
            node.SkinnedMeshFileId = m_in.ReadInt64();
            node.SkinnedEnabled = m_in.ReadBoolean();
            Texts(node.SkinnedMaterialGuids);
            int colliders = m_in.ReadInt32();
            for (int i = 0; i < colliders; i++)
            {
                node.Colliders.Add(new ColliderInfo
                {
                    ClassId = Text(),
                    IsTrigger = m_in.ReadBoolean(),
                    Center = Vector(),
                    Size = Vector(),
                    Radius = m_in.ReadSingle(),
                    Height = m_in.ReadSingle(),
                    Direction = m_in.ReadInt32(),
                    Convex = m_in.ReadBoolean(),
                    MeshGuid = Text(),
                });
            }
            int children = m_in.ReadInt32();
            for (int i = 0; i < children; i++)
                node.Children.Add(ReadNode(node));
            return node;
        }

        private Vector3 Vector()
        {
            return new Vector3(m_in.ReadSingle(), m_in.ReadSingle(), m_in.ReadSingle());
        }

        private void Texts(List<string> into)
        {
            int count = m_in.ReadInt32();
            for (int i = 0; i < count; i++)
                into.Add(Text());
        }

        private string Text()
        {
            int number = m_in.ReadInt32();
            if (number != -1)
                return m_strings[number];
            string text = m_in.ReadString();
            m_strings.Add(text);
            return text;
        }
    }
}
