using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Rust;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("CustomWallpaperTool", "1320Veteran", "3.5.0")]
    [Description("Dedicated Custom Wallpaper Tool (indestructible skinned Spray Can / /wallpaper) that applies Steam Workshop wallpapers to interior & exterior walls, floors, foundations, and ceilings, prevents wallpaper decay on both sides, and allows removing broken skins.")]
    public class CustomWallpaperTool : RustPlugin
    {
        [PluginReference]
        private Plugin ImageLibrary;

        private const string UI_MAIN = "CWT_MainUI";
        private const string STEAM_API_DETAILS = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
        private const ulong CUSTOM_TOOL_SKIN = 3356819012UL;
        private const string TOOL_SHORTNAME = "spraycan";

        private StoredData _data;
        private readonly Dictionary<ulong, PlayerSession> _sessions = new Dictionary<ulong, PlayerSession>();
        private Timer _healTimer;
        private bool _debugDecay;

        #region Data Models

        private class StoredData
        {
            public List<WallpaperEntry> CommunityLibrary = new List<WallpaperEntry>();
            public Dictionary<ulong, List<ulong>> PlayerHistory = new Dictionary<ulong, List<ulong>>();
            public Dictionary<ulong, Dictionary<int, ulong>> ProtectedWallpapers = new Dictionary<ulong, Dictionary<int, ulong>>();
        }

        private class WallpaperEntry
        {
            public ulong WorkshopId;
            public string Title;
            public string PreviewUrl;
            public string AddedBy;
            public long AddedTimestamp;
        }

        private class PlayerSession
        {
            public ulong ActiveSkinId;
            public bool ClearMode;
            public string ActiveTab = "community";
            public int Page;
            public string PendingInput = string.Empty;
            public float NextAllowedPaintTime;
            public ulong LastPaintedBlockId;
            public int LastPaintedSide = -1;
        }

        private class SteamFileResponse
        {
            [JsonProperty("response")]
            public SteamResponseInner Response;
        }

        private class SteamResponseInner
        {
            [JsonProperty("publishedfiledetails")]
            public List<SteamFileDetail> Details;
        }

        private class SteamFileDetail
        {
            [JsonProperty("publishedfileid")]
            public string PublishedFileId;

            [JsonProperty("result")]
            public int Result;

            [JsonProperty("consumer_app_id")]
            public int ConsumerAppId;

            [JsonProperty("title")]
            public string Title;

            [JsonProperty("preview_url")]
            public string PreviewUrl;

            [JsonProperty("tags")]
            public List<SteamTag> Tags;
        }

        private class SteamTag
        {
            [JsonProperty("tag")]
            public string Tag;
        }

        #endregion

        #region Oxide Hooks

        private void Init()
        {
            LoadData();
        }

        private void OnServerInitialized()
        {
            if (ImageLibrary != null && ImageLibrary.IsLoaded)
            {
                foreach (var entry in _data.CommunityLibrary)
                {
                    if (!string.IsNullOrEmpty(entry.PreviewUrl))
                        ImageLibrary.Call("AddImage", entry.PreviewUrl, $"cwt_{entry.WorkshopId}", 0UL);
                }
            }

            ScanAndProtectExistingWallpapers();
            RestoreAndHealWallpapers();
            _healTimer = timer.Every(15f, RestoreAndHealWallpapers);
        }

        private void Unload()
        {
            _healTimer?.Destroy();
            SaveData();
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, UI_MAIN);
            }
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player != null)
            {
                CuiHelper.DestroyUi(player, UI_MAIN);
                _sessions.Remove(player.userID);
            }
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            var block = entity as BuildingBlock;
            if (block != null && block.net != null)
            {
                _data.ProtectedWallpapers.Remove(block.net.ID.Value);
            }
        }

        private void OnLoseCondition(Item item, ref float amount)
        {
            if (IsCustomWallpaperTool(item))
            {
                amount = 0f;
                if (item.hasCondition)
                {
                    item.condition = item.maxCondition;
                }
            }
        }

        private object OnEntityTakeDamage(BuildingBlock block, HitInfo info)
        {
            if (block == null || info == null || block.net == null) return null;

            if (info.damageTypes.Has(DamageType.Decay))
            {
                bool hasProtected = _data.ProtectedWallpapers.ContainsKey(block.net.ID.Value);
                if (hasProtected || block.HasWallpaper(0) || block.HasWallpaper(1))
                {
                    if (hasProtected)
                    {
                        NextTick(() =>
                        {
                            if (block != null && !block.IsDestroyed)
                                HealBlockWallpapers(block);
                        });
                    }

                    if (block.HasWallpaper(0)) block.wallpaperHealth = 100f;
                    if (block.HasWallpaper(1)) block.wallpaperHealth2 = 100f;
                }
            }

            return null;
        }

        private object OnSprayCanUse(SprayCan tool, BasePlayer player)
        {
            if (tool != null && IsCustomWallpaperTool(tool.GetItem()))
                return false;
            return null;
        }

        private object OnSprayCreate(SprayCan tool, Vector3 pos, Quaternion rot)
        {
            if (tool != null && IsCustomWallpaperTool(tool.GetItem()))
                return false;
            return null;
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            var decal = entity as SprayCanSpray;
            if (decal == null) return;

            var owner = BasePlayer.FindByID(decal.OwnerID);
            if (owner != null && IsCustomWallpaperTool(owner.GetActiveItem()))
            {
                NextTick(() =>
                {
                    if (decal != null && !decal.IsDestroyed)
                        decal.Kill();
                });
            }
        }

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null) return;

            var activeItem = player.GetActiveItem();
            if (!IsCustomWallpaperTool(activeItem))
                return;

            if (input.WasJustPressed(BUTTON.FIRE_THIRD) || input.WasJustPressed(BUTTON.RELOAD))
            {
                OpenUI(player);
                return;
            }

            if (input.WasJustPressed(BUTTON.FIRE_PRIMARY) || input.IsDown(BUTTON.FIRE_PRIMARY))
            {
                var session = GetSession(player.userID);
                TryPaintSurface(player, activeItem, session, input.WasJustPressed(BUTTON.FIRE_PRIMARY));
            }
        }

        #endregion

        #region Zero-Decay Protection & Healing (Both Sides)

        private void ScanAndProtectExistingWallpapers()
        {
            int added = 0;
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                var block = entity as BuildingBlock;
                if (block == null || block.IsDestroyed || block.net == null) continue;

                for (int side = 0; side <= 1; side++)
                {
                    if (block.HasWallpaper(side))
                    {
                        ulong skinId = block.GetWallpaperSkin(side);
                        if (skinId > 0UL)
                        {
                            if (!_data.ProtectedWallpapers.TryGetValue(block.net.ID.Value, out var map))
                            {
                                map = new Dictionary<int, ulong>();
                                _data.ProtectedWallpapers[block.net.ID.Value] = map;
                            }
                            if (!map.ContainsKey(side))
                            {
                                map[side] = skinId;
                                added++;
                            }
                        }
                    }
                }
            }

            if (added > 0)
            {
                SaveData();
            }
        }

        private void RestoreAndHealWallpapers()
        {
            if (_data.ProtectedWallpapers.Count == 0) return;

            List<ulong> deadBlocks = null;
            int healedCount = 0;

            foreach (var kvp in _data.ProtectedWallpapers)
            {
                var block = BaseNetworkable.serverEntities.Find(new NetworkableId(kvp.Key)) as BuildingBlock;
                if (block == null || block.IsDestroyed)
                {
                    if (deadBlocks == null) deadBlocks = new List<ulong>();
                    deadBlocks.Add(kvp.Key);
                    continue;
                }

                if (HealBlockWallpapers(block, kvp.Value))
                    healedCount++;
            }

            if (_debugDecay && healedCount > 0)
            {
                Puts($"[Debug] Healed/restored zero-decay protection on {healedCount} building block(s).");
            }

            if (deadBlocks != null)
            {
                foreach (var id in deadBlocks)
                    _data.ProtectedWallpapers.Remove(id);
            }
        }

        private bool HealBlockWallpapers(BuildingBlock block, Dictionary<int, ulong> sides = null)
        {
            if (block == null || block.IsDestroyed || block.net == null) return false;

            if (sides == null && !_data.ProtectedWallpapers.TryGetValue(block.net.ID.Value, out sides))
                return false;

            bool updated = false;

            foreach (var sideEntry in sides)
            {
                int side = sideEntry.Key;
                ulong skinId = sideEntry.Value;

                if (skinId == 0UL) continue;

                float beforeHp = side == 0 ? block.wallpaperHealth : block.wallpaperHealth2;
                bool missingOrWrongSkin = !block.HasWallpaper(side) || block.GetWallpaperSkin(side) != skinId;

                if (missingOrWrongSkin)
                {
                    block.SetWallpaper(skinId, side);
                    updated = true;
                }

                if (side == 0 && block.wallpaperHealth < 100f)
                {
                    block.wallpaperHealth = 100f;
                    updated = true;
                }
                else if (side == 1 && block.wallpaperHealth2 < 100f)
                {
                    block.wallpaperHealth2 = 100f;
                    updated = true;
                }

                if (updated && _debugDecay)
                {
                    Puts($"[Debug] Block {block.net.ID.Value} Side {side} took decay (HP was {beforeHp:F1}, missing={missingOrWrongSkin}) -> Restored to 100 HP & Skin {skinId}");
                }
            }

            if (updated)
            {
                block.SendNetworkUpdateImmediate();
            }

            return updated;
        }

        private void TrackProtectedWallpaper(ulong blockNetId, int side, ulong skinId)
        {
            if (skinId == 0UL)
            {
                if (_data.ProtectedWallpapers.TryGetValue(blockNetId, out var sides))
                {
                    sides.Remove(side);
                    if (sides.Count == 0)
                        _data.ProtectedWallpapers.Remove(blockNetId);
                }
                return;
            }

            if (!_data.ProtectedWallpapers.TryGetValue(blockNetId, out var map))
            {
                map = new Dictionary<int, ulong>();
                _data.ProtectedWallpapers[blockNetId] = map;
            }
            map[side] = skinId;
        }

        private void RemoveWallpaperFromLibrary(BasePlayer player, ulong workshopId)
        {
            if (workshopId == 0UL) return;

            var removed = _data.CommunityLibrary.FirstOrDefault(x => x.WorkshopId == workshopId);
            string title = removed != null ? removed.Title : workshopId.ToString();

            _data.CommunityLibrary.RemoveAll(x => x.WorkshopId == workshopId);

            foreach (var hist in _data.PlayerHistory.Values)
            {
                hist.RemoveAll(id => id == workshopId);
            }

            // Clear active selection for anyone currently holding that broken skin
            foreach (var sess in _sessions.Values)
            {
                if (sess.ActiveSkinId == workshopId)
                    sess.ActiveSkinId = 0UL;
            }

            // Strip it off any walls in the world that still had it applied
            var affectedBlocks = new List<ulong>();
            foreach (var kvp in _data.ProtectedWallpapers)
            {
                var sidesToRemove = kvp.Value.Where(s => s.Value == workshopId).Select(s => s.Key).ToList();
                if (sidesToRemove.Count == 0) continue;

                var block = BaseNetworkable.serverEntities.Find(new NetworkableId(kvp.Key)) as BuildingBlock;
                foreach (int side in sidesToRemove)
                {
                    kvp.Value.Remove(side);
                    if (block != null && !block.IsDestroyed)
                    {
                        block.RemoveWallpaper(side);
                    }
                }

                if (block != null && !block.IsDestroyed)
                {
                    block.SendNetworkUpdateImmediate();
                }

                if (kvp.Value.Count == 0)
                    affectedBlocks.Add(kvp.Key);
            }

            foreach (var id in affectedBlocks)
                _data.ProtectedWallpapers.Remove(id);

            SaveData();

            if (player != null)
            {
                player.ChatMessage($"<color=#ff6b6b>[Wallpaper]</color> Removed broken skin <b>{title}</b> ({workshopId}) from the library & history.");
                OpenUI(player);
            }
        }

        private void InspectLookedAtWall(BasePlayer player)
        {
            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 6.0f, Layers.Mask.Construction, QueryTriggerInteraction.Ignore))
            {
                player.ChatMessage("<color=#ff6b6b>[Wallpaper Check]</color> Look directly at a wall, floor, or ceiling within 6m.");
                return;
            }

            var block = hit.GetEntity() as BuildingBlock;
            if (block == null || block.net == null)
            {
                player.ChatMessage("<color=#ff6b6b>[Wallpaper Check]</color> Not looking at a valid building block.");
                return;
            }

            int hitSide = GetTargetSurfaceSide(block, hit.normal);
            bool isProtected = _data.ProtectedWallpapers.TryGetValue(block.net.ID.Value, out var protSides);
            ulong prot0 = (isProtected && protSides.ContainsKey(0)) ? protSides[0] : 0UL;
            ulong prot1 = (isProtected && protSides.ContainsKey(1)) ? protSides[1] : 0UL;

            player.ChatMessage(
                $"<color=#4ecdc4>[Wallpaper Check]</color> Block: <b>{block.ShortPrefabName}</b> (ID: {block.net.ID.Value}) | Aiming Side: <b>{hitSide}</b>\n" +
                $"• <b>Side 0 (Interior/Floor):</b> Has={block.HasWallpaper(0)} | Skin={block.GetWallpaperSkin(0)} | HP={block.wallpaperHealth:F1}/100 | Protected={(prot0 > 0 ? "Yes" : "No")}\n" +
                $"• <b>Side 1 (Exterior/Ceiling):</b> Has={block.HasWallpaper(1)} | Skin={block.GetWallpaperSkin(1)} | HP={block.wallpaperHealth2:F1}/100 | Protected={(prot1 > 0 ? "Yes" : "No")}"
            );
        }

        #endregion

        #region Custom Wallpaper Tool Logic

        private bool IsCustomWallpaperTool(Item item)
        {
            return item != null && item.info.shortname == TOOL_SHORTNAME && item.skin == CUSTOM_TOOL_SKIN;
        }

        private void GiveCustomTool(BasePlayer player)
        {
            if (player == null) return;

            foreach (var existing in player.inventory.containerBelt.itemList)
            {
                if (IsCustomWallpaperTool(existing))
                {
                    if (existing.hasCondition) existing.condition = existing.maxCondition;
                    player.ChatMessage("<color=#4ecdc4>[Wallpaper]</color> You already have a <b>Custom Wallpaper Tool</b> on your hotbar! Hold it and press <b>Middle-Click</b> or <b>R</b> to pick a skin.");
                    return;
                }
            }
            foreach (var existing in player.inventory.containerMain.itemList)
            {
                if (IsCustomWallpaperTool(existing))
                {
                    if (existing.hasCondition) existing.condition = existing.maxCondition;
                    player.ChatMessage("<color=#4ecdc4>[Wallpaper]</color> You already have a <b>Custom Wallpaper Tool</b> in your inventory! Equip it and press <b>Middle-Click</b> or <b>R</b>.");
                    return;
                }
            }

            var item = ItemManager.CreateByName(TOOL_SHORTNAME, 1, CUSTOM_TOOL_SKIN);
            if (item == null) return;

            item.name = "Custom Wallpaper Tool";
            if (item.hasCondition)
            {
                item.condition = item.maxCondition;
            }

            if (!player.inventory.GiveItem(item))
            {
                item.Drop(player.transform.position, Vector3.up);
            }

            player.ChatMessage("<color=#4ecdc4>[Wallpaper]</color> Received <b>Custom Wallpaper Tool</b>! Equip it and press <b>Middle-Click</b> or <b>R</b> to choose a Workshop wallpaper, then <b>Left-Click</b> walls, floors, or ceilings to apply.");
        }

        private void TryPaintSurface(BasePlayer player, Item toolItem, PlayerSession session, bool justPressed)
        {
            if (toolItem != null && toolItem.hasCondition && toolItem.condition < toolItem.maxCondition)
            {
                toolItem.condition = toolItem.maxCondition;
            }

            if (UnityEngine.Time.realtimeSinceStartup < session.NextAllowedPaintTime)
                return;

            if (session.ActiveSkinId == 0 && !session.ClearMode)
            {
                if (justPressed)
                {
                    OpenUI(player);
                }
                return;
            }

            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 5.5f, Layers.Mask.Construction, QueryTriggerInteraction.Ignore))
                return;

            var block = hit.GetEntity() as BuildingBlock;
            if (block == null || block.IsDestroyed || block.net == null)
                return;

            if (!player.CanBuild(block.WorldSpaceBounds()))
            {
                if (justPressed)
                    player.ChatMessage("<color=#ff6b6b>[Wallpaper]</color> Building privilege required.");
                session.NextAllowedPaintTime = UnityEngine.Time.realtimeSinceStartup + 0.35f;
                return;
            }

            int side = GetTargetSurfaceSide(block, hit.normal);
            if (side < 0)
                return;

            ulong blockNetId = block.net.ID.Value;
            if (!justPressed && session.LastPaintedBlockId == blockNetId && session.LastPaintedSide == side)
                return;

            ulong targetSkin = session.ClearMode ? 0UL : session.ActiveSkinId;

            if (targetSkin == 0UL && !block.HasWallpaper(side))
                return;
            if (targetSkin > 0UL && block.HasWallpaper(side) && block.GetWallpaperSkin(side) == targetSkin)
                return;

            if (targetSkin == 0UL)
            {
                block.RemoveWallpaper(side);
            }
            else
            {
                block.RemoveWallpaper(side);
                block.SetWallpaper(targetSkin, side);
                if (side == 0) block.wallpaperHealth = 100f;
                else block.wallpaperHealth2 = 100f;
            }

            TrackProtectedWallpaper(blockNetId, side, targetSkin);
            block.SendNetworkUpdateImmediate();

            Effect.server.Run("assets/prefabs/tools/spraycan/reskineffect.prefab", hit.point);

            session.LastPaintedBlockId = blockNetId;
            session.LastPaintedSide = side;
            session.NextAllowedPaintTime = UnityEngine.Time.realtimeSinceStartup + 0.12f;
        }

        private int GetTargetSurfaceSide(BuildingBlock block, Vector3 hitNormal)
        {
            string prefab = block.PrefabName;

            if (prefab.Contains("foundation"))
            {
                return hitNormal.y > 0.2f ? 0 : -1;
            }

            if (prefab.Contains("floor") || prefab.Contains("roof"))
            {
                Vector3 localNormal = block.transform.InverseTransformDirection(hitNormal);
                return localNormal.y < 0f ? 0 : 1;
            }

            Vector3 localNormalWall = block.transform.InverseTransformDirection(hitNormal);

            if (Mathf.Abs(localNormalWall.x) >= Mathf.Abs(localNormalWall.z))
            {
                if (localNormalWall.x < -0.2f) return 0;
                if (localNormalWall.x > 0.2f) return 1;
                return -1;
            }

            if (localNormalWall.z > 0.2f) return 0;
            if (localNormalWall.z < -0.2f) return 1;
            return -1;
        }

        #endregion

        #region Steam Workshop Import

        private void ImportWorkshopId(BasePlayer player, string rawInput)
        {
            if (string.IsNullOrWhiteSpace(rawInput))
            {
                player.ChatMessage("<color=#ff6b6b>[Wallpaper]</color> Paste a Steam Workshop ID or URL first.");
                return;
            }

            var match = Regex.Match(rawInput.Trim(), @"\b(\d{7,12})\b");
            if (!match.Success || !ulong.TryParse(match.Groups[1].Value, out ulong workshopId))
            {
                player.ChatMessage("<color=#ff6b6b>[Wallpaper]</color> Could not find a valid Workshop ID in that text.");
                return;
            }

            var existing = _data.CommunityLibrary.FirstOrDefault(x => x.WorkshopId == workshopId);
            if (existing != null)
            {
                AddToPlayerHistory(player.userID, workshopId);
                var session = GetSession(player.userID);
                session.ActiveSkinId = workshopId;
                session.ClearMode = false;
                session.LastPaintedBlockId = 0;
                SaveData();
                player.ChatMessage($"<color=#4ecdc4>[Wallpaper]</color> Equipped <b>{existing.Title}</b> ({workshopId}). Left-click walls, floors, or ceilings!");
                OpenUI(player);
                return;
            }

            string postBody = $"itemcount=1&publishedfileids[0]={workshopId}";
            webrequest.Enqueue(STEAM_API_DETAILS, postBody, (code, response) =>
            {
                if (player == null || !player.IsConnected) return;

                if (code != 200 || string.IsNullOrEmpty(response))
                {
                    player.ChatMessage("<color=#ff6b6b>[Wallpaper]</color> Steam Workshop API did not respond. Try again in a moment.");
                    return;
                }

                try
                {
                    var parsed = JsonConvert.DeserializeObject<SteamFileResponse>(response);
                    var detail = parsed?.Response?.Details?.FirstOrDefault();

                    if (detail == null || detail.Result != 1 || detail.ConsumerAppId != 252490)
                    {
                        player.ChatMessage("<color=#ff6b6b>[Wallpaper]</color> Invalid or private Rust Workshop item.");
                        return;
                    }

                    bool isWallpaper = detail.Tags != null &&
                                       detail.Tags.Any(t => t.Tag != null && t.Tag.IndexOf("wallpaper", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (!isWallpaper)
                    {
                        player.ChatMessage("<color=#ff6b6b>[Wallpaper]</color> That Workshop item is not tagged as a Wallpaper.");
                        return;
                    }

                    var newEntry = new WallpaperEntry
                    {
                        WorkshopId = workshopId,
                        Title = string.IsNullOrWhiteSpace(detail.Title) ? $"Wallpaper #{workshopId}" : detail.Title,
                        PreviewUrl = detail.PreviewUrl ?? string.Empty,
                        AddedBy = player.displayName,
                        AddedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };

                    _data.CommunityLibrary.Insert(0, newEntry);
                    AddToPlayerHistory(player.userID, workshopId);
                    SaveData();

                    if (ImageLibrary != null && ImageLibrary.IsLoaded && !string.IsNullOrEmpty(newEntry.PreviewUrl))
                    {
                        ImageLibrary.Call("AddImage", newEntry.PreviewUrl, $"cwt_{workshopId}", 0UL);
                    }

                    var session = GetSession(player.userID);
                    session.ActiveSkinId = workshopId;
                    session.ClearMode = false;
                    session.LastPaintedBlockId = 0;

                    player.ChatMessage($"<color=#4ecdc4>[Wallpaper]</color> Imported & equipped <b>{newEntry.Title}</b>!");
                    OpenUI(player);
                }
                catch (Exception ex)
                {
                    PrintError($"Failed parsing Steam Workshop response: {ex.Message}");
                    player.ChatMessage("<color=#ff6b6b>[Wallpaper]</color> Error reading Workshop data.");
                }
            }, this, RequestMethod.POST);
        }

        private void AddToPlayerHistory(ulong userId, ulong workshopId)
        {
            if (!_data.PlayerHistory.TryGetValue(userId, out var list))
            {
                list = new List<ulong>();
                _data.PlayerHistory[userId] = list;
            }

            list.Remove(workshopId);
            list.Insert(0, workshopId);
        }

        #endregion

        #region Chat & Console Commands

        [ChatCommand("wallpaper")]
        private void CmdWallpaper(BasePlayer player, string command, string[] args)
        {
            if (args != null && args.Length > 0)
            {
                if (args[0].Equals("tool", StringComparison.OrdinalIgnoreCase))
                {
                    GiveCustomTool(player);
                    return;
                }
                if (args[0].Equals("check", StringComparison.OrdinalIgnoreCase))
                {
                    InspectLookedAtWall(player);
                    return;
                }
                if (args[0].Equals("debug", StringComparison.OrdinalIgnoreCase))
                {
                    _debugDecay = !_debugDecay;
                    player.ChatMessage($"<color=#4ecdc4>[Wallpaper]</color> Decay console debug logging is now <b>{(_debugDecay ? "ON" : "OFF")}</b>.");
                    return;
                }
                if (args[0].Equals("remove", StringComparison.OrdinalIgnoreCase) || args[0].Equals("delete", StringComparison.OrdinalIgnoreCase))
                {
                    if (args.Length > 1 && ulong.TryParse(args[1], out ulong targetId))
                    {
                        RemoveWallpaperFromLibrary(player, targetId);
                    }
                    else
                    {
                        player.ChatMessage("<color=#ff6b6b>[Wallpaper]</color> Usage: <b>/wallpaper remove <workshopID></b> (or click the red <b>X</b> on any tile in /wallpaper).");
                    }
                    return;
                }
            }

            OpenUI(player);
        }

        [ChatCommand("wp")]
        private void CmdWallpaperShort(BasePlayer player, string command, string[] args)
        {
            CmdWallpaper(player, command, args);
        }

        [ChatCommand("wptool")]
        private void CmdWallpaperTool(BasePlayer player)
        {
            GiveCustomTool(player);
        }

        [ConsoleCommand("cwt.givetool")]
        private void ConsoleGiveTool(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) GiveCustomTool(player);
        }

        [ConsoleCommand("cwt.close")]
        private void ConsoleClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) CuiHelper.DestroyUi(player, UI_MAIN);
        }

        [ConsoleCommand("cwt.tab")]
        private void ConsoleSwitchTab(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var session = GetSession(player.userID);
            session.ActiveTab = arg.GetString(0, "community");
            session.Page = 0;
            OpenUI(player);
        }

        [ConsoleCommand("cwt.page")]
        private void ConsoleChangePage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var session = GetSession(player.userID);
            session.Page = Mathf.Max(0, arg.GetInt(0, 0));
            OpenUI(player);
        }

        [ConsoleCommand("cwt.input")]
        private void ConsoleUpdateInput(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var session = GetSession(player.userID);
            session.PendingInput = arg.FullString.ToString();
        }

        [ConsoleCommand("cwt.import")]
        private void ConsoleImport(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            var session = GetSession(player.userID);
            string fullStr = arg.FullString.ToString();
            string input = !string.IsNullOrWhiteSpace(fullStr)
                ? fullStr
                : session.PendingInput;

            ImportWorkshopId(player, input);
        }

        [ConsoleCommand("cwt.select")]
        private void ConsoleSelectSkin(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            ulong id = arg.GetUInt64(0, 0);
            var session = GetSession(player.userID);
            session.ActiveSkinId = id;
            session.ClearMode = false;
            session.LastPaintedBlockId = 0;

            if (id > 0) AddToPlayerHistory(player.userID, id);
            SaveData();
            OpenUI(player);
        }

        [ConsoleCommand("cwt.remove")]
        private void ConsoleRemoveSkin(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            ulong id = arg.GetUInt64(0, 0);
            if (id > 0)
            {
                RemoveWallpaperFromLibrary(player, id);
            }
        }

        [ConsoleCommand("cwt.mode")]
        private void ConsoleSetMode(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            string mode = arg.GetString(0, "none");
            var session = GetSession(player.userID);
            session.LastPaintedBlockId = 0;

            if (mode == "clear")
            {
                session.ClearMode = true;
                session.ActiveSkinId = 0;
            }
            else
            {
                session.ClearMode = false;
                session.ActiveSkinId = 0;
            }

            OpenUI(player);
        }

        #endregion

        #region CUI Interface

        private void OpenUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UI_MAIN);
            var session = GetSession(player.userID);

            var elements = new CuiElementContainer();

            elements.Add(new CuiPanel
            {
                Image = { Color = "0.10 0.11 0.13 0.96" },
                RectTransform = { AnchorMin = "0.18 0.14", AnchorMax = "0.82 0.86" },
                CursorEnabled = true
            }, "Overlay", UI_MAIN);

            elements.Add(new CuiPanel
            {
                Image = { Color = "0.15 0.17 0.20 1" },
                RectTransform = { AnchorMin = "0 0.91", AnchorMax = "1 1" }
            }, UI_MAIN, "CWT_Header");

            string statusText = session.ClearMode
                ? "<color=#ff6b6b>MODE: CLEAR WALLPAPER</color>"
                : session.ActiveSkinId > 0
                    ? $"<color=#4ecdc4>ACTIVE ID: {session.ActiveSkinId}</color>"
                    : "<color=#aaaaaa>SELECT A WALLPAPER BELOW</color>";

            elements.Add(new CuiLabel
            {
                Text = { Text = $"  CUSTOM WALLPAPER TOOL   |   {statusText}", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.01 0", AnchorMax = "0.85 1" }
            }, "CWT_Header");

            elements.Add(new CuiButton
            {
                Button = { Command = "cwt.close", Color = "0.75 0.22 0.22 1" },
                RectTransform = { AnchorMin = "0.93 0.15", AnchorMax = "0.99 0.85" },
                Text = { Text = "X", FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, "CWT_Header");

            elements.Add(new CuiPanel
            {
                Image = { Color = "0.07 0.08 0.09 1" },
                RectTransform = { AnchorMin = "0.02 0.82", AnchorMax = "0.72 0.89" }
            }, UI_MAIN, "CWT_InputBg");

            elements.Add(new CuiElement
            {
                Parent = "CWT_InputBg",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = session.PendingInput,
                        FontSize = 13,
                        Align = TextAnchor.MiddleLeft,
                        Command = "cwt.input",
                        Color = "1 1 1 1",
                        CharsLimit = 120
                    },
                    new CuiRectTransformComponent { AnchorMin = "0.02 0", AnchorMax = "0.98 1" }
                }
            });

            elements.Add(new CuiButton
            {
                Button = { Command = "cwt.import", Color = "0.20 0.62 0.55 1" },
                RectTransform = { AnchorMin = "0.73 0.82", AnchorMax = "0.98 0.89" },
                Text = { Text = "IMPORT WORKSHOP ID / URL", FontSize = 12, Align = TextAnchor.MiddleCenter }
            }, UI_MAIN);

            string commColor = session.ActiveTab == "community" ? "0.24 0.45 0.68 1" : "0.18 0.20 0.24 1";
            string persColor = session.ActiveTab == "personal" ? "0.24 0.45 0.68 1" : "0.18 0.20 0.24 1";
            string clearColor = session.ClearMode ? "0.78 0.28 0.28 1" : "0.22 0.24 0.28 1";

            elements.Add(new CuiButton
            {
                Button = { Command = "cwt.tab community", Color = commColor },
                RectTransform = { AnchorMin = "0.02 0.74", AnchorMax = "0.26 0.80" },
                Text = { Text = $"COMMUNITY LIBRARY ({_data.CommunityLibrary.Count})", FontSize = 11, Align = TextAnchor.MiddleCenter }
            }, UI_MAIN);

            int personalCount = _data.PlayerHistory.TryGetValue(player.userID, out var histList) ? histList.Count : 0;
            elements.Add(new CuiButton
            {
                Button = { Command = "cwt.tab personal", Color = persColor },
                RectTransform = { AnchorMin = "0.27 0.74", AnchorMax = "0.51 0.80" },
                Text = { Text = $"MY HISTORY ({personalCount})", FontSize = 11, Align = TextAnchor.MiddleCenter }
            }, UI_MAIN);

            elements.Add(new CuiButton
            {
                Button = { Command = "cwt.mode clear", Color = clearColor },
                RectTransform = { AnchorMin = "0.62 0.74", AnchorMax = "0.79 0.80" },
                Text = { Text = "CLEAR WALLPAPER", FontSize = 11, Align = TextAnchor.MiddleCenter }
            }, UI_MAIN);

            elements.Add(new CuiButton
            {
                Button = { Command = "cwt.givetool", Color = "0.25 0.52 0.35 1" },
                RectTransform = { AnchorMin = "0.80 0.74", AnchorMax = "0.98 0.80" },
                Text = { Text = "GET CUSTOM TOOL", FontSize = 11, Align = TextAnchor.MiddleCenter }
            }, UI_MAIN);

            List<WallpaperEntry> items;
            if (session.ActiveTab == "personal" && histList != null)
            {
                items = histList
                    .Select(id => _data.CommunityLibrary.FirstOrDefault(e => e.WorkshopId == id))
                    .Where(e => e != null)
                    .ToList();
            }
            else
            {
                items = _data.CommunityLibrary;
            }

            const int perPage = 10;
            int totalPages = Mathf.Max(1, Mathf.CeilToInt(items.Count / (float)perPage));
            session.Page = Mathf.Clamp(session.Page, 0, totalPages - 1);

            var pageItems = items.Skip(session.Page * perPage).Take(perPage).ToList();
            bool hasImgLib = ImageLibrary != null && ImageLibrary.IsLoaded;

            for (int i = 0; i < pageItems.Count; i++)
            {
                var entry = pageItems[i];
                int col = i % 5;
                int row = i / 5;

                float xMin = 0.02f + col * 0.194f;
                float xMax = xMin + 0.184f;
                float yMax = 0.71f - row * 0.31f;
                float yMin = yMax - 0.29f;

                bool isSelected = !session.ClearMode && session.ActiveSkinId == entry.WorkshopId;
                string cardBg = isSelected ? "0.20 0.55 0.48 1" : "0.16 0.18 0.21 1";
                string cardId = $"CWT_Card_{i}";

                elements.Add(new CuiPanel
                {
                    Image = { Color = cardBg },
                    RectTransform = { AnchorMin = $"{xMin} {yMin}", AnchorMax = $"{xMax} {yMax}" }
                }, UI_MAIN, cardId);

                if (hasImgLib)
                {
                    string png = (string)ImageLibrary.Call("GetImage", $"cwt_{entry.WorkshopId}", 0UL);
                    if (!string.IsNullOrEmpty(png))
                    {
                        elements.Add(new CuiElement
                        {
                            Parent = cardId,
                            Components =
                            {
                                new CuiRawImageComponent { Png = png },
                                new CuiRectTransformComponent { AnchorMin = "0.06 0.28", AnchorMax = "0.94 0.94" }
                            }
                        });
                    }
                }

                elements.Add(new CuiLabel
                {
                    Text = { Text = $"{entry.Title}\n<size=9><color=#bbbbbb>by {entry.AddedBy}</color></size>", FontSize = 11, Align = TextAnchor.MiddleCenter },
                    RectTransform = { AnchorMin = "0.04 0.02", AnchorMax = "0.96 0.27" }
                }, cardId);

                // Main tile select button
                elements.Add(new CuiButton
                {
                    Button = { Command = $"cwt.select {entry.WorkshopId}", Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Text = { Text = "" }
                }, cardId);

                // Small red X button in the top-right corner of each card to remove broken/unwanted skins
                elements.Add(new CuiButton
                {
                    Button = { Command = $"cwt.remove {entry.WorkshopId}", Color = "0.78 0.20 0.20 0.92" },
                    RectTransform = { AnchorMin = "0.83 0.83", AnchorMax = "0.97 0.97" },
                    Text = { Text = "X", FontSize = 10, Align = TextAnchor.MiddleCenter }
                }, cardId);
            }

            elements.Add(new CuiButton
            {
                Button = { Command = $"cwt.page {session.Page - 1}", Color = "0.20 0.22 0.26 1" },
                RectTransform = { AnchorMin = "0.35 0.02", AnchorMax = "0.45 0.08" },
                Text = { Text = "< PREV", FontSize = 11, Align = TextAnchor.MiddleCenter }
            }, UI_MAIN);

            elements.Add(new CuiLabel
            {
                Text = { Text = $"Page {session.Page + 1} / {totalPages}", FontSize = 12, Align = TextAnchor.MiddleCenter },
                RectTransform = { AnchorMin = "0.45 0.02", AnchorMax = "0.55 0.08" }
            }, UI_MAIN);

            elements.Add(new CuiButton
            {
                Button = { Command = $"cwt.page {session.Page + 1}", Color = "0.20 0.22 0.26 1" },
                RectTransform = { AnchorMin = "0.55 0.02", AnchorMax = "0.65 0.08" },
                Text = { Text = "NEXT >", FontSize = 11, Align = TextAnchor.MiddleCenter }
            }, UI_MAIN);

            CuiHelper.AddUi(player, elements);
        }

        #endregion

        #region Helpers & Persistence

        private PlayerSession GetSession(ulong userId)
        {
            if (!_sessions.TryGetValue(userId, out var session))
            {
                session = new PlayerSession();
                _sessions[userId] = session;
            }
            return session;
        }

        private void LoadData()
        {
            try
            {
                _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name) ?? new StoredData();
            }
            catch
            {
                _data = new StoredData();
            }
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, _data);
        }

        #endregion
    }
}
