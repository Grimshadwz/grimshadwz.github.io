using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("HeavyArmedZombies", "1320 Veteran", "1.7.0")]
    [Description("Spawns defending zombies strictly locked inside roadside radtown_1.prefab with automatic JSON configuration in oxide/config/HeavyArmedZombies.json.")]
    public class HeavyArmedZombies : RustPlugin
    {
        // =========================================================================
        //  AUTOMATIC JSON CONFIGURATION (Saved to oxide/config/HeavyArmedZombies.json)
        // =========================================================================

        private PluginConfig _config;

        private class PluginConfig
        {
            [JsonProperty("1. Spawn Counts Per Role")]
            public RoleSpawnCounts SpawnCounts { get; set; } = new RoleSpawnCounts();

            [JsonProperty("2. Zombie Health, Respawn & Radtown Boundary")]
            public GeneralSettings Settings { get; set; } = new GeneralSettings();

            [JsonProperty("3. Weapons Per Role (Shortnames)")]
            public RoleWeapons Weapons { get; set; } = new RoleWeapons();

            [JsonProperty("4. Clothing & Armor (Shortname & Workshop Skin ID)")]
            public List<ClothingEntry> ClothingAndArmor { get; set; } = new List<ClothingEntry>
            {
                new ClothingEntry("metal.facemask", 3803089246),
                new ClothingEntry("hoodie", 3816954119),
                new ClothingEntry("pants", 3816954201),
                new ClothingEntry("metal.plate.torso", 3820010667),
                new ClothingEntry("roadsign.kilt", 3816954286),
                new ClothingEntry("shoes.boots", 0)
            };


            [JsonProperty("5. General Loot Table (Dropped by all zombies)")]
            public List<LootEntry> GeneralLootTable { get; set; } = new List<LootEntry>
            {
                new LootEntry("scrap", 25, 75, 100f),
                new LootEntry("ammo.rifle", 30, 60, 80f),
                new LootEntry("ammo.rifle.hv", 15, 30, 40f),
                new LootEntry("syringe.medical", 2, 4, 75f),
                new LootEntry("techparts", 1, 3, 50f),
                new LootEntry("supply.signal", 1, 1, 10f)
            };

            [JsonProperty("6. Bonus Loot Per Role")]
            public Dictionary<string, List<LootEntry>> RoleBonusLoot { get; set; } = new Dictionary<string, List<LootEntry>>
            {
                ["AK"] = new List<LootEntry>
                {
                    new LootEntry("rifle.ak", 1, 1, 35f),
                    new LootEntry("ammo.rifle.explosive", 15, 30, 30f)
                },
                ["SKS"] = new List<LootEntry>
                {
                    new LootEntry("rifle.sks", 1, 1, 45f),
                    new LootEntry("weapon.mod.holosight", 1, 1, 25f)
                },
                ["BoltSniper"] = new List<LootEntry>
                {
                    new LootEntry("rifle.bolt", 1, 1, 35f),
                    new LootEntry("weapon.mod.8x.scope", 1, 1, 25f)
                },
                ["Rocketeer"] = new List<LootEntry>
                {
                    new LootEntry("rocket.launcher", 1, 1, 25f),
                    new LootEntry("ammo.rocket.basic", 1, 2, 50f)
                },
                ["Grenadier"] = new List<LootEntry>
                {
                    new LootEntry("grenade.f1", 2, 4, 75f),
                    new LootEntry("grenade.beancan", 2, 5, 60f)
                },
                ["C4Breacher"] = new List<LootEntry>
                {
                    new LootEntry("explosive.timed", 1, 1, 30f),
                    new LootEntry("explosives", 5, 15, 65f)
                }
            };
        }

        private class RoleSpawnCounts
        {
            [JsonProperty("AK Zombies")]
            public int AK { get; set; } = 2;

            [JsonProperty("SKS Zombies")]
            public int SKS { get; set; } = 2;

            [JsonProperty("Bolt Sniper Zombies")]
            public int BoltSniper { get; set; } = 2;

            [JsonProperty("Rocketeer Zombies")]
            public int Rocketeer { get; set; } = 2;

            [JsonProperty("Grenadier Zombies")]
            public int Grenadier { get; set; } = 2;

            [JsonProperty("C4 Breacher Zombies")]
            public int C4Breacher { get; set; } = 2;
        }

        private class GeneralSettings
        {
            [JsonProperty("Zombie Max Health")]
            public float ZombieHealth { get; set; } = 300f;

            [JsonProperty("Respawn Delay (Seconds)")]
            public float RespawnDelaySeconds { get; set; } = 600f;

            [JsonProperty("Radtown Perimeter Radius (Meters)")]
            public float RadTownRadius { get; set; } = 85f;
        }

        private class RoleWeapons
        {
            [JsonProperty("AK Role Weapon")]
            public string AK { get; set; } = "rifle.ak";

            [JsonProperty("SKS Role Weapon")]
            public string SKS { get; set; } = "rifle.sks";

            [JsonProperty("Bolt Sniper Role Weapon")]
            public string BoltSniper { get; set; } = "rifle.bolt";

            [JsonProperty("Rocketeer Role Weapon")]
            public string Rocketeer { get; set; } = "rocket.launcher";

            [JsonProperty("Grenadier Role Weapon")]
            public string Grenadier { get; set; } = "grenade.f1";

            [JsonProperty("C4 Breacher Role Weapon")]
            public string C4Breacher { get; set; } = "explosive.timed";
        }

        private class LootEntry
        {
            [JsonProperty("Item Shortname")]
            public string Shortname { get; set; }

            [JsonProperty("Min Amount")]
            public int MinAmount { get; set; }

            [JsonProperty("Max Amount")]
            public int MaxAmount { get; set; }

            [JsonProperty("Drop Chance (0-100)")]
            public float DropChance { get; set; }

            public LootEntry() { }

            public LootEntry(string shortname, int minAmount, int maxAmount, float dropChance)
            {
                Shortname = shortname;
                MinAmount = minAmount;
                MaxAmount = maxAmount;
                DropChance = dropChance;
            }
        }

        private class ClothingEntry
        {
            [JsonProperty("Clothing Shortname")]
            public string Shortname { get; set; }

            [JsonProperty("Workshop Skin ID (0 for none)")]
            public ulong SkinID { get; set; }

            public ClothingEntry() { }

            public ClothingEntry(string shortname, ulong skinId)
            {
                Shortname = shortname;
                SkinID = skinId;
            }
        }


        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new default configuration file for HeavyArmedZombies...");
            _config = new PluginConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                PrintError("Configuration file is corrupt! Generating a fresh default config...");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        // =========================================================================
        //  CORE PLUGIN LOGIC
        // =========================================================================

        private const string ScientistPrefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roam.prefab";
        private const string RocketPrefab = "assets/prefabs/ammo/rocket/rocket_basic.prefab";
        private const string F1GrenadePrefab = "assets/prefabs/weapons/f1 grenade/grenade.f1.deployed.prefab";
        private const string C4Prefab = "assets/prefabs/tools/c4/explosive.timed.deployed.prefab";
        private const string TargetMonumentPrefab = "assets/bundled/prefabs/autospawn/monument/roadside/radtown_1.prefab";

        private enum ZombieRole { AK, SKS, BoltSniper, Rocketeer, Grenadier, C4Breacher }

        private class ZombieData
        {
            public Vector3 HomePos;
            public ZombieRole Role;
            public float NextSpecialAttackTime;
            public float NextBoltShotTime;
        }

        private readonly Dictionary<ulong, ZombieData> _zombies = new Dictionary<ulong, ZombieData>();
        private readonly HashSet<ulong> _zombieUserIds = new HashSet<ulong>();
        private readonly List<Vector3> _radTownCenters = new List<Vector3>();

        private void OnServerInitialized()
        {
            timer.Once(3f, () =>
            {
                FindRadTownMonuments();

                if (_radTownCenters.Count == 0)
                {
                    PrintWarning($"Could not find {TargetMonumentPrefab}! Stand in Radtown and run /setradtown.");
                    return;
                }

                SpawnInitialSquads();
            });
        }

        private void SpawnInitialSquads()
        {
            foreach (var center in _radTownCenters)
            {
                SpawnSquadAtCenter(center);
            }
        }

        private void SpawnSquadAtCenter(Vector3 center)
        {
            var roleCounts = new Dictionary<ZombieRole, int>
            {
                { ZombieRole.AK, _config.SpawnCounts.AK },
                { ZombieRole.SKS, _config.SpawnCounts.SKS },
                { ZombieRole.BoltSniper, _config.SpawnCounts.BoltSniper },
                { ZombieRole.Rocketeer, _config.SpawnCounts.Rocketeer },
                { ZombieRole.Grenadier, _config.SpawnCounts.Grenadier },
                { ZombieRole.C4Breacher, _config.SpawnCounts.C4Breacher }
            };

            int totalSpawned = 0;
            foreach (var kvp in roleCounts)
            {
                for (int i = 0; i < kvp.Value; i++)
                {
                    Vector3 pos = GetValidSpawnPointInRadTown(center);
                    if (pos != Vector3.zero)
                    {
                        SpawnZombie(pos, kvp.Key);
                        totalSpawned++;
                    }
                }
            }

            Puts($"Spawned {totalSpawned} HeavyArmedZombies at Radtown ({center})");
        }

        private void Unload()
        {
            foreach (var id in _zombies.Keys.ToList())
            {
                var ent = BaseNetworkable.serverEntities.Find(new NetworkableId(id)) as BaseEntity;
                if (ent != null && !ent.IsDestroyed)
                    ent.Kill();
            }
            _zombies.Clear();
            _zombieUserIds.Clear();
        }

        private void FindRadTownMonuments()
        {
            _radTownCenters.Clear();
            if (TerrainMeta.Path?.Monuments == null) return;

            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                if (monument == null) continue;

                string name = monument.name?.ToLower() ?? "";

                if (name == TargetMonumentPrefab || name.Contains("roadside/radtown"))
                {
                    _radTownCenters.Add(monument.transform.position);
                    Puts($"Locked HeavyArmedZombies to {monument.name} at {monument.transform.position}");
                }
            }
        }

        private Vector3 GetNearestRadTownCenter(Vector3 worldPos)
        {
            if (_radTownCenters.Count == 0) return Vector3.zero;
            return _radTownCenters.OrderBy(c => Vector3.Distance(worldPos, c)).First();
        }

        private bool IsInsideRadTown(Vector3 worldPos)
        {
            float radius = _config.Settings.RadTownRadius;
            foreach (var center in _radTownCenters)
            {
                float flatDist = Vector2.Distance(new Vector2(worldPos.x, worldPos.z), new Vector2(center.x, center.z));
                if (flatDist <= radius)
                    return true;
            }
            return false;
        }

        private Vector3 GetValidSpawnPointInRadTown(Vector3 center)
        {
            if (center == Vector3.zero) return Vector3.zero;

            float radius = _config.Settings.RadTownRadius;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                Vector2 randCircle = UnityEngine.Random.insideUnitCircle * (radius * 0.70f);
                Vector3 candidate = new Vector3(center.x + randCircle.x, center.y + 15f, center.z + randCircle.y);

                RaycastHit rayHit;
                if (Physics.Raycast(candidate, Vector3.down, out rayHit, 40f, LayerMask.GetMask("Terrain", "World", "Construction", "Default")))
                {
                    candidate = rayHit.point + Vector3.up * 0.2f;
                }
                else if (TerrainMeta.HeightMap != null)
                {
                    candidate.y = TerrainMeta.HeightMap.GetHeight(candidate) + 0.2f;
                }

                UnityEngine.AI.NavMeshHit navHit;
                if (UnityEngine.AI.NavMesh.SamplePosition(candidate, out navHit, 6f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    if (IsInsideRadTown(navHit.position))
                        return navHit.position;
                }

                if (IsInsideRadTown(candidate))
                    return candidate;
            }

            return center + Vector3.up * 1f;
        }

        [ChatCommand("setradtown")]
        private void CmdSetRadTown(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;

            Vector3 pos = player.transform.position;
            _radTownCenters.Add(pos);
            player.ChatMessage($"Added Radtown zone at {pos} (Radius: {_config.Settings.RadTownRadius}m). Spawning configured squad...");

            SpawnSquadAtCenter(pos);
        }

        [ChatCommand("spawnheavyzombie")]
        private void CmdSpawnHeavyZombie(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;

            if (_radTownCenters.Count == 0)
            {
                FindRadTownMonuments();
                if (_radTownCenters.Count == 0)
                {
                    _radTownCenters.Add(player.transform.position);
                }
            }

            ZombieRole role = (ZombieRole)UnityEngine.Random.Range(0, 6);
            string firstArg = args?.FirstOrDefault();
            if (!string.IsNullOrEmpty(firstArg))
            {
                switch (firstArg.ToLower())
                {
                    case "ak": role = ZombieRole.AK; break;
                    case "sks": role = ZombieRole.SKS; break;
                    case "bolt": role = ZombieRole.BoltSniper; break;
                    case "rocket": role = ZombieRole.Rocketeer; break;
                    case "grenade": role = ZombieRole.Grenadier; break;
                    case "c4": role = ZombieRole.C4Breacher; break;
                }
            }

            Vector3 spawnPos = IsInsideRadTown(player.transform.position)
                ? player.transform.position + Vector3.up * 0.2f
                : GetValidSpawnPointInRadTown(GetNearestRadTownCenter(player.transform.position));

            SpawnZombie(spawnPos, role);
            player.ChatMessage($"Spawned {role} Zombie at {spawnPos}. Total active: {_zombies.Count}");
        }

        private void SpawnZombie(Vector3 spawnPos, ZombieRole role)
        {
            var npc = GameManager.server.CreateEntity(ScientistPrefab, spawnPos, Quaternion.identity, false) as ScientistNPC;
            if (npc == null)
            {
                PrintWarning("Failed to instantiate ScientistNPC prefab!");
                return;
            }

            npc.gameObject.AwakeFromInstantiate();
            npc.Spawn();

            npc.displayName = $"Radtown Zombie [{role}]";
            npc.InitializeHealth(_config.Settings.ZombieHealth, _config.Settings.ZombieHealth);

            var navigator = npc.GetComponent<BaseNavigator>();
            if (navigator != null)
            {
                navigator.DefaultArea = "Walkable";
                navigator.CanUseCustomNav = true;
                if (npc.NavAgent != null)
                {
                    npc.NavAgent.areaMask = 1;
                    npc.NavAgent.agentTypeID = -1372625422;
                }
            }

            EquipZombieRole(npc, role);

            _zombieUserIds.Add(npc.userID);
            _zombies.Add(npc.net.ID.Value, new ZombieData
            {
                HomePos = spawnPos,
                Role = role,
                NextSpecialAttackTime = Time.realtimeSinceStartup + 2f,
                NextBoltShotTime = Time.realtimeSinceStartup + 2f
            });

            timer.Every(0.5f, () => UpdateZombieCombat(npc));
        }

        private void EquipZombieRole(ScientistNPC npc, ZombieRole role)
        {
            npc.inventory.Strip();

            if (_config.ClothingAndArmor != null)
            {
                foreach (var clothing in _config.ClothingAndArmor)
                {
                    if (clothing != null && !string.IsNullOrEmpty(clothing.Shortname))
                    {
                        ItemManager.CreateByName(clothing.Shortname, 1, clothing.SkinID)?.MoveToContainer(npc.inventory.containerWear);

                    }
                }
            }


            string weaponShortname = _config.Weapons.AK;
            switch (role)
            {
                case ZombieRole.AK: weaponShortname = _config.Weapons.AK; break;
                case ZombieRole.SKS: weaponShortname = _config.Weapons.SKS; break;
                case ZombieRole.BoltSniper: weaponShortname = _config.Weapons.BoltSniper; break;
                case ZombieRole.Rocketeer: weaponShortname = _config.Weapons.Rocketeer; break;
                case ZombieRole.Grenadier: weaponShortname = _config.Weapons.Grenadier; break;
                case ZombieRole.C4Breacher: weaponShortname = _config.Weapons.C4Breacher; break;
            }

            var weapon = ItemManager.CreateByName(weaponShortname, 1, 0);
            if (weapon != null)
            {
                weapon.MoveToContainer(npc.inventory.containerBelt);
                npc.UpdateActiveItem(weapon.uid);

                ItemManager.CreateByName("ammo.rifle", 120, 0)?.MoveToContainer(npc.inventory.containerMain);
            }
        }

        private void UpdateZombieCombat(ScientistNPC npc)
        {
            if (npc == null || npc.IsDestroyed || !_zombies.TryGetValue(npc.net.ID.Value, out ZombieData data))
                return;

            var navigator = npc.GetComponent<BaseNavigator>();
            var brain = npc.GetComponent<ScientistBrain>();
            if (navigator == null || brain == null) return;

            if (!IsInsideRadTown(npc.transform.position))
            {
                brain.Senses.Memory.Targets.Clear();
                Vector3 safeInsidePoint = IsInsideRadTown(data.HomePos) ? data.HomePos : GetValidSpawnPointInRadTown(GetNearestRadTownCenter(npc.transform.position));
                navigator.SetDestination(safeInsidePoint, BaseNavigator.NavigationSpeed.Fast);
                return;
            }

            BasePlayer target = brain.Senses.GetNearestTarget(_config.Settings.RadTownRadius) as BasePlayer;

            if (target != null && !IsInsideRadTown(target.transform.position))
            {
                brain.Senses.Memory.Targets.Remove(target);
                target = null;
            }

            if (target == null)
            {
                if (!navigator.Moving)
                {
                    Vector3 center = GetNearestRadTownCenter(data.HomePos);
                    Vector3 patrolPos = GetValidSpawnPointInRadTown(center);
                    if (patrolPos != Vector3.zero)
                    {
                        navigator.SetDestination(patrolPos, BaseNavigator.NavigationSpeed.Slow);
                    }
                }
                return;
            }

            float distToTarget = Vector3.Distance(npc.transform.position, target.transform.position);

            if (data.Role == ZombieRole.BoltSniper && Time.realtimeSinceStartup >= data.NextBoltShotTime)
            {
                if (distToTarget <= _config.Settings.RadTownRadius && brain.Senses.Memory.IsLOS(target))
                {
                    FireBoltSniperShot(npc, target);
                    data.NextBoltShotTime = Time.realtimeSinceStartup + 3.2f;
                }
            }

            if (data.Role == ZombieRole.C4Breacher)
            {
                navigator.SetDestination(target.transform.position, BaseNavigator.NavigationSpeed.Fast);
                if (distToTarget <= 4.0f && Time.realtimeSinceStartup >= data.NextSpecialAttackTime)
                {
                    PlantC4(npc, target);
                    data.NextSpecialAttackTime = Time.realtimeSinceStartup + 10f;
                }
            }
            else if (Time.realtimeSinceStartup >= data.NextSpecialAttackTime)
            {
                if (data.Role == ZombieRole.Rocketeer && distToTarget > 8f && distToTarget < 75f)
                {
                    FireRocket(npc, target);
                    data.NextSpecialAttackTime = Time.realtimeSinceStartup + 7f;
                }
                else if (data.Role == ZombieRole.Grenadier && distToTarget > 6f && distToTarget < 35f)
                {
                    ThrowF1Grenade(npc, target);
                    data.NextSpecialAttackTime = Time.realtimeSinceStartup + 6f;
                }
            }
        }

        private void FireBoltSniperShot(ScientistNPC npc, BasePlayer target)
        {
            var heldWeapon = npc.GetHeldEntity() as BaseProjectile;
            if (heldWeapon != null)
            {
                if (heldWeapon.primaryMagazine != null && heldWeapon.primaryMagazine.contents <= 0)
                {
                    heldWeapon.primaryMagazine.contents = heldWeapon.primaryMagazine.capacity;
                }
                npc.ShotTest(Vector3.Distance(npc.transform.position, target.transform.position));
            }
        }

        private void FireRocket(ScientistNPC npc, BasePlayer target)
        {
            Vector3 origin = npc.eyes.position + npc.eyes.HeadForward() * 1.2f;
            Vector3 direction = ((target.transform.position + Vector3.up * 1.0f) - origin).normalized;

            BaseEntity rocket = GameManager.server.CreateEntity(RocketPrefab, origin, Quaternion.LookRotation(direction));
            if (rocket == null) return;

            rocket.OwnerID = npc.userID;

            var projectile = rocket.GetComponent<ServerProjectile>();
            if (projectile != null)
            {
                projectile.InitializeVelocity(direction * 35f);
            }

            rocket.Spawn();
        }

        private void ThrowF1Grenade(ScientistNPC npc, BasePlayer target)
        {
            Vector3 origin = npc.eyes.position + npc.eyes.HeadForward() * 0.8f;
            Vector3 toTarget = target.transform.position - origin;
            Vector3 throwVelocity = toTarget.normalized * 14f + Vector3.up * 5f;

            TimedExplosive grenade = GameManager.server.CreateEntity(F1GrenadePrefab, origin, Quaternion.identity) as TimedExplosive;
            if (grenade == null) return;

            grenade.OwnerID = npc.userID;
            grenade.SetVelocity(throwVelocity);
            grenade.Spawn();
            grenade.SetFuse(3.5f);
        }

        private void PlantC4(ScientistNPC npc, BasePlayer target)
        {
            Vector3 dropPos = target.transform.position + Vector3.up * 0.8f;
            TimedExplosive c4 = GameManager.server.CreateEntity(C4Prefab, dropPos, Quaternion.identity) as TimedExplosive;
            if (c4 == null) return;

            c4.OwnerID = npc.userID;
            c4.Spawn();
            c4.SetParent(target, true, true);
            c4.SetFuse(4f);
        }

        private object OnNpcTarget(ScientistNPC npc, BaseEntity target)
        {
            if (!_zombies.ContainsKey(npc.net.ID.Value)) return null;

            if (target is ScientistNPC && _zombies.ContainsKey(target.net.ID.Value))
                return true;

            if (target != null && !IsInsideRadTown(target.transform.position))
                return true;

            return null;
        }

        private void OnEntityDeath(ScientistNPC npc, HitInfo info)
        {
            if (npc == null || !_zombies.ContainsKey(npc.net.ID.Value)) return;

            ZombieData data = _zombies[npc.net.ID.Value];
            Vector3 deathPos = npc.transform.position;

            _zombies.Remove(npc.net.ID.Value);
            _zombieUserIds.Remove(npc.userID);

            npc.inventory.Strip();
            SpawnCustomLootBag(deathPos, data.Role);

            Vector3 center = GetNearestRadTownCenter(data.HomePos);
            timer.Once(_config.Settings.RespawnDelaySeconds, () =>
            {
                Vector3 pos = GetValidSpawnPointInRadTown(center);
                if (pos != Vector3.zero)
                    SpawnZombie(pos, data.Role);
            });
        }

        private void SpawnCustomLootBag(Vector3 pos, ZombieRole role)
        {
            var container = GameManager.server.CreateEntity("assets/prefabs/misc/item drop/item_drop_backpack.prefab", pos + Vector3.up * 0.5f, Quaternion.identity) as DroppedItemContainer;
            if (container == null) return;

            container.inventory = new ItemContainer();
            container.inventory.ServerInitialize(null, 24);
            container.inventory.GiveUID();
            container.Spawn();

            if (_config.GeneralLootTable != null)
            {
                foreach (var entry in _config.GeneralLootTable)
                {
                    TryAddLootItem(container.inventory, entry);
                }
            }

            string roleKey = role.ToString();
            if (_config.RoleBonusLoot != null && _config.RoleBonusLoot.TryGetValue(roleKey, out List<LootEntry> bonusList))
            {
                foreach (var entry in bonusList)
                {
                    TryAddLootItem(container.inventory, entry);
                }
            }

            if (container.inventory.itemList.Count == 0)
            {
                container.Kill();
            }
        }

        private void TryAddLootItem(ItemContainer inv, LootEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Shortname)) return;
            if (UnityEngine.Random.Range(0f, 100f) > entry.DropChance) return;

            int amount = UnityEngine.Random.Range(entry.MinAmount, entry.MaxAmount + 1);
            if (amount <= 0) return;

            Item item = ItemManager.CreateByName(entry.Shortname, amount, 0);
            if (item != null)
            {
                if (!item.MoveToContainer(inv))
                {
                    item.Remove();
                }
            }
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity is ScientistNPC && _zombies.ContainsKey(entity.net.ID.Value) && info?.Initiator != null)
            {
                if (_zombieUserIds.Contains(info.Initiator.OwnerID) || (info.Initiator is ScientistNPC && _zombies.ContainsKey(info.Initiator.net.ID.Value)))
                {
                    return true;
                }
            }
            return null;
        }
    }
}
