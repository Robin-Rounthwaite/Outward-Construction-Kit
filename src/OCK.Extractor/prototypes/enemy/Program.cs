using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

// Builds a bundle holding one enemy by copying its objects byte for byte. The bundle mirrors the level's list of
// external files and its table of types, so every pointer in those bytes, scripts included, keeps its meaning.
// Pointers to objects left behind in the level simply find nothing, and read as null.
// args: <game data folder> <level file> <template bundle> <hierarchy path> <output bundle>
string data = args[0];
string levelName = args[1];
string templatePath = args[2];
string targetPath = args[3];
string outputPath = args[4];

var am = new AssetsManager();
var bundle = am.LoadBundleFile(templatePath, true);
var entry = bundle.file.BlockAndDirInfo.DirectoryInfos.First(d => !d.Name.EndsWith(".resS") && !d.Name.EndsWith(".resource"));
var outFile = am.LoadAssetsFileFromBundle(bundle, entry.Name, false).file;
var types = outFile.Metadata.TypeTreeTypes.GroupBy(t => t.TypeId).ToDictionary(g => g.Key, g => g.First());

var level = am.LoadAssetsFile(Path.Combine(data, levelName), false);
var file = level.file;
var byId = file.AssetInfos.ToDictionary(i => i.PathId);

AssetTypeValueField Read(AssetFileInfo info)
{
    var template = new AssetTypeTemplateField();
    template.FromTypeTree(types[info.TypeId]);
    return template.MakeValue(file.Reader, info.GetAbsoluteByteOffset(file));
}

var names = new Dictionary<long, string>();
var transformOf = new Dictionary<long, long>();
var fatherOf = new Dictionary<long, long>();
var childrenOf = new Dictionary<long, List<long>>();
var goOf = new Dictionary<long, long>();
foreach (var info in file.AssetInfos.Where(i => i.TypeId == 1))
    names[info.PathId] = Read(info)["m_Name"].AsString;
foreach (var info in file.AssetInfos.Where(i => i.TypeId == 4))
{
    var f = Read(info);
    long go = f["m_GameObject.m_PathID"].AsLong;
    transformOf[go] = info.PathId;
    goOf[info.PathId] = go;
    fatherOf[info.PathId] = f["m_Father.m_PathID"].AsLong;
    childrenOf[info.PathId] = f["m_Children.Array"].Children.Select(c => c["m_PathID"].AsLong).ToList();
}

string[] parts = targetPath.Split('/');
long rootTransform = transformOf.First(p => fatherOf[p.Value] == 0 && names.TryGetValue(p.Key, out var n) && n == parts[0]).Value;
foreach (string part in parts.Skip(1))
    rootTransform = childrenOf[rootTransform].First(c => names[goOf[c]] == part);
long rootGo = goOf[rootTransform];

var keep = new HashSet<long>();
var queue = new Queue<long>();
void Enqueue(long id)
{
    if (id != 0 && keep.Add(id))
        queue.Enqueue(id);
}
void AddHierarchy(long t)
{
    Enqueue(t);
    Enqueue(goOf[t]);
    foreach (var c in Read(byId[goOf[t]])["m_Component.Array"].Children)
        Enqueue(c["component.m_PathID"].AsLong);
    foreach (long child in childrenOf[t])
        AddHierarchy(child);
}
AddHierarchy(rootTransform);

void VisitPointers(AssetTypeValueField f, Action<AssetTypeValueField> onPointer)
{
    if (f.TemplateField.Type.StartsWith("PPtr<"))
    {
        onPointer(f);
        return;
    }
    if (f.Children != null)
        foreach (var child in f.Children)
            VisitPointers(child, onPointer);
}

// Built-in components can point at assets stored in the level, such as meshes, which have to come along.
// Scripts aren't read, so anything only a script points at is reported as a risk by its absence.
var hierarchyClasses = new HashSet<int> { 1, 4 };
var leftBehind = new Dictionary<int, int>();
var externalTargets = new Dictionary<string, int>();
while (queue.Count > 0)
{
    var info = byId[queue.Dequeue()];
    if (info.TypeId == 114 || info.TypeId == 43 || !types.ContainsKey(info.TypeId))
        continue;
    VisitPointers(Read(info), p =>
    {
        int fileId = p["m_FileID"].AsInt;
        long target = p["m_PathID"].AsLong;
        if (target == 0)
            return;
        if (fileId != 0)
        {
            string name = file.Metadata.Externals[fileId - 1].PathName;
            externalTargets[name] = externalTargets.GetValueOrDefault(name) + 1;
            return;
        }
        if (keep.Contains(target) || !byId.TryGetValue(target, out var hit))
            return;
        if (hierarchyClasses.Contains(hit.TypeId) || hit.TypeId == 114)
            leftBehind[hit.TypeId] = leftBehind.GetValueOrDefault(hit.TypeId) + 1;
        else
            Enqueue(target);
    });
}
Console.WriteLine($"Keeping {keep.Count} objects: " + string.Join(", ", keep.GroupBy(k => byId[k].TypeId).OrderByDescending(g => g.Count()).Select(g => g.Key + " x" + g.Count())));
Console.WriteLine("Built-in components point into: " + string.Join(", ", externalTargets.Select(e => e.Key + " x" + e.Value)));
Console.WriteLine("Left behind by built-in components: " + (leftBehind.Count == 0 ? "nothing" : string.Join(", ", leftBehind.Select(e => "class " + e.Key + " x" + e.Value))));

// The AssetBundle object: one entry, naming the enemy.
AssetFileInfo bundleObject = outFile.AssetInfos.First(i => i.TypeId == 142);
var abTemplate = new AssetTypeTemplateField();
abTemplate.FromTypeTree(types[142]);
var ab = abTemplate.MakeValue(outFile.Reader, bundleObject.GetAbsoluteByteOffset(outFile));
ab["m_Name"].AsString = "ock_extract_enemy";
ab["m_AssetBundleName"].AsString = "ock_extract_enemy";
var preloadArray = ab["m_PreloadTable.Array"];
preloadArray.Children.Clear();
foreach (long id in keep.OrderBy(i => i))
{
    var item = ValueBuilder.DefaultValueFieldFromArrayTemplate(preloadArray);
    item["m_FileID"].AsInt = 0;
    item["m_PathID"].AsLong = id;
    preloadArray.Children.Add(item);
}
var container = ab["m_Container.Array"];
var slot = container.Children[0];
container.Children.Clear();
slot["first"].AsString = "assets/ock/extracted.prefab";
slot["second.preloadIndex"].AsInt = 0;
slot["second.preloadSize"].AsInt = keep.Count;
slot["second.asset.m_FileID"].AsInt = 0;
slot["second.asset.m_PathID"].AsLong = rootGo;
container.Children.Add(slot);
byte[] abBytes = ab.WriteToByteArray();

string variant = args.Length > 5 ? args[5] : "";
if (variant.Contains("noscripts"))
    foreach (long id in keep.ToList())
        if (byId[id].TypeId == 114)
            keep.Remove(id);
Console.WriteLine($"Variant '{variant}': {keep.Count} objects");

// Mirror the level's external files and scripts. Unity refuses a bundle with no type trees, so each type used keeps
// the level's fingerprints but borrows the template's description, which for a script covers only its base fields.
// Where a fingerprint matches the running game, Unity should read the object directly and not need the description.
foreach (var info in outFile.AssetInfos.ToList())
    outFile.Metadata.RemoveAssetInfo(info);
var meta = outFile.Metadata;
meta.Externals = file.Metadata.Externals;
meta.ScriptTypes = variant.Contains("noscripts") || variant.Contains("noscripttable") ? new List<AssetPPtr>() : file.Metadata.ScriptTypes;
var abType = types[142];
// Unity keeps common names in a shared table, marked by a high bit, so the same offsets are used here.
var commonStrings = new Dictionary<string, uint>();
foreach (var known in types.Values)
    foreach (var node in known.Nodes)
    {
        if ((node.TypeStrOffset & 0x80000000) != 0)
            commonStrings[node.GetTypeString(known.StringBufferBytes, null)] = node.TypeStrOffset;
        if ((node.NameStrOffset & 0x80000000) != 0)
            commonStrings[node.GetNameString(known.StringBufferBytes, null)] = node.NameStrOffset;
    }
var scriptFiles = new Dictionary<string, AssetsFile>();
var generator = new AssetsTools.NET.Extra.MonoCecilTempGenerator(Path.Combine(data, "Managed"));
int scriptObjects = 0;
int scriptMismatches = 0;
var newTypes = new List<TypeTreeType>();
var typeIndex = new Dictionary<int, int>();
foreach (int oldIndex in keep.Select(k => byId[k].TypeIdOrIndex).Distinct().OrderBy(i => i))
{
    var source = file.Metadata.TypeTreeTypes[oldIndex];
    if (!types.TryGetValue(source.TypeId, out var described))
        throw new Exception("No description for class " + source.TypeId);
    typeIndex[oldIndex] = newTypes.Count;
    newTypes.Add(new TypeTreeType
    {
        TypeId = source.TypeId,
        IsStrippedType = source.IsStrippedType,
        ScriptTypeIndex = source.ScriptTypeIndex,
        ScriptIdHash = source.ScriptIdHash,
        TypeHash = source.TypeHash,
        StringBufferBytes = described.StringBufferBytes,
        Nodes = described.Nodes,
        TypeDependencies = new int[0],
    });
    if (source.TypeId == 114)
        DescribeScript(newTypes[newTypes.Count - 1], source, described);
}
newTypes.Add(abType);
int abIndex = newTypes.Count - 1;
meta.TypeTreeTypes = newTypes;
meta.TypeTreeEnabled = true;

// A script's fields come from the game's code. Each object's exact size is a strong check of the result.
void DescribeScript(TypeTreeType type, TypeTreeType source, TypeTreeType baseType)
{
    var pointer = file.Metadata.ScriptTypes[source.ScriptTypeIndex];
    string scriptFileName = file.Metadata.Externals[pointer.FileId - 1].PathName;
    if (!scriptFiles.TryGetValue(scriptFileName, out var scriptFile))
        scriptFiles[scriptFileName] = scriptFile = am.LoadAssetsFile(Path.Combine(data, scriptFileName), false).file;
    var scriptTemplate = new AssetTypeTemplateField();
    scriptTemplate.FromTypeTree(types[115]);
    var scriptInfo = scriptFile.AssetInfos.First(i => i.PathId == pointer.PathId);
    var script = scriptTemplate.MakeValue(scriptFile.Reader, scriptInfo.GetAbsoluteByteOffset(scriptFile));
    string assembly = script["m_AssemblyName"].AsString;
    if (!assembly.EndsWith(".dll"))
        assembly += ".dll";

    var baseTemplate = new AssetTypeTemplateField();
    baseTemplate.FromTypeTree(baseType);
    var full = generator.GetTemplateField(baseTemplate, assembly, script["m_Namespace"].AsString, script["m_ClassName"].AsString, new UnityVersion(file.Metadata.UnityVersion));
    if (full == null)
        throw new Exception("No description for script " + script["m_ClassName"].AsString);

    foreach (var info in keep.Select(k => byId[k]).Where(i => file.Metadata.TypeTreeTypes[i.TypeIdOrIndex] == source))
    {
        var value = full.MakeValue(file.Reader, info.GetAbsoluteByteOffset(file));
        int size = value.WriteToByteArray().Length;
        scriptObjects++;
        if (size != (int)info.ByteSize)
        {
            scriptMismatches++;
            Console.WriteLine($"  MISMATCH {script["m_ClassName"].AsString}: description gives {size} bytes, object has {info.ByteSize}");
        }
    }

    var nodes = new List<TypeTreeNode>();
    var strings = new MemoryStream();
    var localStrings = new Dictionary<string, uint>();
    uint AddString(string text)
    {
        if (commonStrings.TryGetValue(text, out uint common))
            return common;
        if (localStrings.TryGetValue(text, out uint known))
            return known;
        uint offset = (uint)strings.Position;
        localStrings[text] = offset;
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
        strings.Write(bytes, 0, bytes.Length);
        strings.WriteByte(0);
        return offset;
    }
    int Flatten(AssetTypeTemplateField field, byte level)
    {
        var typeInfo = TreeRules.TypeOf(field);
        var node = new TypeTreeNode
        {
            Version = (ushort)typeInfo.version,
            Level = level,
            TypeFlags = (TypeTreeNodeFlags)(field.IsArray ? 1 : 0),
            TypeStrOffset = AddString(typeInfo.type),
            NameStrOffset = AddString(field.Name),
            Index = (uint)nodes.Count,
            MetaFlags = (uint)(TreeRules.Aligned(field) ? 0x4000 : 0) | (level == 0 ? 0x8000u : 0u),
        };
        nodes.Add(node);
        int size = field.ValueType switch
        {
            AssetValueType.Bool or AssetValueType.Int8 or AssetValueType.UInt8 => 1,
            AssetValueType.Int16 or AssetValueType.UInt16 => 2,
            AssetValueType.Int32 or AssetValueType.UInt32 or AssetValueType.Float => 4,
            AssetValueType.Int64 or AssetValueType.UInt64 or AssetValueType.Double => 8,
            _ => -1,
        };
        if (size < 0 && !field.IsArray && field.Type != "string" && field.Children.Count > 0)
        {
            size = 0;
            foreach (var child in field.Children)
            {
                int childSize = Flatten(child, (byte)(level + 1));
                size = size < 0 || childSize < 0 ? -1 : size + childSize;
            }
        }
        else
            foreach (var child in field.Children)
                Flatten(child, (byte)(level + 1));
        node.ByteSize = field.IsArray || field.Type == "string" ? -1 : size;
        return node.ByteSize;
    }
    Flatten(full, 0);
    type.StringBufferBytes = strings.ToArray();
    type.Nodes = nodes;
}
Console.WriteLine($"Script objects checked: {scriptObjects}, size mismatches: {scriptMismatches}");
entry.Name = "CAB-ock-extract-" + Path.GetFileName(outputPath); // two loaded bundles can't share one
Console.WriteLine($"Type entries: {newTypes.Count}, of which script types {newTypes.Count(t => t.TypeId == 114)}");

// Unity looks for a bundle's own record at ID 1, and calls the bundle incompatible if it isn't there.
const long abPathId = 1;
if (keep.Contains(abPathId))
    throw new Exception("Object 1 clashes with the bundle's own record.");
var abInfo = new AssetFileInfo { PathId = abPathId, TypeIdOrIndex = abIndex, OldTypeId = 142, ScriptTypeIndex = 0xFFFF, TypeId = 142 };
abInfo.SetNewData(abBytes);
meta.AddAssetInfo(abInfo);
long total = 0;
foreach (long id in keep.OrderBy(i => i))
{
    var info = byId[id];
    file.Reader.Position = info.GetAbsoluteByteOffset(file);
    byte[] bytes = file.Reader.ReadBytes((int)info.ByteSize);
    if (id == rootTransform)
    {
        var f = Read(info);
        f["m_Father.m_FileID"].AsInt = 0;
        f["m_Father.m_PathID"].AsLong = 0;
        bytes = f.WriteToByteArray();
    }
    total += bytes.Length;
    var added = new AssetFileInfo { PathId = id, TypeIdOrIndex = typeIndex[info.TypeIdOrIndex], OldTypeId = info.OldTypeId, ScriptTypeIndex = info.ScriptTypeIndex, Stripped = info.Stripped, TypeId = info.TypeId };
    added.SetNewData(bytes);
    meta.AddAssetInfo(added);
}

entry.SetNewData(outFile);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
using (var writer = new AssetsFileWriter(outputPath))
    bundle.file.Write(writer);
Console.WriteLine($"Wrote {outputPath}: {new FileInfo(outputPath).Length / 1024} KB, {total / 1024} KB of objects, {meta.Externals.Count} external files, {meta.TypeTreeTypes.Count} type entries, {meta.ScriptTypes.Count} script entries.");

// Read it back, to prove it at least parses.
var checkManager = new AssetsManager();
var check = checkManager.LoadBundleFile(outputPath, true);
var checkFile = checkManager.LoadAssetsFileFromBundle(check, entry.Name, false);
Console.WriteLine($"Read back: {checkFile.file.AssetInfos.Count} objects, type tree {checkFile.file.Metadata.TypeTreeEnabled}, classes [{string.Join(", ", checkFile.file.AssetInfos.GroupBy(i => i.TypeId).OrderByDescending(g => g.Count()).Select(g => g.Key + "x" + g.Count()))}]");
