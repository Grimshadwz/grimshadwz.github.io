using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("AutoWaterTreatment", "Paradox Studios", "1.0.4")]
    [Description("Keeps Water Treatment active and roadside water pipes 100% full at all times.")]
    public class AutoWaterTreatment : RustPlugin
    {
        private readonly List<BaseEntity> _waterEntities = new List<BaseEntity>();
        private readonly HashSet<ItemContainer> _waterContainers = new HashSet<ItemContainer>();
        private Timer _loopTimer;
        private ItemDefinition _cleanWaterDef;
        private const int MaxWaterAmount = 50000;

        private void OnServerInitialized()
        {
            _cleanWaterDef = ItemManager.FindItemDefinition("water");
            ScanEntities();
            KeepWaterFlowing();
            _loopTimer = timer.Every(5f, KeepWaterFlowing);
        }

        private void Unload()
        {
            _loopTimer?.Destroy();
        }

        private void ScanEntities()
        {
            _waterEntities.Clear();
            _waterContainers.Clear();

            Vector3 wtPos = Vector3.zero;
            bool foundMonument = false;

            if (TerrainMeta.Path != null && TerrainMeta.Path.Monuments != null)
            {
                foreach (var monument in TerrainMeta.Path.Monuments)
                {
                    if (monument != null && monument.name != null && monument.name.ToLower().Contains("water_treatment"))
                    {
                        wtPos = monument.transform.position;
                        foundMonument = true;
                        break;
                    }
                }
            }

            foreach (var net in BaseNetworkable.serverEntities)
            {
                var ent = net as BaseEntity;
                if (ent == null) continue;

                string prefab = ent.ShortPrefabName.ToLower();
                string typeName = ent.GetType().Name.ToLower();

                bool isWaterSystem =
                    prefab.Contains("gearbox") ||
                    prefab.Contains("water_pipe") ||
                    prefab.Contains("waterstation") ||
                    prefab.Contains("water_station") ||
                    prefab.Contains("roadside") ||
                    prefab.Contains("hydrant") ||
                    typeName.Contains("gearbox") ||
                    typeName.Contains("watertreatment") ||
                    typeName.Contains("waterpipe") ||
                    typeName.Contains("pressur");

                if (!isWaterSystem && foundMonument && Vector3.Distance(ent.transform.position, wtPos) < 180f)
                {
                    if (prefab.Contains("tank") || prefab.Contains("valve") || prefab.Contains("pump") || prefab.Contains("water"))
                    {
                        isWaterSystem = true;
                    }
                }

                if (isWaterSystem)
                {
                    _waterEntities.Add(ent);
                    var containerOwner = ent as IItemContainerEntity;
                    if (containerOwner != null && containerOwner.inventory != null)
                    {
                        _waterContainers.Add(containerOwner.inventory);
                    }
                }
            }

            Puts($"[AutoWaterTreatment] Active on {_waterEntities.Count} water system entities.");
        }

        private void RefillContainer(ItemContainer container)
        {
            if (container == null || _cleanWaterDef == null) return;

            int targetAmount = container.maxStackSize > 0 ? container.maxStackSize : MaxWaterAmount;
            if (targetAmount < MaxWaterAmount) targetAmount = MaxWaterAmount;

            var waterItem = container.FindItemByItemID(_cleanWaterDef.itemid);
            if (waterItem == null)
            {
                waterItem = ItemManager.Create(_cleanWaterDef, targetAmount);
                if (waterItem != null && !waterItem.MoveToContainer(container))
                {
                    waterItem.Remove();
                }
            }
            else if (waterItem.amount < targetAmount)
            {
                waterItem.amount = targetAmount;
                waterItem.MarkDirty();
            }
        }

        private void KeepWaterFlowing()
        {
            foreach (var ent in _waterEntities)
            {
                if (ent == null || ent.IsDestroyed) continue;

                ent.flags |= BaseEntity.Flags.On;
                ent.flags |= BaseEntity.Flags.Reserved1;
                ent.flags |= BaseEntity.Flags.Reserved2;
                ent.flags |= BaseEntity.Flags.Reserved3;
                ent.flags |= BaseEntity.Flags.Reserved4;

                var type = ent.GetType();
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                float maxVal = 1000f;
                foreach (var f in fields)
                {
                    string fname = f.Name.ToLower();
                    if ((fname.Contains("maxpressure") || fname.Contains("maxcharge") || fname.Contains("maxwater")) && f.FieldType == typeof(float))
                    {
                        float val = (float)f.GetValue(ent);
                        if (val > 0f) maxVal = val;
                    }
                }

                foreach (var f in fields)
                {
                    string fname = f.Name.ToLower();
                    if ((fname.Contains("pressure") || fname.Contains("charge") || fname == "water" || fname == "currentwater") && !fname.Contains("max") && f.FieldType == typeof(float))
                    {
                        f.SetValue(ent, maxVal);
                    }
                }

                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var m in methods)
                {
                    if (m.GetParameters().Length == 0)
                    {
                        string mname = m.Name.ToLower();
                        if (mname.Contains("pressur") || mname.Contains("updatestate") || mname.Contains("refresh") || mname.Contains("fill"))
                        {
                            try { m.Invoke(ent, null); } catch { }
                        }
                    }
                }

                var containerOwner = ent as IItemContainerEntity;
                if (containerOwner != null && containerOwner.inventory != null)
                {
                    RefillContainer(containerOwner.inventory);
                }

                ent.SendNetworkUpdateImmediate();
            }
        }

        // Instantly top off water the moment a player or hose draws any liquid from a roadside pipe
        private void OnItemUse(Item item, int amountToUse)
        {
            if (item == null || item.parent == null) return;
            if (_waterContainers.Contains(item.parent))
            {
                NextTick(() => RefillContainer(item.parent));
            }
        }

        private void OnLoseCondition(Item item, ref float amount)
        {
            if (item == null || item.info == null) return;
            if (item.info.shortname == "gears")
            {
                var parentEnt = item.GetRootContainer()?.entityOwner;
                if (parentEnt != null && parentEnt.ShortPrefabName.ToLower().Contains("gearbox"))
                {
                    amount = 0f;
                }
            }
        }

        private object CanMoveItem(Item item, PlayerInventory playerLoot, ItemContainerId targetContainerId, int targetSlot, int amount)
        {
            var container = playerLoot?.FindContainer(targetContainerId);
            var entity = container?.entityOwner;

            if (entity != null && entity.ShortPrefabName.ToLower().Contains("gearbox"))
            {
                return false;
            }

            return null;
        }
    }
}
