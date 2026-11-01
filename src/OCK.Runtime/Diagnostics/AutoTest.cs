using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// Plays a test without anyone at the keyboard, when tests/run-game-test.ps1 leaves a request file: loads the
    /// named character, walks through each named region's checks, goes home so the game saves, and quits or stays
    /// open for a person to take over. The request is deleted as it's read, so a crash can't repeat itself, and with
    /// no request nothing here runs. Every step logs an [AUTOTEST] line.
    /// </summary>
    internal static class AutoTest
    {
        private const string REQUEST_FILE = "OCK_AutoTest.txt";
        private const string RESULTS_FOLDER = "OCK_AutoTest";
        private const float STEP_TIMEOUT_SECONDS = 240f;
        private const float CHECKS_SECONDS = 12f; // longer than the checks' own wait
        private const string TEST_SHADER = "Custom/Water/CustomWater";
        private const float GROUND_SEARCH_DISTANCE = 6f;
        private const string VISIT = "visit "; // the Test Region button: goes there, then it's normal play

        public static bool Begin(KitPlugin plugin)
        {
            string file = Path.Combine(Paths.BepInExRootPath, REQUEST_FILE);
            if (!File.Exists(file))
                return false;

            string[] request = File.ReadAllLines(file);
            File.Delete(file);
            if (!request.Any(l => l.StartsWith(VISIT)))
                plugin.SelfCheck.Value = true;
            Application.runInBackground = true; // the game pauses itself otherwise, if something else has focus
            plugin.StartCoroutine(Run(request));
            return true;
        }

        private static void Log(string message)
        {
            KitPlugin.Log.LogMessage("[AUTOTEST] " + message);
        }

        private static IEnumerator WaitFor(System.Func<bool> condition, string what, System.Action<bool> done)
        {
            float waited = 0f;
            while (!condition())
            {
                if (waited > STEP_TIMEOUT_SECONDS)
                {
                    Log($"FAIL timed out waiting for {what}.");
                    done(false);
                    yield break;
                }
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            Log($"{what}, after {waited:0} s.");
            done(true);
        }

        private static IEnumerator Run(string[] request)
        {
            string[] regions = request.Where(l => l.StartsWith("region ")).Select(l => l.Substring(7).Trim()).ToArray();
            bool quit = !request.Contains("stay");
            bool ok = false;
            System.Action<bool> result = r => ok = r;

            MainScreen menu = null;
            Button continueButton = null;
            yield return WaitFor(() =>
            {
                menu = Resources.FindObjectsOfTypeAll<MainScreen>().FirstOrDefault(m => m.gameObject.activeInHierarchy);
                if (menu == null || SaveManager.Instance == null || !SaveManager.Instance.SaveRetrieved)
                    return false;
                continueButton = (Button)AccessTools.Field(typeof(MainScreen), "m_btnContinue").GetValue(menu);
                return continueButton != null && continueButton.gameObject.activeInHierarchy && continueButton.interactable;
            }, "the main menu is ready", result);
            if (!ok)
                yield break;

            yield return new WaitForSecondsRealtime(2f);
            Log("Pressing Continue.");
            continueButton.onClick.Invoke();
            yield return new WaitForSecondsRealtime(2f);

            string wanted = request.Where(l => l.StartsWith("character ")).Select(l => l.Substring(10).Trim()).FirstOrDefault();
            List<CharacterSaveInstanceHolder> saves = SaveManager.Instance.CharacterSaves.Where(s => s.MostRecentInstance != null).ToList();
            CharacterSaveInstanceHolder chosen = wanted == null ? saves.FirstOrDefault() : saves.FirstOrDefault(s => s.MostRecentInstance.CharSave.PSave.Name == wanted);
            if (chosen == null)
            {
                Log($"FAIL no character called '{wanted}'. There are: {string.Join(", ", saves.Select(s => s.MostRecentInstance.CharSave.PSave.Name).ToArray())}");
                yield break;
            }
            var panel = (CharacterSelectionPanel)AccessTools.Field(typeof(MainScreen), "m_characterSelection").GetValue(menu);
            Log($"Choosing {chosen.MostRecentInstance.CharSave.PSave.Name}.");
            panel.OnCharacterClicked(chosen);

            yield return WaitFor(() => Player() != null && Loaded(), "the character is in the world", result);
            if (!ok)
                yield break;
            yield return PressAnyKey(result);
            if (!ok)
                yield break;

            string visit = request.Where(l => l.StartsWith(VISIT)).Select(l => l.Substring(VISIT.Length).Trim()).FirstOrDefault();
            if (visit != null)
            {
                Region visited = Region.All.FirstOrDefault(r => r.Id == visit);
                if (visited == null || !visited.CanLoad)
                {
                    KitPlugin.Log.LogError($"Couldn't go to '{visit}', because it isn't installed or can't load. The log above says why.");
                    Log($"FAIL couldn't visit '{visit}'.");
                    yield break;
                }
                yield return new WaitForSecondsRealtime(1f);
                yield return Travel(visited.SceneName, 0, result);
                if (ok)
                    Log($"FINISHED, in {visit}, for a person to play.");
                yield break;
            }

            // A run that ended in a fight can save its character close to death, and they'd die on the next arrival.
            Player().Stats.RestoreAllVitals();
            yield return new WaitForSecondsRealtime(3f);
            string startIn = request.Where(l => l.StartsWith("startin ")).Select(l => l.Substring(8).Trim()).FirstOrDefault();
            if (startIn != null && startIn != SceneManager.GetActiveScene().name)
            {
                yield return Travel(startIn, 0, result);
                if (!ok)
                    yield break;
                yield return new WaitForSecondsRealtime(3f);
            }
            string home = SceneManager.GetActiveScene().name;
            Log($"Started in {home}, at memory {SceneExtractor.PrivateMemory() / (1024 * 1024)} MB.");
            CheckStanding();
            if (KitPlugin.Instance.UseCache.Value)
            {
                Shader rareShader = AssetCache.LoadShader(TEST_SHADER);
                Log(rareShader != null && rareShader.isSupported ? $"A shader from the cache works: {rareShader.name}." : $"FAIL couldn't load {TEST_SHADER} from the cache.");
                AssetCache.ReleaseAll();
            }

            foreach (string id in regions)
            {
                Region region = Region.All.FirstOrDefault(r => r.Id == id);
                if (region == null)
                {
                    Log($"FAIL no region called '{id}'.");
                    continue;
                }
                if (!region.CanLoad)
                {
                    Log($"'{id}' can't load, so it's left closed, and isn't visited.");
                    continue;
                }

                yield return Travel(region.SceneName, 0, result);
                if (!ok)
                    yield break;
                Log($"In {id}, at memory {SceneExtractor.PrivateMemory() / (1024 * 1024)} MB.");
                Player().Invincible = true; // no one's at the keyboard, and enemies that work will kill a character standing still
                yield return new WaitForSecondsRealtime(CHECKS_SECONDS);
                yield return Screenshot(id);
            }

            if (regions.Length > 0 && request.Contains("dieinregion"))
            {
                string died = SceneManager.GetActiveScene().name;
                Log($"Dying in {died}.");
                Player().Invincible = false;
                Player().Stats.ReceiveDamage(Player().Stats.MaxHealth * 10f); // as an enemy would, which sets off the defeat
                bool returned = false;
                yield return WaitFor(() => SceneManager.GetActiveScene().name == home && Player() != null && Loaded(), "arriving back in " + home, r => returned = r);
                if (!returned)
                    yield break;
                yield return PressAnyKey(result);
                if (!ok)
                    yield break;
                yield return new WaitForSecondsRealtime(3f);
                Log($"Died in {died} and came back in {SceneManager.GetActiveScene().name}, alive: {Player().IsDead == false}, health {Player().Stats.CurrentHealth:0}.");
                CheckStanding();
                Player().Stats.RestoreAllVitals();
            }

            if (regions.Length > 0 && !request.Contains("endinregion"))
            {
                yield return Travel(home, 0, result);
                if (!ok)
                    yield break;
                Log($"Back in {home}, so the game saved on the way, at memory {SceneExtractor.PrivateMemory() / (1024 * 1024)} MB.");
                Player().Invincible = false;
                yield return new WaitForSecondsRealtime(3f);
            }

            Log(quit ? "FINISHED, closing the game." : "FINISHED, staying open for a person to take over.");
            if (quit)
                Application.Quit();
        }

        private static void CheckStanding()
        {
            Vector3 feet = Player().transform.position;
            Log(Physics.Raycast(feet + Vector3.up, Vector3.down, GROUND_SEARCH_DISTANCE, Global.FullEnvironmentMask, QueryTriggerInteraction.Ignore)
                ? $"Standing on the ground at {feet}." : $"FAIL not standing on anything at {feet}.");
        }

        private static Character Player()
        {
            return CharacterManager.Instance != null ? CharacterManager.Instance.GetFirstLocalCharacter() : null;
        }

        private static bool Loaded()
        {
            return NetworkLevelLoader.Instance != null && NetworkLevelLoader.Instance.IsOverallLoadingDone;
        }

        // A level waits for a key press to begin, with everyone frozen, and this is what that key press does.
        private static IEnumerator PressAnyKey(System.Action<bool> result)
        {
            AccessTools.Method(typeof(NetworkLevelLoader), "SetContinueAfterLoading").Invoke(NetworkLevelLoader.Instance, null);
            yield return WaitFor(() => !NetworkLevelLoader.Instance.IsGameplayPaused, "gameplay has begun", result);
        }

        private static IEnumerator Travel(string sceneName, int spawnPoint, System.Action<bool> result)
        {
            Area area = AreaManager.Instance.GetAreaFromSceneName(sceneName);
            if (area == null)
            {
                Log($"FAIL no area for scene {sceneName}.");
                result(false);
                yield break;
            }

            // The same call a door makes.
            Log($"Travelling to {sceneName}.");
            CharacterManager.Instance.RequestAreaSwitch(Player(), area, 0, spawnPoint, 1.5f, "");
            bool arrived = false;
            yield return WaitFor(() => SceneManager.GetActiveScene().name == sceneName && Player() != null && Loaded(), "arrived in " + sceneName, r => arrived = r);
            if (!arrived)
            {
                result(false);
                yield break;
            }
            yield return PressAnyKey(result);
        }

        private static IEnumerator Screenshot(string name)
        {
            string folder = Path.Combine(Paths.BepInExRootPath, RESULTS_FOLDER);
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, name + ".png");
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(file);
            Log("Screenshot " + file);
        }
    }
}
