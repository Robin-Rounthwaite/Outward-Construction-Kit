using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using NUnit.Framework;
using OutwardConstructionKit.Extractor;

namespace OutwardConstructionKit.Tests
{
    /// <summary>Builds real bundles from the game's files, read-only, into a temporary folder.</summary>
    [TestFixture]
    public class ExtractorTests
    {
        private const string MONSOON = "level52";
        private const string CHERSONESE = "level5";
        private const string BARRELS = "Interiors/mdl_env_buildingInteriorMonsoonPlayerhouseA_c/mdl_env_propBarrelGroupA";
        private const string HYENA = "AISquadManagerStructure/SquadReserve/Squad Hyena 2AI/Hound (1)";
        private const string COVERAGE_VARIABLE = "OCK_TEST_COVERAGE_OUT";

        private string m_folder;
        private BundleBuilder m_builder;

        private static string GameData
        {
            get
            {
                string managed = typeof(ExtractorTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "OutwardManaged").Value;
                return Path.GetDirectoryName(managed);
            }
        }

        [OneTimeSetUp]
        public void Prepare()
        {
            m_folder = Path.Combine(Path.GetTempPath(), "ock-extractor-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_folder);
            m_builder = new BundleBuilder(GameData);
        }

        [OneTimeTearDown]
        public void CleanUp()
        {
            Directory.Delete(m_folder, true);
        }

        private AssetsFile Open(string file)
        {
            var manager = new AssetsManager();
            BundleFileInstance bundle = manager.LoadBundleFile(file, true);
            AssetBundleDirectoryInfo entry = bundle.file.BlockAndDirInfo.DirectoryInfos[0];
            return manager.LoadAssetsFileFromBundle(bundle, entry.Name, false).file;
        }

        [Test]
        public void BuildsAStaticBatchedProp()
        {
            string file = Path.Combine(m_folder, "barrels");
            BuildReport report = m_builder.Build(MONSOON, BARRELS, file);

            AssetsFile built = Open(file);
            Assert.That(built.Metadata.TypeTreeEnabled, Is.True);
            Assert.That(built.AssetInfos.Count(i => i.TypeId == 43), Is.EqualTo(2), "its two combined meshes");
            Assert.That(report.MeshBytes, Is.GreaterThan(0), "streamed vertices brought inline");
            Assert.That(built.Metadata.Externals.Any(e => e.PathName == "sharedassets1.assets"), Is.True, "material stays a pointer");
            Assert.That(built.AssetInfos.Any(i => i.TypeId == 28), Is.False, "no textures copied");
        }

        // Unity gives a misleading "not compatible" error if it isn't here.
        [Test]
        public void KeepsTheBundleRecordAtIdOne()
        {
            string file = Path.Combine(m_folder, "barrels_record");
            m_builder.Build(MONSOON, BARRELS, file);
            Assert.That(Open(file).AssetInfos.First(i => i.PathId == 1).TypeId, Is.EqualTo(142));
        }

        [Test]
        public void BuildsAnEnemyWithItsScripts()
        {
            string file = Path.Combine(m_folder, "hyena");
            BuildReport report = m_builder.Build(CHERSONESE, HYENA, file);

            Assert.That(report.Scripts, Is.EqualTo(59));
            AssetsFile built = Open(file);
            TypeTreeType script = built.Metadata.TypeTreeTypes.First(t => t.TypeId == 114);
            Assert.That(script.Nodes.Count, Is.GreaterThan(10), "scripts need full descriptions, or they load empty");
        }

        [Test]
        public void GivesEachBundleItsOwnInternalName()
        {
            string one = Path.Combine(m_folder, "one");
            string two = Path.Combine(m_folder, "two");
            m_builder.Build(MONSOON, BARRELS, one);
            m_builder.Build(MONSOON, BARRELS, two);

            var manager = new AssetsManager();
            string first = manager.LoadBundleFile(one, true).file.BlockAndDirInfo.DirectoryInfos[0].Name;
            string second = manager.LoadBundleFile(two, true).file.BlockAndDirInfo.DirectoryInfos[0].Name;
            Assert.That(first, Is.Not.EqualTo(second), "two loaded bundles can't share one");
        }

        [Test]
        public void RefusesAnObjectThatIsntThere()
        {
            Assert.Throws<InvalidDataException>(() => m_builder.Build(MONSOON, "Nowhere/Nothing", Path.Combine(m_folder, "none")));
        }

        [Test]
        public void LeavesNoPartialFileBehind()
        {
            Assert.That(Directory.GetFiles(m_folder, "*.tmp"), Is.Empty);
        }

        // Not a pass or fail: writes how much of each level could be carried, when asked to with the variable.
        [Test]
        public void ReportsCoverage()
        {
            string output = Environment.GetEnvironmentVariable(COVERAGE_VARIABLE);
            if (string.IsNullOrEmpty(output))
                Assert.Ignore("Set " + COVERAGE_VARIABLE + " to a file to measure coverage.");

            var lines = new List<string>();
            foreach (string level in new[] { MONSOON, CHERSONESE })
            {
                List<string> paths = m_builder.CandidatePaths(level);
                var missing = new Dictionary<string, int>();
                int blocked = 0;
                foreach (string path in paths)
                {
                    string[] classes = m_builder.MissingClasses(level, path);
                    if (classes.Length > 0)
                        blocked++;
                    foreach (string name in classes)
                        missing[name] = missing.TryGetValue(name, out int count) ? count + 1 : 1;
                }
                lines.Add($"{level}: {paths.Count} objects, {blocked} blocked. Missing: " + string.Join(", ", missing.OrderByDescending(m => m.Value).Select(m => m.Key + " x" + m.Value)));

                int built = 0;
                var failures = new Dictionary<string, int>();
                string scratch = Path.Combine(m_folder, "coverage");
                foreach (string path in paths.Where((p, i) => m_builder.MissingClasses(level, p).Length == 0 && (p.StartsWith("AISquad") || i % 25 == 0)))
                {
                    try
                    {
                        m_builder.Build(level, path, scratch);
                        built++;
                    }
                    catch (Exception e)
                    {
                        string key = e.GetType().Name + ": " + e.Message.Substring(0, Math.Min(90, e.Message.Length));
                        failures[key] = failures.TryGetValue(key, out int count) ? count + 1 : 1;
                    }
                }
                lines.Add($"{level}: full builds of a sample plus every squad member: {built} worked, failures: " + string.Join(" | ", failures.OrderByDescending(f => f.Value).Take(8).Select(f => f.Value + "x " + f.Key)));
            }
            File.WriteAllLines(output, lines);
        }
    }
}
