using System;
using System.Collections.Generic;
using System.Linq;
using Facepunch;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MyBaseCompanion", "Paradox Studios", "1.0.4")]
    [Description("Silent Deployable Base Companion NPC with custom naming, searchable 25m loot room CUI, saved loadouts, and direct drag-and-drop wardrobe.")]
    public class MyBaseCompanion : RustPlugin
    {
        private const string UI_MAIN = "MyBaseCompanion_UI";
        private const ulong TOKEN_SKIN_ID = 3349182742;
        private const float SCAN_RADIUS = 25f;

        private static readonly string[] NpcPrefabCandidates = new string[]
        {
            "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roam.prefab",
            "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_any.prefab",
            "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_peacekeeper.prefab",
            "assets/prefabs/npc/scientist/scientist.prefab"
        };

        private StoredData _data;
        private readonly Dictionary<ulong, ActiveSession> _sessions = new Dictionary<ulong, ActiveSession>();
        private readonly Dictionary<ulong, WardrobeSession> _wardrobeSessions = new Dictionary<ulong, WardrobeSession>();
        private readonly HashSet<ulong> _npcIds = new HashSet<ulong>();

        private class ActiveSession
        {
            public BasePlayer Npc;
            public string Category = "ALL";
            public string Search = "";
            public int Page = 0;
        }

        private class WardrobeSession
        {
            public BasePlayer Npc;
            public StorageContainer TempBox;
        }

        private class SavedGearItem
        {
            public string Shortname;
            public int Amount;
            public ulong Skin;
            public string Container;
            public int Position;
        }

        private class StoredData
        {
            public Dictionary<ulong, List<SavedGearItem>> SavedLoadouts = new Dictionary<ulong, List<SavedGearItem>>();
            public Dictionary<ulong, List<SavedGearItem>> NpcOutfits = new Dictionary<ulong, List<SavedGearItem>>();
            public Dictionary<ulong, SavedNpcLocation> ActiveNpcs = new Dictionary<ulong, SavedNpcLocation>();
            public Dictionary<ulong, string> NpcNames = new Dictionary<ulong, string>();
        }

        private class SavedNpcLocation
        {
            public float X, Y, Z;
            public float RotY;
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _data);

        private void LoadData()
        {
            _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name) ?? new StoredData();
        }

        #region Lifecycle & Hooks
        private void OnServerInitialized()
        {
            LoadData();
            timer.Once(3f, RespawnSavedCompanions);
        }

        private void Unload()
        {
            SaveData();
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, UI_MAIN);
                if (_wardrobeSessions.TryGetValue(player.userID, out WardrobeSession ws))
                {
                    player.EndLooting();
                    if (ws.TempBox != null && !ws.TempBox.IsDestroyed)
                        ws.TempBox.Kill();
                }
            }
            _wardrobeSessions.Clear();

            foreach (ulong netId in _npcIds.ToList())
            {
                BaseEntity ent = BaseNetworkable.serverEntities.Find(new NetworkableId(netId)) as BaseEntity;
                if (ent != null && !ent.IsDestroyed)
                {
                    ent.Kill();
                }
            }
            _npcIds.Clear();
        }

        [ChatCommand("companion")]
        private void CmdGiveCompanion(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            GiveCompanionToken(player);
            player.ChatMessage("<color=#55ff55>You received a Base Companion Post!</color> Rotate and place it like a small box to position your companion.");
        }

        [ConsoleCommand("companion.give")]
        private void ConsoleGiveCompanion(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin && arg.Connection.authLevel == 0) return;
            if (!arg.HasArgs()) return;

            BasePlayer target = BasePlayer.Find(arg.GetString(0));
            if (target != null)
            {
                GiveCompanionToken(target);
            }
        }

        private void GiveCompanionToken(BasePlayer player)
        {
            Item boxToken = ItemManager.CreateByName("box.wooden", 1, TOKEN_SKIN_ID);
            if (boxToken != null)
            {
                boxToken.name = "Base Companion Post";
                player.GiveItem(boxToken);
            }
        }

        private void OnEntityBuilt(Planner plan, GameObject go)
        {
            if (plan == null || go == null) return;

            BasePlayer player = plan.GetOwnerPlayer();
            if (player == null) return;

            BaseEntity entity = go.ToBaseEntity();
            if (entity == null || entity.skinID != TOKEN_SKIN_ID) return;

            Vector3 spawnPos = entity.transform.position;
            // Capture the exact yaw angle the player rotated the deployable box to
            float yaw = entity.transform.eulerAngles.y;

            NextTick(() =>
            {
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill();
                }
                SpawnCompanionForPlayer(player.userID, spawnPos, yaw, player);
            });
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity != null && entity.net != null && _npcIds.Contains(entity.net.ID.Value))
            {
                return true;
            }
            return null;
        }

        private object OnNpcTarget(BaseNpc npc, BaseEntity target)
        {
            if (target != null && target.net != null && _npcIds.Contains(target.net.ID.Value))
                return true;
            if (npc != null && npc.net != null && _npcIds.Contains(npc.net.ID.Value))
                return true;
            return null;
        }

        private object OnTurretTarget(AutoTurret turret, BaseCombatEntity target)
        {
            if (target != null && target.net != null && _npcIds.Contains(target.net.ID.Value))
                return true;
            return null;
        }

        private object OnNpcRadioChatter(ScientistNPC npc)
        {
            if (npc != null && npc.net != null && _npcIds.Contains(npc.net.ID.Value))
                return true;
            return null;
        }

        private object CanLootPlayer(BasePlayer target, BasePlayer looter)
        {
            if (target != null && target.net != null && _npcIds.Contains(target.net.ID.Value))
            {
                return false;
            }
            return null;
        }

        private object CanLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (entity != null && entity.net != null && _npcIds.Contains(entity.net.ID.Value))
            {
                return false;
            }
            return null;
        }

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null) return;
            if (!input.WasJustPressed(BUTTON.USE)) return;

            if (!Physics.Raycast(player.eyes.HeadRay(), out RaycastHit hit, 3.5f)) return;

            BasePlayer npc = hit.GetEntity() as BasePlayer;
            if (npc == null || npc.net == null || !_npcIds.Contains(npc.net.ID.Value)) return;

            if (npc.OwnerID != player.userID && !player.IsAdmin)
            {
                player.ChatMessage("<color=#ff5555>This Base Companion belongs to another player.</color>");
                return;
            }

            player.EndLooting();
            _sessions[player.userID] = new ActiveSession { Npc = npc };
            DrawCompanionUI(player);
        }

        private void OnPlayerLootEnd(PlayerLoot inventory)
        {
            BasePlayer player = inventory?.GetComponent<BasePlayer>();
            if (player == null) return;

            if (_wardrobeSessions.TryGetValue(player.userID, out WardrobeSession ws))
            {
                _wardrobeSessions.Remove(player.userID);
                if (ws.Npc != null && !ws.Npc.IsDestroyed && ws.TempBox != null && !ws.TempBox.IsDestroyed)
                {
                    SaveWardrobeContainerToNpc(ws.Npc, ws.TempBox.inventory);
                    player.ChatMessage("<color=#55ff55>Companion outfit updated & saved!</color>");
                }
                if (ws.TempBox != null && !ws.TempBox.IsDestroyed)
                {
                    ws.TempBox.inventory.Clear();
                    ws.TempBox.Kill();
                }
            }
        }
        #endregion

        #region Companion Spawning, Naming & Direct Wardrobe
        private string GetCompanionName(ulong ownerId)
        {
            if (_data.NpcNames.TryGetValue(ownerId, out string customName) && !string.IsNullOrWhiteSpace(customName))
            {
                return customName;
            }
            return "Base Companion";
        }

        private void ApplyCompanionName(BasePlayer npc, string name)
        {
            if (npc == null || npc.IsDestroyed) return;
            npc._name = name;
            npc.displayName = name;
            npc.SendNetworkUpdateImmediate();
        }

        private void SetCompanionFacing(BasePlayer npc, float yaw)
        {
            if (npc == null || npc.IsDestroyed) return;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            npc.transform.rotation = rot;
            npc.ServerRotation = rot;
            npc.eyes.rotation = rot;
            npc.viewAngles = new Vector3(0f, yaw, 0f);
            if (npc.net != null)
            {
                npc.SendNetworkUpdateImmediate();
            }
        }

        private void RespawnSavedCompanions()
        {
            foreach (var kvp in _data.ActiveNpcs.ToList())
            {
                Vector3 pos = new Vector3(kvp.Value.X, kvp.Value.Y, kvp.Value.Z);
                SpawnCompanionForPlayer(kvp.Key, pos, kvp.Value.RotY, null);
            }
        }

        private BaseEntity CreateNpcEntity(Vector3 pos, Quaternion rot)
        {
            foreach (string path in NpcPrefabCandidates)
            {
                BaseEntity ent = GameManager.server.CreateEntity(path, pos, rot);
                if (ent != null) return ent;
            }
            return null;
        }

        private void SpawnCompanionForPlayer(ulong ownerId, Vector3 pos, float yaw, BasePlayer notifyPlayer)
        {
            foreach (ulong existingId in _npcIds.ToList())
            {
                BaseEntity existing = BaseNetworkable.serverEntities.Find(new NetworkableId(existingId)) as BaseEntity;
                if (existing != null && !existing.IsDestroyed && existing.OwnerID == ownerId)
                {
                    _npcIds.Remove(existingId);
                    existing.Kill();
                }
            }

            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            BasePlayer npc = CreateNpcEntity(pos, rot) as BasePlayer;
            if (npc == null)
            {
                notifyPlayer?.ChatMessage("<color=#ff5555>Error: Could not spawn Base Companion NPC prefab.</color>");
                return;
            }

            string companionName = GetCompanionName(ownerId);
            npc.OwnerID = ownerId;
            npc._name = companionName;
            npc.displayName = companionName;
            npc.enableSaving = false;
            npc.Spawn();

            if (npc is ScientistNPC sci)
            {
                if (sci.Brain != null)
                {
                    sci.Brain.enabled = false;
                    UnityEngine.Object.Destroy(sci.Brain);
                }
                sci.radioChatterType = ScientistNPC.RadioChatterType.NONE;
                sci.CancelInvoke();
            }

            npc.CancelInvoke();
            npc.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, true);

            // Force both ServerRotation and viewAngles so the client renders him facing the exact box rotation
            SetCompanionFacing(npc, yaw);
            ApplyCompanionName(npc, companionName);

            // Re-sync rotation one tick later after Spawn() finishes initializing eyes/network transform
            NextTick(() =>
            {
                if (npc != null && !npc.IsDestroyed)
                {
                    SetCompanionFacing(npc, yaw);
                }
            });

            if (npc.inventory != null)
            {
                npc.inventory.containerBelt.Clear();
                ApplySavedOutfitToNpc(npc, ownerId);
            }

            _npcIds.Add(npc.net.ID.Value);
            _data.ActiveNpcs[ownerId] = new SavedNpcLocation
            {
                X = pos.x,
                Y = pos.y,
                Z = pos.z,
                RotY = yaw
            };
            SaveData();

            Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.updated.prefab", pos);
            notifyPlayer?.ChatMessage($"<color=#55ff55>{companionName} stationed!</color> Press <color=#ffff55>E</color> on them to browse your loot room, rename them, or open their Wardrobe.");
        }

        private void OpenNpcWardrobe(BasePlayer player, BasePlayer npc)
        {
            if (player == null || npc == null || npc.IsDestroyed || npc.inventory == null) return;

            CuiHelper.DestroyUi(player, UI_MAIN);
            _sessions.Remove(player.userID);

            player.EndLooting();

            timer.Once(0.15f, () =>
            {
                if (player == null || npc == null || npc.IsDestroyed) return;

                Vector3 hiddenPos = npc.transform.position + new Vector3(0f, -2.5f, 0f);
                StorageContainer tempBox = GameManager.server.CreateEntity("assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab", hiddenPos) as StorageContainer;
                if (tempBox == null) return;

                tempBox.enableSaving = false;
                tempBox.Spawn();
                tempBox.inventory.capacity = 7;
                tempBox.inventory.canAcceptItem = (p, item, i) => item != null && item.info.category == ItemCategory.Attire;

                foreach (Item worn in npc.inventory.containerWear.itemList)
                {
                    if (worn == null) continue;
                    Item copy = ItemManager.CreateByName(worn.info.shortname, 1, worn.skin);
                    if (copy != null)
                    {
                        copy.MoveToContainer(tempBox.inventory, worn.position);
                    }
                }

                tempBox.inventory.onItemAddedRemoved = (item, added, looter) =>
                {
                    if (npc != null && !npc.IsDestroyed && tempBox != null && !tempBox.IsDestroyed)
                    {
                        SaveWardrobeContainerToNpc(npc, tempBox.inventory);
                    }
                };

                _wardrobeSessions[player.userID] = new WardrobeSession
                {
                    Npc = npc,
                    TempBox = tempBox
                };

                player.inventory.loot.Clear();
                player.inventory.loot.PositionChecks = false;
                player.inventory.loot.entitySource = tempBox;
                player.inventory.loot.itemSource = null;
                player.inventory.loot.AddContainer(tempBox.inventory);
                player.inventory.loot.SendImmediate();
                player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), "generic_resizable");
                player.ChatMessage("<color=#55ff55>Wardrobe opened!</color> Drag clothes/armor into the right-side loot slots to dress your companion.");
            });
        }

        private void SaveWardrobeContainerToNpc(BasePlayer npc, ItemContainer wardrobe)
        {
            if (npc == null || npc.inventory == null || wardrobe == null) return;

            npc.inventory.containerWear.Clear();
            List<SavedGearItem> saved = new List<SavedGearItem>();

            foreach (Item item in wardrobe.itemList)
            {
                if (item == null || item.info.category != ItemCategory.Attire) continue;
                Item equip = ItemManager.CreateByName(item.info.shortname, 1, item.skin);
                if (equip != null)
                {
                    equip.MoveToContainer(npc.inventory.containerWear, item.position);
                    saved.Add(new SavedGearItem
                    {
                        Shortname = item.info.shortname,
                        Amount = 1,
                        Skin = item.skin,
                        Container = "wear",
                        Position = item.position
                    });
                }
            }

            npc.SendNetworkUpdateImmediate();
            _data.NpcOutfits[npc.OwnerID] = saved;
            SaveData();
        }

        private void ApplySavedOutfitToNpc(BasePlayer npc, ulong ownerId)
        {
            npc.inventory.containerWear.Clear();

            if (_data.NpcOutfits.TryGetValue(ownerId, out List<SavedGearItem> outfit))
            {
                foreach (SavedGearItem gear in outfit)
                {
                    Item item = ItemManager.CreateByName(gear.Shortname, 1, gear.Skin);
                    if (item != null)
                    {
                        item.MoveToContainer(npc.inventory.containerWear, gear.Position);
                    }
                }
            }
            else
            {
                Item hoodie = ItemManager.CreateByName("hoodie", 1);
                hoodie?.MoveToContainer(npc.inventory.containerWear);
                Item pants = ItemManager.CreateByName("pants", 1);
                pants?.MoveToContainer(npc.inventory.containerWear);
                Item boots = ItemManager.CreateByName("shoes.boots", 1);
                boots?.MoveToContainer(npc.inventory.containerWear);
            }
        }
        #endregion

        #region 25m TC-Free Storage Scanning & Pulling
        private List<StorageContainer> GetNearbyOwnedBoxes(Vector3 center, ulong ownerId)
        {
            List<StorageContainer> boxes = new List<StorageContainer>();
            List<BaseEntity> nearby = Pool.Get<List<BaseEntity>>();
            Vis.Entities(center, SCAN_RADIUS, nearby);

            foreach (BaseEntity ent in nearby)
            {
                if (ent == null || ent.IsDestroyed) continue;
                if (ownerId != 0 && ent.OwnerID != ownerId) continue;

                if (ent is StorageContainer box && !(box is BuildingPrivlidge) && box.inventory != null && box.enableSaving)
                {
                    boxes.Add(box);
                }
            }

            Pool.FreeUnmanaged(ref nearby);
            return boxes;
        }

        private class AggregatedItem
        {
            public ItemDefinition Info;
            public ulong Skin;
            public int TotalAmount;
        }

        private List<AggregatedItem> GetAggregatedStorage(Vector3 center, ulong ownerId, string category, string search)
        {
            List<StorageContainer> boxes = GetNearbyOwnedBoxes(center, ownerId);
            Dictionary<string, AggregatedItem> map = new Dictionary<string, AggregatedItem>();

            foreach (StorageContainer box in boxes)
            {
                foreach (Item item in box.inventory.itemList)
                {
                    if (item == null) continue;

                    if (category != "ALL" && !MatchesCategory(item, category)) continue;

                    if (!string.IsNullOrEmpty(search))
                    {
                        string display = item.info.displayName.english ?? item.info.shortname;
                        if (display.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                            item.info.shortname.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                    }

                    string key = $"{item.info.itemid}_{item.skin}";
                    if (!map.TryGetValue(key, out AggregatedItem agg))
                    {
                        agg = new AggregatedItem { Info = item.info, Skin = item.skin, TotalAmount = 0 };
                        map[key] = agg;
                    }
                    agg.TotalAmount += item.amount;
                }
            }

            return map.Values.OrderBy(x => x.Info.displayName.english).ToList();
        }

        private int PullItemFromBoxes(BasePlayer player, Vector3 center, ulong ownerId, int itemId, ulong skin, int desiredAmount, ItemContainer preferredTarget = null, int targetSlot = -1)
        {
            List<StorageContainer> boxes = GetNearbyOwnedBoxes(center, ownerId);
            int remaining = desiredAmount;

            foreach (StorageContainer box in boxes)
            {
                if (remaining <= 0) break;

                for (int i = box.inventory.itemList.Count - 1; i >= 0; i--)
                {
                    if (remaining <= 0) break;
                    Item item = box.inventory.itemList[i];
                    if (item == null || item.info.itemid != itemId || item.skin != skin) continue;

                    if (item.amount <= remaining)
                    {
                        int taken = item.amount;
                        if (MoveItemToPlayer(player, item, preferredTarget, targetSlot))
                        {
                            remaining -= taken;
                        }
                    }
                    else
                    {
                        Item split = item.SplitItem(remaining);
                        if (split != null)
                        {
                            if (MoveItemToPlayer(player, split, preferredTarget, targetSlot))
                            {
                                remaining = 0;
                            }
                            else
                            {
                                split.MoveToContainer(box.inventory);
                            }
                        }
                    }
                }
            }

            return desiredAmount - remaining;
        }

        private bool MoveItemToPlayer(BasePlayer player, Item item, ItemContainer preferredTarget, int targetSlot)
        {
            if (preferredTarget != null)
            {
                if (targetSlot >= 0 && preferredTarget.GetSlot(targetSlot) == null)
                {
                    if (item.MoveToContainer(preferredTarget, targetSlot)) return true;
                }
                if (item.MoveToContainer(preferredTarget)) return true;
            }

            return item.MoveToContainer(player.inventory.containerMain) ||
                   item.MoveToContainer(player.inventory.containerBelt);
        }

        private void SavePlayerLoadout(BasePlayer player)
        {
            List<SavedGearItem> loadout = new List<SavedGearItem>();

            foreach (Item item in player.inventory.containerWear.itemList)
            {
                if (item == null) continue;
                loadout.Add(new SavedGearItem
                {
                    Shortname = item.info.shortname,
                    Amount = item.amount,
                    Skin = item.skin,
                    Container = "wear",
                    Position = item.position
                });
            }

            foreach (Item item in player.inventory.containerBelt.itemList)
            {
                if (item == null) continue;
                loadout.Add(new SavedGearItem
                {
                    Shortname = item.info.shortname,
                    Amount = item.amount,
                    Skin = item.skin,
                    Container = "belt",
                    Position = item.position
                });
            }

            _data.SavedLoadouts[player.userID] = loadout;
            SaveData();
            player.ChatMessage($"<color=#55ff55>Saved current loadout ({loadout.Count} items)!</color>");
        }

        private void EquipPlayerLoadout(BasePlayer player, BasePlayer npc)
        {
            if (!_data.SavedLoadouts.TryGetValue(player.userID, out List<SavedGearItem> loadout) || loadout.Count == 0)
            {
                player.ChatMessage("<color=#ffaa00>You haven't saved a loadout yet. Wear your gear and click SAVE KIT first.</color>");
                return;
            }

            int pulledCount = 0;
            foreach (SavedGearItem entry in loadout)
            {
                ItemDefinition def = ItemManager.FindItemDefinition(entry.Shortname);
                if (def == null) continue;

                ItemContainer dest = entry.Container == "wear" ? player.inventory.containerWear : player.inventory.containerBelt;
                int got = PullItemFromBoxes(player, npc.transform.position, npc.OwnerID, def.itemid, entry.Skin, entry.Amount, dest, entry.Position);
                if (got > 0) pulledCount++;
            }

            string compName = GetCompanionName(npc.OwnerID);
            player.ChatMessage($"<color=#55ff55>{compName} equipped {pulledCount}/{loadout.Count} loadout items from your loot room!</color>");
        }

        private bool MatchesCategory(Item item, string cat)
        {
            switch (cat)
            {
                case "WEAPONS": return item.info.category == ItemCategory.Weapon;
                case "AMMO": return item.info.category == ItemCategory.Ammunition;
                case "MEDICAL": return item.info.category == ItemCategory.Medical;
                case "RESOURCES": return item.info.category == ItemCategory.Resources;
                case "COMPONENTS": return item.info.category == ItemCategory.Component;
                case "ATTIRE": return item.info.category == ItemCategory.Attire;
                default: return true;
            }
        }
        #endregion

        #region CUI Storage Browser & Controls
        private void DrawCompanionUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UI_MAIN);

            if (!_sessions.TryGetValue(player.userID, out ActiveSession session) || session.Npc == null || session.Npc.IsDestroyed)
                return;

            List<AggregatedItem> items = GetAggregatedStorage(session.Npc.transform.position, session.Npc.OwnerID, session.Category, session.Search);

            const int perPage = 18;
            int totalPages = Mathf.Max(1, Mathf.CeilToInt(items.Count / (float)perPage));
            session.Page = Mathf.Clamp(session.Page, 0, totalPages - 1);

            string currentName = GetCompanionName(session.Npc.OwnerID);
            if (session.Npc.displayName != currentName)
            {
                ApplyCompanionName(session.Npc, currentName);
            }

            CuiElementContainer container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.10 0.11 0.13 0.96" },
                RectTransform = { AnchorMin = "0.22 0.20", AnchorMax = "0.78 0.82" },
                CursorEnabled = true
            }, "Overlay", UI_MAIN);

            container.Add(new CuiLabel
            {
                Text = { Text = $"{currentName.ToUpper()} - LOOT ROOM REQUESTS (25M)", FontSize = 13, Align = TextAnchor.MiddleLeft, Color = "0.90 0.90 0.90 1" },
                RectTransform = { AnchorMin = "0.02 0.91", AnchorMax = "0.62 0.98" }
            }, UI_MAIN);

            container.Add(new CuiPanel
            {
                Image = { Color = "0.16 0.18 0.21 1" },
                RectTransform = { AnchorMin = "0.63 0.92", AnchorMax = "0.91 0.975" }
            }, UI_MAIN, "MBC_NameBg");

            container.Add(new CuiElement
            {
                Parent = "MBC_NameBg",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = currentName,
                        FontSize = 10,
                        Align = TextAnchor.MiddleLeft,
                        Color = "0.85 0.95 1 1",
                        Command = "companion.ui rename"
                    },
                    new CuiRectTransformComponent { AnchorMin = "0.04 0", AnchorMax = "0.96 1" }
                }
            });

            container.Add(new CuiButton
            {
                Button = { Command = "companion.ui close", Color = "0.65 0.20 0.20 1" },
                RectTransform = { AnchorMin = "0.93 0.92", AnchorMax = "0.98 0.98" },
                Text = { Text = "X", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UI_MAIN);

            string[] tabs = { "ALL", "WEAPONS", "AMMO", "MEDICAL", "RESOURCES", "COMPONENTS", "ATTIRE" };
            float tabWidth = 0.96f / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
            {
                string t = tabs[i];
                bool active = session.Category == t;
                float xMin = 0.02f + (i * tabWidth);
                float xMax = xMin + tabWidth - 0.005f;

                container.Add(new CuiButton
                {
                    Button = { Command = $"companion.ui cat {t}", Color = active ? "0.22 0.55 0.25 1" : "0.20 0.22 0.25 1" },
                    RectTransform = { AnchorMin = $"{xMin} 0.84", AnchorMax = $"{xMax} 0.90" },
                    Text = { Text = t, FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
                }, UI_MAIN);
            }

            container.Add(new CuiPanel
            {
                Image = { Color = "0.16 0.18 0.21 1" },
                RectTransform = { AnchorMin = "0.02 0.77", AnchorMax = "0.45 0.825" }
            }, UI_MAIN, "MBC_SearchBg");

            container.Add(new CuiElement
            {
                Parent = "MBC_SearchBg",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = string.IsNullOrEmpty(session.Search) ? "Search item name..." : session.Search,
                        FontSize = 10,
                        Align = TextAnchor.MiddleLeft,
                        Color = "1 1 1 1",
                        Command = "companion.ui search"
                    },
                    new CuiRectTransformComponent { AnchorMin = "0.03 0", AnchorMax = "0.97 1" }
                }
            });

            container.Add(new CuiButton
            {
                Button = { Command = "companion.ui clearsearch", Color = "0.28 0.30 0.34 1" },
                RectTransform = { AnchorMin = "0.46 0.77", AnchorMax = "0.55 0.825" },
                Text = { Text = "CLEAR", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UI_MAIN);

            container.Add(new CuiButton
            {
                Button = { Command = "companion.ui wardrobe", Color = "0.45 0.32 0.60 1" },
                RectTransform = { AnchorMin = "0.57 0.77", AnchorMax = "0.71 0.825" },
                Text = { Text = "WARDROBE", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UI_MAIN);

            container.Add(new CuiButton
            {
                Button = { Command = "companion.ui saveloadout", Color = "0.22 0.45 0.65 1" },
                RectTransform = { AnchorMin = "0.72 0.77", AnchorMax = "0.84 0.825" },
                Text = { Text = "SAVE KIT", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UI_MAIN);

            container.Add(new CuiButton
            {
                Button = { Command = "companion.ui equiploadout", Color = "0.22 0.55 0.25 1" },
                RectTransform = { AnchorMin = "0.85 0.77", AnchorMax = "0.98 0.825" },
                Text = { Text = "EQUIP KIT", FontSize = 8, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UI_MAIN);

            var pageItems = items.Skip(session.Page * perPage).Take(perPage).ToList();
            for (int i = 0; i < pageItems.Count; i++)
            {
                AggregatedItem entry = pageItems[i];
                int col = i % 6;
                int row = i / 6;

                float xMin = 0.02f + (col * 0.161f);
                float xMax = xMin + 0.153f;
                float yMax = 0.74f - (row * 0.215f);
                float yMin = yMax - 0.20f;

                string cardName = $"MBC_Card_{i}";
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.16 0.18 0.21 1" },
                    RectTransform = { AnchorMin = $"{xMin} {yMin}", AnchorMax = $"{xMax} {yMax}" }
                }, UI_MAIN, cardName);

                container.Add(new CuiElement
                {
                    Parent = cardName,
                    Components =
                    {
                        new CuiImageComponent { ItemId = entry.Info.itemid, SkinId = entry.Skin },
                        new CuiRectTransformComponent { AnchorMin = "0.22 0.32", AnchorMax = "0.78 0.92" }
                    }
                });

                string itemLabel = $"{entry.Info.displayName.english}\n<color=#88ff88>x{entry.TotalAmount:N0}</color>";
                container.Add(new CuiLabel
                {
                    Text = { Text = itemLabel, FontSize = 8, Align = TextAnchor.MiddleCenter, Color = "0.92 0.92 0.92 1" },
                    RectTransform = { AnchorMin = "0.02 0.02", AnchorMax = "0.98 0.34" }
                }, cardName);

                int pullAmount = Mathf.Min(entry.Info.stackable, entry.TotalAmount);
                container.Add(new CuiButton
                {
                    Button = { Command = $"companion.ui pull {entry.Info.itemid} {entry.Skin} {pullAmount}", Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Text = { Text = "" }
                }, cardName);
            }

            container.Add(new CuiButton
            {
                Button = { Command = "companion.ui pickup", Color = "0.50 0.20 0.20 1" },
                RectTransform = { AnchorMin = "0.02 0.02", AnchorMax = "0.22 0.08" },
                Text = { Text = "PICK UP NPC", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UI_MAIN);

            container.Add(new CuiLabel
            {
                Text = { Text = $"Click any item to grab 1 stack  |  Page {session.Page + 1} / {totalPages}", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "0.75 0.75 0.75 1" },
                RectTransform = { AnchorMin = "0.25 0.02", AnchorMax = "0.75 0.08" }
            }, UI_MAIN);

            container.Add(new CuiButton
            {
                Button = { Command = "companion.ui page_prev", Color = "0.25 0.27 0.30 1" },
                RectTransform = { AnchorMin = "0.78 0.02", AnchorMax = "0.87 0.08" },
                Text = { Text = "< PREV", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UI_MAIN);

            container.Add(new CuiButton
            {
                Button = { Command = "companion.ui page_next", Color = "0.25 0.27 0.30 1" },
                RectTransform = { AnchorMin = "0.89 0.02", AnchorMax = "0.98 0.08" },
                Text = { Text = "NEXT >", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UI_MAIN);

            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("companion.ui")]
        private void CmdCompanionUi(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || !arg.HasArgs()) return;

            string action = arg.GetString(0).ToLower();
            if (action == "close")
            {
                _sessions.Remove(player.userID);
                CuiHelper.DestroyUi(player, UI_MAIN);
                return;
            }

            if (!_sessions.TryGetValue(player.userID, out ActiveSession session) || session.Npc == null || session.Npc.IsDestroyed)
            {
                CuiHelper.DestroyUi(player, UI_MAIN);
                return;
            }

            switch (action)
            {
                case "rename":
                    string newName = string.Join(" ", arg.Args.Skip(1)).Trim();
                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        if (newName.Length > 24) newName = newName.Substring(0, 24);
                        _data.NpcNames[session.Npc.OwnerID] = newName;
                        SaveData();
                        ApplyCompanionName(session.Npc, newName);
                        player.ChatMessage($"<color=#55ff55>Companion renamed to:</color> {newName}");
                    }
                    break;

                case "cat":
                    session.Category = arg.GetString(1, "ALL").ToUpper();
                    session.Page = 0;
                    break;

                case "search":
                    session.Search = string.Join(" ", arg.Args.Skip(1)).Trim();
                    session.Page = 0;
                    break;

                case "clearsearch":
                    session.Search = "";
                    session.Page = 0;
                    break;

                case "page_prev":
                    session.Page = Mathf.Max(0, session.Page - 1);
                    break;

                case "page_next":
                    session.Page++;
                    break;

                case "pull":
                    if (arg.Args.Length >= 4)
                    {
                        int itemId = arg.GetInt(1);
                        ulong skin = arg.GetUInt64(2);
                        int amount = arg.GetInt(3);
                        int pulled = PullItemFromBoxes(player, session.Npc.transform.position, session.Npc.OwnerID, itemId, skin, amount);
                        if (pulled > 0)
                        {
                            Effect.server.Run("assets/prefabs/locks/keypad/effects/lock.code.updated.prefab", player.transform.position);
                        }
                    }
                    break;

                case "wardrobe":
                    OpenNpcWardrobe(player, session.Npc);
                    return;

                case "saveloadout":
                    SavePlayerLoadout(player);
                    break;

                case "equiploadout":
                    EquipPlayerLoadout(player, session.Npc);
                    break;

                case "pickup":
                    ulong ownerId = session.Npc.OwnerID;
                    _npcIds.Remove(session.Npc.net.ID.Value);
                    session.Npc.Kill();
                    _data.ActiveNpcs.Remove(ownerId);
                    SaveData();
                    _sessions.Remove(player.userID);
                    CuiHelper.DestroyUi(player, UI_MAIN);
                    GiveCompanionToken(player);
                    player.ChatMessage("<color=#ffaa00>Companion picked up as a deployable Base Companion Post.</color>");
                    return;
            }

            DrawCompanionUI(player);
        }
        #endregion
    }
}
