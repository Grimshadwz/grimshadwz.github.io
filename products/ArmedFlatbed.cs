using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Oxide.Core;
using Oxide.Game.Rust.Cui;

namespace Oxide.Plugins
{
    [Info("ArmedFlatbed", "Paradox Studios", "3.4.0")]
    [Description("Builds a custom armed flatbed with CUI dashboard controls, Tier 3 workbench aura, campfire water purifier, recycler, siren, lights, mini fridge, auto turret, and full restart persistence.")]
    public class ArmedFlatbed : RustPlugin
    {
        private const string PermUse = "armedflatbed.use";
        private const string UiName = "ArmedFlatbed_ControlPanel";
        private const string FlatbedDataFile = "ArmedFlatbedData";
        private const string TurretDataFile = "ArmedFlatbed_Turrets";
        private const ulong ArmedCarSkinTag = 99887766;

        // =========================================================================
        // EASY POSITION & ROTATION ADJUSTMENTS (X = Left/Right, Y = Up/Down, Z = Front/Back)
        // =========================================================================

        // Large Wood Box
        private readonly Vector3 BoxLocalPos = new Vector3(0f, 0.6f, 0.2f);
        private readonly Vector3 BoxLocalRot = new Vector3(0f, 0f, 0f);
/*
        // Coffin 1 & Coffin 2 (Additional Storage)
        private readonly Vector3 Coffin1LocalPos = new Vector3(-0.52f, 0.76f, -1.25f);
        private readonly Vector3 Coffin1LocalRot = new Vector3(90f, 90f, 0f);
        private readonly Vector3 Coffin2LocalPos = new Vector3(0.82f, 1.06f, -0.55f);
        private readonly Vector3 Coffin2LocalRot = new Vector3(90f, 90f, 0f);
*/
        // Beach Towel / Sleeping Bag Respawn
        private readonly Vector3 SleepingBagLocalPos = new Vector3(0f, 1.35f, 0.2f);
        private readonly Vector3 SleepingBagLocalRot = new Vector3(0f, 90f, 0f);

        // Beach Towel Custom Respawn Offset (Relative to Truck)
        private readonly Vector3 RespawnOffsetPos = new Vector3(1.65f, 5.25f, -0.50f);

        // Mini Fridge
        private readonly Vector3 MiniFridgeLocalPos = new Vector3(-0.35f, 0.76f, -0.65f);
        private readonly Vector3 MiniFridgeLocalRot = new Vector3(0f, 180f, 0f);

        // Campfire & Campfire Water Purifier (Purifier sits directly over Campfire)
        private readonly Vector3 CampfireLocalPos = new Vector3(-0.38f, 0.76f, -1.50f);
        private readonly Vector3 CampfireLocalRot = new Vector3(0f, 0f, 0f);

        private readonly Vector3 WaterPurifierLocalPos = new Vector3(-0.38f, 0.76f, -1.5f);
        private readonly Vector3 WaterPurifierLocalRot = new Vector3(0f, 180f, 0f);

        // Functional Recycler
        private readonly Vector3 RecyclerLocalPos = new Vector3(0.3f, 0.76f, -1.25f);
        private readonly Vector3 RecyclerLocalRot = new Vector3(0f, 90f, 0f);

        // Rotating Red Siren Light
        private readonly Vector3 BeaconLocalPos = new Vector3(0.65f, 1.35f, 0.45f);
        private readonly Vector3 BeaconLocalRot = new Vector3(0f, 0f, 0f);

        // 4 Simple Lights (Mounted along the 4 bed corners)
        private readonly Vector3 SimpleLight1LocalPos = new Vector3(-0.65f, 2.0f, 1.4f);
        private readonly Vector3 SimpleLight1LocalRot = new Vector3(0f, 0f, 0f);

        private readonly Vector3 SimpleLight2LocalPos = new Vector3(0.65f, 2.0f, 1.4f);
        private readonly Vector3 SimpleLight2LocalRot = new Vector3(0f, 0f, 0f);

        private readonly Vector3 SimpleLight3LocalPos = new Vector3(-0.75f, 1.12f, -1.25f);
        private readonly Vector3 SimpleLight3LocalRot = new Vector3(0f, -90f, 0f);

        private readonly Vector3 SimpleLight4LocalPos = new Vector3(0.75f, 1.12f, -1.25f);
        private readonly Vector3 SimpleLight4LocalRot = new Vector3(0f, 90f, 0f);

        // Auto Turret
        private readonly Vector3 TurretLocalPos = new Vector3(0f, 2.0f, 1.0f);
        private readonly Vector3 TurretLocalRot = new Vector3(0f, 180f, 0f);

        // Electric Switch
        private readonly Vector3 SwitchLocalPos = new Vector3(-0.84f, 1.0f, 0.42f);
        private readonly Vector3 SwitchLocalRot = new Vector3(0f, -90f, 0f);

        // =========================================================================

        private const string BoxPrefab = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab";
        private const string TurretPrefab = "assets/prefabs/npc/autoturret/autoturret_deployed.prefab";
        private const string GeneratorPrefab = "assets/prefabs/io/electric/generators/generator.static_hidden.prefab";
        private const string SwitchPrefab = "assets/prefabs/deployable/playerioents/simpleswitch/switch.prefab";
        private const string RecyclerPrefab = "assets/bundled/prefabs/static/recycler_static.prefab";
        private const string Workbench3Prefab = "assets/prefabs/deployable/tier 3 workbench/workbench3.deployed.prefab";

        private readonly Dictionary<ulong, uint> _activeUiCar = new Dictionary<ulong, uint>();
        private readonly Dictionary<ulong, uint> _pendingBagRespawnCar = new Dictionary<ulong, uint>();
        private readonly HashSet<uint> _t3WorkbenchActiveCars = new HashSet<uint>();
        private readonly HashSet<ulong> _playersWithTruckT3 = new HashSet<ulong>();
        private Timer _workbenchCheckTimer;

        private readonly Dictionary<ulong, FlatbedSaveData> _flatbedSaveData = new Dictionary<ulong, FlatbedSaveData>();
        private Dictionary<uint, SavedTurretData> _savedTurrets = new Dictionary<uint, SavedTurretData>();

        private class FlatbedSaveData
        {
            public ulong CarId;
            public ulong OwnerId;
            public bool WorkbenchEnabled;
            public bool HeadlightsOn;
            public bool SimpleLightsOn;
            public bool BeaconOn;
            public bool FridgeOn;
            public bool TurretOn;
        }

        private class SavedTurretData
        {
            public List<ulong> AuthorizedPlayers = new List<ulong>();
            public List<SavedItemData> Items = new List<SavedItemData>();
        }

        private class SavedItemData
        {
            public string Shortname;
            public int Amount;
            public int Slot;
            public ulong Skin;
            public float Condition;
            public string AmmoType;
            public int AmmoCount;
        }
        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            LoadFlatbedData();
            _savedTurrets = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<uint, SavedTurretData>>(TurretDataFile) ?? new Dictionary<uint, SavedTurretData>();
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            var baseEnt = entity as BaseEntity;
            if (baseEnt == null || baseEnt is BaseVehicleModule || baseEnt is ModularCar) return;
            if (baseEnt.ShortPrefabName.Contains("fuel")) return;

            var car = baseEnt.GetComponentInParent<ModularCar>();
            if (car != null)
            {
                foreach (var col in baseEnt.GetComponentsInChildren<Collider>(true))
                {
                    if (col != null) col.enabled = false;
                }
            }
        }
        private Timer _turretAiTimer;
//Refresh Truck Generators (to keep them powered)
        private void RefreshTruckGenerators()
        {
            foreach (var car in BaseNetworkable.serverEntities.OfType<ModularCar>())
            {
                if (car == null || car.IsDestroyed || !IsArmedFlatbed(car)) continue;

                foreach (var gen in car.GetComponentsInChildren<ElectricGenerator>(true))
                {
                    if (gen == null || gen.IsDestroyed) continue;

                    var parentIo = gen.GetParentEntity() as IOEntity;
                    if (parentIo == null || parentIo.IsDestroyed) continue;

                    // Keep generator powered unless the user explicitly turned that device OFF in /truckui
                    if (parentIo is AutoTurret)
                    {
                        var sw = car.children.OfType<ElectricSwitch>().FirstOrDefault();
                        if (sw != null && !sw.IsOn()) continue;
                    }
                    else if (!parentIo.HasFlag(BaseEntity.Flags.On))
                    {
                        continue;
                    }

                    if (gen.electricAmount < 100f || gen.GetCurrentEnergy() <= 0)
                    {
                        gen.electricAmount = 100f;
                        gen.ForcePuzzleReset();
                        gen.MarkDirtyForceUpdateOutputs();
                    }
                }
            }
        }


        private void OnServerInitialized()
        {
            timer.Once(3.0f, () =>
            {
                foreach (var car in BaseNetworkable.serverEntities.OfType<ModularCar>())
                {
                    if (car == null || car.IsDestroyed || !car.IsFullySpawned() || !IsArmedFlatbed(car)) continue;

                    RestoreArmedFlatbedAfterRestart(car);
                }
            });

            _workbenchCheckTimer = timer.Every(1.0f, UpdateVirtualWorkbenchAura);
            _turretAiTimer = timer.Every(0.5f, UpdateTruckTurretNpcTargets);
            timer.Every(30f, RefreshTruckGenerators);


            
        }
      
        private Timer _generatorRefreshTimer;

        private void OnServerSave()
        {
            _savedTurrets.Clear();
            foreach (var car in BaseNetworkable.serverEntities.OfType<ModularCar>())
            {
                if (car == null || car.IsDestroyed || !IsArmedFlatbed(car)) continue;
                car.skinID = ArmedCarSkinTag;
                CaptureFlatbedState(car);

                var turret = car.GetComponentInChildren<AutoTurret>();
                if (turret != null && turret.inventory != null)
                {
                    var data = new SavedTurretData
                    {
                        AuthorizedPlayers = turret.authorizedPlayers.ToList()
                    };

                    foreach (var item in turret.inventory.itemList)
                    {
                        var itemData = new SavedItemData
                        {
                            Shortname = item.info.shortname,
                            Amount = item.amount,
                            Slot = item.position,
                            Skin = item.skin,
                            Condition = item.condition
                        };

                        var held = item.GetHeldEntity() as BaseProjectile;
                        if (held != null && held.primaryMagazine != null)
                        {
                            itemData.AmmoType = held.primaryMagazine.ammoType?.shortname;
                            itemData.AmmoCount = held.primaryMagazine.contents;
                        }

                        data.Items.Add(itemData);
                    }

                    _savedTurrets[(uint)car.net.ID.Value] = data;
                }
            }

            Interface.Oxide.DataFileSystem.WriteObject(TurretDataFile, _savedTurrets);
        }
        private void RestoreArmedFlatbedAfterRestart(ModularCar car)
        {
            uint id = (uint)car.net.ID.Value;
            car.skinID = ArmedCarSkinTag;
            GetOrRestoreFuelContainer(car);

            foreach (var child in car.children.ToList())
            {
                if (child == null || child is BaseVehicleModule || child.ShortPrefabName.Contains("fuel"))
                    continue;

                StripPhysicsAndStability(child);

                if (child is WaterPurifier || child.ShortPrefabName.Contains("purifier"))
                {
                    FillPurifierWater(child);
                }

                if (child is IOEntity ioEnt)
                {
                    foreach (var gen in ioEnt.GetComponentsInChildren<ElectricGenerator>(true))
                    {
                        StripPhysicsAndStability(gen);
                        gen.electricAmount = 100f;
                        gen.ForcePuzzleReset();
                        gen.MarkDirtyForceUpdateOutputs();
                    }

                    if (!(ioEnt is ElectricSwitch) && !(ioEnt is AutoTurret))
                    {
                        ioEnt.flags |= (BaseEntity.Flags.On | BaseEntity.Flags.Reserved8);
                    }
                    ioEnt.SendNetworkUpdateImmediate();
                }

                child.SendNetworkUpdateImmediate();
            }

            if (car.GetComponentInChildren<Recycler>() == null)
            {
                var recycler = GameManager.server.CreateEntity(RecyclerPrefab, car.transform.position, car.transform.rotation) as Recycler;
                if (recycler != null)
                {
                    StripPhysicsAndStability(recycler);
                    recycler.enableSaving = false;
                    recycler.OwnerID = car.OwnerID;
                    recycler.Spawn();
                    recycler.SetParent(car, worldPositionStays: false);
                    recycler.transform.localPosition = RecyclerLocalPos;
                    recycler.transform.localRotation = Quaternion.Euler(RecyclerLocalRot);
                    recycler.SendNetworkUpdateImmediate();
                }
            }

            if (car.GetComponentInChildren<AutoTurret>() == null)
            {
                var turret = GameManager.server.CreateEntity(TurretPrefab, car.transform.position, car.transform.rotation) as AutoTurret;
                if (turret != null)
                {
                    StripPhysicsAndStability(turret);
                    turret.enableSaving = false;
                    turret.OwnerID = car.OwnerID;
                    turret.Spawn();

                    SavedTurretData tData;
                    if (_savedTurrets.TryGetValue(id, out tData))
                    {
                        foreach (var authId in tData.AuthorizedPlayers)
                            turret.authorizedPlayers.Add(authId);

                        // 1. Restore the weapon in slot 0 FIRST and attach it
                        foreach (var savedItem in tData.Items.Where(x => x.Slot == 0))
                        {
                            var restored = ItemManager.CreateByName(savedItem.Shortname, savedItem.Amount, savedItem.Skin);
                            if (restored != null)
                            {
                                restored.condition = savedItem.Condition;
                                restored.MoveToContainer(turret.inventory, 0);

                                var held = restored.GetHeldEntity() as BaseProjectile;
                                if (held != null && held.primaryMagazine != null && !string.IsNullOrEmpty(savedItem.AmmoType))
                                {
                                    var ammoDef = ItemManager.FindItemDefinition(savedItem.AmmoType);
                                    if (ammoDef != null) held.primaryMagazine.ammoType = ammoDef;
                                    held.primaryMagazine.contents = savedItem.AmmoCount;
                                }
                            }
                        }

                        turret.UpdateAttachedWeapon();

                        // 2. Restore reserve ammo into slots 1-6 AFTER the weapon is attached
                        foreach (var savedItem in tData.Items.Where(x => x.Slot > 0))
                        {
                            var restoredAmmo = ItemManager.CreateByName(savedItem.Shortname, savedItem.Amount, savedItem.Skin);
                            if (restoredAmmo != null)
                            {
                                if (!restoredAmmo.MoveToContainer(turret.inventory, savedItem.Slot))
                                {
                                    restoredAmmo.MoveToContainer(turret.inventory);
                                }
                            }
                        }

                        turret.UpdateTotalAmmo();
                    }

                    else if (car.OwnerID != 0)
                    {
                        turret.authorizedPlayers.Add(car.OwnerID);
                    }

                    turret.SetParent(car, worldPositionStays: false);
                    turret.transform.localPosition = TurretLocalPos;
                    turret.transform.localRotation = Quaternion.Euler(TurretLocalRot);

                    var electricSwitch = car.children.OfType<ElectricSwitch>().FirstOrDefault();
                    if (electricSwitch != null)
                    {
                        electricSwitch.outputs.First().connectedTo.Set(turret);
                        electricSwitch.outputs.First().connectedToSlot = 0;
                        turret.inputs.First().connectedTo.Set(electricSwitch);
                        turret.inputs.First().connectedToSlot = 0;
                        electricSwitch.MarkDirtyForceUpdateOutputs();
                    }

                    turret.SendNetworkUpdateImmediate();
                }
            }

            FlatbedSaveData savedState;
            if (_flatbedSaveData.TryGetValue((ulong)id, out savedState))
            {
                if (savedState.WorkbenchEnabled)
                {
                    _t3WorkbenchActiveCars.Add(id);
                    EnsureHiddenWorkbenchTrigger(car, true);
                }
                else
                {
                    _t3WorkbenchActiveCars.Remove(id);
                    EnsureHiddenWorkbenchTrigger(car, false);
                }
                ApplySavedFlatbedState(car, savedState);
            }
            else
            {
                _t3WorkbenchActiveCars.Add(id);
                EnsureHiddenWorkbenchTrigger(car, true);
            }
        }
        private void Unload()
        {
            _workbenchCheckTimer?.Destroy();

            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, UiName);
                if (_playersWithTruckT3.Contains(player.userID))
                {
                    player.cachedCraftLevel = 0f;
                    player.nextCheckTime = 0f;
                    player.ClientRPC(RpcTarget.Player("craftMode", player), 0);
                }
            }

            foreach (var car in BaseNetworkable.serverEntities.OfType<ModularCar>())
            {
                if (car != null && !car.IsDestroyed)
                {
                    EnsureHiddenWorkbenchTrigger(car, false);
                }
            }

            OnServerSave();
            SaveFlatbedData();

            _activeUiCar.Clear();
            _pendingBagRespawnCar.Clear();
            _t3WorkbenchActiveCars.Clear();
            _playersWithTruckT3.Clear();
            _turretAiTimer?.Destroy();
            _generatorRefreshTimer?.Destroy();

        }

        private void EnsureHiddenWorkbenchTrigger(ModularCar car, bool enable)
        {
            if (car == null || car.IsDestroyed) return;

            var existing = car.children.OfType<Workbench>().FirstOrDefault(w => w != null && w.Workbenchlevel == 3);
            if (!enable)
            {
                if (existing != null && !existing.IsDestroyed)
                {
                    existing.Kill();
                }
                return;
            }

            if (existing != null && !existing.IsDestroyed) return;

            var wb = GameManager.server.CreateEntity(Workbench3Prefab, car.transform.position, car.transform.rotation) as Workbench;
            if (wb != null)
            {
                StripPhysicsAndStability(wb);
                wb.enableSaving = false;
                wb.Spawn();
                wb.SetParent(car, worldPositionStays: false);
                wb.transform.localPosition = new Vector3(0f, -2.5f, 0f);
                wb.transform.localRotation = Quaternion.identity;
                wb.SendNetworkUpdateImmediate();
            }
        }

        private void UpdateVirtualWorkbenchAura()
        {
            if (_t3WorkbenchActiveCars.Count == 0 && _playersWithTruckT3.Count == 0) return;

            var activeCars = new List<ModularCar>();
            var invalidIds = new List<uint>();

            foreach (uint netId in _t3WorkbenchActiveCars)
            {
                var car = BaseNetworkable.serverEntities.Find(new NetworkableId(netId)) as ModularCar;
                if (car == null || car.IsDestroyed)
                {
                    invalidIds.Add(netId);
                }
                else
                {
                    activeCars.Add(car);
                }
            }

            foreach (uint badId in invalidIds)
            {
                _t3WorkbenchActiveCars.Remove(badId);
            }

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected || player.IsDead()) continue;

                bool nearActiveTruck = false;
                if (activeCars.Count > 0)
                {
                    if (player.isMounted)
                    {
                        var mountedCar = player.GetMounted()?.GetComponentInParent<ModularCar>();
                        if (mountedCar != null && _t3WorkbenchActiveCars.Contains((uint)mountedCar.net.ID.Value))
                        {
                            nearActiveTruck = true;
                        }
                    }

                    if (!nearActiveTruck)
                    {
                        Vector3 pPos = player.transform.position;
                        for (int i = 0; i < activeCars.Count; i++)
                        {
                            if (Vector3.Distance(pPos, activeCars[i].transform.position) <= 6.5f)
                            {
                                nearActiveTruck = true;
                                break;
                            }
                        }
                    }
                }

                if (nearActiveTruck)
                {
                    _playersWithTruckT3.Add(player.userID);
                    player.cachedCraftLevel = 3f;
                    player.nextCheckTime = UnityEngine.Time.realtimeSinceStartup + 2.5f;
                    player.ClientRPC(RpcTarget.Player("craftMode", player), 1);
                }
                else if (_playersWithTruckT3.Contains(player.userID))
                {
                    _playersWithTruckT3.Remove(player.userID);
                    player.cachedCraftLevel = 0f;
                    player.nextCheckTime = 0f;
                    player.ClientRPC(RpcTarget.Player("craftMode", player), 0);
                }
            }
        }

        private object CanCraft(ItemCrafter itemCrafter, ItemBlueprint bp, int amount)
        {
            var player = itemCrafter?.GetComponent<BasePlayer>();
            if (player != null && _playersWithTruckT3.Contains(player.userID))
            {
                player.cachedCraftLevel = 3f;
                player.nextCheckTime = UnityEngine.Time.realtimeSinceStartup + 2.5f;
            }
            return null;
        }
        
        private void UpdateTruckTurretNpcTargets()
        {
            foreach (var car in BaseNetworkable.serverEntities.OfType<ModularCar>())
            {
                if (car == null || car.IsDestroyed || !IsArmedFlatbed(car)) continue;

                var turret = car.GetComponentInChildren<AutoTurret>();
                if (turret == null || turret.IsDestroyed || !turret.IsOnline() || turret.GetTotalAmmo() == 0) continue;

                if (turret.target != null && !turret.target.IsDestroyed && turret.target.IsAlive())
                {
                    if (!IsExcludedTruckTarget(turret.target) &&
                        Vector3.Distance(turret.transform.position, turret.target.transform.position) <= turret.sightRange &&
                        turret.ObjectVisible(turret.target))
                    {
                        continue;
                    }
                    turret.SetTarget(null);
                }

                BaseCombatEntity bestTarget = null;
                float bestDist = turret.sightRange;

                var nearby = new List<BaseCombatEntity>();
                Vis.Entities(turret.transform.position, turret.sightRange, nearby, LayerMask.GetMask("Player (Server)", "AI"));

                foreach (var entity in nearby)
                {
                    if (entity == null || entity.IsDestroyed || !entity.IsAlive()) continue;
                    if (IsExcludedTruckTarget(entity)) continue;

                    bool isNpc = entity.IsNpc || entity is NPCPlayer || entity is BaseNpc;
                    bool isAnimal = entity is BaseAnimalNPC || entity is SimpleShark;

                    if (!isNpc && !isAnimal) continue;
                    if (!turret.ObjectVisible(entity)) continue;

                    float dist = Vector3.Distance(turret.transform.position, entity.transform.position);
                    if (bestDist > dist)
                    {
                        bestDist = dist;
                        bestTarget = entity;
                    }
                }

                if (bestTarget != null)
                {
                    turret.SetTarget(bestTarget);
                }
            }
        }

        private bool IsExcludedTruckTarget(BaseCombatEntity entity)
        {
            if (entity == null) return true;

            // Block real players (non-NPC BasePlayer)
            if (entity is BasePlayer player && !player.IsNpc && !(entity is NPCPlayer))
                return true;

            // Block Horses and Cows
            if (entity is RidableHorse ||
                entity.ShortPrefabName.Contains("horse") ||
                entity.ShortPrefabName.Contains("cow") ||
                entity.ShortPrefabName.Contains("sheep") ||
                entity.ShortPrefabName.Contains("calf"))
                return true;

            return false;
        }

        private object CanBeTargeted(BaseCombatEntity target, MonoBehaviour turret)
        {
            if (target == null || !(turret is AutoTurret autoTurret)) return null;

            if (autoTurret.GetComponentInParent<ModularCar>() != null)
            {
                if (IsExcludedTruckTarget(target))
                {
                    return false;
                }

                if (target.IsNpc || target is NPCPlayer || target is BaseNpc || target is BaseAnimalNPC)
                {
                    return true;
                }
            }

            return null;
        }


        
        private string GetDeployablePrefab(string itemShortname)
        {
            var itemDef = ItemManager.FindItemDefinition(itemShortname);
            var deployable = itemDef?.GetComponent<ItemModDeployable>();
            return deployable?.entityPrefab?.resourcePath;
        }

        private string FindPrefabFromManifest(string[] itemShortnames, string[] prefabKeywords, string[] excludeKeywords = null)
        {
            if (itemShortnames != null)
            {
                foreach (var shortname in itemShortnames)
                {
                    string path = GetDeployablePrefab(shortname);
                    if (!string.IsNullOrEmpty(path)) return path;
                }
            }

            if (prefabKeywords != null)
            {
                foreach (var str in GameManifest.Current.pooledStrings)
                {
                    if (!str.str.EndsWith(".prefab") || str.str.Contains("/effects/") || str.str.Contains("viewmodel"))
                        continue;

                    if (excludeKeywords != null && excludeKeywords.Any(ex => str.str.Contains(ex)))
                        continue;

                    foreach (var kw in prefabKeywords)
                    {
                        if (str.str.Contains(kw))
                        {
                            return str.str;
                        }
                    }
                }
            }

            return null;
        }

        private string FindCarFuelPrefab()
        {
            foreach (var str in GameManifest.Current.pooledStrings)
            {
                if (str.str.Contains("fuel_storage") && str.str.EndsWith(".prefab"))
                {
                    return str.str;
                }
            }
            return "assets/content/vehicles/modularcar/subents/modular_car_fuel_storage.prefab";
        }

        private StorageContainer GetOrRestoreFuelContainer(ModularCar car)
        {
            if (car == null) return null;

            var fuelSys = car.GetFuelSystem() as EntityFuelSystem;
            if (fuelSys != null)
            {
                var existing = fuelSys.fuelStorageInstance.Get(true) as StorageContainer;
                if (existing != null && !existing.IsDestroyed)
                {
                    return existing;
                }
            }

            var childFuel = car.children.OfType<StorageContainer>()
                .FirstOrDefault(s => s != null && s.ShortPrefabName.Contains("fuel"));
            if (childFuel != null)
            {
                if (fuelSys != null) fuelSys.fuelStorageInstance.Set(childFuel);
                return childFuel;
            }

            string fuelPrefab = FindCarFuelPrefab();
            var newFuel = GameManager.server.CreateEntity(fuelPrefab, car.transform.position, car.transform.rotation) as StorageContainer;
            if (newFuel != null)
            {
                newFuel.Spawn();
                newFuel.SetParent(car, worldPositionStays: false);
                newFuel.transform.localPosition = new Vector3(0f, 0.55f, -2.05f);
                newFuel.transform.localRotation = Quaternion.identity;
                newFuel.SendNetworkUpdateImmediate();
                if (fuelSys != null) fuelSys.fuelStorageInstance.Set(newFuel);
            }

            return newFuel;
        }

        private object CanPickupEntity(BasePlayer player, BaseEntity entity)
        {
            if (entity == null) return null;
            if (entity.GetComponentInParent<ModularCar>() != null)
            {
                if (entity is SleepingBag ||
                    entity is Recycler ||
                    entity is BaseOven ||
                    entity is WaterPurifier ||
                    entity is Workbench ||
                    entity.ShortPrefabName.Contains("fridge") ||
                    entity.ShortPrefabName.Contains("beachtowel") ||
                    entity.ShortPrefabName.Contains("purifier") ||
                    entity.ShortPrefabName.Contains("siren") ||
                    entity.ShortPrefabName.Contains("flasher") ||
                    entity.ShortPrefabName.Contains("simplelight") ||
                    entity.ShortPrefabName.Contains("coffin"))
                {
                    player?.ChatMessage("Use /removebed to clear the custom truck bed.");
                    return false;
                }
            }
            return null;
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return null;
            if (info.damageTypes.Has(Rust.DamageType.Decay))
            {
                var car = entity as ModularCar ?? entity.GetComponentInParent<ModularCar>();
                if (car != null && IsArmedFlatbed(car))
                {
                    return true;
                }
            }
            return null;
        }

        private bool IsArmedFlatbed(ModularCar car)
        {
            if (car == null) return false;
            return car.skinID == ArmedCarSkinTag ||
                   car.GetComponentInChildren<AutoTurret>() != null ||
                   car.children.Any(c => c != null && !(c is BaseVehicleModule) && !c.ShortPrefabName.Contains("fuel"));
        }

        private Vector3 CalculateTruckRespawnPos(ModularCar car)
        {
            Vector3 desiredPos = car.transform.TransformPoint(RespawnOffsetPos);

            RaycastHit hit;
            if (Physics.Raycast(desiredPos + Vector3.up * 1.5f, Vector3.down, out hit, 6f, LayerMask.GetMask("Terrain", "World", "Construction", "Default")))
            {
                desiredPos = hit.point + Vector3.up * 0.1f;
            }

            return desiredPos;
        }

        private object OnPlayerRespawn(BasePlayer player, SleepingBag bag)
        {
            if (player == null || bag == null) return null;

            var car = bag.GetComponentInParent<ModularCar>();
            if (car == null) return null;

            _pendingBagRespawnCar[player.userID] = (uint)car.net.ID.Value;

            return new BasePlayer.SpawnPoint
            {
                pos = CalculateTruckRespawnPos(car),
                rot = car.transform.rotation
            };
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null) return;

            uint netId;
            if (_pendingBagRespawnCar.TryGetValue(player.userID, out netId))
            {
                _pendingBagRespawnCar.Remove(player.userID);
                var car = BaseNetworkable.serverEntities.Find(new NetworkableId(netId)) as ModularCar;
                if (car != null && !car.IsDestroyed)
                {
                    Vector3 targetPos = CalculateTruckRespawnPos(car);
                    NextTick(() =>
                    {
                        if (player != null && player.IsConnected && car != null && !car.IsDestroyed)
                        {
                            player.Teleport(targetPos);
                        }
                    });
                }
            }
        }
        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (container?.entityOwner == null || item == null) return;
            var ownerEnt = container.entityOwner;

            if (ownerEnt.GetComponentInParent<ModularCar>() != null && ownerEnt.ShortPrefabName.Contains("fridge"))
            {
                if (ownerEnt.HasFlag(BaseEntity.Flags.On) && item.hasCondition)
                {
                    item.condition = item.maxCondition;
                }
            }
        }

        private void OnItemUse(Item item, int amountToUse)
        {
            if (item == null || item.parent?.entityOwner == null) return;
            var ownerEnt = item.parent.entityOwner;

            if (ownerEnt.GetComponentInParent<ModularCar>() != null &&
                (ownerEnt is WaterPurifier || ownerEnt.ShortPrefabName.Contains("purifier") || ownerEnt.GetComponentInParent<WaterPurifier>() != null) &&
                item.info.shortname == "water")
            {
                item.amount = 2000;
            }
        }

        private void FillPurifierWater(BaseEntity purifier)
        {
            if (purifier == null || purifier.IsDestroyed) return;

            var mainStorage = purifier as StorageContainer;
            if (mainStorage != null && mainStorage.inventory != null)
            {
                var waterItem = mainStorage.inventory.FindItemByItemName("water");
                if (waterItem != null)
                {
                    waterItem.amount = 2000;
                }
                else
                {
                    var freshWater = ItemManager.CreateByName("water", 2000);
                    if (freshWater != null) freshWater.MoveToContainer(mainStorage.inventory);
                }
            }

            foreach (var liq in purifier.GetComponentsInChildren<LiquidContainer>(true))
            {
                if (liq == null || liq.inventory == null) continue;
                var existingWater = liq.inventory.FindItemByItemName("water");
                if (existingWater != null)
                {
                    existingWater.amount = 2000;
                }
                else
                {
                    var water = ItemManager.CreateByName("water", 2000);
                    if (water != null) water.MoveToContainer(liq.inventory);
                }
                liq.SendNetworkUpdateImmediate();
            }

            purifier.SendNetworkUpdateImmediate();
        }

        private void PowerUpIoDevice(IOEntity ioEnt, bool turnOn)
        {
            if (ioEnt == null || ioEnt.IsDestroyed) return;
            var gen = ioEnt.GetComponentInChildren<ElectricGenerator>();
            if (turnOn)
            {
                ioEnt.flags |= (BaseEntity.Flags.On | BaseEntity.Flags.Reserved8);
                if (gen != null)
                {
                    gen.electricAmount = 100f;
                    gen.ForcePuzzleReset();
                    gen.MarkDirtyForceUpdateOutputs();
                }
            }
            else
            {
                ioEnt.flags &= ~(BaseEntity.Flags.On | BaseEntity.Flags.Reserved8);
                if (gen != null)
                {
                    gen.electricAmount = 0f;
                    gen.MarkDirtyForceUpdateOutputs();
                }
            }
            ioEnt.SendNetworkUpdateImmediate();
        }

        [ChatCommand("removebed")]
        private void CmdRemoveBed(BasePlayer player, string command, string[] args)
        {
            var car = FindTargetCar(player);
            if (car == null) return;

            uint carId = (uint)car.net.ID.Value;
            car.skinID = 0;
            _t3WorkbenchActiveCars.Remove(carId);
            _savedTurrets.Remove(carId);
            RemoveFlatbedState(car);
            EnsureHiddenWorkbenchTrigger(car, false);

            var fuelSys = car.GetFuelSystem() as EntityFuelSystem;
            var fuelEnt = fuelSys?.fuelStorageInstance.Get(true);

            int removed = 0;
            foreach (var child in car.children.ToList())
            {
                if (child == null) continue;
                if (child is BaseVehicleModule) continue;
                if (child == fuelEnt || child.ShortPrefabName.Contains("fuel")) continue;

                if (child is AutoTurret ||
                    child is StorageContainer ||
                    child is IOEntity ||
                    child is Signage ||
                    child is SleepingBag ||
                    child is Recycler ||
                    child is BaseOven ||
                    child is WaterPurifier ||
                    child is Workbench ||
                    child.ShortPrefabName.Contains("sign") ||
                    child.ShortPrefabName.Contains("fridge") ||
                    child.ShortPrefabName.Contains("beachtowel") ||
                    child.ShortPrefabName.Contains("purifier") ||
                    child.ShortPrefabName.Contains("recycler") ||
                    child.ShortPrefabName.Contains("campfire") ||
                    child.ShortPrefabName.Contains("siren") ||
                    child.ShortPrefabName.Contains("flasher") ||
                    child.ShortPrefabName.Contains("simplelight") ||
                    child.ShortPrefabName.Contains("coffin"))
                {
                    foreach (var grandChild in child.children.ToList())
                    {
                        grandChild.Kill();
                    }
                    child.Kill();
                    removed++;
                }
            }

            UpdateVirtualWorkbenchAura();
            CuiHelper.DestroyUi(player, UiName);
            player.ChatMessage(removed > 0 ? "Removed custom flatbed build from truck." : "No custom flatbed found on this truck.");
        }

        [ChatCommand("buildbed")]
        private void CmdBuildBed(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermUse) && !player.IsAdmin)
            {
                player.ChatMessage("You do not have permission to use this command.");
                return;
            }

            var car = FindTargetCar(player);
            if (car == null) return;

            if (car.TotalSockets < 3)
            {
                player.ChatMessage("This requires at least a 3-socket modular car chassis.");
                return;
            }

            bool rearOccupied = car.AttachedModuleEntities.Any(m =>
            {
                if (m == null) return false;
                int sockets = m.ShortPrefabName.Contains("2mod") ? 2 : 1;
                return m.FirstSocketIndex <= 2 && (m.FirstSocketIndex + sockets - 1) >= 2;
            });

            if (rearOccupied)
            {
                player.ChatMessage("Remove any module from the rear socket before building the custom flatbed!");
                return;
            }

            if (car.GetComponentInChildren<AutoTurret>() != null)
            {
                player.ChatMessage("This truck already has a custom armed flatbed installed.");
                return;
            }

            GetOrRestoreFuelContainer(car);
            Build3SlotFlatbed(car, player);

            uint carId = (uint)car.net.ID.Value;
            _t3WorkbenchActiveCars.Add(carId);
            EnsureHiddenWorkbenchTrigger(car, true);
            CaptureFlatbedState(car);
            UpdateVirtualWorkbenchAura();

            OnServerSave();
            timer.Once(1.0f, () => SaveRestore.Save(true));

            player.ChatMessage("Custom 3-slot armed flatbed built! Use /truckui anytime to control fuel, storage, T3 crafting, lights, siren, fridge, and turret.");
        }

        [ChatCommand("truckui")]
        private void CmdTruckUi(BasePlayer player, string command, string[] args)
        {
            if (_activeUiCar.ContainsKey(player.userID))
            {
                CuiHelper.DestroyUi(player, UiName);
                _activeUiCar.Remove(player.userID);
                return;
            }

            var car = GetPlayerCar(player);
            if (car == null)
            {
                player.ChatMessage("Sit inside or look directly at your Modular Car within 8 meters to open /truckui.");
                return;
            }

            _activeUiCar[player.userID] = (uint)car.net.ID.Value;
            ShowTruckUi(player, car);
        }
        [ConsoleCommand("armedflatbed.ui")]
        private void ConsoleTruckUiAction(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            string action = arg.GetString(0, "");
            if (action == "close")
            {
                CuiHelper.DestroyUi(player, UiName);
                _activeUiCar.Remove(player.userID);
                return;
            }

            ModularCar car = null;
            uint netId;
            if (_activeUiCar.TryGetValue(player.userID, out netId))
            {
                car = BaseNetworkable.serverEntities.Find(new NetworkableId(netId)) as ModularCar;
            }
            if (car == null) car = GetPlayerCar(player);

            if (car == null || car.IsDestroyed)
            {
                CuiHelper.DestroyUi(player, UiName);
                _activeUiCar.Remove(player.userID);
                player.ChatMessage("Truck no longer in range.");
                return;
            }

            switch (action)
            {
                case "fuel":
                    CuiHelper.DestroyUi(player, UiName);
                    _activeUiCar.Remove(player.userID);
                    var fuelContainer = GetOrRestoreFuelContainer(car);
                    if (fuelContainer != null)
                        fuelContainer.PlayerOpenLoot(player, "", false);
                    else
                        player.ChatMessage("No fuel container found on this vehicle.");
                    return;

                case "storage":
                    CuiHelper.DestroyUi(player, UiName);
                    _activeUiCar.Remove(player.userID);
                    var woodBox = car.children.OfType<StorageContainer>()
                        .FirstOrDefault(s => s != null && s.ShortPrefabName.Contains("box.wooden.large"));
                    if (woodBox != null)
                        woodBox.PlayerOpenLoot(player, "", false);
                    else
                        player.ChatMessage("No Large Wood Box found on this truck bed.");
                    return;

                case "workbench":
                    uint wbCarId = (uint)car.net.ID.Value;
                    if (_t3WorkbenchActiveCars.Contains(wbCarId))
                    {
                        _t3WorkbenchActiveCars.Remove(wbCarId);
                        EnsureHiddenWorkbenchTrigger(car, false);
                    }
                    else
                    {
                        _t3WorkbenchActiveCars.Add(wbCarId);
                        EnsureHiddenWorkbenchTrigger(car, true);
                    }
                    CaptureFlatbedState(car);
                    UpdateVirtualWorkbenchAura();
                    break;

                case "lights":
                    bool lightsOn = car.HasFlag(BaseEntity.Flags.Reserved5);
                    if (lightsOn) car.flags &= ~BaseEntity.Flags.Reserved5;
                    else car.flags |= BaseEntity.Flags.Reserved5;
                    car.SendNetworkUpdateImmediate();
                    CaptureFlatbedState(car);
                    break;

                case "simplelights":
                    var sLights = car.children.OfType<IOEntity>()
                        .Where(c => c != null && c.ShortPrefabName.Contains("simplelight"))
                        .ToList();
                    if (sLights.Count > 0)
                    {
                        bool targetState = !sLights.Any(l => l.HasFlag(BaseEntity.Flags.On));
                        foreach (var sl in sLights) PowerUpIoDevice(sl, targetState);
                    }
                    CaptureFlatbedState(car);
                    break;

                case "beacon":
                    var beacon = car.children.OfType<IOEntity>()
                        .FirstOrDefault(c => c != null && (c.ShortPrefabName.Contains("siren") || c.ShortPrefabName.Contains("flasher")));
                    if (beacon != null) PowerUpIoDevice(beacon, !beacon.HasFlag(BaseEntity.Flags.On));
                    CaptureFlatbedState(car);
                    break;

                case "fridge":
                    var fridge = car.children.FirstOrDefault(c => c != null && c.ShortPrefabName.Contains("fridge"));
                    if (fridge != null)
                    {
                        bool fridgeOn = fridge.HasFlag(BaseEntity.Flags.On);
                        var fridgeGen = fridge.GetComponentInChildren<ElectricGenerator>();
                        if (fridgeOn)
                        {
                            fridge.flags &= ~(BaseEntity.Flags.On | BaseEntity.Flags.Reserved8);
                            if (fridgeGen != null) fridgeGen.electricAmount = 0f;
                        }
                        else
                        {
                            fridge.flags |= (BaseEntity.Flags.On | BaseEntity.Flags.Reserved8);
                            if (fridgeGen != null)
                            {
                                fridgeGen.electricAmount = 100f;
                                fridgeGen.ForcePuzzleReset();
                                fridgeGen.MarkDirtyForceUpdateOutputs();
                            }
                        }
                        fridge.SendNetworkUpdateImmediate();
                    }
                    CaptureFlatbedState(car);
                    break;

                case "turret":
                    var electricSwitch = car.children.OfType<ElectricSwitch>().FirstOrDefault();
                    var turret = car.children.OfType<AutoTurret>().FirstOrDefault();
                    if (electricSwitch != null)
                    {
                        electricSwitch.SetSwitch(!electricSwitch.IsOn());
                        electricSwitch.MarkDirtyForceUpdateOutputs();
                        electricSwitch.SendNetworkUpdateImmediate();
                    }
                    else if (turret != null)
                    {
                        if (turret.IsOnline()) turret.SetIsOnline(false);
                        else turret.InitiateStartup();
                        turret.SendNetworkUpdateImmediate();
                    }
                    CaptureFlatbedState(car);
                    break;
            }

            ShowTruckUi(player, car);
        }
        private void ShowTruckUi(BasePlayer player, ModularCar car)
        {
            CuiHelper.DestroyUi(player, UiName);

            bool wbOn = _t3WorkbenchActiveCars.Contains((uint)car.net.ID.Value);
            bool lightsOn = car.HasFlag(BaseEntity.Flags.Reserved5);

            var sLights = car.children.OfType<IOEntity>().Where(c => c != null && c.ShortPrefabName.Contains("simplelight")).ToList();
            bool hasSimpleLights = sLights.Count > 0;
            bool simpleLightsOn = hasSimpleLights && sLights.Any(l => l.HasFlag(BaseEntity.Flags.On));

            var beacon = car.children.OfType<IOEntity>().FirstOrDefault(c => c != null && (c.ShortPrefabName.Contains("siren") || c.ShortPrefabName.Contains("flasher")));
            bool hasBeacon = beacon != null;
            bool beaconOn = hasBeacon && beacon.HasFlag(BaseEntity.Flags.On);

            var fridge = car.children.FirstOrDefault(c => c != null && c.ShortPrefabName.Contains("fridge"));
            bool hasFridge = fridge != null;
            bool fridgeOn = hasFridge && fridge.HasFlag(BaseEntity.Flags.On);

            var electricSwitch = car.children.OfType<ElectricSwitch>().FirstOrDefault();
            var turret = car.children.OfType<AutoTurret>().FirstOrDefault();
            bool hasTurret = turret != null;
            bool turretOn = electricSwitch != null ? electricSwitch.IsOn() : (turret != null && turret.IsOnline());

            var elements = new CuiElementContainer();

            elements.Add(new CuiPanel
            {
                Image = { Color = "0.10 0.11 0.13 0.94" },
                RectTransform = { AnchorMin = "0.35 0.21", AnchorMax = "0.65 0.80" },
                CursorEnabled = true
            }, "Overlay", UiName);

            elements.Add(new CuiLabel
            {
                Text = { Text = "ARMED TRUCK CONTROL PANEL", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.95 0.95 0.95 1" },
                RectTransform = { AnchorMin = "0.05 0.90", AnchorMax = "0.88 0.98" }
            }, UiName);

            elements.Add(new CuiButton
            {
                Button = { Command = "armedflatbed.ui close", Color = "0.65 0.18 0.18 0.95" },
                RectTransform = { AnchorMin = "0.89 0.91", AnchorMax = "0.97 0.97" },
                Text = { Text = "X", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UiName);

            elements.Add(new CuiButton
            {
                Button = { Command = "armedflatbed.ui fuel", Color = "0.75 0.42 0.12 0.95" },
                RectTransform = { AnchorMin = "0.06 0.79", AnchorMax = "0.48 0.87" },
                Text = { Text = "OPEN FUEL TANK", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UiName);

            elements.Add(new CuiButton
            {
                Button = { Command = "armedflatbed.ui storage", Color = "0.24 0.46 0.68 0.95" },
                RectTransform = { AnchorMin = "0.52 0.79", AnchorMax = "0.94 0.87" },
                Text = { Text = "OPEN WOOD BOX", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UiName);

            elements.Add(new CuiButton
            {
                Button = { Command = "armedflatbed.ui workbench", Color = wbOn ? "0.20 0.62 0.30 0.95" : "0.28 0.30 0.34 0.95" },
                RectTransform = { AnchorMin = "0.06 0.67", AnchorMax = "0.94 0.75" },
                Text = { Text = wbOn ? "T3 WORKBENCH CRAFTING: ON" : "T3 WORKBENCH CRAFTING: OFF", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UiName);

            elements.Add(new CuiButton
            {
                Button = { Command = "armedflatbed.ui lights", Color = lightsOn ? "0.20 0.62 0.30 0.95" : "0.28 0.30 0.34 0.95" },
                RectTransform = { AnchorMin = "0.06 0.55", AnchorMax = "0.94 0.63" },
                Text = { Text = lightsOn ? "HEADLIGHTS: ON" : "HEADLIGHTS: OFF", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UiName);

            elements.Add(new CuiButton
            {
                Button = { Command = "armedflatbed.ui simplelights", Color = !hasSimpleLights ? "0.20 0.20 0.22 0.7" : (simpleLightsOn ? "0.20 0.62 0.30 0.95" : "0.60 0.22 0.22 0.95") },
                RectTransform = { AnchorMin = "0.06 0.43", AnchorMax = "0.94 0.51" },
                Text = { Text = !hasSimpleLights ? "BED LIGHTS: NOT INSTALLED" : (simpleLightsOn ? "BED LIGHTS (4x): ON" : "BED LIGHTS (4x): OFF"), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UiName);

            elements.Add(new CuiButton
            {
                Button = { Command = "armedflatbed.ui beacon", Color = !hasBeacon ? "0.20 0.20 0.22 0.7" : (beaconOn ? "0.20 0.62 0.30 0.95" : "0.60 0.22 0.22 0.95") },
                RectTransform = { AnchorMin = "0.06 0.31", AnchorMax = "0.94 0.39" },
                Text = { Text = !hasBeacon ? "SIREN LIGHT: NOT INSTALLED" : (beaconOn ? "SIREN LIGHT: ON" : "SIREN LIGHT: OFF"), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UiName);

            elements.Add(new CuiButton
            {
                Button = { Command = "armedflatbed.ui fridge", Color = !hasFridge ? "0.20 0.20 0.22 0.7" : (fridgeOn ? "0.20 0.62 0.30 0.95" : "0.60 0.22 0.22 0.95") },
                RectTransform = { AnchorMin = "0.06 0.19", AnchorMax = "0.94 0.27" },
                Text = { Text = !hasFridge ? "MINI FRIDGE: NOT INSTALLED" : (fridgeOn ? "MINI FRIDGE POWER: ON" : "MINI FRIDGE POWER: OFF"), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UiName);

            elements.Add(new CuiButton
            {
                Button = { Command = "armedflatbed.ui turret", Color = !hasTurret ? "0.20 0.20 0.22 0.7" : (turretOn ? "0.20 0.62 0.30 0.95" : "0.60 0.22 0.22 0.95") },
                RectTransform = { AnchorMin = "0.06 0.07", AnchorMax = "0.94 0.15" },
                Text = { Text = !hasTurret ? "AUTO TURRET: NOT INSTALLED" : (turretOn ? "AUTO TURRET POWER: ON" : "AUTO TURRET POWER: OFF"), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, UiName);

            CuiHelper.AddUi(player, elements);
        }

        private ModularCar GetPlayerCar(BasePlayer player)
        {
            if (player == null) return null;
            if (player.isMounted)
            {
                var seatedCar = player.GetMounted()?.GetComponentInParent<ModularCar>();
                if (seatedCar != null) return seatedCar;
            }
            return FindTargetCar(player, true);
        }

        private ModularCar FindTargetCar(BasePlayer player, bool silent = false)
        {
            var hits = Physics.RaycastAll(player.eyes.HeadRay(), 8f);
            foreach (var h in hits.OrderBy(x => x.distance))
            {
                var ent = h.GetEntity();
                if (ent != null && ent != player)
                {
                    var car = ent as ModularCar ?? ent.GetComponentInParent<ModularCar>();
                    if (car != null) return car;
                }
            }
            if (!silent) player.ChatMessage("Look directly at a 3-socket Modular Car within 8 meters.");
            return null;
        }
        private void Build3SlotFlatbed(ModularCar car, BasePlayer owner)
        {
            ulong ownerId = owner != null ? owner.userID : car.OwnerID;
            car.skinID = ArmedCarSkinTag;
            car.OwnerID = ownerId;

            string floorSignPrefab = GetDeployablePrefab("sign.wooden.medium");
            string railSignPrefab = GetDeployablePrefab("sign.wooden.small");

            if (!string.IsNullOrEmpty(floorSignPrefab))
            {
                SpawnProp(car, floorSignPrefab, new Vector3(0.22f, 0.75f, -1.15f), Quaternion.Euler(-90f, 90f, 0f));
                SpawnProp(car, floorSignPrefab, new Vector3(0.80f, 0.75f, -1.15f), Quaternion.Euler(-90f, 90f, 0f));
            }

            if (!string.IsNullOrEmpty(railSignPrefab))
            {
                SpawnProp(car, railSignPrefab, new Vector3(-0.75f, 0.72f, -1.75f), Quaternion.Euler(0f, -90f, 0f));
                SpawnProp(car, railSignPrefab, new Vector3(-0.75f, 0.72f, -0.75f), Quaternion.Euler(0f, -90f, 0f));
                SpawnProp(car, railSignPrefab, new Vector3(0.75f, 0.72f, -0.75f), Quaternion.Euler(0f, 90f, 0f));
                SpawnProp(car, railSignPrefab, new Vector3(0.75f, 0.72f, -1.75f), Quaternion.Euler(0f, 90f, 0f));
            }

            var box = GameManager.server.CreateEntity(BoxPrefab, car.transform.position, car.transform.rotation) as StorageContainer;
            if (box != null)
            {
                StripPhysicsAndStability(box);
                box.OwnerID = ownerId;
                box.Spawn();
                box.SetParent(car, worldPositionStays: false);
                box.transform.localPosition = BoxLocalPos;
                box.transform.localRotation = Quaternion.Euler(BoxLocalRot);
                box.SendNetworkUpdateImmediate();
            }

            string bagPrefab = GetDeployablePrefab("beachtowel") ?? GetDeployablePrefab("beachtowel.summer") ?? GetDeployablePrefab("sleepingbag");
            if (!string.IsNullOrEmpty(bagPrefab))
            {
                var bag = GameManager.server.CreateEntity(bagPrefab, car.transform.position, car.transform.rotation) as SleepingBag;
                if (bag != null)
                {
                    StripPhysicsAndStability(bag);
                    bag.OwnerID = ownerId;
                    bag.deployerUserID = ownerId;
                    bag.niceName = "Armed Truck";
                    bag.Spawn();

                    bag.SetParent(car, worldPositionStays: false);
                    bag.transform.localPosition = SleepingBagLocalPos;
                    bag.transform.localRotation = Quaternion.Euler(SleepingBagLocalRot);
                    bag.SendNetworkUpdateImmediate();
                }
            }

            string miniFridgePrefab = GetDeployablePrefab("minifridge");
            if (!string.IsNullOrEmpty(miniFridgePrefab))
            {
                var fridge = GameManager.server.CreateEntity(miniFridgePrefab, car.transform.position, car.transform.rotation) as BaseCombatEntity;
                if (fridge != null)
                {
                    StripPhysicsAndStability(fridge);
                    fridge.OwnerID = ownerId;
                    fridge.Spawn();

                    fridge.SetParent(car, worldPositionStays: false);
                    fridge.transform.localPosition = MiniFridgeLocalPos;
                    fridge.transform.localRotation = Quaternion.Euler(MiniFridgeLocalRot);

                    var fridgeIo = fridge as IOEntity;
                    if (fridgeIo != null && fridgeIo.inputs != null && fridgeIo.inputs.Length > 0)
                    {
                        timer.Once(0.2f, () =>
                        {
                            if (fridgeIo == null || fridgeIo.IsDestroyed) return;
                            var fridgeGen = GameManager.server.CreateEntity(GeneratorPrefab, fridgeIo.transform.position, fridgeIo.transform.rotation) as ElectricGenerator;
                            if (fridgeGen != null)
                            {
                                StripPhysicsAndStability(fridgeGen);
                                fridgeGen.electricAmount = 100f;
                                fridgeGen.Spawn();
                                fridgeGen.SetParent(fridgeIo, worldPositionStays: false);
                                fridgeGen.transform.localPosition = new Vector3(0f, -0.1f, 0f);

                                fridgeGen.outputs.First().connectedTo.Set(fridgeIo);
                                fridgeGen.outputs.First().connectedToSlot = 0;
                                fridgeIo.inputs.First().connectedTo.Set(fridgeGen);
                                fridgeIo.inputs.First().connectedToSlot = 0;

                                fridgeGen.ForcePuzzleReset();
                                fridgeGen.MarkDirtyForceUpdateOutputs();
                            }
                            fridgeIo.flags |= (BaseEntity.Flags.On | BaseEntity.Flags.Reserved8);
                            fridgeIo.SendNetworkUpdateImmediate();
                        });
                    }
                    else
                    {
                        fridge.flags |= (BaseEntity.Flags.On | BaseEntity.Flags.Reserved8);
                    }

                    fridge.SendNetworkUpdateImmediate();
                }
            }

            string campfirePrefab = FindPrefabFromManifest(new[] { "campfire" }, new[] { "campfire.deployed" });
            if (!string.IsNullOrEmpty(campfirePrefab))
            {
                var fire = GameManager.server.CreateEntity(campfirePrefab, car.transform.position, car.transform.rotation) as BaseOven;
                if (fire != null)
                {
                    StripPhysicsAndStability(fire);
                    fire.OwnerID = ownerId;
                    fire.Spawn();
                    fire.SetParent(car, worldPositionStays: false);
                    fire.transform.localPosition = CampfireLocalPos;
                    fire.transform.localRotation = Quaternion.Euler(CampfireLocalRot);

                    var wood = ItemManager.CreateByName("wood", 500);
                    if (wood != null && fire.inventory != null)
                    {
                        wood.MoveToContainer(fire.inventory);
                    }
                    fire.SendNetworkUpdateImmediate();
                }
            }

            string purifierPrefab = GetDeployablePrefab("water.purifier") ?? "assets/prefabs/deployable/water purifier/waterpurifier.deployed.prefab";
            if (!string.IsNullOrEmpty(purifierPrefab))
            {
                var purifier = GameManager.server.CreateEntity(purifierPrefab, car.transform.position, car.transform.rotation) as BaseCombatEntity;
                if (purifier != null)
                {
                    StripPhysicsAndStability(purifier);
                    purifier.OwnerID = ownerId;
                    purifier.Spawn();
                    purifier.SetParent(car, worldPositionStays: false);
                    purifier.transform.localPosition = WaterPurifierLocalPos;
                    purifier.transform.localRotation = Quaternion.Euler(WaterPurifierLocalRot);

                    timer.Once(0.2f, () =>
                    {
                        if (purifier == null || purifier.IsDestroyed) return;
                        FillPurifierWater(purifier);
                    });

                    purifier.SendNetworkUpdateImmediate();
                }
            }
            var recycler = GameManager.server.CreateEntity(RecyclerPrefab, car.transform.position, car.transform.rotation) as Recycler;
            if (recycler != null)
            {
                StripPhysicsAndStability(recycler);
                recycler.enableSaving = false;
                recycler.OwnerID = ownerId;
                recycler.Spawn();
                recycler.SetParent(car, worldPositionStays: false);
                recycler.transform.localPosition = RecyclerLocalPos;
                recycler.transform.localRotation = Quaternion.Euler(RecyclerLocalRot);
                recycler.SendNetworkUpdateImmediate();
            }

            string beaconPrefab = FindPrefabFromManifest(
                new[] { "electric.sirenlight", "sirenlight", "electric.flasherlight", "flasherlight" },
                new[] { "sirenlight", "flasherlight" }
            );
            if (!string.IsNullOrEmpty(beaconPrefab))
            {
                var beacon = GameManager.server.CreateEntity(beaconPrefab, car.transform.position, car.transform.rotation) as IOEntity;
                if (beacon != null)
                {
                    StripPhysicsAndStability(beacon);
                    beacon.OwnerID = ownerId;
                    beacon.Spawn();
                    beacon.SetParent(car, worldPositionStays: false);
                    beacon.transform.localPosition = BeaconLocalPos;
                    beacon.transform.localRotation = Quaternion.Euler(BeaconLocalRot);

                    timer.Once(0.2f, () =>
                    {
                        if (beacon == null || beacon.IsDestroyed) return;
                        var beaconGen = GameManager.server.CreateEntity(GeneratorPrefab, beacon.transform.position, beacon.transform.rotation) as ElectricGenerator;
                        if (beaconGen != null)
                        {
                            StripPhysicsAndStability(beaconGen);
                            beaconGen.electricAmount = 100f;
                            beaconGen.Spawn();
                            beaconGen.SetParent(beacon, worldPositionStays: false);
                            beaconGen.transform.localPosition = new Vector3(0f, -0.1f, 0f);

                            if (beacon.inputs != null && beacon.inputs.Length > 0)
                            {
                                beaconGen.outputs.First().connectedTo.Set(beacon);
                                beaconGen.outputs.First().connectedToSlot = 0;
                                beacon.inputs.First().connectedTo.Set(beaconGen);
                                beacon.inputs.First().connectedToSlot = 0;
                            }

                            beaconGen.ForcePuzzleReset();
                            beaconGen.MarkDirtyForceUpdateOutputs();
                        }
                        beacon.flags |= (BaseEntity.Flags.On | BaseEntity.Flags.Reserved8);
                        beacon.SendNetworkUpdateImmediate();
                    });
                }
            }

            string simpleLightPrefab = FindPrefabFromManifest(
                new[] { "electric.simplelight", "simplelight" },
                new[] { "simplelight" }
            );
            if (!string.IsNullOrEmpty(simpleLightPrefab))
            {
                Vector3[] lightPositions = { SimpleLight1LocalPos, SimpleLight2LocalPos, SimpleLight3LocalPos, SimpleLight4LocalPos };
                Vector3[] lightRotations = { SimpleLight1LocalRot, SimpleLight2LocalRot, SimpleLight3LocalRot, SimpleLight4LocalRot };

                for (int i = 0; i < 4; i++)
                {
                    var sLight = GameManager.server.CreateEntity(simpleLightPrefab, car.transform.position, car.transform.rotation) as IOEntity;
                    if (sLight != null)
                    {
                        StripPhysicsAndStability(sLight);
                        sLight.OwnerID = ownerId;
                        sLight.Spawn();
                        sLight.SetParent(car, worldPositionStays: false);
                        sLight.transform.localPosition = lightPositions[i];
                        sLight.transform.localRotation = Quaternion.Euler(lightRotations[i]);

                        var capturedLight = sLight;
                        timer.Once(0.2f, () =>
                        {
                            if (capturedLight == null || capturedLight.IsDestroyed) return;
                            var lightGen = GameManager.server.CreateEntity(GeneratorPrefab, capturedLight.transform.position, capturedLight.transform.rotation) as ElectricGenerator;
                            if (lightGen != null)
                            {
                                StripPhysicsAndStability(lightGen);
                                lightGen.electricAmount = 100f;
                                lightGen.Spawn();
                                lightGen.SetParent(capturedLight, worldPositionStays: false);
                                lightGen.transform.localPosition = new Vector3(0f, -0.1f, 0f);

                                if (capturedLight.inputs != null && capturedLight.inputs.Length > 0)
                                {
                                    lightGen.outputs.First().connectedTo.Set(capturedLight);
                                    lightGen.outputs.First().connectedToSlot = 0;
                                    capturedLight.inputs.First().connectedTo.Set(lightGen);
                                    capturedLight.inputs.First().connectedToSlot = 0;
                                }

                                lightGen.ForcePuzzleReset();
                                lightGen.MarkDirtyForceUpdateOutputs();
                            }
                            capturedLight.flags |= (BaseEntity.Flags.On | BaseEntity.Flags.Reserved8);
                            capturedLight.SendNetworkUpdateImmediate();
                        });
                    }
                }
            }

            var turret = GameManager.server.CreateEntity(TurretPrefab, car.transform.position, car.transform.rotation) as AutoTurret;
            if (turret != null)
            {
                StripPhysicsAndStability(turret);
                turret.enableSaving = false;
                turret.OwnerID = ownerId;
                turret.Spawn();
                turret.authorizedPlayers.Add(ownerId);

                turret.SetParent(car, worldPositionStays: false);
                turret.transform.localPosition = TurretLocalPos;
                turret.transform.localRotation = Quaternion.Euler(TurretLocalRot);
            }

            var electricSwitch = GameManager.server.CreateEntity(SwitchPrefab, car.transform.position, car.transform.rotation) as ElectricSwitch;
            if (electricSwitch != null)
            {
                StripPhysicsAndStability(electricSwitch);
                electricSwitch.OwnerID = ownerId;
                electricSwitch.Spawn();

                electricSwitch.SetParent(car, worldPositionStays: false);
                electricSwitch.transform.localPosition = SwitchLocalPos;
                electricSwitch.transform.localRotation = Quaternion.Euler(SwitchLocalRot);
                electricSwitch.SendNetworkUpdateImmediate();
            }

            if (turret != null && electricSwitch != null)
            {
                timer.Once(0.2f, () =>
                {
                    if (turret == null || turret.IsDestroyed || electricSwitch == null || electricSwitch.IsDestroyed) return;

                    var gen = GameManager.server.CreateEntity(GeneratorPrefab, turret.transform.position, turret.transform.rotation) as ElectricGenerator;
                    if (gen != null)
                    {
                        StripPhysicsAndStability(gen);
                        gen.electricAmount = 100f;
                        gen.Spawn();
                        gen.SetParent(turret, worldPositionStays: false);
                        gen.transform.localPosition = new Vector3(0f, -0.15f, 0f);

                        gen.outputs.First().connectedTo.Set(electricSwitch);
                        gen.outputs.First().connectedToSlot = 0;
                        electricSwitch.inputs.First().connectedTo.Set(gen);
                        electricSwitch.inputs.First().connectedToSlot = 0;

                        electricSwitch.outputs.First().connectedTo.Set(turret);
                        electricSwitch.outputs.First().connectedToSlot = 0;
                        turret.inputs.First().connectedTo.Set(electricSwitch);
                        turret.inputs.First().connectedToSlot = 0;

                        gen.ForcePuzzleReset();
                        gen.MarkDirtyForceUpdateOutputs();
                        electricSwitch.MarkDirtyForceUpdateOutputs();
                    }

                    turret.SendNetworkUpdateImmediate();
                });
            }
        }
        private void SpawnProp(ModularCar car, string prefab, Vector3 localPos, Quaternion localRot)
        {
            var prop = GameManager.server.CreateEntity(prefab, car.transform.position, car.transform.rotation);
            if (prop == null) return;

            StripPhysicsAndStability(prop);
            prop.Spawn();
            prop.SetParent(car, worldPositionStays: false);
            prop.transform.localPosition = localPos;
            prop.transform.localRotation = localRot;
            prop.SendNetworkUpdateImmediate();
        }

        private void StripPhysicsAndStability(BaseEntity entity)
        {
            UnityEngine.Object.DestroyImmediate(entity.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.DestroyImmediate(entity.GetComponent<GroundWatch>());

            var rb = entity.GetComponent<Rigidbody>();
            if (rb != null)
            {
                UnityEngine.Object.DestroyImmediate(rb);
            }

            foreach (var col in entity.GetComponentsInChildren<Collider>(true))
            {
                if (col != null)
                {
                    col.enabled = false;
                }
            }

            var stability = entity as StabilityEntity;
            if (stability != null)
            {
                stability.grounded = true;
            }
        }

        private void LoadFlatbedData()
        {
            _flatbedSaveData.Clear();

            var data = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, FlatbedSaveData>>(FlatbedDataFile);
            if (data == null) return;

            foreach (var kvp in data)
            {
                ulong carId;
                if (ulong.TryParse(kvp.Key, out carId))
                {
                    _flatbedSaveData[carId] = kvp.Value;
                }
            }
        }

        private void SaveFlatbedData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(
                FlatbedDataFile,
                _flatbedSaveData.ToDictionary(x => x.Key.ToString(), x => x.Value));
        }

        private void CaptureFlatbedState(ModularCar car)
        {
            if (car == null || car.IsDestroyed) return;

            var state = new FlatbedSaveData
            {
                CarId = (ulong)car.net.ID.Value,
                OwnerId = car.OwnerID,
                WorkbenchEnabled = _t3WorkbenchActiveCars.Contains((uint)car.net.ID.Value),
                HeadlightsOn = car.HasFlag(BaseEntity.Flags.Reserved5)
            };

            var sLights = car.children.OfType<IOEntity>()
                .Where(c => c != null && c.ShortPrefabName.Contains("simplelight"))
                .ToList();
            state.SimpleLightsOn = sLights.Count > 0 && sLights.Any(l => l.HasFlag(BaseEntity.Flags.On));

            var beacon = car.children.OfType<IOEntity>()
                .FirstOrDefault(c => c != null && (c.ShortPrefabName.Contains("siren") || c.ShortPrefabName.Contains("flasher")));
            state.BeaconOn = beacon != null && beacon.HasFlag(BaseEntity.Flags.On);

            var fridge = car.children.FirstOrDefault(c => c != null && c.ShortPrefabName.Contains("fridge"));
            state.FridgeOn = fridge != null && fridge.HasFlag(BaseEntity.Flags.On);

            var electricSwitch = car.children.OfType<ElectricSwitch>().FirstOrDefault();
            var turret = car.children.OfType<AutoTurret>().FirstOrDefault();
            state.TurretOn = electricSwitch != null ? electricSwitch.IsOn() : (turret != null && turret.IsOnline());

            _flatbedSaveData[state.CarId] = state;
            SaveFlatbedData();
        }

        private void RemoveFlatbedState(ModularCar car)
        {
            if (car == null) return;
            _flatbedSaveData.Remove((ulong)car.net.ID.Value);
            SaveFlatbedData();
        }

        private void ApplySavedFlatbedState(ModularCar car, FlatbedSaveData state)
        {
            if (car == null || car.IsDestroyed) return;

            if (state.HeadlightsOn)
                car.flags |= BaseEntity.Flags.Reserved5;
            else
                car.flags &= ~BaseEntity.Flags.Reserved5;

            var sLights = car.children.OfType<IOEntity>()
                .Where(c => c != null && c.ShortPrefabName.Contains("simplelight"))
                .ToList();
            if (sLights.Count > 0)
            {
                foreach (var sl in sLights)
                {
                    PowerUpIoDevice(sl, state.SimpleLightsOn);
                }
            }

            var beacon = car.children.OfType<IOEntity>()
                .FirstOrDefault(c => c != null && (c.ShortPrefabName.Contains("siren") || c.ShortPrefabName.Contains("flasher")));
            if (beacon != null)
            {
                PowerUpIoDevice(beacon, state.BeaconOn);
            }

            var fridge = car.children.FirstOrDefault(c => c != null && c.ShortPrefabName.Contains("fridge"));
            if (fridge != null)
            {
                var fridgeGen = fridge.GetComponentInChildren<ElectricGenerator>();
                if (state.FridgeOn)
                {
                    fridge.flags |= (BaseEntity.Flags.On | BaseEntity.Flags.Reserved8);
                    if (fridgeGen != null)
                    {
                        fridgeGen.electricAmount = 100f;
                        fridgeGen.ForcePuzzleReset();
                        fridgeGen.MarkDirtyForceUpdateOutputs();
                    }
                }
                else
                {
                    fridge.flags &= ~(BaseEntity.Flags.On | BaseEntity.Flags.Reserved8);
                    if (fridgeGen != null) fridgeGen.electricAmount = 0f;
                }
                fridge.SendNetworkUpdateImmediate();
            }

            var electricSwitch = car.children.OfType<ElectricSwitch>().FirstOrDefault();
            var turret = car.children.OfType<AutoTurret>().FirstOrDefault();
            if (turret != null)
            {
                if (state.TurretOn)
                {
                    if (!turret.IsOnline())
                        turret.InitiateStartup();
                }
                else
                {
                    if (turret.IsOnline())
                        turret.SetIsOnline(false);
                }
                turret.SendNetworkUpdateImmediate();
            }
            else if (electricSwitch != null)
            {
                electricSwitch.SetSwitch(state.TurretOn);
                electricSwitch.MarkDirtyForceUpdateOutputs();
                electricSwitch.SendNetworkUpdateImmediate();
            }
        }
    }
}
