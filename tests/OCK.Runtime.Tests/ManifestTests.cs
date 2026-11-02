using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using BepInEx.Logging;
using NUnit.Framework;
using OutwardConstructionKit.Runtime;
using UnityEngine;

namespace OutwardConstructionKit.Tests
{
    [TestFixture]
    public class ManifestTests
    {
        // Folders of real exports to try as well, separated by semicolons.
        private const string EXTRA_MANIFESTS_VARIABLE = "OCK_TEST_MANIFESTS";

        // Parses with the plugin's log captured, since a line it can't read is only reported there.
        private static RegionManifest Parse(IEnumerable<string> lines, out List<string> errors)
        {
            var found = new List<string>();
            var log = new ManualLogSource("test");
            log.LogEvent += (sender, e) =>
            {
                if (e.Level == LogLevel.Error)
                    found.Add(e.Data.ToString());
            };
            KitPlugin.Log = log;
            RegionManifest manifest = RegionManifest.Parse(lines);
            errors = found;
            return manifest;
        }

        private static RegionManifest ParseSample()
        {
            List<string> errors;
            string sample = Path.Combine(TestContext.CurrentContext.TestDirectory, "samples", "everything.refs.txt");
            RegionManifest manifest = Parse(File.ReadAllLines(sample), out errors);
            Assert.That(errors, Is.Empty, "lines the parser rejected");
            return manifest;
        }

        [Test]
        public void ReadsTheRegionsIdentity()
        {
            RegionManifest manifest = ParseSample();
            Assert.That(manifest.regionId, Is.EqualTo("sample_region"));
            Assert.That(manifest.displayName, Is.EqualTo("Sample Region"));
            Assert.That(manifest.gameFingerprint, Is.EqualTo("ABC123"));
            Assert.That(manifest.environmentDonor, Is.EqualTo("Monsoon"));
        }

        [Test]
        public void ReadsTheEntranceDoor()
        {
            RegionManifest manifest = ParseSample();
            Assert.That(manifest.doorScene, Is.EqualTo("CierzoNewTerrain"));
            Assert.That(manifest.doorPosition, Is.EqualTo("1.5,-2,3"));
            Assert.That(manifest.doorRotationY, Is.EqualTo(90f));
        }

        [Test]
        public void ReadsEnemiesWithSpacesInTheirPaths()
        {
            RegionManifest.EnemyReference enemy = ParseSample().enemies.Single();
            Assert.That(enemy.path, Is.EqualTo("AISquadManagerStructure/SquadReserve/Squad Hyena 2AI/Hound (1)"));
            Assert.That(enemy.position, Is.EqualTo(new Vector3(0f, 0f, 20f)));
            Assert.That(enemy.rotationY, Is.EqualTo(45.5f));
        }

        [Test]
        public void ReadsPropsWithAndWithoutACheck()
        {
            List<RegionManifest.PropReference> props = ParseSample().props;
            Assert.That(props.Count, Is.EqualTo(2));
            Assert.That(props[0].check, Is.EqualTo("mdl_rock:1"));
            Assert.That(props[0].rotation, Is.EqualTo(new Vector3(0f, 30f, 0f)), "full rotation");
            Assert.That(props[1].check, Is.Null);
            Assert.That(props[1].rotation, Is.EqualTo(new Vector3(0f, 90f, 0f)), "rotation given as just y");
        }

        [Test]
        public void ReadsGroundLayersWithABlankNormalMap()
        {
            List<RegionManifest.GroundLayer> layers = ParseSample().groundLayers;
            Assert.That(layers.Count, Is.EqualTo(2));
            Assert.That(layers[0].normal, Is.EqualTo("tex_grass_n"));
            Assert.That(layers[1].layer, Is.EqualTo("Ground_Dirt"));
            Assert.That(layers[1].normal, Is.Empty);
        }

        [Test]
        public void ReadsWater()
        {
            RegionManifest.WaterReference water = ParseSample().water[0];
            Assert.That(water.scene, Is.EqualTo("Monsoon"));
            Assert.That(water.position, Is.EqualTo(new Vector3(-35f, -1.5f, 0f)));
            Assert.That(water.size.x, Is.EqualTo(40f), "width");
            Assert.That(water.size.z, Is.EqualTo(30f), "length");
            Assert.That(water.rotationY, Is.EqualTo(15f));
        }

        [Test]
        public void ReadsReturnDoors()
        {
            RegionManifest.ReturnDoor door = ParseSample().returnDoors.Single();
            Assert.That(door.position, Is.EqualTo(new Vector3(-6f, 0f, 6f)));
            Assert.That(door.rotationY, Is.EqualTo(135f));
        }

        [Test]
        public void ListsEachDonorSceneOnce()
        {
            Assert.That(ParseSample().DonorScenes(), Is.EquivalentTo(new[] { "ChersoneseNewTerrain", "Monsoon" }));
        }

        private static string Replace(string scene, string[] installed, Dictionary<string, string> entrances)
        {
            return MissingRegionGuard.Replacement(scene, installed, entrances);
        }

        [Test]
        public void LeavesAVanillaSaveAlone()
        {
            Assert.That(Replace("Monsoon", new string[0], new Dictionary<string, string>()), Is.Null);
        }

        [Test]
        public void LeavesASaveInAnInstalledRegionAlone()
        {
            Assert.That(Replace("OCK_a", new[] { "OCK_a" }, new Dictionary<string, string>()), Is.Null);
        }

        [Test]
        public void SendsASaveInARemovedRegionToItsEntrance()
        {
            Assert.That(Replace("OCK_a", new string[0], new Dictionary<string, string> { { "OCK_a", "Monsoon" } }), Is.EqualTo("Monsoon"));
        }

        [Test]
        public void SendsASaveInAnUnknownRegionSomewhereSafe()
        {
            Assert.That(Replace("OCK_a", new string[0], new Dictionary<string, string>()), Is.EqualTo("CierzoNewTerrain"));
        }

        [Test]
        public void WontSendASaveThroughAnotherRemovedRegion()
        {
            Assert.That(Replace("OCK_a", new string[0], new Dictionary<string, string> { { "OCK_a", "OCK_b" } }), Is.EqualTo("CierzoNewTerrain"));
        }

        [Test]
        public void ReadsAnEmbeddedMaterialsShader()
        {
            List<string> errors;
            RegionManifest manifest = Parse(new[] { "shader | ElderberryLeavesSummerF | Nature/SpeedTreeSeasons" }, out errors);
            Assert.That(errors, Is.Empty);
            Assert.That(manifest.embeddedShaders["ElderberryLeavesSummerF"], Is.EqualTo("Nature/SpeedTreeSeasons"));
        }

        [Test]
        public void ReadsAWaterType()
        {
            List<string> errors;
            RegionManifest manifest = Parse(new[] { "water | Monsoon | 1,2,3 | 10,1,10 | 0 | Salt", "water | Monsoon | 1,2,3 | 10,1,10 | 0" }, out errors);
            Assert.That(errors, Is.Empty);
            Assert.That(manifest.water[0].waterType, Is.EqualTo("Salt"));
            Assert.That(manifest.water[1].waterType, Is.Null);
        }

        [Test]
        public void ReadsItems()
        {
            List<string> errors;
            RegionManifest manifest = Parse(new[] { "item | 5010100 | 1,2,3 | 0,90,0", "item | 4000010 | -1,0,0.5 | 45" }, out errors);
            Assert.That(errors, Is.Empty);
            Assert.That(manifest.items.Count, Is.EqualTo(2));
            Assert.That(manifest.items[0].id, Is.EqualTo(5010100));
            Assert.That(manifest.items[0].position, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(manifest.items[1].rotation, Is.EqualTo(new Vector3(0f, 45f, 0f)));
        }

        [TestCase("item | notanumber | 1,2,3 | 0")]
        [TestCase("item | -5 | 1,2,3 | 0")]
        [TestCase("item | 5010100 | 1,2 | 0")]
        [TestCase("item | 5010100 | 1,2,3")]
        public void ReportsABadItemLine(string line)
        {
            List<string> errors;
            RegionManifest manifest = Parse(new[] { line }, out errors);
            Assert.That(manifest.items, Is.Empty);
            Assert.That(errors.Count, Is.EqualTo(1));
        }

        [Test]
        public void ItemKeysFollowTheItemAndItsSpot()
        {
            var item = new RegionManifest.ItemReference { id = 5010100, position = new Vector3(1f, 2f, 3f) };
            string key = RegionItems.Key("region", item);
            Assert.That(RegionItems.Key("region", new RegionManifest.ItemReference { id = 5010100, position = new Vector3(1f, 2f, 3f), rotation = Vector3.up }), Is.EqualTo(key), "turning it isn't a new item");
            Assert.That(RegionItems.Key("region", new RegionManifest.ItemReference { id = 5010100, position = new Vector3(1f, 2f, 4f) }), Is.Not.EqualTo(key));
            Assert.That(RegionItems.Key("region", new RegionManifest.ItemReference { id = 5010101, position = new Vector3(1f, 2f, 3f) }), Is.Not.EqualTo(key));
            Assert.That(RegionItems.Key("other", item), Is.Not.EqualTo(key));
        }

        [Test]
        public void ThePlacedItemsRecordSurvivesASave()
        {
            string saved = RegionItems.WriteRecord(new[] { "b2", "a1" });
            Assert.That(RegionItems.ReadRecord(saved), Is.EquivalentTo(new[] { "a1", "b2" }));
            Assert.That(RegionItems.ReadRecord(RegionItems.WriteRecord(new string[0])), Is.Empty);
            Assert.That(RegionItems.ReadRecord(null), Is.Empty);
            Assert.That(RegionItems.ReadRecord("old map data from before the field was retired"), Is.Empty);
        }

        [TestCase("ockitem1234abcd-Xy_z", "1234abcd")]
        [TestCase("Xy_z-abc", null)]
        [TestCase("ockitem", null)]
        [TestCase("", null)]
        public void FindsTheKeyInAnItemsUid(string uid, string key)
        {
            Assert.That(RegionItems.KeyOfUid(uid), Is.EqualTo(key));
        }

        [Test]
        public void ItemCatalogLinesStayOnOneLineWithFourParts()
        {
            string line = ItemCatalog.Line(5010100, "Item", "5010100_CookingPotKit", "Cooking | Pot\nKit");
            Assert.That(line, Is.EqualTo("5010100 | Item | 5010100_CookingPotKit | Cooking / Pot Kit"));
        }

        [Test]
        public void ReturnsADefeatedPlayerWhereTheyCameFrom()
        {
            Assert.That(DeathReturn.Destination("Berg", "CierzoNewTerrain"), Is.EqualTo("Berg"));
        }

        [Test]
        public void ReturnsADefeatedPlayerToTheEntranceIfTheyLoadedInThere()
        {
            Assert.That(DeathReturn.Destination(null, "Berg"), Is.EqualTo("Berg"));
        }

        [Test]
        public void ReturnsADefeatedPlayerSomewhereSafeWithNothingElseToGoOn()
        {
            Assert.That(DeathReturn.Destination(null, ""), Is.EqualTo("CierzoNewTerrain"));
        }

        [Test]
        public void EnemiesGetTheSameNetworkIdsOnEveryPlayersGame()
        {
            Assert.That(EnemyDonor.ViewId(3, 1), Is.EqualTo(EnemyDonor.ViewId(3, 1)));
            Assert.That(EnemyDonor.ViewId(3, 49), Is.LessThan(EnemyDonor.ViewId(4, 0)));
        }

        [Test]
        public void NoticesAPlayerWithoutARegion()
        {
            string description = CoopGuard.Describe(new[] { "a=1", "b=2" });
            Assert.That(CoopGuard.Has(description, "b=2"), Is.True);
            Assert.That(CoopGuard.Has(description, "b=3"), Is.False, "another version");
            Assert.That(CoopGuard.Has(description, "c=1"), Is.False, "another region");
            Assert.That(CoopGuard.Has(null, "a=1"), Is.False, "no kit at all");
        }

        [Test]
        public void ReadsADoorModel()
        {
            List<string> errors;
            RegionManifest manifest = Parse(new[] { "doormodel | Monsoon | a/door | 0,90,0 | 1.5" }, out errors);
            Assert.That(errors, Is.Empty);
            Assert.That(manifest.doorModel.path, Is.EqualTo("a/door"));
            Assert.That(manifest.doorModel.rotation.y, Is.EqualTo(90f));
            Assert.That(manifest.doorModel.scale, Is.EqualTo(1.5f));
        }

        [Test]
        public void LeavesTheDoorModelToTheKitWhenNoneIsNamed()
        {
            List<string> errors;
            Assert.That(Parse(new[] { "environment | Monsoon" }, out errors).doorModel, Is.Null);
        }

        [Test]
        public void ReadsAPropsScaleWhenItHasOne()
        {
            List<string> errors;
            RegionManifest manifest = Parse(new[] { "prop | Monsoon | a/b | 1,2,3 | 0 | c:1 | 0.75", "prop | Monsoon | a/c | 1,2,3 | 0 | c:1" }, out errors);
            Assert.That(errors, Is.Empty);
            Assert.That(manifest.props[0].scale, Is.EqualTo(0.75f));
            Assert.That(manifest.props[1].scale, Is.EqualTo(1f));
        }

        [Test]
        public void RefusesAScaleThatIsntPositive()
        {
            List<string> errors;
            RegionManifest manifest = Parse(new[] { "prop | Monsoon | a/b | 1,2,3 | 0 | c:1 | -2" }, out errors);
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(manifest.props, Is.Empty);
        }

        [Test]
        public void NeedsNoDonorSceneWhenEverythingIsCached()
        {
            Assert.That(ParseSample().DonorScenes((scene, path) => true), Is.Empty);
        }

        [Test]
        public void NeedsTheDonorSceneIfAnyPartOfTheEnvironmentIsntCached()
        {
            Assert.That(ParseSample().DonorScenes((scene, path) => path != "Main Camera"), Is.EquivalentTo(new[] { ParseSample().environmentDonor }));
        }

        [Test]
        public void ReportsLinesItCantRead()
        {
            List<string> errors;
            RegionManifest manifest = Parse(new[] { "prop | too | few", "nonsense", "water | Monsoon | not,a,vector | 1,1,1 | 0" }, out errors);
            Assert.That(errors.Count, Is.EqualTo(3));
            Assert.That(manifest.props, Is.Empty);
            Assert.That(manifest.water, Is.Empty);
        }

        [Test]
        public void IgnoresCommentsAndBlankLines()
        {
            List<string> errors;
            Parse(new[] { "# a comment", "", "   " }, out errors);
            Assert.That(errors, Is.Empty);
        }

        // A player's PC may write 1,5 for one and a half, but manifests are shared between PCs.
        [Test]
        public void ReadsTheSameUnderAnyLanguage()
        {
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                Assert.That(ParseSample().water[0].position.y, Is.EqualTo(-1.5f));
                Assert.That(ParseSample().enemies[0].rotationY, Is.EqualTo(45.5f));
                Assert.That(KitPlugin.FormatVector(new Vector3(1.5f, -2f, 3.25f)), Is.EqualTo("1.5,-2,3.25"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("1,2")]
        [TestCase("1,2,3,4")]
        [TestCase("a,b,c")]
        public void VectorsRejectBadInput(string bad)
        {
            Vector3 v;
            Assert.That(KitPlugin.TryParseVector(bad, out v), Is.False);
        }

        [Test]
        public void VectorsAllowSpaces()
        {
            Vector3 v;
            Assert.That(KitPlugin.TryParseVector(" 1 , -2.5 , 3 ", out v), Is.True);
            Assert.That(v, Is.EqualTo(new Vector3(1f, -2.5f, 3f)));
        }

        // Saves and placed doors depend on these, so a region's number must never change.
        [TestCase("test_alpha", 451190)]
        [TestCase("catalystenbridges", 467569)]
        public void AreaNumbersDontChange(string id, int number)
        {
            Assert.That(Region.AreaIdFor(id), Is.EqualTo(number));
        }

        [TestCase("a")]
        [TestCase("test_gamma")]
        [TestCase("a region with spaces")]
        [TestCase("ÜnïcödeID")]
        public void AreaNumbersAvoidTheGamesOwn(string id)
        {
            Assert.That(Region.AreaIdFor(id), Is.GreaterThanOrEqualTo(1000));
        }

        [Test]
        public void RealManifestsAllParse()
        {
            string folders = Environment.GetEnvironmentVariable(EXTRA_MANIFESTS_VARIABLE);
            if (string.IsNullOrEmpty(folders))
                Assert.Ignore("No real exports were given, in " + EXTRA_MANIFESTS_VARIABLE + ".");

            foreach (string folder in folders.Split(';'))
            {
                string[] files = Directory.GetFiles(folder, "*.refs.txt", SearchOption.AllDirectories);
                Assert.That(files, Is.Not.Empty, "no manifests in " + folder);
                foreach (string file in files)
                {
                    List<string> errors;
                    Parse(File.ReadAllLines(file), out errors);
                    Assert.That(errors, Is.Empty, Path.GetFileName(file));
                }
            }
        }
    }
}
