using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RemoveTool", "Custom", "2.2.0")]
    [Description("Removes owned items with Salvaged Hammer safely.")]
    public class RemoveTool : RustPlugin
    {
        private const string Tool = "hammer.salvaged";
        private Dictionary<string, ItemDefinition> items = new Dictionary<string, ItemDefinition>();
        private Dictionary<ulong, float> cooldowns = new Dictionary<ulong, float>();
        private Dictionary<Vector3, float> safeSpots = new Dictionary<Vector3, float>();

        private void OnServerInitialized()
        {
            items.Clear();
            foreach (var def in ItemManager.GetItemDefinitions())
            {
                var dep = def.GetComponent<ItemModDeployable>();
                if (dep?.entityPrefab != null && !string.IsNullOrEmpty(dep.entityPrefab.resourcePath))
                    items[dep.entityPrefab.resourcePath] = def;
            }
        }

        private object OnEntityGroundMissing(BaseEntity ent)
        {
            if (ent == null || ent is BuildingBlock) return null;
            foreach (var s in safeSpots)
                if (Time.realtimeSinceStartup - s.Value <= 2f && Vector3.Distance(ent.transform.position, s.Key) <= 8f)
                    return true;
            return null;
        }

        private object OnEntityTakeDamage(BaseCombatEntity ent, HitInfo info)
        {
            var p = info?.InitiatorPlayer;
            if (ent != null && p != null && p.GetActiveItem()?.info.shortname == Tool && ent.OwnerID == p.userID)
            {
                info.damageTypes.ScaleAll(0f);
                return true;
            }
            return null;
        }

        private void OnPlayerAttack(BasePlayer p, HitInfo info)
        {
            if (p == null || info?.HitEntity == null || p.GetActiveItem()?.info.shortname != Tool) return;
            float now = Time.realtimeSinceStartup;
            if (cooldowns.ContainsKey(p.userID) && now - cooldowns[p.userID] < 0.35f) return;

            RaycastHit hit;
            BaseEntity target = Physics.Raycast(p.eyes.HeadRay(), out hit, 3.5f) ? hit.GetEntity() : info.HitEntity;
            if (target == null) target = info.HitEntity;
            if (target.HasParent() && !(target is Door) && target.GetParentEntity() != null) target = target.GetParentEntity();

            if ((!(target is BuildingBlock) && !(target is DecayEntity)) || target.OwnerID != p.userID || target.IsDestroyed) return;

            cooldowns[p.userID] = now;
            Vector3 pos = target.transform.position;
            safeSpots[pos] = now;

            NextTick(() =>
            {
                if (target == null || target.IsDestroyed) return;
                var nearby = new List<BaseEntity>();
                Vis.Entities(pos, 8f, nearby);
                foreach (var n in nearby)
                {
                    if (n == null || n == target || n.IsDestroyed || n is BuildingBlock) continue;
                    n.CancelInvoke("GroundCheck");
                    var stab = n as StabilityEntity;
                    if (stab != null) { stab.grounded = true; stab.cachedStability = 1f; stab.CancelInvoke("StabilityCheck"); }
                }

                (target as StorageContainer)?.DropItems();
                ItemDefinition def;
                if (!items.TryGetValue(target.PrefabName, out def)) def = (target as BaseCombatEntity)?.pickup.itemTarget;
                if (def != null && p != null && p.IsConnected)
                {
                    var item = ItemManager.Create(def, 1, target.skinID);
                    if (item != null) { item.condition = item.maxCondition; p.GiveItem(item, BaseEntity.GiveItemReason.PickedUp); }
                }
                target.Kill(BaseNetworkable.DestroyMode.None);
            });
        }
    }
}
