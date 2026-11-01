using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

// Builds a bundle holding one prop, straight from the game's files, with no copy of anything stored in other files.
// args: <game data folder> <level file> <template bundle> <hierarchy path> <output bundle>
string data = args[0];
string levelName = args[1];
string templatePath = args[2];
string targetPath = args[3];
string outputPath = args[4];

var am = new AssetsManager();
var bundle = am.LoadBundleFile(templatePath, true);
var entry = bundle.file.BlockAndDirInfo.DirectoryInfos.First(d => !d.Name.EndsWith(".resS") && !d.Name.EndsWith(".resource"));
var outInst = am.LoadAssetsFileFromBundle(bundle, entry.Name, false);
var outFile = outInst.file;
Console.WriteLine($"Template {entry.Name}: {outFile.AssetInfos.Count} objects, externals [{string.Join(", ", outFile.Metadata.Externals.Select(e => e.PathName))}]");
var types = outFile.Metadata.TypeTreeTypes.ToDictionary(t => t.TypeId);
Console.WriteLine("Types: " + string.Join(", ", types.Keys.OrderBy(k => k)));

var level = am.LoadAssetsFile(Path.Combine(data, levelName), false);
var file = level.file;
var byId = file.AssetInfos.ToDictionary(i => i.PathId);

AssetTypeValueField Read(AssetFileInfo info, AssetsFile from)
{
    var template = new AssetTypeTemplateField();
    template.FromTypeTree(types[info.TypeId]);
    return template.MakeValue(from.Reader, info.GetAbsoluteByteOffset(from));
}

var names = new Dictionary<long, string>();
var transformOf = new Dictionary<long, long>();
var fatherOf = new Dictionary<long, long>();
var childrenOf = new Dictionary<long, List<long>>();
var goOf = new Dictionary<long, long>();
foreach (var info in file.AssetInfos.Where(i => i.TypeId == 1))
    names[info.PathId] = Read(info, file)["m_Name"].AsString;
foreach (var info in file.AssetInfos.Where(i => i.TypeId == 4))
{
    var f = Read(info, file);
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

// World transform of the prop, because static-batched vertices are already in world space.
Vector3 pos = Vector3.Zero;
Quaternion rot = Quaternion.Identity;
Vector3 scale = Vector3.One;
var chain = new List<long>();
for (long t = rootTransform; t != 0; t = fatherOf[t])
    chain.Insert(0, t);
foreach (long t in chain)
{
    var f = Read(byId[t], file);
    var lp = new Vector3(f["m_LocalPosition.x"].AsFloat, f["m_LocalPosition.y"].AsFloat, f["m_LocalPosition.z"].AsFloat);
    var lr = new Quaternion(f["m_LocalRotation.x"].AsFloat, f["m_LocalRotation.y"].AsFloat, f["m_LocalRotation.z"].AsFloat, f["m_LocalRotation.w"].AsFloat);
    var ls = new Vector3(f["m_LocalScale.x"].AsFloat, f["m_LocalScale.y"].AsFloat, f["m_LocalScale.z"].AsFloat);
    pos += Vector3.Transform(lp * scale, rot);
    rot *= lr;
    scale *= ls;
}
Console.WriteLine($"Prop world position {pos}, rotation {rot}, scale {scale}");

// Everything local the prop reaches: its hierarchy and the meshes they use. Anything else local becomes null.
var wanted = new HashSet<int> { 1, 4, 33, 23, 205, 65, 135, 136, 64, 54, 43 };
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
    foreach (var c in Read(byId[goOf[t]], file)["m_Component.Array"].Children)
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

while (queue.Count > 0)
{
    long id = queue.Dequeue();
    var info = byId[id];
    if (!types.ContainsKey(info.TypeId))
        throw new Exception($"Object {id} is class {info.TypeId}, which the template has no type for.");
    if (info.TypeId == 43)
        continue; // no pointers worth following
    VisitPointers(Read(info, file), p =>
    {
        long target = p["m_PathID"].AsLong;
        if (p["m_FileID"].AsInt == 0 && target != 0 && byId.TryGetValue(target, out var hit) && wanted.Contains(hit.TypeId) && hit.TypeId == 43)
            Enqueue(target);
    });
}
Console.WriteLine($"Keeping {keep.Count} objects: " + string.Join(", ", keep.GroupBy(k => byId[k].TypeId).Select(g => "class " + g.Key + " x" + g.Count())));

int ExternalIndex(int sourceFileId)
{
    var source = file.Metadata.Externals[sourceFileId - 1];
    int found = outFile.Metadata.Externals.FindIndex(e => e.PathName == source.PathName);
    if (found < 0)
    {
        outFile.Metadata.Externals.Add(source);
        found = outFile.Metadata.Externals.Count - 1;
    }
    return found + 1;
}

// Clear the template's own objects, keeping its AssetBundle object to fill in.
AssetFileInfo bundleObject = outFile.AssetInfos.First(i => i.TypeId == 142);
foreach (var info in outFile.AssetInfos.Where(i => i.TypeId != 142).ToList())
    outFile.Metadata.RemoveAssetInfo(info);
if (keep.Contains(bundleObject.PathId))
    throw new Exception("Path ID clash with the AssetBundle object.");

var preload = new List<(int fileId, long pathId)>();
long totalBytes = 0;
foreach (long id in keep.OrderBy(i => i))
{
    var info = byId[id];
    var field = Read(info, file);
    int sizeCheck = field.WriteToByteArray().Length;
    if (sizeCheck != (int)info.ByteSize)
        Console.WriteLine($"  NOTE class {info.TypeId} object {id} re-serialised as {sizeCheck} bytes, was {info.ByteSize}");

    if (info.TypeId == 43)
    {
        string streamPath = field["m_StreamData.path"].AsString;
        Console.WriteLine($"  Mesh {id} '{field["m_Name"].AsString}': {info.ByteSize / 1024} KB, {field["m_VertexData.m_VertexCount"].AsUInt} vertices, stream '{streamPath}' {field["m_StreamData.size"].AsUInt} bytes, submeshes {field["m_SubMeshes.Array"].Children.Count}");
        if (!string.IsNullOrEmpty(streamPath))
        {
            // Bring streamed vertices inline, since the stream's file belongs to the scene.
            string res = Path.Combine(data, Path.GetFileName(streamPath));
            var bytes = new byte[field["m_StreamData.size"].AsUInt];
            using (var stream = File.OpenRead(res))
            {
                stream.Position = (long)field["m_StreamData.offset"].AsULong;
                stream.ReadExactly(bytes);
            }
            field["m_VertexData.m_DataSize"].AsByteArray = bytes;
            field["m_StreamData.path"].AsString = "";
            field["m_StreamData.offset"].AsULong = 0;
            field["m_StreamData.size"].AsUInt = 0;
        }
    }
    else
    {
        VisitPointers(field, p =>
        {
            int fileId = p["m_FileID"].AsInt;
            long target = p["m_PathID"].AsLong;
            if (fileId == 0)
            {
                if (target != 0 && !keep.Contains(target))
                {
                    Console.WriteLine($"  Nulling a local pointer to class {(byId.ContainsKey(target) ? byId[target].TypeId : -1)} on class {info.TypeId} object {id}");
                    p["m_PathID"].AsLong = 0;
                }
            }
            else
            {
                int mapped = ExternalIndex(fileId);
                p["m_FileID"].AsInt = mapped;
                if (!preload.Contains((mapped, target)))
                    preload.Add((mapped, target));
                Console.WriteLine($"  Reference on class {info.TypeId}: {outFile.Metadata.Externals[mapped - 1].PathName}:{target}");
            }
        });
    }

    if (id == rootTransform)
    {
        field["m_Father.m_FileID"].AsInt = 0;
        field["m_Father.m_PathID"].AsLong = 0;
        field["m_LocalPosition.x"].AsFloat = pos.X;
        field["m_LocalPosition.y"].AsFloat = pos.Y;
        field["m_LocalPosition.z"].AsFloat = pos.Z;
        field["m_LocalRotation.x"].AsFloat = rot.X;
        field["m_LocalRotation.y"].AsFloat = rot.Y;
        field["m_LocalRotation.z"].AsFloat = rot.Z;
        field["m_LocalRotation.w"].AsFloat = rot.W;
        field["m_LocalScale.x"].AsFloat = scale.X;
        field["m_LocalScale.y"].AsFloat = scale.Y;
        field["m_LocalScale.z"].AsFloat = scale.Z;
    }

    byte[] written = field.WriteToByteArray();
    totalBytes += written.Length;
    var added = AssetFileInfo.Create(outFile, id, info.TypeId, (ClassDatabaseFile)null);
    added.SetNewData(written);
    outFile.Metadata.AddAssetInfo(added);
    preload.Add((0, id));
}

// The AssetBundle object: one entry, naming the prop, which needs all of the above loaded.
var ab = Read(bundleObject, outFile);
ab["m_Name"].AsString = "ock_extract_test";
ab["m_AssetBundleName"].AsString = "ock_extract_test";
var preloadArray = ab["m_PreloadTable.Array"];
preloadArray.Children.Clear();
foreach (var (fileId, pathId) in preload)
{
    var item = ValueBuilder.DefaultValueFieldFromArrayTemplate(preloadArray);
    item["m_FileID"].AsInt = fileId;
    item["m_PathID"].AsLong = pathId;
    preloadArray.Children.Add(item);
}
var container = ab["m_Container.Array"];
var slot = container.Children[0];
container.Children.Clear();
slot["first"].AsString = "assets/ock/extracted.prefab";
slot["second.preloadIndex"].AsInt = 0;
slot["second.preloadSize"].AsInt = preload.Count;
slot["second.asset.m_FileID"].AsInt = 0;
slot["second.asset.m_PathID"].AsLong = rootGo;
container.Children.Add(slot);
bundleObject.SetNewData(ab);

entry.SetNewData(outFile);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
using (var writer = new AssetsFileWriter(outputPath))
    bundle.file.Write(writer);
Console.WriteLine($"Wrote {outputPath}: {new FileInfo(outputPath).Length / 1024} KB, {keep.Count} objects, {totalBytes / 1024} KB of them, externals [{string.Join(", ", outFile.Metadata.Externals.Select(e => e.PathName))}].");

// Read it back, to prove it at least parses.
var checkManager = new AssetsManager();
var check = checkManager.LoadBundleFile(outputPath, true);
var checkFile = checkManager.LoadAssetsFileFromBundle(check, entry.Name, false);
Console.WriteLine($"Read back: {checkFile.file.AssetInfos.Count} objects, types [{string.Join(", ", checkFile.file.AssetInfos.GroupBy(i => i.TypeId).Select(g => g.Key + "x" + g.Count()))}]");
