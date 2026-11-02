using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace OutwardConstructionKit.Extractor
{
    /// <summary>
    /// Builds a bundle for one object in a level, straight from the game's files, with no scene loaded. Its objects
    /// are copied byte for byte, and the bundle mirrors the level's lists of external files and scripts, so every
    /// pointer in those bytes, scripts included, keeps its meaning. Art in other files stays a pointer into the
    /// player's own copy of the game. Pointers to objects left behind in the level find nothing, and read as null.
    /// </summary>
    public class BundleBuilder
    {
        private const string TEMPLATE_RESOURCE = "ock_template";
        public const string PREFAB_PATH = "assets/ock/extracted.prefab";
        public const string POINTER_PATH = "assets/ock/extracted.pointer";

        private const int GAME_OBJECT = 1;
        private const int TRANSFORM = 4;
        private const int TEXTURE = 28;
        private const int SHADER = 48;
        private const int WIND_ZONE = 182;
        private const int MESH = 43;
        private const int SCRIPT = 114;
        private const int MESH_FILTER = 33;
        private const int ASSET_BUNDLE = 142;
        private const int CHARACTER_CONTROLLER = 143;
        private const int LOD_GROUP = 205;
        private const long BUNDLE_RECORD_ID = 1; // Unity calls a bundle incompatible if its own record is anywhere else

        private readonly string m_gameData;
        private readonly AssetsManager m_manager = new AssetsManager();
        private readonly byte[] m_template;
        private readonly Dictionary<int, TypeTreeType> m_types;
        private readonly ScriptDescriber m_describer;
        private readonly MonoCecilTempGenerator m_scriptReader;
        private readonly Dictionary<string, LevelIndex> m_levels = new Dictionary<string, LevelIndex>();
        private readonly Dictionary<string, AssetsFile> m_scriptFiles = new Dictionary<string, AssetsFile>();
        private readonly Dictionary<string, AssetTypeTemplateField> m_scriptFields = new Dictionary<string, AssetTypeTemplateField>();
        private HashSet<long> m_onlyChildren; // for the build in progress: the root's children, if not all of them

        private class LevelIndex
        {
            public AssetsFile File;
            public Dictionary<long, AssetFileInfo> ById;
            public Dictionary<long, string> Names = new Dictionary<long, string>();
            public Dictionary<long, long> GameObjectOf = new Dictionary<long, long>();
            public Dictionary<long, long> FatherOf = new Dictionary<long, long>();
            public Dictionary<long, List<long>> ChildrenOf = new Dictionary<long, List<long>>();
            public List<long> RootTransforms = new List<long>();
            public Dictionary<ushort, string> ScriptClasses = new Dictionary<ushort, string>();
        }

        public BundleBuilder(string gameData)
        {
            m_gameData = gameData;
            using (Stream resource = typeof(BundleBuilder).Assembly.GetManifestResourceStream(TEMPLATE_RESOURCE))
            using (var copy = new MemoryStream())
            {
                resource.CopyTo(copy);
                m_template = copy.ToArray();
            }

            AssetsFile template = OpenTemplate(out _, out _);
            m_types = template.Metadata.TypeTreeTypes.GroupBy(t => t.TypeId).ToDictionary(g => g.Key, g => g.First());
            m_describer = new ScriptDescriber(m_types.Values);
            m_scriptReader = new MonoCecilTempGenerator(Path.Combine(gameData, "Managed"));
            DescribeWindZone();
        }

        // Unity writes no wind zones into a bundle it builds, though scenes have them, so the template can't supply this.
        private void DescribeWindZone()
        {
            var fields = new AssetTypeTemplateField();
            fields.FromTypeTree(m_types[SCRIPT]);
            fields.Type = "WindZone";
            fields.Children = fields.Children.Where(c => c.Name == "m_GameObject" || c.Name == "m_Enabled").ToList();
            fields.Children.Add(SimpleField("m_Mode", "int", AssetValueType.Int32));
            foreach (string name in new[] { "m_Radius", "m_WindMain", "m_WindTurbulence", "m_WindPulseMagnitude", "m_WindPulseFrequency" })
                fields.Children.Add(SimpleField(name, "float", AssetValueType.Float));

            var type = new TypeTreeType { TypeId = WIND_ZONE, ScriptTypeIndex = 0xFFFF, TypeDependencies = new int[0] };
            m_describer.Describe(fields, type);
            m_types[WIND_ZONE] = type;
        }

        private static AssetTypeTemplateField SimpleField(string name, string type, AssetValueType valueType)
        {
            return new AssetTypeTemplateField { Name = name, Type = type, ValueType = valueType, HasValue = true, Children = new List<AssetTypeTemplateField>() };
        }

        private AssetsFile OpenTemplate(out BundleFileInstance bundle, out AssetBundleDirectoryInfo entry)
        {
            var manager = new AssetsManager();
            bundle = manager.LoadBundleFile(new MemoryStream(m_template), "ock_template", true);
            entry = bundle.file.BlockAndDirInfo.DirectoryInfos.First(d => !d.Name.EndsWith(".resS") && !d.Name.EndsWith(".resource"));
            return manager.LoadAssetsFileFromBundle(bundle, entry.Name, false).file;
        }

        private AssetTypeValueField Read(AssetFileInfo info, AssetsFile file)
        {
            var template = new AssetTypeTemplateField();
            template.FromTypeTree(m_types[info.TypeId]);
            return template.MakeValue(file.Reader, info.GetAbsoluteByteOffset(file));
        }

        private LevelIndex LevelFor(string levelFile)
        {
            LevelIndex level;
            if (m_levels.TryGetValue(levelFile, out level))
                return level;

            AssetsFile file = m_manager.LoadAssetsFile(Path.Combine(m_gameData, levelFile), false).file;
            level = new LevelIndex { File = file, ById = file.AssetInfos.ToDictionary(i => i.PathId) };
            foreach (AssetFileInfo info in file.AssetInfos.Where(i => i.TypeId == GAME_OBJECT))
                level.Names[info.PathId] = Read(info, file)["m_Name"].AsString;
            foreach (AssetFileInfo info in file.AssetInfos.Where(i => i.TypeId == TRANSFORM))
            {
                AssetTypeValueField transform = Read(info, file);
                long father = transform["m_Father.m_PathID"].AsLong;
                level.GameObjectOf[info.PathId] = transform["m_GameObject.m_PathID"].AsLong;
                level.FatherOf[info.PathId] = father;
                level.ChildrenOf[info.PathId] = transform["m_Children.Array"].Children.Select(c => c["m_PathID"].AsLong).ToList();
                if (father == 0)
                    level.RootTransforms.Add(info.PathId);
            }
            m_levels[levelFile] = level;
            return level;
        }

        private static long FindTransform(LevelIndex level, string path)
        {
            string[] parts = path.Split('/');
            long current = level.RootTransforms.FirstOrDefault(t => NameOf(level, t) == parts[0]);
            for (int i = 1; i < parts.Length && current != 0; i++)
                current = level.ChildrenOf[current].FirstOrDefault(c => NameOf(level, c) == parts[i]);
            return current;
        }

        private static string NameOf(LevelIndex level, long transform)
        {
            string name;
            return level.Names.TryGetValue(level.GameObjectOf[transform], out name) ? name : "";
        }

        /// <summary>Writes the bundle. Throws, leaving nothing behind, if the object can't be carried across.</summary>
        public BuildReport Build(string levelFile, string hierarchyPath, string outputFile)
        {
            LevelIndex level = LevelFor(levelFile);
            long root = FindTransform(level, hierarchyPath);
            if (root == 0)
                throw new InvalidDataException($"'{hierarchyPath}' isn't in {levelFile}. The game may have changed.");
            return BuildFrom(level, root, hierarchyPath, outputFile);
        }

        /// <summary>
        /// Builds the object holding a given script, for things found by what they do rather than where they are.
        /// With several, takes the first, or if asked, the one with the largest scale, which is how a level's main
        /// water plane stands out from its puddles.
        /// </summary>
        public BuildReport BuildScriptHolder(string levelFile, string scriptClass, bool largest, string outputFile)
        {
            LevelIndex level = LevelFor(levelFile);
            List<long> holders = level.File.AssetInfos.Where(i => i.TypeId == SCRIPT && ClassOf(level, i) == scriptClass)
                .Select(i => TransformOfScript(level, i)).Where(t => t != 0).ToList();
            if (holders.Count == 0)
                throw new InvalidDataException($"{levelFile} has nothing holding a {scriptClass}.");
            long chosen = largest ? holders.OrderByDescending(ScaleOf(level)).First() : FirstInSceneOrder(level, holders);
            return BuildFrom(level, chosen, scriptClass, outputFile);
        }

        // The order they're stored in means nothing, and a level's first source is its general-purpose one.
        private static long FirstInSceneOrder(LevelIndex level, List<long> candidates)
        {
            var wanted = new HashSet<long>(candidates);
            Func<long, long> search = null;
            search = t =>
            {
                if (wanted.Contains(t))
                    return t;
                foreach (long child in level.ChildrenOf[t])
                {
                    long found = search(child);
                    if (found != 0)
                        return found;
                }
                return 0;
            };
            return level.RootTransforms.Select(search).FirstOrDefault(t => t != 0);
        }

        private System.Func<long, float> ScaleOf(LevelIndex level)
        {
            return t =>
            {
                AssetTypeValueField f = Read(level.ById[t], level.File);
                bool drawn = Read(level.ById[level.GameObjectOf[t]], level.File)["m_Component.Array"].Children.Any(c => level.ById[c["component.m_PathID"].AsLong].TypeId == MESH_FILTER);
                return drawn ? f["m_LocalScale.x"].AsFloat : 0f;
            };
        }

        private long TransformOfScript(LevelIndex level, AssetFileInfo script)
        {
            long gameObject = Read(script, level.File)["m_GameObject.m_PathID"].AsLong;
            foreach (KeyValuePair<long, long> pair in level.GameObjectOf)
                if (pair.Value == gameObject)
                    return pair.Key;
            return 0;
        }

        private string ClassOf(LevelIndex level, AssetFileInfo script)
        {
            TypeTreeType type = level.File.Metadata.TypeTreeTypes[script.TypeIdOrIndex];
            string name;
            if (level.ScriptClasses.TryGetValue(type.ScriptTypeIndex, out name))
                return name;
            AssetPPtr pointer = level.File.Metadata.ScriptTypes[type.ScriptTypeIndex];
            string fileName = level.File.Metadata.Externals[pointer.FileId - 1].PathName;
            AssetsFile scriptFile = ScriptFileFor(fileName);
            name = Read(scriptFile.AssetInfos.First(i => i.PathId == pointer.PathId), scriptFile)["m_ClassName"].AsString;
            level.ScriptClasses[type.ScriptTypeIndex] = name;
            return name;
        }

        private AssetsFile ScriptFileFor(string fileName)
        {
            AssetsFile file;
            if (!m_scriptFiles.TryGetValue(fileName, out file))
                m_scriptFiles[fileName] = file = m_manager.LoadAssetsFile(Path.Combine(m_gameData, fileName), false).file;
            return file;
        }

        /// <summary>
        /// Builds a bundle holding nothing but a pointer to a texture in the game's files, found by name, for textures a
        /// region uses directly rather than through an object. It looks first among the files the level uses, then in
        /// the rest, because a rip also offers what a level only reaches indirectly.
        /// </summary>
        public BuildReport BuildTexturePointer(string levelFile, string textureName, string outputFile)
        {
            LevelIndex level = LevelFor(levelFile);
            List<AssetsFileExternal> candidates = level.File.Metadata.Externals.Where(e => e.PathName.EndsWith(".assets")).ToList();
            foreach (string path in Directory.GetFiles(m_gameData, "*.assets").OrderBy(p => p))
                if (!candidates.Any(c => c.PathName == Path.GetFileName(path)))
                    candidates.Add(new AssetsFileExternal { PathName = Path.GetFileName(path), OriginalPathName = Path.GetFileName(path), VirtualAssetPathName = "", Type = AssetsFileExternalType.Normal });

            AssetsFileExternal external = null;
            long pathId = 0;
            foreach (AssetsFileExternal candidate in candidates)
            {
                if (candidate.PathName == "globalgamemanagers.assets" || !File.Exists(Path.Combine(m_gameData, candidate.PathName)))
                    continue;
                AssetsFile file = ScriptFileFor(candidate.PathName);
                foreach (AssetFileInfo info in file.AssetInfos.Where(i => i.TypeId == TEXTURE))
                {
                    // Every named object starts with its name, as a length and then the text.
                    file.Reader.Position = info.GetAbsoluteByteOffset(file);
                    int length = file.Reader.ReadInt32();
                    if (length != textureName.Length)
                        continue;
                    if (System.Text.Encoding.UTF8.GetString(file.Reader.ReadBytes(length)) == textureName)
                    {
                        external = candidate;
                        pathId = info.PathId;
                        break;
                    }
                }
                if (external != null)
                    break;
            }
            if (external == null)
                throw new InvalidDataException($"No texture called {textureName} in the game's files.");
            return BuildPointer(external, pathId, outputFile);
        }

        /// <summary>
        /// The same for a shader, which any level might use, so all the game's files are searched. It has to read
        /// every shader until it finds the one, so this is only for one that isn't otherwise in memory.
        /// </summary>
        public BuildReport BuildShaderPointer(string shaderName, string outputFile)
        {
            foreach (string path in Directory.GetFiles(m_gameData, "*.assets").OrderBy(p => p))
            {
                string fileName = Path.GetFileName(path);
                AssetsFile file = ScriptFileFor(fileName);
                foreach (AssetFileInfo info in file.AssetInfos.Where(i => i.TypeId == SHADER))
                {
                    if (Read(info, file)["m_ParsedForm.m_Name"].AsString != shaderName)
                        continue;
                    var external = new AssetsFileExternal { PathName = fileName, OriginalPathName = fileName, VirtualAssetPathName = "", Type = AssetsFileExternalType.Normal };
                    return BuildPointer(external, info.PathId, outputFile);
                }
            }
            throw new InvalidDataException($"No shader called {shaderName} in the game's files.");
        }

        private BuildReport BuildPointer(AssetsFileExternal external, long pathId, string outputFile)
        {
            BundleFileInstance bundle;
            AssetBundleDirectoryInfo entry;
            AssetsFile output = OpenTemplate(out bundle, out entry);
            AssetFileInfo existing = output.AssetInfos.First(i => i.TypeId == ASSET_BUNDLE);
            AssetTypeValueField record = Read(existing, output);
            string name = Path.GetFileName(outputFile);
            record["m_Name"].AsString = name;
            record["m_AssetBundleName"].AsString = name;
            AssetTypeValueField preload = record["m_PreloadTable.Array"];
            preload.Children.Clear();
            AssetTypeValueField item = ValueBuilder.DefaultValueFieldFromArrayTemplate(preload);
            item["m_FileID"].AsInt = 1;
            item["m_PathID"].AsLong = pathId;
            preload.Children.Add(item);
            AssetTypeValueField container = record["m_Container.Array"];
            AssetTypeValueField slot = container.Children[0];
            container.Children.Clear();
            slot["first"].AsString = POINTER_PATH;
            slot["second.preloadIndex"].AsInt = 0;
            slot["second.preloadSize"].AsInt = 1;
            slot["second.asset.m_FileID"].AsInt = 1;
            slot["second.asset.m_PathID"].AsLong = pathId;
            container.Children.Add(slot);
            byte[] bytes = record.WriteToByteArray();

            foreach (AssetFileInfo info in output.AssetInfos.ToList())
                output.Metadata.RemoveAssetInfo(info);
            output.Metadata.Externals = new List<AssetsFileExternal> { external };
            output.Metadata.ScriptTypes = new List<AssetPPtr>();
            output.Metadata.TypeTreeTypes = new List<TypeTreeType> { m_types[ASSET_BUNDLE] };
            var added = new AssetFileInfo { PathId = BUNDLE_RECORD_ID, TypeIdOrIndex = 0, OldTypeId = ASSET_BUNDLE, ScriptTypeIndex = 0xFFFF, TypeId = ASSET_BUNDLE };
            added.SetNewData(bytes);
            output.Metadata.AddAssetInfo(added);
            return Write(bundle, entry, output, outputFile, new BuildReport { Objects = 1 });
        }

        private static BuildReport Write(BundleFileInstance bundle, AssetBundleDirectoryInfo entry, AssetsFile output, string outputFile, BuildReport report)
        {
            entry.Name = "CAB-ock-" + Path.GetFileName(outputFile); // two loaded bundles can't share one
            entry.SetNewData(output);

            string temporary = outputFile + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile));
            using (var writer = new AssetsFileWriter(temporary))
                bundle.file.Write(writer);
            if (File.Exists(outputFile))
                File.Delete(outputFile);
            File.Move(temporary, outputFile);
            report.Bytes = new FileInfo(outputFile).Length;
            return report;
        }

        /// <summary>
        /// Builds a scene's whole Environment object without the scene: only the named children, plus whatever holds
        /// the named script, which moves up to sit directly under the root. Everything else there is terrain and props.
        /// </summary>
        public BuildReport BuildPruned(string levelFile, string rootName, string[] keepChildren, string keepHolding, string outputFile)
        {
            LevelIndex level = LevelFor(levelFile);
            long root = FindTransform(level, rootName);
            if (root == 0)
                throw new InvalidDataException($"'{rootName}' isn't in {levelFile}. The game may have changed.");

            var children = new HashSet<long>(level.ChildrenOf[root].Where(c => keepChildren.Contains(NameOf(level, c))));
            foreach (AssetFileInfo script in level.File.AssetInfos.Where(i => i.TypeId == SCRIPT && ClassOf(level, i) == keepHolding))
            {
                long holder = TransformOfScript(level, script);
                bool covered = holder == root;
                for (long t = holder; t != 0 && t != root && !covered; t = level.FatherOf[t])
                    covered = children.Contains(t);
                if (holder != 0 && !covered)
                    children.Add(holder);
            }
            return BuildFrom(level, root, rootName, outputFile, children);
        }

        private BuildReport BuildFrom(LevelIndex level, long root, string hierarchyPath, string outputFile, HashSet<long> onlyChildren = null)
        {
            m_onlyChildren = onlyChildren;
            HashSet<long> keep = Gather(level, root);
            string missing = string.Join(", ", keep.Select(k => level.ById[k].TypeId).Distinct().Where(t => !m_types.ContainsKey(t)).OrderBy(t => t));
            if (missing.Length > 0)
                throw new NotSupportedException($"'{hierarchyPath}' uses classes the template can't describe yet: {missing}.");

            BundleFileInstance bundle;
            AssetBundleDirectoryInfo entry;
            AssetsFile output = OpenTemplate(out bundle, out entry);
            byte[] bundleRecord = MakeBundleRecord(output, keep, level.GameObjectOf[root], Path.GetFileName(outputFile));

            foreach (AssetFileInfo info in output.AssetInfos.ToList())
                output.Metadata.RemoveAssetInfo(info);
            AssetsFileMetadata metadata = output.Metadata;
            metadata.Externals = level.File.Metadata.Externals;
            metadata.ScriptTypes = level.File.Metadata.ScriptTypes;
            metadata.TypeTreeEnabled = true;

            var newTypes = new List<TypeTreeType>();
            var typeIndex = new Dictionary<int, int>();
            var report = new BuildReport();
            foreach (int oldIndex in keep.Select(k => level.ById[k].TypeIdOrIndex).Distinct().OrderBy(i => i))
            {
                TypeTreeType source = level.File.Metadata.TypeTreeTypes[oldIndex];
                TypeTreeType described = m_types[source.TypeId];
                typeIndex[oldIndex] = newTypes.Count;
                var type = new TypeTreeType
                {
                    TypeId = source.TypeId,
                    IsStrippedType = source.IsStrippedType,
                    ScriptTypeIndex = source.ScriptTypeIndex,
                    ScriptIdHash = source.ScriptIdHash,
                    TypeHash = source.TypeHash,
                    StringBufferBytes = described.StringBufferBytes,
                    Nodes = described.Nodes,
                    TypeDependencies = new int[0],
                };
                newTypes.Add(type);
                if (source.TypeId == SCRIPT)
                    CheckAndDescribeScript(level, keep, oldIndex, source, type, report);
            }
            newTypes.Add(m_types[ASSET_BUNDLE]);
            metadata.TypeTreeTypes = newTypes;

            if (keep.Contains(BUNDLE_RECORD_ID))
                throw new NotSupportedException($"'{hierarchyPath}' holds object {BUNDLE_RECORD_ID}, which a bundle needs for itself.");
            var record = new AssetFileInfo { PathId = BUNDLE_RECORD_ID, TypeIdOrIndex = newTypes.Count - 1, OldTypeId = ASSET_BUNDLE, ScriptTypeIndex = 0xFFFF, TypeId = ASSET_BUNDLE };
            record.SetNewData(bundleRecord);
            metadata.AddAssetInfo(record);

            foreach (long id in keep.OrderBy(i => i))
            {
                AssetFileInfo info = level.ById[id];
                byte[] bytes = BytesFor(level, info, root, report);
                var added = new AssetFileInfo { PathId = id, TypeIdOrIndex = typeIndex[info.TypeIdOrIndex], OldTypeId = info.OldTypeId, ScriptTypeIndex = info.ScriptTypeIndex, Stripped = info.Stripped, TypeId = info.TypeId };
                added.SetNewData(bytes);
                metadata.AddAssetInfo(added);
                report.Objects++;
            }

            return Write(bundle, entry, output, outputFile, report);
        }

        /// <summary>Reports what a build would meet, without writing anything, to measure what the template covers.</summary>
        public string[] MissingClasses(string levelFile, string hierarchyPath)
        {
            LevelIndex level = LevelFor(levelFile);
            long root = FindTransform(level, hierarchyPath);
            if (root == 0)
                return new[] { "not found" };
            m_onlyChildren = null;
            return Gather(level, root).Select(k => level.ById[k].TypeId).Distinct().Where(t => !m_types.ContainsKey(t)).Select(t => "class " + t).ToArray();
        }

        // The hierarchy, and assets stored in the level that built-in components point at, such as combined meshes.
        // Scripts aren't followed, because whatever else they reach in the level belongs to the scene around them.
        private HashSet<long> Gather(LevelIndex level, long root)
        {
            var keep = new HashSet<long>();
            var queue = new Queue<long>();
            Action<long> enqueue = id =>
            {
                if (id != 0 && keep.Add(id))
                    queue.Enqueue(id);
            };
            Action<long> addHierarchy = null;
            addHierarchy = t =>
            {
                enqueue(t);
                long gameObject = level.GameObjectOf[t];
                enqueue(gameObject);
                foreach (AssetTypeValueField component in Read(level.ById[gameObject], level.File)["m_Component.Array"].Children)
                    enqueue(component["component.m_PathID"].AsLong);
                foreach (long child in t == root && m_onlyChildren != null ? m_onlyChildren : (IEnumerable<long>)level.ChildrenOf[t])
                    addHierarchy(child);
            };
            addHierarchy(root);

            while (queue.Count > 0)
            {
                AssetFileInfo info = level.ById[queue.Dequeue()];
                if (info.TypeId == SCRIPT || info.TypeId == MESH || !m_types.ContainsKey(info.TypeId))
                    continue;
                VisitPointers(Read(info, level.File), pointer =>
                {
                    AssetFileInfo target;
                    if (pointer["m_FileID"].AsInt == 0 && level.ById.TryGetValue(pointer["m_PathID"].AsLong, out target)
                        && target.TypeId != GAME_OBJECT && target.TypeId != TRANSFORM && target.TypeId != SCRIPT)
                        enqueue(target.PathId);
                });
            }
            return keep;
        }

        private static void VisitPointers(AssetTypeValueField field, Action<AssetTypeValueField> onPointer)
        {
            if (field.TemplateField.Type.StartsWith("PPtr<"))
            {
                onPointer(field);
                return;
            }
            if (field.Children != null)
                foreach (AssetTypeValueField child in field.Children)
                    VisitPointers(child, onPointer);
        }

        private byte[] MakeBundleRecord(AssetsFile template, HashSet<long> keep, long mainObject, string name)
        {
            AssetFileInfo existing = template.AssetInfos.First(i => i.TypeId == ASSET_BUNDLE);
            AssetTypeValueField record = Read(existing, template);
            record["m_Name"].AsString = name;
            record["m_AssetBundleName"].AsString = name;

            AssetTypeValueField preload = record["m_PreloadTable.Array"];
            preload.Children.Clear();
            foreach (long id in keep.OrderBy(i => i))
            {
                AssetTypeValueField item = ValueBuilder.DefaultValueFieldFromArrayTemplate(preload);
                item["m_FileID"].AsInt = 0;
                item["m_PathID"].AsLong = id;
                preload.Children.Add(item);
            }

            AssetTypeValueField container = record["m_Container.Array"];
            AssetTypeValueField slot = container.Children[0];
            container.Children.Clear();
            slot["first"].AsString = PREFAB_PATH;
            slot["second.preloadIndex"].AsInt = 0;
            slot["second.preloadSize"].AsInt = keep.Count;
            slot["second.asset.m_FileID"].AsInt = 0;
            slot["second.asset.m_PathID"].AsLong = mainObject;
            container.Children.Add(slot);
            return record.WriteToByteArray();
        }

        // A script's fields come from the game's code. Each object's exact size is a strong check of the result.
        private void CheckAndDescribeScript(LevelIndex level, HashSet<long> keep, int oldIndex, TypeTreeType source, TypeTreeType type, BuildReport report)
        {
            AssetTypeTemplateField fields = ScriptFields(level, source);
            foreach (AssetFileInfo info in keep.Select(k => level.ById[k]).Where(i => i.TypeIdOrIndex == oldIndex))
            {
                int size = fields.MakeValue(level.File.Reader, info.GetAbsoluteByteOffset(level.File)).WriteToByteArray().Length;
                if (size != (int)info.ByteSize)
                    throw new NotSupportedException($"A script's description gives {size} bytes where the game has {info.ByteSize}, so its data can't be trusted.");
                report.Scripts++;
            }
            m_describer.Describe(fields, type);
        }

        private AssetTypeTemplateField ScriptFields(LevelIndex level, TypeTreeType source)
        {
            AssetPPtr pointer = level.File.Metadata.ScriptTypes[source.ScriptTypeIndex];
            string fileName = level.File.Metadata.Externals[pointer.FileId - 1].PathName;
            AssetsFile scriptFile = ScriptFileFor(fileName);

            AssetFileInfo scriptInfo = scriptFile.AssetInfos.First(i => i.PathId == pointer.PathId);
            AssetTypeValueField script = Read(scriptInfo, scriptFile);
            string space = script["m_Namespace"].AsString;
            string className = script["m_ClassName"].AsString;
            string key = space + "." + className;

            AssetTypeTemplateField fields;
            if (m_scriptFields.TryGetValue(key, out fields))
                return fields;

            string assembly = script["m_AssemblyName"].AsString;
            if (!assembly.EndsWith(".dll"))
                assembly += ".dll";
            var baseFields = new AssetTypeTemplateField();
            baseFields.FromTypeTree(m_types[SCRIPT]);
            fields = m_scriptReader.GetTemplateField(baseFields, assembly, space, className, new UnityVersion(level.File.Metadata.UnityVersion));
            if (fields == null)
                throw new NotSupportedException("No description for script " + key);
            m_scriptFields[key] = fields;
            return fields;
        }

        private byte[] BytesFor(LevelIndex level, AssetFileInfo info, long root, BuildReport report)
        {
            AssetsFile file = level.File;
            if (info.PathId == root)
                return RootTransformBytes(level, info);
            if (info.TypeId == TRANSFORM && m_onlyChildren != null && m_onlyChildren.Contains(info.PathId) && level.FatherOf[info.PathId] != root)
            {
                AssetTypeValueField moved = Read(info, file);
                moved["m_Father.m_PathID"].AsLong = root;
                return moved.WriteToByteArray();
            }
            if (info.TypeId == MESH)
            {
                AssetTypeValueField mesh = Read(info, file);
                string streamPath = mesh["m_StreamData.path"].AsString;
                if (!string.IsNullOrEmpty(streamPath))
                {
                    // Brought inline, since the stream's file belongs to the scene.
                    var bytes = new byte[mesh["m_StreamData.size"].AsUInt];
                    using (FileStream stream = File.OpenRead(Path.Combine(m_gameData, Path.GetFileName(streamPath))))
                    {
                        stream.Position = (long)mesh["m_StreamData.offset"].AsULong;
                        int read = 0;
                        while (read < bytes.Length)
                            read += stream.Read(bytes, read, bytes.Length - read);
                    }
                    mesh["m_VertexData.m_DataSize"].AsByteArray = bytes;
                    mesh["m_StreamData.path"].AsString = "";
                    mesh["m_StreamData.offset"].AsULong = 0;
                    mesh["m_StreamData.size"].AsUInt = 0;
                    report.MeshBytes += bytes.Length;
                    return mesh.WriteToByteArray();
                }
            }

            file.Reader.Position = info.GetAbsoluteByteOffset(file);
            return file.Reader.ReadBytes((int)info.ByteSize);
        }

        // The root leaves its parents behind, carrying their placement, because batched geometry is in world space.
        private byte[] RootTransformBytes(LevelIndex level, AssetFileInfo info)
        {
            Placement world = Placement.Identity;
            var chain = new List<long>();
            for (long t = info.PathId; t != 0; t = level.FatherOf[t])
                chain.Insert(0, t);
            foreach (long t in chain)
            {
                AssetTypeValueField f = Read(level.ById[t], level.File);
                world = world.Then(new Placement
                {
                    PX = f["m_LocalPosition.x"].AsFloat, PY = f["m_LocalPosition.y"].AsFloat, PZ = f["m_LocalPosition.z"].AsFloat,
                    RX = f["m_LocalRotation.x"].AsFloat, RY = f["m_LocalRotation.y"].AsFloat, RZ = f["m_LocalRotation.z"].AsFloat, RW = f["m_LocalRotation.w"].AsFloat,
                    SX = f["m_LocalScale.x"].AsFloat, SY = f["m_LocalScale.y"].AsFloat, SZ = f["m_LocalScale.z"].AsFloat,
                });
            }

            AssetTypeValueField root = Read(info, level.File);
            root["m_Father.m_FileID"].AsInt = 0;
            root["m_Father.m_PathID"].AsLong = 0;
            root["m_LocalPosition.x"].AsFloat = world.PX;
            root["m_LocalPosition.y"].AsFloat = world.PY;
            root["m_LocalPosition.z"].AsFloat = world.PZ;
            root["m_LocalRotation.x"].AsFloat = world.RX;
            root["m_LocalRotation.y"].AsFloat = world.RY;
            root["m_LocalRotation.z"].AsFloat = world.RZ;
            root["m_LocalRotation.w"].AsFloat = world.RW;
            root["m_LocalScale.x"].AsFloat = world.SX;
            root["m_LocalScale.y"].AsFloat = world.SY;
            root["m_LocalScale.z"].AsFloat = world.SZ;
            if (m_onlyChildren != null)
            {
                AssetTypeValueField children = root["m_Children.Array"];
                children.Children.Clear();
                foreach (long child in m_onlyChildren)
                {
                    AssetTypeValueField item = ValueBuilder.DefaultValueFieldFromArrayTemplate(children);
                    item["m_FileID"].AsInt = 0;
                    item["m_PathID"].AsLong = child;
                    children.Children.Add(item);
                }
            }
            return root.WriteToByteArray();
        }

        /// <summary>
        /// Every object a modder could reference: the highest with a LOD group or its own mesh, and every character.
        /// Used to measure how much of a level the template can carry.
        /// </summary>
        public List<string> CandidatePaths(string levelFile)
        {
            LevelIndex level = LevelFor(levelFile);
            var found = new List<string>();
            Action<long, string> walk = null;
            walk = (t, parent) =>
            {
                string path = parent.Length == 0 ? NameOf(level, t) : parent + "/" + NameOf(level, t);
                HashSet<int> classes = new HashSet<int>(Read(level.ById[level.GameObjectOf[t]], level.File)["m_Component.Array"].Children.Select(c => level.ById[c["component.m_PathID"].AsLong].TypeId));
                if (parent.Length > 0 && (classes.Contains(LOD_GROUP) || classes.Contains(MESH_FILTER) || classes.Contains(CHARACTER_CONTROLLER)))
                {
                    found.Add(path);
                    return;
                }
                foreach (long child in level.ChildrenOf[t])
                    walk(child, path);
            };
            foreach (long root in level.RootTransforms)
                walk(root, "");
            return found;
        }
    }

    // Unity's position, rotation and scale chain, kept here because the game's runtime can't be counted on for a math library.
    internal struct Placement
    {
        public float PX, PY, PZ, RX, RY, RZ, RW, SX, SY, SZ;

        public static Placement Identity
        {
            get { return new Placement { RW = 1f, SX = 1f, SY = 1f, SZ = 1f }; }
        }

        public Placement Then(Placement child)
        {
            float x = child.PX * SX, y = child.PY * SY, z = child.PZ * SZ;

            // Rotating a point by a quaternion: p + 2w(r x p) + 2r x (r x p).
            float cx = RY * z - RZ * y, cy = RZ * x - RX * z, cz = RX * y - RY * x;
            float dx = RY * cz - RZ * cy, dy = RZ * cx - RX * cz, dz = RX * cy - RY * cx;

            return new Placement
            {
                PX = PX + x + 2f * (RW * cx + dx),
                PY = PY + y + 2f * (RW * cy + dy),
                PZ = PZ + z + 2f * (RW * cz + dz),
                RX = RW * child.RX + RX * child.RW + RY * child.RZ - RZ * child.RY,
                RY = RW * child.RY - RX * child.RZ + RY * child.RW + RZ * child.RX,
                RZ = RW * child.RZ + RX * child.RY - RY * child.RX + RZ * child.RW,
                RW = RW * child.RW - RX * child.RX - RY * child.RY - RZ * child.RZ,
                SX = SX * child.SX,
                SY = SY * child.SY,
                SZ = SZ * child.SZ,
            };
        }
    }

    public class BuildReport
    {
        public int Objects;
        public int Scripts;
        public long MeshBytes;
        public long Bytes;
    }
}
