using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>
    /// A player without a region can't load its scene, and would wait forever while everyone else moves on. So each
    /// player tells the room which regions they have, a door to one that someone lacks says so rather than opening,
    /// and a host in a region removes a guest who joins without it. Someone with no kit at all tells the room
    /// nothing, which counts as having no regions. Not yet tried with a real second player.
    /// </summary>
    internal static class CoopGuard
    {
        private const string PROPERTY = "ock_regions";
        private const char SEPARATOR = ';';
        private const float SECONDS_TO_HEAR_FROM_GUEST = 8f;

        public static void Patch(Harmony harmony)
        {
            harmony.PatchAll(typeof(JoinedRoomPatch));
            harmony.PatchAll(typeof(PlayerConnectedPatch));
            harmony.PatchAll(typeof(AreaSwitchPatch));
        }

        /// <summary>What a player's regions look like to the rest of the room.</summary>
        internal static string Describe(IEnumerable<string> regionSignatures)
        {
            return string.Join(SEPARATOR.ToString(), regionSignatures.ToArray());
        }

        internal static bool Has(string description, string regionSignature)
        {
            return !string.IsNullOrEmpty(description) && description.Split(SEPARATOR).Contains(regionSignature);
        }

        private static bool Lacks(PhotonPlayer player, Region region)
        {
            return !Has(player.CustomProperties[PROPERTY] as string, region.Signature);
        }

        private static void Tell(string message)
        {
            KitPlugin.Log.LogWarning(message);
            Character local = CharacterManager.Instance != null ? CharacterManager.Instance.GetFirstLocalCharacter() : null;
            if (local != null && local.CharacterUI != null)
                local.CharacterUI.ShowInfoNotification(message);
        }

        private static IEnumerator CheckGuestSoon(PhotonPlayer guest)
        {
            yield return new WaitForSecondsRealtime(SECONDS_TO_HEAR_FROM_GUEST);
            Region here = Region.ForScene(SceneManager.GetActiveScene().name);
            if (here == null || !PhotonNetwork.isMasterClient || !PhotonNetwork.playerList.Contains(guest) || !Lacks(guest, here))
                yield break;
            Tell($"{guest.NickName} doesn't have this version of {here.DisplayName}, so they couldn't join.");
            PhotonNetwork.CloseConnection(guest);
        }

        [HarmonyPatch(typeof(NetworkLevelLoader), nameof(NetworkLevelLoader.OnJoinedRoom))]
        private static class JoinedRoomPatch
        {
            private static void Prefix()
            {
                try
                {
                    PhotonNetwork.player.SetCustomProperties(new Hashtable { { PROPERTY, Describe(Region.All.Select(r => r.Signature)) } });
                }
                catch (System.Exception e)
                {
                    KitPlugin.Log.LogError("Couldn't tell the room which regions are installed: " + e);
                }
            }
        }

        [HarmonyPatch(typeof(NetworkLevelLoader), nameof(NetworkLevelLoader.OnPhotonPlayerConnected))]
        private static class PlayerConnectedPatch
        {
            private static void Postfix(PhotonPlayer _newPlayer)
            {
                if (PhotonNetwork.isMasterClient && Region.ForScene(SceneManager.GetActiveScene().name) != null)
                    KitPlugin.Instance.StartCoroutine(CheckGuestSoon(_newPlayer));
            }
        }

        [HarmonyPatch(typeof(CharacterManager), nameof(CharacterManager.RequestAreaSwitch))]
        private static class AreaSwitchPatch
        {
            private static bool Prefix(Area _areaToSwitchTo)
            {
                Region region = _areaToSwitchTo != null ? Region.ForScene(_areaToSwitchTo.SceneName) : null;
                if (region == null)
                    return true;
                List<string> lacking = PhotonNetwork.otherPlayers.Where(p => Lacks(p, region)).Select(p => p.NickName).ToList();
                if (lacking.Count == 0)
                    return true;
                Tell($"{string.Join(", ", lacking.ToArray())} can't come to {region.DisplayName}, because they don't have this version of it.");
                return false;
            }
        }
    }
}
