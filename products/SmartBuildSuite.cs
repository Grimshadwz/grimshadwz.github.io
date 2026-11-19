using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("SmartBuildSuite", "Paradox Studios", "1.2.1")]
    [Description("Per-TC CUI build controls, admin /cleanfloat command to wipe floating remnants without needing TC access (RealPVE compatible), creative freeplacement toggle, deployable grid/wall snapping, precision nudge/rotate, foundation-snapped symmetry mirroring, no-decay, and salvaged hammer instant pickup/demolish.")]
    public class SmartBuildSuite : RustPlugin
    {
        #region Fields & Data

        private const string CUI_PANEL = "SmartBuildSuite_TC_Panel";
        private const string CUI_HUD = "SmartBuildSuite_Nudge_HUD";
        private const string SALVAGED_HAMMER_SHORTNAME = "hammer.salvaged";
        private const string REGULAR_HAMMER_SHORTNAME = "hammer";

        private StoredData _data;
        private readonly Dictionary<ulong, BuildingPrivlidge> _openTcUsers = new Dictionary<ulong, BuildingPrivlidge>();
        private readonly Dictionary<ulong, NudgeMode> _playerNudgeMode = new Dictionary<ulong, NudgeMode>();
        private readonly Dictionary<ulong, float> _lastActionTime = new Dictionary<ulong, float>();
        private readonly HashSet<ulong> _mirrorSpawnGuard = new HashSet<ulong>();

        private enum NudgeMode
        {
            Rotate,
            PushPull,
            SlideLeftRight,
            RaiseLower
        }

        private class TcSettings
        {
            public bool DisableStability = false;
            public bool FreePlacement = false;
            public int GridSnapMode = 0;      // 0 = OFF, 1 = 0.25m, 2 = 0.50m, 3 = Center
            public int RotationSnapMode = 0;  // 0 = Free, 1 = 45 deg, 2 = 90 deg
            public bool WallHeightLock = false;
            public bool NoDecay = false;
            public bool BypassOverlap = false;
            public int MirrorMode = 0;        // 0 = OFF, 1 = 2-Way (X), 2 = 4-Way (X & Z)

            // Optional custom override (if false, defaults to exact center & rotation of the foundation under the TC)
            public bool HasCustomMirrorOrigin = false;
            public float MirrorOriginX = 0f;
            public float MirrorOriginY = 0f;
            public float MirrorOriginZ = 0f;
            public float MirrorRotY = 0f;
        }

        private class StoredData
        {
            public Dictionary<ulong, TcSettings> TcConfigs = new Dictionary<ulong, TcSettings>();
        }

        #endregion

        #region Oxide Hooks

        private void Init()
        {
            LoadData();
        }

        private void OnServerInitialized()
        {
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                var block = entity as BuildingBlock;
                if (block == null) continue;

                var tc = block.GetBuildingPrivilege();
                if (tc != null && GetTcSettings(tc.net.ID.Value, false)?.DisableStability == true)
                {
                    ApplyNoStability(block);
                }
            }

            timer.Every(0.5f, DrawActiveMirrorAxes);
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, CUI_PANEL);
                CuiHelper.DestroyUi(player, CUI_HUD);
            }
            SaveData();
        }

        private void OnNewSave(string filename)
        {
            _data = new StoredData();
            SaveData();
        }

        private bool CanManageTc(BasePlayer player, BuildingPrivlidge tc)
        {
            if (player == null || tc == null) return false;
            return tc.IsAuthed(player) || player.IsAdmin;
        }

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null) return;

            var tc = entity as BuildingPrivlidge;
            if (tc == null) return;

            if (!CanManageTc(player, tc)) return;

            _openTcUsers[player.userID] = tc;
            SyncFreePlacementConVar(tc);
            DrawTcCui(player, tc);
        }

        private void OnLootEntityEnd(BasePlayer player, BaseCombatEntity entity)
        {
            if (player == null) return;
            if (_openTcUsers.Remove(player.userID))
            {
                CuiHelper.DestroyUi(player, CUI_PANEL);
            }
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            var tc = entity as BuildingPrivlidge;
            if (tc != null && tc.net != null)
            {
                if (_data.TcConfigs.Remove(tc.net.ID.Value))
                {
                    SaveData();
                }
                return;
            }

            var block = entity as BuildingBlock;
            if (block != null && !_mirrorSpawnGuard.Contains(block.net?.ID.Value ?? 0))
            {
                uint bId = block.buildingID;
                Vector3 brokenPos = block.transform.position;
                var blockTc = block.GetBuildingPrivilege();

                if (blockTc != null)
                {
                    var settings = GetTcSettings(blockTc.net.ID.Value, false);
                    if (settings != null && settings.DisableStability)
                    {
                        return;
                    }
                }

                timer.Once(0.15f, () => RemoveDisconnectedInRange(brokenPos, 50f, blockTc, bId, false));
            }
        }

        #endregion

        #region Visual Mirror Axis Lines

        private void DrawActiveMirrorAxes()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected || player.IsSleeping()) continue;

                var activeItem = player.GetActiveItem();
                if (activeItem == null) continue;

                string shortname = activeItem.info.shortname;
                bool isHoldingBuilderTool = shortname == "building.planner" ||
                                            shortname == REGULAR_HAMMER_SHORTNAME ||
                                            activeItem.info.GetComponent<ItemModDeployable>() != null;

                if (!isHoldingBuilderTool) continue;

                var tc = player.GetBuildingPrivilege();
                if (tc == null || !CanManageTc(player, tc)) continue;

                var settings = GetTcSettings(tc.net.ID.Value, false);
                if (settings == null || settings.MirrorMode == 0) continue;

                Vector3 origin;
                Quaternion rot;
                GetMirrorTransform(tc, settings, out origin, out rot);

                Vector3 drawBase = new Vector3(origin.x, player.transform.position.y + 0.08f, origin.z);
                Vector3 right = rot * Vector3.right;
                Vector3 forward = rot * Vector3.forward;
                float lineHalfLength = 15f;

                player.SendConsoleCommand("ddraw.line", 0.55f, Color.cyan, drawBase - forward * lineHalfLength, drawBase + forward * lineHalfLength);
                player.SendConsoleCommand("ddraw.sphere", 0.55f, Color.green, drawBase, 0.25f);

                if (settings.MirrorMode == 2)
                {
                    player.SendConsoleCommand("ddraw.line", 0.55f, Color.magenta, drawBase - right * lineHalfLength, drawBase + right * lineHalfLength);
                }
            }
        }

        #endregion

        #region Stability, FreePlacement & Admin /cleanfloat Cleanup

        private void SyncFreePlacementConVar(BuildingPrivlidge tc)
        {
            if (tc == null) return;
            var settings = GetTcSettings(tc.net.ID.Value, false);
            bool enabled = settings != null && settings.FreePlacement;
            ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), $"creative.freeplacement {(enabled ? "true" : "false")}");
        }

        private void ApplyNoStability(BuildingBlock block)
        {
            if (block == null || block.IsDestroyed) return;
            block.grounded = true;
            block.cachedStability = 1f;
            block.SendNetworkUpdate();
        }

        private void RecalculateBuildingStability(BuildingPrivlidge tc)
        {
            if (tc == null) return;
            var building = tc.GetBuilding();
            if (building == null) return;

            var settings = GetTcSettings(tc.net.ID.Value, false);
            bool disableStability = settings != null && settings.DisableStability;

            var allBlocks = new List<BuildingBlock>(building.buildingBlocks);

            if (disableStability)
            {
                foreach (var block in allBlocks)
                {
                    if (block == null || block.IsDestroyed) continue;
                    ApplyNoStability(block);
                }
                return;
            }

            foreach (var block in allBlocks)
            {
                if (block == null || block.IsDestroyed) continue;
                bool isFoundation = IsGroundAnchor(block);
                block.grounded = isFoundation;
                block.cachedStability = isFoundation ? 1f : 0f;
                block.SendNetworkUpdate();
            }

            RemoveDisconnectedInRange(tc.transform.position, 50f, tc, building.ID, false);
        }

        private bool IsGroundAnchor(BuildingBlock block)
        {
            if (block == null) return false;
            return block.ShortPrefabName.StartsWith("foundation");
        }

        private List<Vector3> GetBlockAnchorPoints(BuildingBlock b)
        {
            var anchors = new List<Vector3>(9);
            if (b == null) return anchors;

            Transform t = b.transform;
            string name = b.ShortPrefabName;

            if (name.StartsWith("foundation") || name.StartsWith("floor") || name.StartsWith("roof"))
            {
                anchors.Add(t.position);
                anchors.Add(t.TransformPoint(new Vector3(1.5f, 0f, 0f)));
                anchors.Add(t.TransformPoint(new Vector3(-1.5f, 0f, 0f)));
                anchors.Add(t.TransformPoint(new Vector3(0f, 0f, 1.5f)));
                anchors.Add(t.TransformPoint(new Vector3(0f, 0f, -1.5f)));
                anchors.Add(t.TransformPoint(new Vector3(1.5f, 0f, 1.5f)));
                anchors.Add(t.TransformPoint(new Vector3(-1.5f, 0f, 1.5f)));
                anchors.Add(t.TransformPoint(new Vector3(1.5f, 0f, -1.5f)));
                anchors.Add(t.TransformPoint(new Vector3(-1.5f, 0f, -1.5f)));
            }
            else if (name.Contains("wall"))
            {
                float h = name.Contains("half") ? 1.5f : (name.Contains("low") ? 0.75f : 3.0f);
                anchors.Add(t.position);
                anchors.Add(t.TransformPoint(new Vector3(-1.5f, 0f, 0f)));
                anchors.Add(t.TransformPoint(new Vector3(1.5f, 0f, 0f)));
                anchors.Add(t.TransformPoint(new Vector3(0f, h, 0f)));
                anchors.Add(t.TransformPoint(new Vector3(-1.5f, h, 0f)));
                anchors.Add(t.TransformPoint(new Vector3(1.5f, h, 0f)));
            }
            else
            {
                OBB obb = b.WorldSpaceBounds();
                anchors.Add(obb.position);
                anchors.Add(t.position);
                anchors.Add(t.TransformPoint(new Vector3(0f, 1.5f, 0f)));
                anchors.Add(t.TransformPoint(new Vector3(0f, 3.0f, 0f)));
            }

            return anchors;
        }

        private bool AreBlocksTouchingByAnchor(BuildingBlock a, BuildingBlock b, Dictionary<BuildingBlock, List<Vector3>> anchorCache)
        {
            if (a == null || b == null || a == b) return false;

            List<Vector3> anchorsA;
            if (!anchorCache.TryGetValue(a, out anchorsA))
            {
                anchorsA = GetBlockAnchorPoints(a);
                anchorCache[a] = anchorsA;
            }

            List<Vector3> anchorsB;
            if (!anchorCache.TryGetValue(b, out anchorsB))
            {
                anchorsB = GetBlockAnchorPoints(b);
                anchorCache[b] = anchorsB;
            }

            const float maxSnapDistSqr = 0.35f * 0.35f;
            for (int i = 0; i < anchorsA.Count; i++)
            {
                Vector3 pa = anchorsA[i];
                for (int j = 0; j < anchorsB.Count; j++)
                {
                    if ((pa - anchorsB[j]).sqrMagnitude <= maxSnapDistSqr)
                        return true;
                }
            }

            return false;
        }

        private int RemoveDisconnectedInRange(Vector3 centerPos, float radius, BuildingPrivlidge referenceTc, uint oldBuildingId, bool forceAdminSweep)
        {
            Vector3 scanOrigin = referenceTc != null ? referenceTc.transform.position : centerPos;
            uint tcBuildingId = referenceTc != null ? referenceTc.buildingID : oldBuildingId;

            // Scan across ALL Unity layers (-1) within radius
            var nearbyEntities = new List<BaseEntity>();
            Vis.Entities(scanOrigin, radius, nearbyEntities, -1);

            var blocksInRange = new HashSet<BuildingBlock>();
            var deployablesInRange = new HashSet<DecayEntity>();

            foreach (var ent in nearbyEntities)
            {
                if (ent == null || ent.IsDestroyed) continue;

                var blk = ent as BuildingBlock;
                if (blk != null)
                {
                    if (!forceAdminSweep)
                    {
                        var blkTc = blk.GetBuildingPrivilege();
                        if (blkTc != null && GetTcSettings(blkTc.net.ID.Value, false)?.DisableStability == true)
                            continue;
                    }
                    blocksInRange.Add(blk);
                    continue;
                }

                var dep = ent as DecayEntity;
                if (dep != null && !(dep is BuildingPrivlidge))
                {
                    deployablesInRange.Add(dep);
                }
            }

            if (blocksInRange.Count == 0 && deployablesInRange.Count == 0) return 0;

            var blockList = blocksInRange.ToList();
            var anchorCache = new Dictionary<BuildingBlock, List<Vector3>>(blockList.Count);

            // Flood-fill support starting ONLY from ground-anchored foundations
            var supportedBlocks = new HashSet<BuildingBlock>();
            var queue = new Queue<BuildingBlock>();

            foreach (var blk in blockList)
            {
                if (IsGroundAnchor(blk))
                {
                    supportedBlocks.Add(blk);
                    queue.Enqueue(blk);
                }
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                for (int i = 0; i < blockList.Count; i++)
                {
                    var neighbor = blockList[i];
                    if (supportedBlocks.Contains(neighbor)) continue;

                    if (AreBlocksTouchingByAnchor(current, neighbor, anchorCache))
                    {
                        supportedBlocks.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            int removedCount = 0;

            // Remove any building block that is disconnected from foundations (or detached from TC)
            foreach (var blk in blockList)
            {
                if (blk == null || blk.IsDestroyed) continue;

                bool isFloating = !supportedBlocks.Contains(blk);
                bool isDetachedFromTc = (tcBuildingId != 0 && blk.buildingID != tcBuildingId && !IsGroundAnchor(blk));

                if (isFloating || isDetachedFromTc)
                {
                    blk.Kill(BaseNetworkable.DestroyMode.Gib);
                    removedCount++;
                }
            }

            // Remove any floating deployables (boxes, furnaces, etc.) not resting on terrain or surviving blocks
            foreach (var dep in deployablesInRange)
            {
                if (dep == null || dep.IsDestroyed) continue;

                bool restingOnGroundOrBlock = false;
                var hits = Physics.OverlapSphere(dep.transform.position, 0.45f, -1);
                for (int i = 0; i < hits.Length; i++)
                {
                    var col = hits[i];
                    if (col == null) continue;
                    var hitEnt = col.ToBaseEntity();
                    if (hitEnt == dep) continue;

                    if (hitEnt == null)
                    {
                        // Terrain or static world mesh
                        restingOnGroundOrBlock = true;
                        break;
                    }

                    var hitBlk = hitEnt as BuildingBlock;
                    if (hitBlk != null && !hitBlk.IsDestroyed && supportedBlocks.Contains(hitBlk))
                    {
                        restingOnGroundOrBlock = true;
                        break;
                    }
                }

                if (!restingOnGroundOrBlock)
                {
                    dep.Kill(BaseNetworkable.DestroyMode.Gib);
                    removedCount++;
                }
            }

            return removedCount;
        }

        private object OnStructureUpgrade(BuildingBlock block, BasePlayer player, BuildingGrade.Enum grade)
        {
            if (block == null) return null;
            var tc = block.GetBuildingPrivilege();
            if (tc != null && GetTcSettings(tc.net.ID.Value, false)?.DisableStability == true)
            {
                NextTick(() => ApplyNoStability(block));
            }
            return null;
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return null;

            if (info.damageTypes.Has(Rust.DamageType.Decay))
            {
                var tc = entity.GetBuildingPrivilege();
                if (tc != null)
                {
                    var settings = GetTcSettings(tc.net.ID.Value, false);
                    if (settings != null && settings.NoDecay)
                    {
                        return true;
                    }
                }
                return null;
            }

            var attacker = info.InitiatorPlayer;
            if (attacker != null && info.Weapon != null)
            {
                var item = info.Weapon.GetItem();
                if (item != null && item.info.shortname == SALVAGED_HAMMER_SHORTNAME)
                {
                    if (TrySalvagedHammerRemove(attacker, entity))
                    {
                        return true;
                    }
                }
            }

            return null;
        }

        #endregion

        #region Placement, Snapping & Mirroring

        private object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            if (planner == null || prefab == null) return null;
            var player = planner.GetOwnerPlayer();
            if (player == null) return null;

            var tc = player.GetBuildingPrivilege();
            if (tc == null || !CanManageTc(player, tc)) return null;

            var settings = GetTcSettings(tc.net.ID.Value, false);
            if (settings == null) return null;

            if (settings.BypassOverlap || settings.FreePlacement)
            {
                prefab.bounds = new Bounds(Vector3.zero, Vector3.zero);
                prefab.canBypassBuildingPermission = true;
            }

            return null;
        }

        private void OnEntityBuilt(Planner plan, GameObject go)
        {
            if (plan == null || go == null) return;
            var entity = go.ToBaseEntity();
            if (entity == null || entity.net == null) return;

            if (_mirrorSpawnGuard.Contains(entity.net.ID.Value)) return;

            var player = plan.GetOwnerPlayer();
            if (player == null) return;

            var tc = entity.GetBuildingPrivilege() ?? player.GetBuildingPrivilege();
            if (tc == null || !CanManageTc(player, tc)) return;

            var settings = GetTcSettings(tc.net.ID.Value, false);
            if (settings == null) return;

            var block = entity as BuildingBlock;
            if (block != null)
            {
                if (settings.DisableStability)
                {
                    ApplyNoStability(block);
                }

                if (settings.MirrorMode > 0)
                {
                    SpawnMirroredEntities(entity, tc, settings);
                }
                return;
            }

            if (!(entity is BuildingPrivlidge))
            {
                ApplyDeployableSnap(entity, tc, settings);

                if (settings.MirrorMode > 0)
                {
                    SpawnMirroredEntities(entity, tc, settings);
                }
            }
        }

        private void ApplyDeployableSnap(BaseEntity entity, BuildingPrivlidge tc, TcSettings settings)
        {
            if (entity == null || tc == null || settings == null) return;

            bool isWallMounted = IsWallMountedDeployable(entity);

            if (isWallMounted)
            {
                SnapToNearestWall(entity, settings);
            }
            else
            {
                SnapToNearestFloor(entity, settings);
            }

            entity.SendNetworkUpdateImmediate();
        }

        private bool IsWallMountedDeployable(BaseEntity entity)
        {
            string name = entity.ShortPrefabName;
            return name.Contains("wall") ||
                   name.Contains("light") ||
                   name.Contains("lantern") ||
                   name.Contains("switch") ||
                   name.Contains("sign") ||
                   name.Contains("frame") ||
                   name.Contains("shelves") ||
                   name.Contains("electrical") ||
                   name.Contains("battery.small") ||
                   name.Contains("splitter") ||
                   name.Contains("branch") ||
                   name.Contains("timer") ||
                   name.Contains("counter") ||
                   name.Contains("memorycell") ||
                   name.Contains("rfbroadcaster") ||
                   name.Contains("rfreceiver");
        }

        private void SnapToNearestFloor(BaseEntity entity, TcSettings settings)
        {
            RaycastHit hit;
            BuildingBlock floorBlock = null;
            if (Physics.Raycast(entity.transform.position + Vector3.up * 0.5f, Vector3.down, out hit, 3.0f, LayerMask.GetMask("Construction")))
            {
                floorBlock = hit.GetEntity() as BuildingBlock;
            }

            if (settings.RotationSnapMode > 0 && floorBlock != null)
            {
                float step = settings.RotationSnapMode == 1 ? 45f : 90f;
                Vector3 localEuler = (Quaternion.Inverse(floorBlock.transform.rotation) * entity.transform.rotation).eulerAngles;
                localEuler.x = 0f;
                localEuler.z = 0f;
                localEuler.y = Mathf.Round(localEuler.y / step) * step;
                entity.transform.rotation = floorBlock.transform.rotation * Quaternion.Euler(localEuler);
            }

            if (settings.GridSnapMode > 0 && floorBlock != null)
            {
                Vector3 localPos = floorBlock.transform.InverseTransformPoint(entity.transform.position);

                if (settings.GridSnapMode == 3)
                {
                    localPos.x = 0f;
                    localPos.z = 0f;
                }
                else
                {
                    float grid = settings.GridSnapMode == 1 ? 0.25f : 0.50f;
                    localPos.x = Mathf.Round(localPos.x / grid) * grid;
                    localPos.z = Mathf.Round(localPos.z / grid) * grid;
                }

                entity.transform.position = floorBlock.transform.TransformPoint(localPos);
            }
        }

        private void SnapToNearestWall(BaseEntity entity, TcSettings settings)
        {
            var nearbyBlocks = new List<BuildingBlock>();
            Vis.Entities(entity.transform.position, 1.5f, nearbyBlocks, LayerMask.GetMask("Construction"));

            BuildingBlock wall = nearbyBlocks
                .Where(b => b.ShortPrefabName.Contains("wall"))
                .OrderBy(b => Vector3.Distance(b.ClosestPoint(entity.transform.position), entity.transform.position))
                .FirstOrDefault();

            if (wall == null) return;

            Vector3 localPos = wall.transform.InverseTransformPoint(entity.transform.position);

            if (settings.GridSnapMode > 0)
            {
                if (settings.GridSnapMode == 3)
                {
                    localPos.x = 0f;
                }
                else
                {
                    float grid = settings.GridSnapMode == 1 ? 0.25f : 0.50f;
                    localPos.x = Mathf.Round(localPos.x / grid) * grid;
                    if (!settings.WallHeightLock)
                    {
                        localPos.y = Mathf.Round(localPos.y / grid) * grid;
                    }
                }
            }

            if (settings.WallHeightLock)
            {
                localPos.y = 1.65f;
            }

            entity.transform.position = wall.transform.TransformPoint(localPos);

            if (settings.RotationSnapMode > 0)
            {
                float dot = Vector3.Dot(entity.transform.forward, wall.transform.forward);
                Vector3 targetForward = dot >= 0 ? wall.transform.forward : -wall.transform.forward;
                entity.transform.rotation = Quaternion.LookRotation(targetForward, wall.transform.up);
            }
        }

        private BuildingBlock GetFoundationUnderTc(BuildingPrivlidge tc)
        {
            if (tc == null) return null;

            RaycastHit hit;
            if (Physics.Raycast(tc.transform.position + Vector3.up * 0.5f, Vector3.down, out hit, 3.0f, LayerMask.GetMask("Construction")))
            {
                var block = hit.GetEntity() as BuildingBlock;
                if (block != null) return block;
            }

            var nearbyBlocks = new List<BuildingBlock>();
            Vis.Entities(tc.transform.position, 2.5f, nearbyBlocks, LayerMask.GetMask("Construction"));
            return nearbyBlocks
                .Where(b => b.ShortPrefabName.Contains("foundation") || b.ShortPrefabName.Contains("floor"))
                .OrderBy(b => Vector3.Distance(b.transform.position, tc.transform.position))
                .FirstOrDefault();
        }

        private void GetMirrorTransform(BuildingPrivlidge tc, TcSettings settings, out Vector3 origin, out Quaternion rotation)
        {
            if (settings != null && settings.HasCustomMirrorOrigin)
            {
                origin = new Vector3(settings.MirrorOriginX, settings.MirrorOriginY, settings.MirrorOriginZ);
                rotation = Quaternion.Euler(0f, settings.MirrorRotY, 0f);
                return;
            }

            var tcFoundation = GetFoundationUnderTc(tc);
            if (tcFoundation != null)
            {
                origin = tcFoundation.transform.position;
                rotation = tcFoundation.transform.rotation;
            }
            else
            {
                origin = tc.transform.position;
                rotation = tc.transform.rotation;
            }
        }

        private void SpawnMirroredEntities(BaseEntity original, BuildingPrivlidge tc, TcSettings settings)
        {
            if (original == null || tc == null) return;

            Vector3 origin;
            Quaternion axisRot;
            GetMirrorTransform(tc, settings, out origin, out axisRot);

            var mirrorSpecs = new List<Vector3>();
            if (settings.MirrorMode == 1)
            {
                mirrorSpecs.Add(new Vector3(-1f, 1f, 1f));
            }
            else if (settings.MirrorMode == 2)
            {
                mirrorSpecs.Add(new Vector3(-1f, 1f, 1f));
                mirrorSpecs.Add(new Vector3(1f, 1f, -1f));
                mirrorSpecs.Add(new Vector3(-1f, 1f, -1f));
            }

            Vector3 localPos = Quaternion.Inverse(axisRot) * (original.transform.position - origin);
            Quaternion localRot = Quaternion.Inverse(axisRot) * original.transform.rotation;

            foreach (var scale in mirrorSpecs)
            {
                Vector3 mirroredLocalPos = new Vector3(localPos.x * scale.x, localPos.y * scale.y, localPos.z * scale.z);
                Vector3 worldPos = origin + (axisRot * mirroredLocalPos);

                Vector3 localFwd = localRot * Vector3.forward;
                Vector3 localUp = localRot * Vector3.up;
                Vector3 mirroredLocalFwd = new Vector3(localFwd.x * scale.x, localFwd.y * scale.y, localFwd.z * scale.z);
                Vector3 mirroredLocalUp = new Vector3(localUp.x * scale.x, localUp.y * scale.y, localUp.z * scale.z);

                Quaternion worldRot = axisRot * Quaternion.LookRotation(mirroredLocalFwd, mirroredLocalUp);

                var existing = new List<BaseEntity>();
                Vis.Entities(worldPos, 0.15f, existing);
                if (existing.Any(e => e.PrefabName == original.PrefabName)) continue;

                var clone = GameManager.server.CreateEntity(original.PrefabName, worldPos, worldRot);
                if (clone == null) continue;

                clone.OwnerID = original.OwnerID;
                clone.skinID = original.skinID;
                clone.Spawn();

                if (clone.net != null)
                {
                    ulong cloneNetId = clone.net.ID.Value;
                    _mirrorSpawnGuard.Add(cloneNetId);
                    NextTick(() => _mirrorSpawnGuard.Remove(cloneNetId));
                }

                var origBlock = original as BuildingBlock;
                var cloneBlock = clone as BuildingBlock;
                if (origBlock != null && cloneBlock != null)
                {
                    cloneBlock.SetGrade(origBlock.grade);
                    cloneBlock.SetHealthToMax();
                    if (settings.DisableStability)
                    {
                        ApplyNoStability(cloneBlock);
                    }
                    cloneBlock.SendNetworkUpdateImmediate();
                }
            }
        }

        #endregion

        #region Hammer Tools: Precision Nudge/Rotate & Salvaged Hammer Instant Pickup/Demolish

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null) return;

            var activeItem = player.GetActiveItem();
            if (activeItem == null) return;

            string shortname = activeItem.info.shortname;

            if (shortname == REGULAR_HAMMER_SHORTNAME)
            {
                HandleRegularHammerInput(player, input);
            }
            else if (shortname == SALVAGED_HAMMER_SHORTNAME && input.WasJustPressed(BUTTON.FIRE_PRIMARY))
            {
                HandleSalvagedHammerSwing(player);
            }
        }

        private void HandleSalvagedHammerSwing(BasePlayer player)
        {
            RaycastHit hit;
            if (Physics.Raycast(player.eyes.HeadRay(), out hit, 4.0f, LayerMask.GetMask("Construction", "Deployed", "Default", "Physics_Debris")))
            {
                var target = hit.GetEntity();
                if (target != null && !(target is BasePlayer))
                {
                    TrySalvagedHammerRemove(player, target);
                }
            }
        }

        private void OnHammerHit(BasePlayer player, HitInfo info)
        {
            if (player == null || info == null || info.HitEntity == null) return;

            var activeItem = player.GetActiveItem();
            if (activeItem == null || activeItem.info.shortname != SALVAGED_HAMMER_SHORTNAME) return;

            TrySalvagedHammerRemove(player, info.HitEntity);
        }

        private bool TrySalvagedHammerRemove(BasePlayer player, BaseEntity target)
        {
            if (player == null || target == null || target.IsDestroyed) return false;

            var tc = target.GetBuildingPrivilege() ?? player.GetBuildingPrivilege();
            if (tc != null && !CanManageTc(player, tc)) return false;
            if (tc == null && !player.IsAdmin) return false;

            if (!CanAct(player.userID, 0.25f)) return true;

            var block = target as BuildingBlock;
            if (block != null)
            {
                Effect.server.Run("assets/bundled/prefabs/fx/building/stone_gib.prefab", block.transform.position);
                NextTick(() =>
                {
                    if (block != null && !block.IsDestroyed)
                        block.Kill(BaseNetworkable.DestroyMode.Gib);
                });
                return true;
            }

            ItemDefinition itemDef = FindItemDefinitionForEntity(target);
            if (itemDef != null)
            {
                var storage = target as StorageContainer;
                if (storage != null && storage.inventory != null)
                {
                    storage.DropItems();
                }

                Item item = ItemManager.Create(itemDef, 1, target.skinID);
                if (item != null)
                {
                    player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
                }

                NextTick(() =>
                {
                    if (target != null && !target.IsDestroyed)
                        target.Kill();
                });
                return true;
            }

            if (target.GetComponent<Deployable>() != null || target is BaseCombatEntity)
            {
                NextTick(() =>
                {
                    if (target != null && !target.IsDestroyed)
                        target.Kill();
                });
                return true;
            }

            return false;
        }

        private void HandleRegularHammerInput(BasePlayer player, InputState input)
        {
            var tc = player.GetBuildingPrivilege();
            if (tc == null || !CanManageTc(player, tc)) return;

            if (input.WasJustPressed(BUTTON.FIRE_THIRD) || input.WasJustPressed(BUTTON.RELOAD))
            {
                if (!CanAct(player.userID, 0.2f)) return;

                NudgeMode current = _playerNudgeMode.ContainsKey(player.userID) ? _playerNudgeMode[player.userID] : NudgeMode.Rotate;
                NudgeMode next = (NudgeMode)(((int)current + 1) % 4);
                _playerNudgeMode[player.userID] = next;

                ShowNudgeHud(player, $"PRECISION MODE: <color=#66ccff>{GetNudgeModeLabel(next)}</color>\n<size=11>Hold SPRINT + Left-Click to adjust (+), DUCK + Left-Click (-)</size>");
                return;
            }

            bool isSprint = input.IsDown(BUTTON.SPRINT);
            bool isDuck = input.IsDown(BUTTON.DUCK);

            if ((isSprint || isDuck) && input.WasJustPressed(BUTTON.FIRE_PRIMARY))
            {
                if (!CanAct(player.userID, 0.12f)) return;

                var targetEntity = GetLookedAtDeployable(player);
                if (targetEntity == null) return;

                var entityTc = targetEntity.GetBuildingPrivilege();
                if (entityTc != null && !CanManageTc(player, entityTc)) return;

                float direction = isSprint ? 1f : -1f;
                NudgeMode mode = _playerNudgeMode.ContainsKey(player.userID) ? _playerNudgeMode[player.userID] : NudgeMode.Rotate;

                ApplyPrecisionAdjustment(player, targetEntity, mode, direction);
            }
        }

        private void ApplyPrecisionAdjustment(BasePlayer player, BaseEntity entity, NudgeMode mode, float dir)
        {
            const float moveStep = 0.05f;
            const float rotStep = 15f;

            switch (mode)
            {
                case NudgeMode.Rotate:
                    entity.transform.Rotate(Vector3.up, rotStep * dir, Space.World);
                    ShowNudgeHud(player, $"Rotated: <color=#88ff88>{entity.transform.eulerAngles.y:F0}°</color>");
                    break;

                case NudgeMode.PushPull:
                    Vector3 flatForward = Vector3.ProjectOnPlane(player.eyes.HeadForward(), Vector3.up).normalized;
                    entity.transform.position += flatForward * (moveStep * dir);
                    ShowNudgeHud(player, $"Push/Pull: <color=#88ff88>{(dir > 0 ? "+0.05m" : "-0.05m")}</color>");
                    break;

                case NudgeMode.SlideLeftRight:
                    Vector3 flatRight = Vector3.ProjectOnPlane(player.eyes.HeadRight(), Vector3.up).normalized;
                    entity.transform.position += flatRight * (moveStep * dir);
                    ShowNudgeHud(player, $"Slide L/R: <color=#88ff88>{(dir > 0 ? "+0.05m" : "-0.05m")}</color>");
                    break;

                case NudgeMode.RaiseLower:
                    entity.transform.position += Vector3.up * (moveStep * dir);
                    ShowNudgeHud(player, $"Height: <color=#88ff88>{(dir > 0 ? "+0.05m" : "-0.05m")}</color>");
                    break;
            }

            entity.SendNetworkUpdateImmediate();
        }

        private BaseEntity GetLookedAtDeployable(BasePlayer player)
        {
            RaycastHit hit;
            if (Physics.Raycast(player.eyes.HeadRay(), out hit, 5.0f, LayerMask.GetMask("Deployed", "Default", "Physics_Debris")))
            {
                var entity = hit.GetEntity();
                if (entity != null && !(entity is BuildingBlock) && !(entity is BasePlayer))
                {
                    return entity;
                }
            }
            return null;
        }

        private ItemDefinition FindItemDefinitionForEntity(BaseEntity entity)
        {
            if (entity == null) return null;

            foreach (var def in ItemManager.itemList)
            {
                var deployableMod = def.GetComponent<ItemModDeployable>();
                if (deployableMod != null && deployableMod.entityPrefab != null &&
                    deployableMod.entityPrefab.resourcePath == entity.PrefabName)
                {
                    return def;
                }
            }
            return null;
        }

        #endregion

        #region CUI Overlay & Commands

        private void DrawTcCui(BasePlayer player, BuildingPrivlidge tc)
        {
            CuiHelper.DestroyUi(player, CUI_PANEL);
            if (player == null || tc == null) return;

            var settings = GetTcSettings(tc.net.ID.Value, true);
            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.12 0.14 0.17 0.95" },
                RectTransform = { AnchorMin = "0.02 0.20", AnchorMax = "0.25 0.85" },
                CursorEnabled = true
            }, "Overlay", CUI_PANEL);

            container.Add(new CuiLabel
            {
                Text = { Text = "SMART BUILD TC CONTROLS", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.95 0.95 0.95 1" },
                RectTransform = { AnchorMin = "0.05 0.92", AnchorMax = "0.95 0.98" }
            }, CUI_PANEL);

            AddToggleRow(container, "Building Stability", settings.DisableStability ? "OFF (FLOATING)" : "ON (VANILLA)",
                settings.DisableStability ? "0.20 0.65 0.35 1" : "0.55 0.25 0.25 1", "smartbuild.toggle stability", 0.83f, 0.90f);

            AddToggleRow(container, "Free Placement", settings.FreePlacement ? "ON (CREATIVE)" : "OFF",
                settings.FreePlacement ? "0.20 0.65 0.35 1" : "0.28 0.30 0.34 1", "smartbuild.toggle freeplace", 0.74f, 0.81f);

            string[] gridLabels = { "OFF", "0.25m GRID", "0.50m GRID", "CENTER SNAP" };
            AddToggleRow(container, "Deployable Snap", gridLabels[settings.GridSnapMode],
                settings.GridSnapMode > 0 ? "0.20 0.55 0.75 1" : "0.28 0.30 0.34 1", "smartbuild.toggle grid", 0.65f, 0.72f);

            string[] rotLabels = { "FREE", "45° SNAP", "90° SNAP" };
            AddToggleRow(container, "Rotation Snap", rotLabels[settings.RotationSnapMode],
                settings.RotationSnapMode > 0 ? "0.20 0.55 0.75 1" : "0.28 0.30 0.34 1", "smartbuild.toggle rot", 0.56f, 0.63f);

            AddToggleRow(container, "Wall Height Lock", settings.WallHeightLock ? "ON (UNIFORM)" : "OFF",
                settings.WallHeightLock ? "0.20 0.65 0.35 1" : "0.28 0.30 0.34 1", "smartbuild.toggle wallheight", 0.47f, 0.54f);

            AddToggleRow(container, "No-Decay Base", settings.NoDecay ? "ON (PROTECTED)" : "OFF",
                settings.NoDecay ? "0.20 0.65 0.35 1" : "0.28 0.30 0.34 1", "smartbuild.toggle nodecay", 0.38f, 0.45f);

            AddToggleRow(container, "Overlap Bypass", settings.BypassOverlap ? "ON (FREE)" : "OFF",
                settings.BypassOverlap ? "0.20 0.65 0.35 1" : "0.28 0.30 0.34 1", "smartbuild.toggle overlap", 0.29f, 0.36f);

            string[] mirrorLabels = { "OFF", "2-WAY MIRROR", "4-WAY MIRROR" };
            AddToggleRow(container, "Symmetry Mode", mirrorLabels[settings.MirrorMode],
                settings.MirrorMode > 0 ? "0.65 0.40 0.80 1" : "0.28 0.30 0.34 1", "smartbuild.toggle mirror", 0.20f, 0.27f);

            AddToggleRow(container, "Mirror Center", settings.HasCustomMirrorOrigin ? "CUSTOM (RESET)" : "TC FOUNDATION",
                settings.HasCustomMirrorOrigin ? "0.20 0.65 0.55 1" : "0.35 0.38 0.44 1", "smartbuild.setorigin", 0.11f, 0.18f);

            container.Add(new CuiLabel
            {
                Text = { Text = "Admin Cmd: /cleanfloat [radius] (wipes nearby floating pieces)\nSalvaged Hammer: Hit = Remove | Hammer: R = Mode", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "0.75 0.78 0.82 1" },
                RectTransform = { AnchorMin = "0.04 0.01", AnchorMax = "0.96 0.10" }
            }, CUI_PANEL);

            CuiHelper.AddUi(player, container);
        }

        private void AddToggleRow(CuiElementContainer container, string label, string btnText, string btnColor, string cmd, float yMin, float yMax)
        {
            container.Add(new CuiLabel
            {
                Text = { Text = label, FontSize = 12, Align = TextAnchor.MiddleLeft, Color = "0.88 0.90 0.92 1" },
                RectTransform = { AnchorMin = $"0.06 {yMin}", AnchorMax = $"0.52 {yMax}" }
            }, CUI_PANEL);

            container.Add(new CuiButton
            {
                Button = { Command = cmd, Color = btnColor },
                RectTransform = { AnchorMin = $"0.53 {yMin}", AnchorMax = $"0.94 {yMax}" },
                Text = { Text = btnText, FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, CUI_PANEL);
        }

        private void ShowNudgeHud(BasePlayer player, string message)
        {
            CuiHelper.DestroyUi(player, CUI_HUD);
            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.10 0.12 0.15 0.82" },
                RectTransform = { AnchorMin = "0.35 0.14", AnchorMax = "0.65 0.20" }
            }, "Overlay", CUI_HUD);

            container.Add(new CuiLabel
            {
                Text = { Text = message, FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.02 0.05", AnchorMax = "0.98 0.95" }
            }, CUI_HUD);

            CuiHelper.AddUi(player, container);

            timer.Once(2.5f, () =>
            {
                if (player != null && player.IsConnected)
                {
                    CuiHelper.DestroyUi(player, CUI_HUD);
                }
            });
        }

        private void SetSnappedMirrorOrigin(BasePlayer player, BuildingPrivlidge tc)
        {
            var settings = GetTcSettings(tc.net.ID.Value, true);

            RaycastHit hit;
            BuildingBlock floorBlock = null;
            if (Physics.Raycast(player.transform.position + Vector3.up * 0.5f, Vector3.down, out hit, 4.0f, LayerMask.GetMask("Construction")))
            {
                floorBlock = hit.GetEntity() as BuildingBlock;
            }

            Vector3 snappedPos = player.transform.position;
            float snappedRotY = player.eyes.rotation.eulerAngles.y;

            if (floorBlock != null)
            {
                Vector3 localEuler = (Quaternion.Inverse(floorBlock.transform.rotation) * Quaternion.Euler(0f, snappedRotY, 0f)).eulerAngles;
                localEuler.y = Mathf.Round(localEuler.y / 90f) * 90f;
                snappedRotY = (floorBlock.transform.rotation * Quaternion.Euler(0f, localEuler.y, 0f)).eulerAngles.y;

                Vector3 localPos = floorBlock.transform.InverseTransformPoint(player.transform.position);
                localPos.x = Mathf.Round(localPos.x / 1.5f) * 1.5f;
                localPos.z = Mathf.Round(localPos.z / 1.5f) * 1.5f;
                snappedPos = floorBlock.transform.TransformPoint(localPos);
            }
            else
            {
                snappedRotY = Mathf.Round(snappedRotY / 90f) * 90f;
            }

            settings.HasCustomMirrorOrigin = true;
            settings.MirrorOriginX = snappedPos.x;
            settings.MirrorOriginY = snappedPos.y;
            settings.MirrorOriginZ = snappedPos.z;
            settings.MirrorRotY = snappedRotY;

            SaveData();
            ShowNudgeHud(player, "<color=#66ffcc>CUSTOM MIRROR CENTER SET!</color>\n<size=11>Click again to reset back to TC Foundation center.</size>");
        }

        [ChatCommand("cleanfloat")]
        private void CmdChatCleanFloat(BasePlayer player, string command, string[] args)
        {
            if (player == null || !player.IsAdmin)
            {
                player?.ChatMessage("<color=#ff6666>Admin access required.</color>");
                return;
            }

            float radius = 50f;
            if (args != null && args.Length > 0 && float.TryParse(args[0], out float parsedRadius))
            {
                radius = Mathf.Clamp(parsedRadius, 5f, 250f);
            }

            // Detect any TC right where the admin is standing/looking (no TC auth required!)
            var nearbyTc = player.GetBuildingPrivilege();
            int removed = RemoveDisconnectedInRange(player.transform.position, radius, nearbyTc, nearbyTc?.buildingID ?? 0, true);

            player.ChatMessage($"<color=#66ffcc>[SmartBuildSuite]</color> Removed <color=#ffff66>{removed}</color> floating / unattached pieces within <color=#66ccff>{radius}m</color>.");
        }

        [ChatCommand("mirrorcenter")]
        private void CmdChatMirrorCenter(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            var tc = player.GetBuildingPrivilege();
            if (tc == null || !CanManageTc(player, tc))
            {
                player.ChatMessage("<color=#ff6666>You must be inside an authorized Tool Cupboard radius to set the mirror center.</color>");
                return;
            }

            SetSnappedMirrorOrigin(player, tc);
        }

        [ConsoleCommand("smartbuild.setorigin")]
        private void CmdConsoleSetOrigin(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            BuildingPrivlidge tc;
            if (!_openTcUsers.TryGetValue(player.userID, out tc) || tc == null || !CanManageTc(player, tc)) return;

            var settings = GetTcSettings(tc.net.ID.Value, true);
            if (settings.HasCustomMirrorOrigin)
            {
                settings.HasCustomMirrorOrigin = false;
                SaveData();
                ShowNudgeHud(player, "<color=#66ffcc>MIRROR CENTER RESET TO TC FOUNDATION!</color>");
            }
            else
            {
                SetSnappedMirrorOrigin(player, tc);
            }

            DrawTcCui(player, tc);
        }

        [ConsoleCommand("smartbuild.toggle")]
        private void CmdToggleSetting(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs()) return;

            BuildingPrivlidge tc;
            if (!_openTcUsers.TryGetValue(player.userID, out tc) || tc == null || !CanManageTc(player, tc)) return;

            var settings = GetTcSettings(tc.net.ID.Value, true);
            string option = arg.GetString(0).ToLower();

            switch (option)
            {
                case "stability":
                    settings.DisableStability = !settings.DisableStability;
                    RecalculateBuildingStability(tc);
                    break;
                case "freeplace":
                    settings.FreePlacement = !settings.FreePlacement;
                    SyncFreePlacementConVar(tc);
                    break;
                case "grid":
                    settings.GridSnapMode = (settings.GridSnapMode + 1) % 4;
                    break;
                case "rot":
                    settings.RotationSnapMode = (settings.RotationSnapMode + 1) % 3;
                    break;
                case "wallheight":
                    settings.WallHeightLock = !settings.WallHeightLock;
                    break;
                case "nodecay":
                    settings.NoDecay = !settings.NoDecay;
                    break;
                case "overlap":
                    settings.BypassOverlap = !settings.BypassOverlap;
                    break;
                case "mirror":
                    settings.MirrorMode = (settings.MirrorMode + 1) % 3;
                    break;
            }

            SaveData();
            DrawTcCui(player, tc);
        }

        #endregion

        #region Helpers & Data Persistence

        private TcSettings GetTcSettings(ulong tcNetId, bool createIfMissing)
        {
            TcSettings settings;
            if (!_data.TcConfigs.TryGetValue(tcNetId, out settings) && createIfMissing)
            {
                settings = new TcSettings();
                _data.TcConfigs[tcNetId] = settings;
            }
            return settings;
        }

        private string GetNudgeModeLabel(NudgeMode mode)
        {
            switch (mode)
            {
                case NudgeMode.Rotate: return "ROTATE (15°)";
                case NudgeMode.PushPull: return "PUSH / PULL (0.05m)";
                case NudgeMode.SlideLeftRight: return "SLIDE LEFT / RIGHT (0.05m)";
                case NudgeMode.RaiseLower: return "RAISE / LOWER (0.05m)";
                default: return "ROTATE";
            }
        }

        private bool CanAct(ulong userId, float cooldown)
        {
            float now = Time.realtimeSinceStartup;
            float last;
            if (_lastActionTime.TryGetValue(userId, out last) && now - last < cooldown)
            {
                return false;
            }
            _lastActionTime[userId] = now;
            return true;
        }

        private void LoadData()
        {
            try
            {
                _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            }
            catch
            {
                _data = new StoredData();
            }
            if (_data == null) _data = new StoredData();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, _data);
        }

        #endregion
    }
}
