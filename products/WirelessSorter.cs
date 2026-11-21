using System.Collections.Generic;
using Facepunch;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("WirelessSorter", "Paradox Studios", "3.0.0")]
    [Description("TC-free wireless auto-sorting system using Storage Adapters, category presets, custom names, and auto-smelting.")]
    public class WirelessSorter : RustPlugin
    {
        private const string UI_PANEL = "WirelessSorter_UI";
        private const float SCAN_RADIUS = 25f;

        private static readonly string[] CategoryPresets = new string[]
        {
            "NONE",
            "WEAPONS",
            "AMMO",
            "MEDICAL",
            "RESOURCES",
            "COMPONENTS",
            "CONSTRUCTION",
            "ELECTRICAL",
            "ATTIRE",
            "TOOLS",
            "FOOD",
            "MISC"
        };

        private StoredData _data;
        private readonly Dictionary<ulong, StorageContainer> _openBoxes = new Dictionary<ulong, StorageContainer>();

        #region Data Persistence
        private class StoredData
        {
            public HashSet<ulong> DropBoxes = new HashSet<ulong>();
            public HashSet<ulong> AutoSmeltBoxes = new HashSet<ulong>();
            // Maps StorageAdapter NetworkID -> Assigned Category/Name
            public Dictionary<ulong, string> AdapterCategories = new Dictionary<ulong, string>();
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _data);

        private void LoadData()
        {
            _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name) ?? new StoredData();
        }
        #endregion

        #region Lifecycle Hooks
        private void OnServerInitialized()
        {
            LoadData();
            timer.Every(3f, ProcessAllDropBoxes);
        }

        private void Unload()
        {
            SaveData();
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, UI_PANEL);
            }
        }

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null) return;

            StorageContainer box = entity as StorageContainer;
            if (box == null || box.net == null || box is BuildingPrivlidge) return;

            if (box.OwnerID != player.userID && !player.IsAdmin) return;

            _openBoxes[player.userID] = box;
            DrawSorterUI(player, box);
        }

        private void OnPlayerLootEnd(PlayerLoot inventory)
        {
            BasePlayer player = inventory?.GetComponent<BasePlayer>();
            if (player == null) return;

            _openBoxes.Remove(player.userID);
            CuiHelper.DestroyUi(player, UI_PANEL);
        }
        #endregion

        #region Storage Adapter Helpers
        private IndustrialStorageAdaptor GetAttachedAdapter(StorageContainer box)
        {
            if (box == null || box.children == null) return null;
            foreach (BaseEntity child in box.children)
            {
                if (child is IndustrialStorageAdaptor adapter && !adapter.IsDestroyed)
                {
                    return adapter;
                }
            }
            return null;
        }

        private string GetAdapterCategory(IndustrialStorageAdaptor adapter)
        {
            if (adapter == null || adapter.net == null) return "NONE";
            if (_data.AdapterCategories.TryGetValue(adapter.net.ID.Value, out string cat))
                return cat;
            return "NONE";
        }
        #endregion

        #region CUI Rendering & Button Callbacks
        private void DrawSorterUI(BasePlayer player, StorageContainer box)
        {
            CuiHelper.DestroyUi(player, UI_PANEL);

            ulong boxId = box.net.ID.Value;
            bool isDropBox = _data.DropBoxes.Contains(boxId);
            bool isSmeltOn = _data.AutoSmeltBoxes.Contains(boxId);

            IndustrialStorageAdaptor adapter = GetAttachedAdapter(box);
            bool hasAdapter = adapter != null;
            string currentCat = GetAdapterCategory(adapter);

            CuiElementContainer container = new CuiElementContainer();

            // Slim dark panel positioned neatly above the top row of storage slots
            container.Add(new CuiPanel
            {
                Image = { Color = "0.12 0.13 0.15 0.95" },
                RectTransform = { AnchorMin = "0.65 0.875", AnchorMax = "0.945 0.965" },
                CursorEnabled = false
            }, "Overlay", UI_PANEL);

            // Top Row: Header + Drop Box + Smelt + Sort Now
            container.Add(new CuiLabel
            {
                Text = { Text = "WIRELESS SORTER", FontSize = 9, Align = TextAnchor.MiddleLeft, Color = "0.85 0.85 0.85 1" },
                RectTransform = { AnchorMin = "0.02 0.54", AnchorMax = "0.33 0.94" }
            }, UI_PANEL);

            container.Add(new CuiButton
            {
                Button = { Command = "wirelesssorter.ui drop", Color = isDropBox ? "0.22 0.55 0.25 1" : "0.25 0.27 0.30 1" },
                RectTransform = { AnchorMin = "0.34 0.54", AnchorMax = "0.56 0.94" },
                Text = { Text = isDropBox ? "DROP: ON" : "SET DROP", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UI_PANEL);

            container.Add(new CuiButton
            {
                Button = { Command = "wirelesssorter.ui smelt", Color = isSmeltOn ? "0.22 0.55 0.25 1" : "0.25 0.27 0.30 1" },
                RectTransform = { AnchorMin = "0.58 0.54", AnchorMax = "0.78 0.94" },
                Text = { Text = isSmeltOn ? "SMELT: ON" : "SMELT: OFF", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UI_PANEL);

            container.Add(new CuiButton
            {
                Button = { Command = "wirelesssorter.ui now", Color = "0.20 0.42 0.65 1" },
                RectTransform = { AnchorMin = "0.80 0.54", AnchorMax = "0.98 0.94" },
                Text = { Text = "SORT NOW", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UI_PANEL);

            // Bottom Row: Storage Adapter Category Selector
            if (!hasAdapter)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = "ATTACH A STORAGE ADAPTER TO ASSIGN CATEGORY", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = "0.65 0.65 0.65 1" },
                    RectTransform = { AnchorMin = "0.02 0.06", AnchorMax = "0.98 0.48" }
                }, UI_PANEL);
            }
            else
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = "ADAPTER:", FontSize = 9, Align = TextAnchor.MiddleLeft, Color = "0.55 0.85 0.55 1" },
                    RectTransform = { AnchorMin = "0.02 0.06", AnchorMax = "0.22 0.48" }
                }, UI_PANEL);

                // Previous Category Button (<)
                container.Add(new CuiButton
                {
                    Button = { Command = "wirelesssorter.ui cat_prev", Color = "0.25 0.27 0.30 1" },
                    RectTransform = { AnchorMin = "0.23 0.06", AnchorMax = "0.31 0.48" },
                    Text = { Text = "<", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, UI_PANEL);

                // Current Category Display / Reset
                string displayColor = currentCat == "NONE" ? "0.22 0.24 0.27 1" : "0.22 0.50 0.30 1";
                container.Add(new CuiButton
                {
                    Button = { Command = "wirelesssorter.ui cat_next", Color = displayColor },
                    RectTransform = { AnchorMin = "0.32 0.06", AnchorMax = "0.89 0.48" },
                    Text = { Text = $"FILTER: {currentCat}", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, UI_PANEL);

                // Next Category Button (>)
                container.Add(new CuiButton
                {
                    Button = { Command = "wirelesssorter.ui cat_next", Color = "0.25 0.27 0.30 1" },
                    RectTransform = { AnchorMin = "0.90 0.06", AnchorMax = "0.98 0.48" },
                    Text = { Text = ">", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, UI_PANEL);
            }

            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("wirelesssorter.ui")]
        private void CmdUiAction(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !arg.HasArgs()) return;

            if (!_openBoxes.TryGetValue(player.userID, out StorageContainer box) || box == null || box.IsDestroyed)
                return;

            if (box.OwnerID != player.userID && !player.IsAdmin) return;

            ulong boxId = box.net.ID.Value;
            string action = arg.GetString(0).ToLower();

            if (action == "drop")
            {
                if (!_data.DropBoxes.Remove(boxId))
                {
                    _data.DropBoxes.Add(boxId);
                }
                else
                {
                    _data.AutoSmeltBoxes.Remove(boxId);
                }
                SaveData();
            }
            else if (action == "smelt")
            {
                if (!_data.AutoSmeltBoxes.Remove(boxId))
                {
                    _data.AutoSmeltBoxes.Add(boxId);
                    _data.DropBoxes.Add(boxId);
                }
                SaveData();
            }
            else if (action == "now")
            {
                ProcessSingleDropBox(box, _data.AutoSmeltBoxes.Contains(boxId));
            }
            else if (action == "cat_next" || action == "cat_prev")
            {
                IndustrialStorageAdaptor adapter = GetAttachedAdapter(box);
                if (adapter != null && adapter.net != null)
                {
                    ulong adapterId = adapter.net.ID.Value;
                    string current = GetAdapterCategory(adapter);
                    int idx = System.Array.IndexOf(CategoryPresets, current);
                    if (idx < 0) idx = 0;

                    if (action == "cat_next")
                        idx = (idx + 1) % CategoryPresets.Length;
                    else
                        idx = (idx - 1 + CategoryPresets.Length) % CategoryPresets.Length;

                    string nextCat = CategoryPresets[idx];
                    if (nextCat == "NONE")
                        _data.AdapterCategories.Remove(adapterId);
                    else
                        _data.AdapterCategories[adapterId] = nextCat;

                    SaveData();
                }
            }

            Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.updated.prefab", player.transform.position);
            DrawSorterUI(player, box);
        }
        #endregion

        #region TC-Free Storage Adapter Sorting Logic
        private void ProcessAllDropBoxes()
        {
            if (_data.DropBoxes.Count == 0) return;

            List<ulong> deadBoxes = new List<ulong>();

            foreach (ulong netId in _data.DropBoxes)
            {
                StorageContainer dropBox = BaseNetworkable.serverEntities.Find(new NetworkableId(netId)) as StorageContainer;
                if (dropBox == null || dropBox.IsDestroyed)
                {
                    deadBoxes.Add(netId);
                    continue;
                }

                ProcessSingleDropBox(dropBox, _data.AutoSmeltBoxes.Contains(netId));
            }

            if (deadBoxes.Count > 0)
            {
                foreach (ulong id in deadBoxes)
                {
                    _data.DropBoxes.Remove(id);
                    _data.AutoSmeltBoxes.Remove(id);
                }
                SaveData();
            }
        }

        private void ProcessSingleDropBox(StorageContainer dropBox, bool useSmelt)
        {
            if (dropBox == null || dropBox.IsDestroyed) return;

            List<BaseEntity> nearbyEntities = Pool.Get<List<BaseEntity>>();
            Vis.Entities(dropBox.transform.position, SCAN_RADIUS, nearbyEntities);

            Dictionary<string, List<StorageContainer>> categorizedBoxes = new Dictionary<string, List<StorageContainer>>();
            List<BaseOven> ovens = new List<BaseOven>();

            foreach (BaseEntity entity in nearbyEntities)
            {
                if (entity == null || entity.IsDestroyed || entity == dropBox) continue;

                // Match strict OwnerID so we never touch neighboring bases or Monument Addons
                if (dropBox.OwnerID != 0 && entity.OwnerID != dropBox.OwnerID) continue;

                if (entity is BaseOven oven)
                {
                    if (oven.ShortPrefabName.Contains("furnace"))
                        ovens.Add(oven);
                }
                else if (entity is StorageContainer container && !(container is BuildingPrivlidge))
                {
                    if (container.net == null || _data.DropBoxes.Contains(container.net.ID.Value)) continue;

                    // Check if this box has an attached Industrial Storage Adapter with a category assigned
                    IndustrialStorageAdaptor adapter = GetAttachedAdapter(container);
                    if (adapter == null) continue;

                    string cat = GetAdapterCategory(adapter);
                    if (string.IsNullOrEmpty(cat) || cat == "NONE") continue;

                    if (!categorizedBoxes.TryGetValue(cat, out List<StorageContainer> list))
                    {
                        list = new List<StorageContainer>();
                        categorizedBoxes[cat] = list;
                    }
                    list.Add(container);
                }
            }

            Pool.FreeUnmanaged(ref nearbyEntities);

            // 1. Pull smelted outputs from furnaces into matching categorized boxes
            if (useSmelt && ovens.Count > 0 && categorizedBoxes.Count > 0)
            {
                PullSmeltedOutputs(ovens, categorizedBoxes);
            }

            // 2. Sort all items sitting in the Drop Box
            if (dropBox.inventory == null || dropBox.inventory.itemList.Count == 0) return;

            for (int i = dropBox.inventory.itemList.Count - 1; i >= 0; i--)
            {
                Item item = dropBox.inventory.itemList[i];
                if (item == null) continue;

                // Priority 1: Raw ores into furnaces if Smelt is enabled
                if (useSmelt && IsSmeltableOre(item.info.shortname) && ovens.Count > 0)
                {
                    if (MoveToAvailableOven(item, ovens)) continue;
                }

                // Priority 2: Route by Item Category to matching Storage Adapter boxes
                string targetCategory = GetItemSortCategory(item);
                if (categorizedBoxes.TryGetValue(targetCategory, out List<StorageContainer> targetList))
                {
                    foreach (StorageContainer target in targetList)
                    {
                        if (target.inventory == null || target.inventory.IsFull()) continue;
                        if (item.MoveToContainer(target.inventory)) break;
                    }
                }
            }
        }

        private void PullSmeltedOutputs(List<BaseOven> ovens, Dictionary<string, List<StorageContainer>> categorizedBoxes)
        {
            foreach (BaseOven oven in ovens)
            {
                if (oven.inventory == null) continue;

                for (int i = oven.inventory.itemList.Count - 1; i >= 0; i--)
                {
                    Item item = oven.inventory.itemList[i];
                    if (item == null) continue;

                    string name = item.info.shortname;
                    if (name == "wood" || IsSmeltableOre(name)) continue;

                    string targetCategory = GetItemSortCategory(item);
                    if (categorizedBoxes.TryGetValue(targetCategory, out List<StorageContainer> targetList))
                    {
                        foreach (StorageContainer target in targetList)
                        {
                            if (target.inventory == null || target.inventory.IsFull()) continue;
                            if (item.MoveToContainer(target.inventory)) break;
                        }
                    }
                }
            }
        }

        private string GetItemSortCategory(Item item)
        {
            switch (item.info.category)
            {
                case ItemCategory.Weapon:
                    return "WEAPONS";
                case ItemCategory.Ammunition:
                    return "AMMO";
                case ItemCategory.Medical:
                    return "MEDICAL";
                case ItemCategory.Resources:
                    return "RESOURCES";
                case ItemCategory.Component:
                    return "COMPONENTS";
                case ItemCategory.Construction:
                    return "CONSTRUCTION";
                case ItemCategory.Electrical:
                    return "ELECTRICAL";
                case ItemCategory.Attire:
                    return "ATTIRE";
                case ItemCategory.Tool:
                    return "TOOLS";
                case ItemCategory.Food:
                    return "FOOD";
                default:
                    return "MISC";
            }
        }

        private bool MoveToAvailableOven(Item ore, List<BaseOven> ovens)
        {
            foreach (BaseOven oven in ovens)
            {
                if (oven.inventory == null || oven.inventory.IsFull()) continue;

                if (ore.MoveToContainer(oven.inventory))
                {
                    if (!oven.IsOn()) oven.StartCooking();
                    return true;
                }
            }
            return false;
        }

        private bool IsSmeltableOre(string shortname) =>
            shortname == "sulfur.ore" || shortname == "metal.ore" || shortname == "hq.metal.ore";
        #endregion
    }
}
