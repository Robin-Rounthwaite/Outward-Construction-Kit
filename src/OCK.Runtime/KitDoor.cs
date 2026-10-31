using UnityEngine;

namespace OutwardConstructionKit.Runtime
{
    /// <summary>A plain slab carrying the game's own area-switch interaction. Local +Z faces the player.</summary>
    internal static class KitDoor
    {
        public static GameObject Build(string name, Vector3 pos, Quaternion rot, int areaId, int spawnIndex)
        {
            var root = new GameObject(name);
            root.transform.position = pos;
            root.transform.rotation = rot;

            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Slab";
            Object.Destroy(slab.GetComponent<Collider>());
            slab.transform.SetParent(root.transform, false);
            slab.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            slab.transform.localScale = new Vector3(1.6f, 2.8f, 0.2f);
            var renderer = slab.GetComponent<Renderer>();
            var shader = Shader.Find("Standard");
            if (shader != null) renderer.material = new Material(shader);
            renderer.material.color = new Color(0.25f, 0.45f, 0.75f);

            // Built inactive so that Awake runs with every field set, as it does for a door saved in a
            // scene: InteractionSwitchArea looks up its Area there and only once.
            var holder = new GameObject("Interaction");
            holder.SetActive(false);
            holder.transform.SetParent(root.transform, false);
            holder.transform.localPosition = new Vector3(0f, 1.3f, 0.35f);

            var trigger = holder.AddComponent<InteractionTriggerBase>();
            var activator = holder.AddComponent<InteractionActivator>();
            var interaction = holder.AddComponent<InteractionSwitchArea>();

            trigger.GenerateColliderIfNone = true;
            trigger.DetectionColliderRadius = 0.8f;
            trigger.SetActivator(activator);
            activator.BasicInteraction = interaction;
            interaction.Area = (AreaManager.AreaEnum)areaId;
            interaction.SpawnPoint = spawnIndex;

            holder.SetActive(true);

            // Awake derives this from the travel time, so it can only be forced afterwards.
            // RequestAreaSwitch does its own networking, and this door has no network UID.
            trigger.SendActivationOverNetwork = false;
            return root;
        }
    }
}
