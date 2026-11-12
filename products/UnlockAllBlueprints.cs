using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Unlock All Blueprints & Auto Workbench Range", "YourName", "3.4.0")]
    [Description("Unlocks all blueprints without fragments/pages/books and equips the native Range Workbench Upgrade on all workbenches.")]
    public class UnlockAllBlueprints : RustPlugin
    {
        private const string RangeUpgradeShortname = "workbench.upgrade.range";

        // Tracks workbenches temporarily unlocked for hammer pickup
        private readonly HashSet<NetworkableId> _pickupUnlocked = new HashSet<NetworkableId>();

        #region Oxide Hooks

        private void OnServerInitialized()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                UnlockEverything(player);
            }

            EquipAllWorkbenches();

            // Periodic safety check every 3 seconds so newly placed benches always get the upgrade
            timer.Every(3.0f, EquipAllWorkbenches);
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            UnlockEverything(player);
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            UnlockEverything(player);
        }

        // Wait 0.25s after placement so the workbench inventory finishes initializing
        private void OnEntitySpawned(Workbench workbench)
        {
            timer.Once(0.25f, () => EquipRangeUpgrade(workbench));
        }

        private void OnEntityKill(Workbench workbench)
        {
            if (workbench != null && workbench.net != null)
            {
                _pickupUnlocked.Remove(workbench.net.ID);
            }
        }

        // Prevent players from manually removing/farming the free Range Upgrade item
        private object CanMoveItem(Item item, PlayerInventory playerLoot, ItemContainerId targetContainerId, int targetSlot, int amount)
        {
            if (item != null && item.info.shortname == RangeUpgradeShortname && item.parent?.entityOwner is Workbench)
            {
                return false;
            }
            return null;
        }

        // Fires BEFORE Rust checks if the workbench inventory is empty when holding E with a hammer
        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null || !input.WasJustPressed(BUTTON.USE))
                return;

            Item activeItem = player.GetActiveItem();
            if (activeItem == null || activeItem.info.shortname != "hammer")
                return;

            if (Physics.Raycast(player.eyes.HeadRay(), out RaycastHit hit, 3.5f))
            {
                Workbench workbench = hit.GetEntity() as Workbench;
                if (workbench != null && !workbench.IsDestroyed)
                {
                    PrepareWorkbenchForPickup(workbench);
                }
            }
        }

        // Also allow hitting a workbench with a hammer to clear the auto-upgrade for 5 seconds
        private void OnHammerHit(BasePlayer player, HitInfo info)
        {
            Workbench workbench = info?.HitEntity as Workbench;
            if (workbench != null && !workbench.IsDestroyed)
            {
                PrepareWorkbenchForPickup(workbench);
            }
        }

        #endregion

        #region Core Methods

        private void UnlockEverything(BasePlayer player)
        {
            if (player == null || player.blueprints == null)
                return;

            player.blueprints.UnlockAll();
        }

        private void EquipAllWorkbenches()
        {
            foreach (BaseNetworkable entity in BaseNetworkable.serverEntities)
            {
                if (entity is Workbench workbench)
                {
                    EquipRangeUpgrade(workbench);
                }
            }
        }

        private void PrepareWorkbenchForPickup(Workbench workbench)
        {
            if (workbench == null || workbench.IsDestroyed || workbench.inventory == null || workbench.net == null)
                return;

            NetworkableId benchId = workbench.net.ID;
            _pickupUnlocked.Add(benchId);

            for (int i = workbench.inventory.itemList.Count - 1; i >= 0; i--)
            {
                Item item = workbench.inventory.itemList[i];
                if (item != null && item.info.shortname == RangeUpgradeShortname)
                {
                    item.RemoveFromContainer();
                    item.Remove();
                }
            }

            timer.Once(5.0f, () =>
            {
                _pickupUnlocked.Remove(benchId);
                if (workbench != null && !workbench.IsDestroyed)
                {
                    EquipRangeUpgrade(workbench);
                }
            });
        }

        private void EquipRangeUpgrade(Workbench workbench)
        {
            if (workbench == null || workbench.IsDestroyed || workbench.inventory == null || workbench.net == null)
                return;

            if (_pickupUnlocked.Contains(workbench.net.ID))
                return;

            foreach (Item existing in workbench.inventory.itemList)
            {
                if (existing.info.shortname == RangeUpgradeShortname)
                    return;
            }

            Item upgradeItem = ItemManager.CreateByName(RangeUpgradeShortname, 1);
            if (upgradeItem != null)
            {
                if (!upgradeItem.MoveToContainer(workbench.inventory))
                {
                    upgradeItem.Remove();
                }
                else
                {
                    workbench.SendNetworkUpdateImmediate();
                }
            }
        }

        #endregion
    }
}
