using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("JunkyardGarageShredder", "Paradox Studios", "2.0.4")]
    [Description("Boosts Junkyard crushable wreck spawns, expands crane pickup targets, places a Salvage Catch Bin with a self-powered Hopper on top, and spawns 2/3/4-module chassis.")]
    public class JunkyardGarageShredder : RustPlugin
    {
        // =========================================================================
        // EASY SETTINGS (Edit any numbers right here in the .cs file!)
        // =========================================================================

        // 1. Junkyard Wreck Spawner Settings
        private const float WreckRespawnMinSeconds = 15f;   // Minimum Spaewn Delay (seconds) for each wreck spawner group
        private const float WreckRespawnMaxSeconds = 30f;   // Max Spaewn Delay (seconds) for each wreck spawner group
        private const int WreckPopulationMultiplier = 2;    // Spawn point Multiplier)

        // 2. Crane Magnet Settings
        private const bool AllowCraneOutsideJunkyard = true; // Prevents crane decay outside monument bounds
        private const float MaxCraneLiftMass = 650f;         // Lighter mass prevents crane cab jerking & tipping

        // Extra vehicles the crane magnet can grab
        private readonly HashSet<string> _extraLiftablePrefabs = new HashSet<string>
        {
            "rowboat",
            "rhib",
            "kayak",
            "bradleyapc",
            "submarinesolo",
            "submarineduo"
        };

        // 3. Salvage Catch Bin + Powered Hopper Settings
        private const bool UseSalvageCatchBin = true;        // Spawns a lootable box + powered hopper at the chute
        private const int CatchBinSlots = 48;                // Number of inventory slots in the catch bin
        private const float BinForwardOffset = -0.6f;         // Distance forward from conveyor chute (meters)
        private const float BinVerticalOffset = -0.85f;      // Height adjustment so bin sits cleanly on ground
        private const float BinYawRotation = 0f;            // Rotates the bin sideways so the lid faces outward
        private const float HopperVacuumRadius = 16.0f;       // Radius around the hopper to vacuum up loose items

        // 4. Guaranteed Resource Payouts (Min / Max per shredded vehicle)
        private readonly List<(string shortname, int min, int max)> _guaranteedResources = new List<(string, int, int)>
        {
            ("scrap", 500, 850), // Not actual ammount just rough estimation
            ("metal.fragments", 100, 1000), // Not actual ammount just rough estimation
            ("metal.refined", 40, 80),
            ("high.quality.metal", 10, 30),
            ("metalpipe", 4, 10),
            ("sheetmetal", 2, 6),
            ("techparts", 2, 12),
            ("gears", 2, 5)
        };

        // 5. Vehicle Module Drop Settings
        private const float FirstModuleChance = 70f;         // % chance to eject a vehicle module
        private const float SecondModuleChance = 30f;        // % bonus chance for a 2nd module

        private readonly string[] _modulePool = new string[]
        {
            "vehicle.1mod.engine",
            "vehicle.1mod.cockpit",
            "vehicle.1mod.cockpit.with.engine",
            "vehicle.1mod.cockpit.armored",
            "vehicle.1mod.storage",
            "vehicle.1mod.flatbed",
            "vehicle.2mod.flatbed",
            "vehicle.2mod.passengers",
            "vehicle.2mod.fuel.tank"
        };

        // 6. Engine Part Drop Settings
        private const float EnginePartDropChance = 60f;      // % chance to eject random engine parts
        private const int EnginePartMinQty = 1;
        private const int EnginePartMaxQty = 3;

        private readonly string[] _enginePartPool = new string[]
        {
            "carburetor1", "carburetor2", "carburetor3",
            "crankshaft1", "crankshaft2", "crankshaft3",
            "piston1", "piston2", "piston3",
            "sparkplug1", "sparkplug2", "sparkplug3",
            "valve1", "valve2", "valve3"
        };

        // 7. Salvaged Bare Chassis Drop Settings
        private const float Chance2ModChassis = 20f;         // % chance for 2-module bare chassis
        private const float Chance3ModChassis = 12f;         // % chance for 3-module bare chassis
        private const float Chance4ModChassis = 6f;          // % chance for 4-module bare chassis
        private const float ChassisForwardOffset = 6.5f;     // Meters forward from conveyor chute (clear of catch bin)
        private const float ChassisMinHealthPercent = 0.40f; // 40% min starting health
        private const float ChassisMaxHealthPercent = 0.75f; // 75% max starting health

        // =========================================================================
        // INTERNAL TRACKING & PREFABS
        // =========================================================================

        private const string CatchBinPrefab = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab";
        private const string Chassis2Mod = "assets/content/vehicles/modularcar/car_chassis_2module.entity.prefab";
        private const string Chassis3Mod = "assets/content/vehicles/modularcar/car_chassis_3module.entity.prefab";
        private const string Chassis4Mod = "assets/content/vehicles/modularcar/car_chassis_4module.entity.prefab";

        private readonly List<LargeShredder> _cachedShredders = new List<LargeShredder>();
        private readonly Dictionary<ulong, StorageContainer> _shredderBins = new Dictionary<ulong, StorageContainer>();
        private readonly Dictionary<ulong, BaseEntity> _shredderHoppers = new Dictionary<ulong, BaseEntity>();
        private readonly Dictionary<ulong, float> _recentlyShredded = new Dictionary<ulong, float>();
        private readonly HashSet<ulong> _customDroppedItemIds = new HashSet<ulong>();

        private Timer _hopperSweepTimer;
        private string _resolvedHopperPrefab;

        private static readonly HashSet<string> _vanillaDropShortnames = new HashSet<string>
        {
            "scrap",
            "metal.fragments",
            "metal.refined",
            "wood"
        };

        private void OnServerInitialized()
        {
            ResolveHopperPrefabPath();
            CacheAllShredders();
            BoostJunkyardWreckSpawners();

            if (UseSalvageCatchBin)
            {
                SetupAllCatchBins();
                StartHopperVacuumTimer();
            }

            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is BaseEntity baseEnt)
                    ConfigureVehicleForCrane(baseEnt);
            }
        }

        private void Unload()
        {
            _hopperSweepTimer?.Destroy();

            foreach (var kvp in _shredderHoppers)
            {
                if (kvp.Value != null && !kvp.Value.IsDestroyed)
                    kvp.Value.Kill();
            }
            _shredderHoppers.Clear();

            foreach (var kvp in _shredderBins)
            {
                if (kvp.Value != null && !kvp.Value.IsDestroyed)
                    kvp.Value.Kill();
            }
            _shredderBins.Clear();
        }

        private void ResolveHopperPrefabPath()
        {
            var itemDef = ItemManager.FindItemDefinition("hopper");
            if (itemDef != null)
            {
                var deployable = itemDef.GetComponent<ItemModDeployable>();
                if (deployable != null && deployable.entityPrefab != null && !string.IsNullOrEmpty(deployable.entityPrefab.resourcePath))
                {
                    _resolvedHopperPrefab = deployable.entityPrefab.resourcePath;
                    return;
                }
            }

            foreach (var entry in StringPool.toNumber)
            {
                if (entry.Key.EndsWith("hopper.deployed.prefab"))
                {
                    _resolvedHopperPrefab = entry.Key;
                    return;
                }
            }
        }

        private void CacheAllShredders()
        {
            _cachedShredders.Clear();
            foreach (var shredder in UnityEngine.Object.FindObjectsOfType<LargeShredder>())
            {
                if (shredder != null && !shredder.IsDestroyed)
                    _cachedShredders.Add(shredder);
            }
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null) return null;
            if (entity is StorageContainer container && _shredderBins.ContainsValue(container))
                return true;
            if (_shredderHoppers.ContainsValue(entity))
                return true;
            return null;
        }

        private object CanPickupEntity(BasePlayer player, BaseEntity entity)
        {
            if (entity == null) return null;
            if (entity is StorageContainer container && _shredderBins.ContainsValue(container))
                return false;
            if (_shredderHoppers.ContainsValue(entity))
                return false;
            return null;
        }

        private void OnEntitySpawned(BaseEntity entity)
        {
            if (entity == null) return;

            if (entity is LargeShredder newShredder)
            {
                if (!_cachedShredders.Contains(newShredder))
                    _cachedShredders.Add(newShredder);
                if (UseSalvageCatchBin)
                    SetupCatchBinForShredder(newShredder);
                return;
            }

            if (entity is DroppedItem droppedItem)
            {
                if (_recentlyShredded.Count > 0 && droppedItem.item != null)
                    HandleVanillaShredDrop(droppedItem);
                return;
            }

            if (entity is MagnetCrane || entity is BaseVehicle)
            {
                ConfigureVehicleForCrane(entity);
            }
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            if (_cachedShredders.Count == 0 || entity == null || !(entity is BaseEntity baseEnt))
                return;

            if (baseEnt.HasParent()) return;
            if (!(baseEnt is BaseVehicle) && !baseEnt.ShortPrefabName.Contains("shreddable"))
                return;
            if (baseEnt.PrefabName.Contains("car_chassis_"))
                return;

            LargeShredder nearestShredder = null;
            float bestDistSqr = 18f * 18f;
            Vector3 entPos = baseEnt.transform.position;

            for (int i = _cachedShredders.Count - 1; i >= 0; i--)
            {
                var shredder = _cachedShredders[i];
                if (shredder == null || shredder.IsDestroyed)
                {
                    _cachedShredders.RemoveAt(i);
                    continue;
                }

                float distSqr = (entPos - shredder.transform.position).sqrMagnitude;
                if (distSqr <= bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    nearestShredder = shredder;
                }
            }

            if (nearestShredder != null)
            {
                TriggerShredderPayout(nearestShredder);
            }
        }

        private void TriggerShredderPayout(LargeShredder shredder)
        {
            ulong netId = shredder.net.ID.Value;

            if (_recentlyShredded.TryGetValue(netId, out float lastTime) && Time.realtimeSinceStartup - lastTime < 10f)
                return;

            _recentlyShredded[netId] = Time.realtimeSinceStartup;

            timer.Once(12.5f, () =>
            {
                if (_recentlyShredded.TryGetValue(netId, out float t) && Time.realtimeSinceStartup - t >= 12f)
                    _recentlyShredded.Remove(netId);
            });

            Vector3 ejectPos = shredder.resourceSpawnPoint != null
                ? shredder.resourceSpawnPoint.position
                : shredder.transform.position;

            timer.Once(1.2f, () =>
            {
                if (shredder != null && !shredder.IsDestroyed)
                    EjectGarageSalvage(shredder, ejectPos);
            });
        }

        // =========================================================================
        // SALVAGE CATCH BIN & POWERED HOPPER SETUP
        // =========================================================================

        private void SetupAllCatchBins()
        {
            foreach (var shredder in _cachedShredders)
            {
                SetupCatchBinForShredder(shredder);
            }
        }

        private StorageContainer SetupCatchBinForShredder(LargeShredder shredder)
        {
            if (shredder == null || shredder.IsDestroyed) return null;

            ulong shredderId = shredder.net.ID.Value;
            if (_shredderBins.TryGetValue(shredderId, out var existingBin) && existingBin != null && !existingBin.IsDestroyed)
            {
                EnsureHopperOnBin(shredderId, existingBin);
                return existingBin;
            }

            Transform chute = shredder.resourceSpawnPoint != null ? shredder.resourceSpawnPoint : shredder.transform;
            Vector3 binPos = chute.position + (chute.forward * BinForwardOffset) + (Vector3.up * BinVerticalOffset);

            if (Physics.Raycast(binPos + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3.5f, LayerMask.GetMask("Terrain", "World", "Construction")))
            {
                binPos = hit.point;
            }

            Quaternion binRot = chute.rotation * Quaternion.Euler(0f, BinYawRotation, 0f);

            BaseEntity boxEnt = GameManager.server.CreateEntity(CatchBinPrefab, binPos, binRot);
            if (boxEnt == null) return null;

            boxEnt.Spawn();

            if (boxEnt is StorageContainer container)
            {
                container.inventory.capacity = CatchBinSlots;
                container.SendNetworkUpdate();
                _shredderBins[shredderId] = container;

                EnsureHopperOnBin(shredderId, container);
                return container;
            }

            return null;
        }

        private void EnsureHopperOnBin(ulong shredderId, StorageContainer bin)
        {
            if (bin == null || bin.IsDestroyed) return;

            if (_shredderHoppers.TryGetValue(shredderId, out var existingHopper) && existingHopper != null && !existingHopper.IsDestroyed)
            {
                PowerOnHopper(existingHopper);
                return;
            }

            if (string.IsNullOrEmpty(_resolvedHopperPrefab))
                ResolveHopperPrefabPath();

            if (string.IsNullOrEmpty(_resolvedHopperPrefab))
            {
                PrintWarning("[JunkyardGarageShredder] Could not resolve Hopper prefab path; Catch Bin will still collect items automatically.");
                return;
            }

            Vector3 hopperPos = bin.transform.position + Vector3.up * 0.85f;
            BaseEntity hopperEnt = GameManager.server.CreateEntity(_resolvedHopperPrefab, hopperPos, bin.transform.rotation);
            if (hopperEnt == null) return;

            hopperEnt.SetParent(bin, worldPositionStays: true);
            hopperEnt.Spawn();
            PowerOnHopper(hopperEnt);
            _shredderHoppers[shredderId] = hopperEnt;
        }

        private void PowerOnHopper(BaseEntity hopperEnt)
        {
            if (hopperEnt == null || hopperEnt.IsDestroyed) return;

            hopperEnt.flags |= BaseEntity.Flags.Reserved8 | BaseEntity.Flags.On;

            if (hopperEnt is IOEntity io)
            {
                io.UpdateHasPower(25, 0);
                io.SendNetworkUpdateImmediate();
            }
            else
            {
                hopperEnt.SendNetworkUpdateImmediate();
            }
        }

        // Continuously vacuums any loose items near the hopper/chute straight into the Catch Bin
        private void StartHopperVacuumTimer()
        {
            _hopperSweepTimer?.Destroy();
            _hopperSweepTimer = timer.Every(1.0f, () =>
            {
                if (_shredderBins.Count == 0) return;

                foreach (var kvp in _shredderBins)
                {
                    ulong shredderId = kvp.Key;
                    StorageContainer bin = kvp.Value;
                    if (bin == null || bin.IsDestroyed || bin.inventory == null) continue;

                    if (_shredderHoppers.TryGetValue(shredderId, out var hopper) && hopper != null && !hopper.IsDestroyed)
                    {
                        if (hopper is IOEntity io && !io.IsPowered())
                            PowerOnHopper(hopper);

                        // Also flush anything inside the hopper's own internal buffer into the Catch Bin
                        if (hopper is StorageContainer hopperStorage && hopperStorage.inventory != null)
                        {
                            for (int i = hopperStorage.inventory.itemList.Count - 1; i >= 0; i--)
                            {
                                var item = hopperStorage.inventory.itemList[i];
                                item?.MoveToContainer(bin.inventory);
                            }
                        }
                    }

                    Vector3 sweepCenter = hopper != null && !hopper.IsDestroyed
                        ? hopper.transform.position
                        : bin.transform.position + Vector3.up * 0.85f;

                    // Query all layers (-1) because DroppedItem entities sit on DroppedItem / Physics_Debris / Water / Ragdoll layers
                    List<DroppedItem> nearbyDrops = Facepunch.Pool.Get<List<DroppedItem>>();
                    Vis.Entities(sweepCenter, HopperVacuumRadius, nearbyDrops, -1, QueryTriggerInteraction.Collide);

                    for (int i = 0; i < nearbyDrops.Count; i++)
                    {
                        DroppedItem drop = nearbyDrops[i];
                        if (drop == null || drop.IsDestroyed || drop.item == null) continue;

                        if (drop.item.MoveToContainer(bin.inventory))
                        {
                            Effect.server.Run("assets/bundled/prefabs/fx/notice/loot.drag.grab.fx.prefab", sweepCenter);
                        }
                    }

                    Facepunch.Pool.FreeUnmanaged(ref nearbyDrops);
                }
            });
        }

        // =========================================================================
        // 1. WRECK SPAWN BOOSTER
        // =========================================================================

        private void BoostJunkyardWreckSpawners()
        {
            int boostedGroups = 0;

            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                if (monument == null || !monument.name.ToLower().Contains("junkyard"))
                    continue;

                var spawnGroups = monument.GetComponentsInChildren<SpawnGroup>(true);
                foreach (var group in spawnGroups)
                {
                    if (group == null || group.prefabs == null) continue;

                    bool isCrushableWreckGroup = false;
                    foreach (var entry in group.prefabs)
                    {
                        if (entry.prefab != null && entry.prefab.resourcePath.Contains("shreddable"))
                        {
                            isCrushableWreckGroup = true;
                            break;
                        }
                    }

                    if (!isCrushableWreckGroup) continue;

                    group.respawnDelayMin = WreckRespawnMinSeconds;
                    group.respawnDelayMax = WreckRespawnMaxSeconds;

                    int totalPoints = group.spawnPoints != null ? group.spawnPoints.Length : group.maxPopulation;
                    group.maxPopulation = Mathf.Min(totalPoints, group.maxPopulation * WreckPopulationMultiplier);

                    group.Clear();
                    group.Fill();
                    boostedGroups++;
                }
            }

            Puts($"[JunkyardGarageShredder] Boosted {boostedGroups} crushable wreck spawn group(s) at Junkyard.");
        }

        // =========================================================================
        // 2. CRANE & LIFTABLE VEHICLE SETUP
        // =========================================================================

        private void ConfigureVehicleForCrane(BaseEntity entity)
        {
            if (AllowCraneOutsideJunkyard && entity is MagnetCrane crane)
            {
                crane.maxDistanceFromOrigin = float.MaxValue;
                return;
            }

            string prefab = entity.ShortPrefabName;
            foreach (string keyword in _extraLiftablePrefabs)
            {
                if (prefab.Contains(keyword))
                {
                    if (entity.GetComponent<MagnetLiftable>() == null)
                    {
                        entity.gameObject.AddComponent<MagnetLiftable>();
                    }

                    var rb = entity.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        rb.isKinematic = false;
                        if (rb.mass > MaxCraneLiftMass)
                            rb.mass = MaxCraneLiftMass;
                    }

                    entity.gameObject.layer = 26; // Vehicle_World layer
                    break;
                }
            }
        }

        // =========================================================================
        // 3. CUSTOM SHREDDER OUTPUT & CHASSIS EJECTION
        // =========================================================================

        private void HandleVanillaShredDrop(DroppedItem droppedItem)
        {
            if (droppedItem.net != null && _customDroppedItemIds.Contains(droppedItem.net.ID.Value))
                return;

            string shortname = droppedItem.item.info.shortname;
            if (!_vanillaDropShortnames.Contains(shortname))
                return;

            Vector3 itemPos = droppedItem.transform.position;

            for (int i = 0; i < _cachedShredders.Count; i++)
            {
                var shredder = _cachedShredders[i];
                if (shredder == null || shredder.IsDestroyed) continue;

                if (!_recentlyShredded.TryGetValue(shredder.net.ID.Value, out float shredTime))
                    continue;

                if (Time.realtimeSinceStartup - shredTime > 12f)
                    continue;

                Vector3 dropPos = shredder.resourceSpawnPoint != null
                    ? shredder.resourceSpawnPoint.position
                    : shredder.transform.position;

                if ((itemPos - dropPos).sqrMagnitude <= 100f)
                {
                    NextTick(() =>
                    {
                        if (droppedItem != null && !droppedItem.IsDestroyed)
                        {
                            if (droppedItem.net != null && _customDroppedItemIds.Contains(droppedItem.net.ID.Value))
                                return;
                            droppedItem.Kill();
                        }
                    });
                    break;
                }
            }
        }

        private void EjectGarageSalvage(LargeShredder shredder, Vector3 ejectPos)
        {
            StorageContainer catchBin = UseSalvageCatchBin ? SetupCatchBinForShredder(shredder) : null;

            // 1. Guaranteed Resources (Scrap, HQM, Frags, Components)
            foreach (var res in _guaranteedResources)
            {
                if (string.IsNullOrEmpty(res.shortname) || res.max <= 0) continue;
                int amount = UnityEngine.Random.Range(res.min, res.max + 1);
                if (amount > 0)
                    DeliverSalvageItem(res.shortname, amount, ejectPos, catchBin);
            }

            // 2. Roll for 1 to 2 Random Vehicle Modules
            if (_modulePool.Length > 0 && UnityEngine.Random.Range(0f, 100f) <= FirstModuleChance)
            {
                string mod1 = _modulePool[UnityEngine.Random.Range(0, _modulePool.Length)];
                DeliverSalvageItem(mod1, 1, ejectPos, catchBin);

                if (UnityEngine.Random.Range(0f, 100f) <= SecondModuleChance)
                {
                    string mod2 = _modulePool[UnityEngine.Random.Range(0, _modulePool.Length)];
                    DeliverSalvageItem(mod2, 1, ejectPos, catchBin);
                }
            }

            // 3. Roll for Random Engine Components
            if (_enginePartPool.Length > 0 && UnityEngine.Random.Range(0f, 100f) <= EnginePartDropChance)
            {
                string part = _enginePartPool[UnityEngine.Random.Range(0, _enginePartPool.Length)];
                int qty = UnityEngine.Random.Range(EnginePartMinQty, EnginePartMaxQty + 1);
                if (qty > 0)
                    DeliverSalvageItem(part, qty, ejectPos, catchBin);
            }

            // 4. Roll for a Salvaged Bare Vehicle Chassis (Evaluated from rarest 4-mod to 2-mod)
            float roll = UnityEngine.Random.Range(0f, 100f);
            float threshold4 = Chance4ModChassis;
            float threshold3 = threshold4 + Chance3ModChassis;
            float threshold2 = threshold3 + Chance2ModChassis;

            if (roll <= threshold4)
            {
                SpawnSalvagedChassis(shredder, ejectPos, Chassis4Mod);
            }
            else if (roll <= threshold3)
            {
                SpawnSalvagedChassis(shredder, ejectPos, Chassis3Mod);
            }
            else if (roll <= threshold2)
            {
                SpawnSalvagedChassis(shredder, ejectPos, Chassis2Mod);
            }
        }

        private void DeliverSalvageItem(string shortname, int amount, Vector3 pos, StorageContainer catchBin)
        {
            Item item = ItemManager.CreateByName(shortname, amount);
            if (item == null) return;

            if (catchBin != null && !catchBin.IsDestroyed && catchBin.inventory != null)
            {
                if (item.MoveToContainer(catchBin.inventory))
                    return;
            }

            Vector3 velocity = new Vector3(
                UnityEngine.Random.Range(-1.2f, 1.2f),
                1.5f,
                UnityEngine.Random.Range(-1.2f, 1.2f)
            );

            BaseEntity droppedEntity = item.Drop(pos + Vector3.up * 0.4f, velocity);
            if (droppedEntity != null && droppedEntity.net != null)
            {
                ulong netId = droppedEntity.net.ID.Value;
                _customDroppedItemIds.Add(netId);
                timer.Once(15f, () => _customDroppedItemIds.Remove(netId));
            }
        }

        private void SpawnSalvagedChassis(LargeShredder shredder, Vector3 ejectPos, string prefabPath)
        {
            Vector3 forwardDir = shredder.resourceSpawnPoint != null
                ? shredder.resourceSpawnPoint.forward
                : shredder.transform.forward;

            Vector3 spawnPos = ejectPos + (forwardDir * ChassisForwardOffset) + Vector3.up * 1.5f;

            if (Physics.Raycast(spawnPos + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, LayerMask.GetMask("Terrain", "World", "Construction")))
            {
                spawnPos = hit.point + Vector3.up * 0.5f;
            }

            BaseEntity chassis = GameManager.server.CreateEntity(prefabPath, spawnPos, shredder.transform.rotation);
            if (chassis == null) return;

            chassis.Spawn();

            if (chassis is BaseCombatEntity combatEntity)
            {
                float hpMult = UnityEngine.Random.Range(ChassisMinHealthPercent, ChassisMaxHealthPercent);
                combatEntity.SetHealth(combatEntity.MaxHealth() * Mathf.Clamp(hpMult, 0.05f, 1.0f));
            }
        }
    }
}
