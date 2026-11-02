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
        private const string TRANSITION_SCENE = "LowMemory_TransitionScene";
        private const string VISIT = "visit "; // the Test Region button: goes there, then it's normal play
        private const string CHARACTER_ID = "characterid ";
        private const string START_AT = "startat "; // the Test from Here button: where in the region to begin
        private const float START_AT_HEIGHT = 0.2f;

        public static bool Begin(KitPlugin plugin)
        {
            string file = Path.Combine(Paths.BepInExRootPath, REQUEST_FILE);
            if (!File.Exists(file))
                return false;

            string[] request = File.ReadAllLines(file);
            File.Delete(file);
            if (request.Contains("forcereset"))
                new Harmony(KitPlugin.GUID + ".autotest").PatchAll(typeof(ForceResetPatch));
            if (!request.Any(l => l.StartsWith(VISIT)))
                plugin.SelfCheck.Value = true;
            Application.runInBackground = true; // the game pauses itself otherwise, if something else has focus
            plugin.StartCoroutine(Run(request));
            return true;
        }

        // A reset takes days of game time, so this one test makes every region count as due for one.
        [HarmonyPatch(typeof(AreaManager), nameof(AreaManager.IsAreaExpired))]
        private static class ForceResetPatch
        {
            private static void Postfix(string _areaName, ref bool __result)
            {
                if (Region.ForScene(_areaName) != null)
                    __result = true;
            }
        }

        private static void ReportWhatsHere(string[] request)
        {
            Log($"Lying here: {string.Join(", ", RegionItems.InWorld().Select(i => i.ItemID.ToString()).ToArray())}. Enemies: {string.Join(", ", CharacterManager.Instance.Characters.Values.Where(c => c != null && !c.IsLocalPlayer && c.gameObject.activeInHierarchy).Select(c => c.name + (c.IsDead ? " dead" : " alive")).ToArray())}.");
            if (!request.Contains("killenemies"))
                return;
            foreach (Character enemy in CharacterManager.Instance.Characters.Values.Where(c => c != null && !c.IsLocalPlayer && !c.IsDead).ToList())
                enemy.Stats.ReceiveDamage(enemy.Stats.MaxHealth * 100f);
        }

        private static void SetMenuInput(bool on)
        {
            CanvasGroup menu = MenuManager.Instance.MasterCanvas;
            menu.interactable = on;
            menu.blocksRaycasts = on;
        }

        private class StartingNotice : MonoBehaviour
        {
            private const string TEXT = "Starting your test. There's no need to click anything.";
            private const int FONT_SIZE = 30;
            private const float HEIGHT = 80f;
            private const float SCREEN_POSITION = 0.2f;

            private void OnGUI()
            {
                var style = new GUIStyle(GUI.skin.box) { fontSize = FONT_SIZE, alignment = TextAnchor.MiddleCenter };
                GUI.Box(new Rect(0f, Screen.height * SCREEN_POSITION, Screen.width, HEIGHT), TEXT, style);
            }
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
                // Continuing under the startup video would stop it from ever bringing the HUD and menus back.
                if (StartupVideo.StartupVideoWillPlay || MenuManager.Instance.IsStartupVideoActive)
                    return false;
                return continueButton != null && continueButton.gameObject.activeInHierarchy && continueButton.interactable;
            }, "the main menu is ready", result);
            if (!ok)
                yield break;

            // Clicks here could interrupt the steps below, which press the menu's buttons directly.
            SetMenuInput(false);
            var notice = KitPlugin.Instance.gameObject.AddComponent<StartingNotice>();
            yield return new WaitForSecondsRealtime(2f);
            Log("Pressing Continue.");
            continueButton.onClick.Invoke();
            yield return new WaitForSecondsRealtime(2f);

            // The Test Region button asks by save ID, because two characters can share a name. The test script asks by name.
            string wantedId = request.Where(l => l.StartsWith(CHARACTER_ID)).Select(l => l.Substring(CHARACTER_ID.Length).Trim()).FirstOrDefault();
            string wanted = request.Where(l => l.StartsWith("character ")).Select(l => l.Substring(10).Trim()).FirstOrDefault();
            List<CharacterSaveInstanceHolder> saves = SaveManager.Instance.CharacterSaves.Where(s => s.MostRecentInstance != null).ToList();
            SetMenuInput(true);
            Object.Destroy(notice);
            CharacterSaveInstanceHolder chosen =wantedId != null ? saves.FirstOrDefault(s => s.CharacterUID == wantedId)
                : wanted != null ? saves.FirstOrDefault(s => s.MostRecentInstance.CharSave.PSave.Name == wanted)
                : saves.FirstOrDefault();
            if (chosen == null)
            {
                Log($"FAIL no character called '{wantedId ?? wanted}'. There are: {string.Join(", ", saves.Select(s => s.MostRecentInstance.CharSave.PSave.Name).ToArray())}");
                yield break;
            }
            var panel = (CharacterSelectionPanel)AccessTools.Field(typeof(MainScreen), "m_characterSelection").GetValue(menu);
            Log($"Choosing {chosen.MostRecentInstance.CharSave.PSave.Name}.");
            panel.OnCharacterClicked(chosen);

            yield return WaitFor(() => Player() != null && Loaded() && SceneManager.GetActiveScene().name != TRANSITION_SCENE, "the character is in the world", result);
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
                string startAt = request.Where(l => l.StartsWith(START_AT)).Select(l => l.Substring(START_AT.Length).Trim()).FirstOrDefault();
                Vector3 spot;
                if (ok && KitPlugin.TryParseVector(startAt, out spot))
                {
                    Player().Teleport(spot + Vector3.up * START_AT_HEIGHT, Player().transform.rotation);
                    Log($"Started from {spot}, as asked.");
                }
                if (ok)
                    Log($"FINISHED, in {visit}, for a person to play.");
                if (ok && request.Contains("trydoor"))
                    yield return TryUsing();
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
                ReportWhatsHere(request);
                if (request.Contains("takeitems"))
                    yield return TakeItems();
                if (request.Contains("tryprops"))
                    yield return TryInteractives();
                if (request.Contains("trytalking"))
                    yield return TryTalking();
            }

            if (regions.Length > 0 && request.Contains("dieinregion"))
            {
                string died = SceneManager.GetActiveScene().name;
                Log($"Dying in {died}, with {Player().Inventory.ContainedSilver} silver and {BagCount()} items.");
                Player().Invincible = false;
                Player().Stats.ReceiveDamage(Player().Stats.MaxHealth * 10f); // as an enemy would, which sets off the defeat
                ProloguePanel defeatScreen = null;
                yield return WaitFor(() => (defeatScreen = Resources.FindObjectsOfTypeAll<ProloguePanel>().FirstOrDefault(p => p.IsDisplayed)) != null, "the defeat screen is showing", result);
                if (!ok)
                    yield break;
                yield return new WaitForSecondsRealtime(2f);
                yield return Screenshot("defeat_screen");
                yield return new WaitForSecondsRealtime(1f);
                defeatScreen.GoToNextPage();
                bool returned = false;
                yield return WaitFor(() => SceneManager.GetActiveScene().name == home && Player() != null && Loaded(), "arriving back in " + home, r => returned = r);
                if (!returned)
                    yield break;
                yield return PressAnyKey(result);
                if (!ok)
                    yield break;
                yield return new WaitForSecondsRealtime(3f);
                Log($"Died in {died} and came back in {SceneManager.GetActiveScene().name}, alive: {Player().IsDead == false}, health {Player().Stats.CurrentHealth:0} of {Player().Stats.ActiveMaxHealth:0}, stamina {Player().Stats.CurrentStamina:0}, with {Player().Inventory.ContainedSilver} silver and {BagCount()} items.");
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

        // What a player does on arriving, by the game's own routes: sees the HUD, walks up to the water and a return
        // door, gets their prompts, and uses them. Only when asked, because it moves the character.
        private static IEnumerator TryUsing()
        {
            yield return new WaitForSecondsRealtime(4f);
            Log(MenuManager.Instance.MasterCanvas.alpha == 1f && MenuManager.Instance.MasterCanvas.interactable && !Global.HideUI
                ? "The HUD and menus are showing." : $"FAIL the HUD and menus are hidden: alpha {MenuManager.Instance.MasterCanvas.alpha}, interactable {MenuManager.Instance.MasterCanvas.interactable}, hidden {Global.HideUI}.");

            WaterRender water = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WaterRender>(true)).FirstOrDefault();
            if (water != null)
            {
                Player().Teleport(water.transform.position + Vector3.up * 0.2f, Player().transform.rotation);
                yield return new WaitForSecondsRealtime(3f);
                InteractionTriggerBase drink = Player().Interactable;
                Log(drink != null && !string.IsNullOrEmpty(Player().PressText + Player().HoldText) ? $"In the water, the prompt says '{Player().PressText}' '{Player().HoldText}'." : "FAIL no prompt in the water.");
                yield return Screenshot("try_water");
                if (drink != null)
                    drink.TryActivateBasicAction(Player());
                yield return new WaitForSecondsRealtime(3f);
            }

            GameObject door = GameObject.Find("OCK_ReturnDoor");
            if (door == null)
            {
                Log("FAIL no return door to try.");
                yield break;
            }
            Player().Teleport(door.transform.position + door.transform.forward * 1.2f, Quaternion.LookRotation(-door.transform.forward));
            yield return new WaitForSecondsRealtime(3f);
            InteractionTriggerBase trigger = Player().Interactable;
            Log(trigger != null ? $"Beside the return door, the prompt says '{Player().PressText}'." : "FAIL no prompt beside the return door.");
            yield return Screenshot("try_door");
            if (trigger == null)
                yield break;
            string region = SceneManager.GetActiveScene().name;
            trigger.TryActivateBasicAction(Player());
            bool left = false;
            yield return WaitFor(() => SceneManager.GetActiveScene().name != region && SceneManager.GetActiveScene().name != TRANSITION_SCENE && Player() != null && Loaded(), "the return door led out of the region", r => left = r);
            Log($"Now in {SceneManager.GetActiveScene().name}.");
        }

        // What the game's take prompt does when pressed, which is more than the test can say about the prompt being seen.
        private static IEnumerator TakeItems()
        {
            List<Item> lying = RegionItems.InWorld();
            Log($"{lying.Count} region items lying here: {string.Join(", ", lying.Select(i => i.Name + " at " + i.transform.position).ToArray())}.");
            foreach (Item item in lying)
            {
                int before = Player().Inventory.ItemCount(item.ItemID);
                Player().Inventory.TakeItem(item, false);
                yield return new WaitForSecondsRealtime(2f);
                int after = Player().Inventory.ItemCount(item.ItemID);
                Log(after > before ? $"Took {item.ItemID}: the bag holds {after}." : $"FAIL couldn't take {item.ItemID}: the bag holds {after}.");
            }
        }

        // Walks up to each character that can talk, and starts the conversation, as pressing the prompt does.
        private static IEnumerator TryTalking()
        {
            NPCInteraction[] talkers = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NPCInteraction>(true)).ToArray();
            Log($"{talkers.Length} characters can talk here.");
            foreach (NPCInteraction talker in talkers)
            {
                Transform npc = talker.DialogueActor.transform;
                InteractionTriggerBase trigger = null;
                for (int tries = 0; tries < 4 && trigger == null; tries++) // they can wander off while the prompt appears
                {
                    Player().Teleport(npc.position + npc.forward * 1.5f, Quaternion.LookRotation(-npc.forward));
                    yield return new WaitForSecondsRealtime(3f);
                    trigger = Player().Interactable;
                }
                Log(trigger != null ? $"Beside {talker.DialogueActor.name}, the prompt says '{Player().PressText}' '{Player().HoldText}'." : $"FAIL no prompt beside {talker.DialogueActor.name}.");
                yield return Screenshot("talk_prompt");
                if (trigger == null)
                    continue;
                trigger.TryActivateBasicAction(Player());
                yield return new WaitForSecondsRealtime(4f);
                Log(Player().CharacterUI.IsDialogueInProgress ? "A conversation has started." : "FAIL no conversation started.");
                yield return Screenshot("talk_line");
                yield return new WaitForSecondsRealtime(2f);
                var answers = (List<DialogueAnswer>)Traverse.Create(Player().CharacterUI.DialoguePanel).Field("m_dialogueOptions").GetValue();
                answers[0].onClick.Invoke();
                yield return new WaitForSecondsRealtime(4f);
                yield return Screenshot("talk_answer");
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        // Walks up to each chest and gathering spot, reads its prompt, and does what pressing it does.
        private static IEnumerator TryInteractives()
        {
            List<Item> here = InteractiveDonor.InWorld();
            Log($"{here.Count} chests and gathering spots here: {string.Join(", ", here.Select(i => i.name).ToArray())}.");
            foreach (Item thing in here)
            {
                Player().Teleport(thing.transform.position + thing.transform.forward * 1.2f + Vector3.up * 0.2f, Quaternion.LookRotation(-thing.transform.forward));
                yield return new WaitForSecondsRealtime(3f);
                InteractionTriggerBase trigger = Player().Interactable;
                var container = thing as SelfFilledItemContainer;
                // A gathering spot already picked, on an earlier visit, rightly has nothing to offer until it regrows.
                if (trigger == null && thing is Gatherable && !container.HasAnyDrops)
                {
                    Log($"{thing.name} is used up, and offers no prompt.");
                    continue;
                }
                Log(trigger != null ? $"Beside {thing.name}, the prompt says '{Player().PressText}' '{Player().HoldText}'." : $"FAIL no prompt beside {thing.name}.");
                yield return Screenshot("try_" + thing.name);
                if (container == null)
                    continue;
                int before = BagCount();
                container.ResponsibleCharacter = Player();
                container.GenerateContents();
                yield return new WaitForSecondsRealtime(3f);
                var box = (ItemContainer)container;
                Log($"{thing.name} now holds {box.ItemCount} items and {box.ContainedSilver} silver, and the bag went from {before} to {BagCount()} items.");
                Log(thing is Gatherable ? (BagCount() > before ? $"Gathered from {thing.name}." : $"FAIL gathered nothing from {thing.name}.") : (box.ItemCount > 0 || box.ContainedSilver > 0 ? $"{thing.name} has something in it." : $"FAIL {thing.name} is empty."));
            }
        }

        private static int BagCount()
        {
            return Player().Inventory.Pouch.ItemCount + (Player().Inventory.EquippedBag != null ? Player().Inventory.EquippedBag.Container.ItemCount : 0);
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
