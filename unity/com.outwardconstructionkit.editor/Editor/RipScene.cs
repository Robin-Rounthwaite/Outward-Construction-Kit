using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Reads the props out of a ripped scene file. These are large (over a million lines) and Unity can't
/// load them without the game's scripts, so this reads just the parts it needs as text.
/// </summary>
internal class RipScene
{
    public class ColliderInfo
    {
        public string ClassId;
        public bool IsTrigger;
        public Vector3 Center;
        public Vector3 Size;
        public float Radius;
        public float Height;
        public int Direction;
        public bool Convex;
        public string MeshGuid;
    }

    public class Node
    {
        public List<ColliderInfo> Colliders = new List<ColliderInfo>();
        public long Id;
        public long Parent;
        public string Name = "";
        public bool Active = true;
        public int RootOrder;
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public Vector3 Scale = Vector3.one;
        public bool HasLodGroup;
        public bool IsCharacter;
        public string MeshGuid;
        public long MeshFileId;
        public bool RendererEnabled;
        public bool HasRenderer;
        public List<string> MaterialGuids = new List<string>();
        public int FirstSubMesh;
        public int SubMeshCount;
        public List<Node> Children = new List<Node>();
        public Node ParentNode;

        public bool HasMesh { get { return !string.IsNullOrEmpty(MeshGuid); } }

        public Matrix4x4 WorldMatrix
        {
            get
            {
                Matrix4x4 local = Matrix4x4.TRS(Position, Rotation, Scale);
                return ParentNode == null ? local : ParentNode.WorldMatrix * local;
            }
        }

        public string ScenePath
        {
            get { return ParentNode == null ? Name : ParentNode.ScenePath + "/" + Name; }
        }

        public IEnumerable<Node> SelfAndDescendants()
        {
            yield return this;
            foreach (Node child in Children)
                foreach (Node node in child.SelfAndDescendants())
                    yield return node;
        }
    }

    public class Prop
    {
        public Node Node;
        public string BaseName;
        public string Category;
        public int InstanceCount = 1;
        public bool Batched;
        public bool IsEnemy;
    }

    // Not scenery, and full of things that aren't props.
    private static readonly HashSet<string> s_skipRoots = new HashSet<string> { "AudioManager", "Main Camera", "SpawnPointManager", "AISquadManagerStructure", "_SNPC", "_DefeatScenario_Holder", "FXWorld" };
    private static readonly Regex s_notProps = new Regex("Sky Dome|Waterplane|WaterPro|Billboard", RegexOptions.IgnoreCase);
    private static readonly Regex s_instanceSuffix = new Regex(@" \(\d+\)$");
    private static readonly Regex s_guid = new Regex(@"guid: (\w+)");
    private static readonly Regex s_fileId = new Regex(@"fileID: (-?\d+)");
    private static readonly Regex s_numbers = new Regex(@": (-?[\d.]+(?:[eE][-+]?\d+)?)");

    public string SceneName;
    public List<Node> Roots = new List<Node>();

    /// <summary>One entry per distinct prop, using its first instance, which is the one a path lookup finds.</summary>
    public List<Prop> Props = new List<Prop>();

    public static RipScene Load(string sceneName, string file)
    {
        var scene = new RipScene { SceneName = sceneName };
        scene.Parse(file);
        scene.FindProps();
        return scene;
    }

    private void Parse(string file)
    {
        var nodes = new Dictionary<long, Node>();
        var transforms = new List<KeyValuePair<long, Node>>();
        Node Get(long id)
        {
            Node node;
            if (!nodes.TryGetValue(id, out node))
                nodes[id] = node = new Node { Id = id };
            return node;
        }

        string type = null;
        Node current = null;
        ColliderInfo collider = null;
        bool inMaterials = false;
        string characterScript = CharacterScriptGuid(file);

        foreach (string line in File.ReadLines(file))
        {
            if (line.StartsWith("--- !u!"))
            {
                int space = line.IndexOf(' ', 7);
                string classId = space > 0 ? line.Substring(7, space - 7) : line.Substring(7);
                bool isCollider = classId == "65" || classId == "136" || classId == "135" || classId == "64";
                bool wanted = isCollider || classId == "1" || classId == "4" || classId == "224" || classId == "33" || classId == "23" || classId == "205" || classId == "114";
                type = wanted ? classId : null;
                current = null;
                collider = isCollider ? new ColliderInfo { ClassId = classId } : null;
                inMaterials = false;
                if (classId == "1")
                {
                    int amp = line.IndexOf('&');
                    current = Get(long.Parse(line.Substring(amp + 1).Split(' ')[0]));
                }
                continue;
            }
            if (type == null)
                continue;

            if (line.StartsWith("  m_GameObject:"))
            {
                current = Get(FileId(line));
                if (type == "205")
                    current.HasLodGroup = true;
                else if (type == "23")
                    current.HasRenderer = true;
                if (collider != null)
                    current.Colliders.Add(collider);
                continue;
            }
            if (current == null)
                continue;

            if (collider != null)
            {
                ReadColliderLine(line, collider);
                continue;
            }

            switch (type)
            {
                case "114":
                    if (characterScript != null && line.StartsWith("  m_Script:") && line.Contains(characterScript))
                        current.IsCharacter = true;
                    break;
                case "1":
                    if (line.StartsWith("  m_Name:"))
                        current.Name = YamlString(line.Substring(9));
                    else if (line.StartsWith("  m_IsActive:"))
                        current.Active = line.EndsWith("1");
                    break;
                case "4":
                case "224":
                    if (line.StartsWith("  m_LocalRotation:"))
                    {
                        float[] v = Numbers(line);
                        current.Rotation = new Quaternion(v[0], v[1], v[2], v[3]);
                    }
                    else if (line.StartsWith("  m_LocalPosition:"))
                    {
                        float[] v = Numbers(line);
                        current.Position = new Vector3(v[0], v[1], v[2]);
                    }
                    else if (line.StartsWith("  m_LocalScale:"))
                    {
                        float[] v = Numbers(line);
                        current.Scale = new Vector3(v[0], v[1], v[2]);
                    }
                    else if (line.StartsWith("  m_Father:"))
                        current.Parent = FileId(line);
                    else if (line.StartsWith("  m_RootOrder:"))
                        current.RootOrder = int.Parse(line.Substring(14).Trim());
                    break;
                case "33":
                    if (line.StartsWith("  m_Mesh:"))
                    {
                        Match guid = s_guid.Match(line);
                        if (guid.Success)
                        {
                            current.MeshGuid = guid.Groups[1].Value;
                            current.MeshFileId = FileId(line);
                        }
                    }
                    break;
                case "23":
                    if (line.StartsWith("  m_Enabled:"))
                        current.RendererEnabled = line.EndsWith("1");
                    else if (line.StartsWith("  m_Materials:"))
                        inMaterials = true;
                    else if (inMaterials && line.StartsWith("  - "))
                    {
                        Match guid = s_guid.Match(line);
                        if (guid.Success)
                            current.MaterialGuids.Add(guid.Groups[1].Value);
                    }
                    else if (inMaterials)
                        inMaterials = false;

                    if (line.StartsWith("    firstSubMesh:"))
                        current.FirstSubMesh = int.Parse(line.Substring(17).Trim());
                    else if (line.StartsWith("    subMeshCount:"))
                        current.SubMeshCount = int.Parse(line.Substring(17).Trim());
                    break;
            }
        }

        // m_Father names a Transform but nodes are keyed by GameObject, so transforms need a second pass.
        ResolveHierarchy(file, nodes);
    }

    // Rips include the game's scripts, and a script's GUID is how a scene says what's attached to an object.
    private static string CharacterScriptGuid(string sceneFile)
    {
        string assets = Path.GetDirectoryName(Path.GetDirectoryName(sceneFile));
        string meta = Path.Combine(assets, "Scripts", "Assembly-CSharp", "Character.cs.meta");
        if (!File.Exists(meta))
            return null;
        return File.ReadLines(meta).Skip(1).First().Replace("guid:", "").Trim();
    }

    private static void ReadColliderLine(string line, ColliderInfo collider)
    {
        if (line.StartsWith("  m_IsTrigger:"))
            collider.IsTrigger = line.EndsWith("1");
        else if (line.StartsWith("  m_Center:"))
        {
            float[] v = Numbers(line);
            collider.Center = new Vector3(v[0], v[1], v[2]);
        }
        else if (line.StartsWith("  m_Size:"))
        {
            float[] v = Numbers(line);
            collider.Size = new Vector3(v[0], v[1], v[2]);
        }
        else if (line.StartsWith("  m_Radius:"))
            collider.Radius = float.Parse(line.Substring(11).Trim(), CultureInfo.InvariantCulture);
        else if (line.StartsWith("  m_Height:"))
            collider.Height = float.Parse(line.Substring(11).Trim(), CultureInfo.InvariantCulture);
        else if (line.StartsWith("  m_Direction:"))
            collider.Direction = int.Parse(line.Substring(14).Trim());
        else if (line.StartsWith("  m_Convex:"))
            collider.Convex = line.EndsWith("1");
        else if (line.StartsWith("  m_Mesh:"))
        {
            Match guid = s_guid.Match(line);
            if (guid.Success)
                collider.MeshGuid = guid.Groups[1].Value;
        }
    }

    private void ResolveHierarchy(string file, Dictionary<long, Node> nodes)
    {
        var transformToNode = new Dictionary<long, Node>();
        long transformId = 0;
        bool inTransform = false;
        foreach (string line in File.ReadLines(file))
        {
            if (line.StartsWith("--- !u!"))
            {
                inTransform = line.StartsWith("--- !u!4 ") || line.StartsWith("--- !u!224 ");
                if (inTransform)
                    transformId = long.Parse(line.Substring(line.IndexOf('&') + 1).Split(' ')[0]);
            }
            else if (inTransform && line.StartsWith("  m_GameObject:"))
            {
                Node node;
                if (nodes.TryGetValue(FileId(line), out node))
                    transformToNode[transformId] = node;
                inTransform = false;
            }
        }

        foreach (Node node in nodes.Values)
        {
            Node parent;
            if (node.Parent != 0 && transformToNode.TryGetValue(node.Parent, out parent))
            {
                node.ParentNode = parent;
                parent.Children.Add(node);
            }
            else if (node.Parent == 0 && !string.IsNullOrEmpty(node.Name))
                Roots.Add(node);
        }
        foreach (Node node in nodes.Values)
            node.Children.Sort((a, b) => a.RootOrder.CompareTo(b.RootOrder));
        Roots.Sort((a, b) => a.RootOrder.CompareTo(b.RootOrder));
    }

    private void FindProps()
    {
        var byName = new Dictionary<string, Prop>();
        foreach (Node root in Roots.Where(r => !s_skipRoots.Contains(r.Name)))
            foreach (Node child in root.Children)
                Walk(child, byName);
        FindEnemies(byName);
        Props = byName.Values.OrderBy(p => p.BaseName).ToList();
    }

    // Anything with the game's Character script. Scenes tend to group them, so foes and friends are told apart by that.
    private void FindEnemies(Dictionary<string, Prop> byName)
    {
        foreach (Node root in Roots)
            foreach (Node node in root.SelfAndDescendants().Where(n => n.IsCharacter))
            {
                string key = "Enemy " + s_instanceSuffix.Replace(node.Name, "");
                Prop prop;
                if (byName.TryGetValue(key, out prop))
                {
                    prop.InstanceCount++;
                    continue;
                }
                // Wild enemies sit in squads, whose paths and names don't say "enemy".
                bool foe = node.ScenePath.IndexOf("Enem", StringComparison.OrdinalIgnoreCase) >= 0 || node.ScenePath.IndexOf("Ennem", StringComparison.OrdinalIgnoreCase) >= 0 || node.ScenePath.IndexOf("AISquad", StringComparison.OrdinalIgnoreCase) >= 0;
                byName[key] = new Prop { Node = node, BaseName = key, Category = foe ? "Enemies" : "Characters", IsEnemy = true };
            }
    }

    // Trees sit inside a wrapper that holds their collider, but buildings can hold many separate props.
    private static bool IsWrapperOfOneProp(Node node)
    {
        return node.Name.StartsWith("mdl_") && node.Children.Count(c =>
            c.Name.IndexOf("collider", StringComparison.OrdinalIgnoreCase) < 0 && (c.HasLodGroup || c.HasMesh)) == 1;
    }

    // A prop is the highest object that is either a LOD group or has a mesh of its own.
    private void Walk(Node node, Dictionary<string, Prop> byName)
    {
        if (s_notProps.IsMatch(node.Name))
            return;
        if (!node.HasLodGroup && !node.HasMesh && !IsWrapperOfOneProp(node))
        {
            foreach (Node child in node.Children)
                Walk(child, byName);
            return;
        }

        List<Node> drawn = node.SelfAndDescendants().Where(n => n.HasMesh && n.HasRenderer).ToList();
        if (drawn.Count == 0)
            return;

        string baseName = s_instanceSuffix.Replace(node.Name, "");
        Prop prop;
        if (byName.TryGetValue(baseName, out prop))
        {
            prop.InstanceCount++;
            return;
        }
        string[] parts = node.ScenePath.Split('/');
        byName[baseName] = new Prop
        {
            Node = node,
            BaseName = baseName,
            Category = parts.Length > 2 ? parts[parts.Length - 2] : parts[0],
            Batched = drawn.Any(n => n.SubMeshCount > 0),
        };
    }

    /// <summary>"first drawn object's name:number of drawn objects". The plugin checks this against the real prop.</summary>
    public static string CheckFor(Node source)
    {
        List<Node> drawn = source.SelfAndDescendants().Where(n => n.HasMesh && n.HasRenderer).ToList();
        return (drawn.Count > 0 ? drawn[0].Name : "") + ":" + drawn.Count;
    }

    private static long FileId(string line)
    {
        return long.Parse(s_fileId.Match(line).Groups[1].Value);
    }

    private static float[] Numbers(string line)
    {
        return s_numbers.Matches(line.Substring(line.IndexOf('{'))).Cast<Match>()
            .Select(m => float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
    }

    private static string YamlString(string raw)
    {
        string text = raw.Trim();
        if (text.Length >= 2 && text[0] == '\'' && text[text.Length - 1] == '\'')
            return text.Substring(1, text.Length - 2).Replace("''", "'");
        if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            return Regex.Unescape(text.Substring(1, text.Length - 2));
        return text;
    }
}
