using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Newtonsoft.Json;
using Rust;
using UnityEngine.AI;

namespace Oxide.Plugins
{
    [Info("ZombieSwarmCore", "Created BY: Alexprojects / Patched By: Paradox Studios (Patched)", "1.0.9-fix2")]
    [Description("High-quality coordinated squads with Chase/Escape logic, Hive-Mind and Squad Formations")]
    public class ZombieSwarmCore : RustPlugin
    {
        private HashSet<BaseCombatEntity> spawnedZombies = new HashSet<BaseCombatEntity>();
        private HashSet<NPCPlayerCorpse> zombieCorpses = new HashSet<NPCPlayerCorpse>();
        private SwarmController swarmController;
        private const string prefabScarecrow = "assets/prefabs/npc/scarecrow/scarecrow.prefab";
        private const string permAdmin = "zombieswarmcore.admin";
        private enum SwarmState { patrol, chase }

        public static int maskTerrainWorld;

        private Configuration config;
        private class Configuration
        {
            [JsonProperty("1. General Settings")] public GeneralSettings general = new GeneralSettings();
            [JsonProperty("2. Horde Management")] public SwarmSettings swarm = new SwarmSettings();
            [JsonProperty("3. Combat Settings")] public CombatSettings combat = new CombatSettings();
            [JsonProperty("4. Spawning Logic")] public SpawningSettings spawning = new SpawningSettings();
            [JsonProperty("5. Movement & Formations")] public MovementSettings movement = new MovementSettings();
            [JsonProperty("6. Patrol Behavior")] public PatrolSettings patrol = new PatrolSettings();
            [JsonProperty("7. Visuals Settings")] public VisualsSettings visuals = new VisualsSettings();

            public class GeneralSettings {
                [JsonProperty("Hordes Always (24/7)")] public bool hordesAlways = true;
                [JsonProperty("Start Hour (0-24)")] public float nightStart = 19f;
                [JsonProperty("End Hour (0-24)")] public float nightEnd = 7f;
                [JsonProperty("Clear Corpses On End")] public bool clearCorpsesOnEnd = true;
                [JsonProperty("Enable Status Message")] public bool enableStatusMessage = true;
                [JsonProperty("Status Message Interval (Minutes)")] public float statusMessageInterval = 5.0f;
            }
            public class SwarmSettings {
                [JsonProperty("Zombies Per Horde (Max 6)")] private int zph = 4;
                [JsonProperty("Max Global Zombies (Max 60)")] private int mgz = 40;
                [JsonProperty("Hordes Per Player (Max 2)")] private int hpp = 1;       
                [JsonIgnore] public int navSyncStep => Mathf.Clamp(zph, 1, (12 >> 1));
                [JsonIgnore] public int netEntityBuffer => Mathf.Clamp(mgz, 1, (120 >> 1));
                [JsonIgnore] public int playerThreadCap => Mathf.Clamp(hpp, 1, (4 >> 1));                
                [JsonProperty("Refill Hordes")] public bool refillSwarms = false;
                [JsonProperty("Refill Cooldown (Seconds)")] public float refillCooldown = 60f;
                [JsonProperty("New Horde Cooldown After Wipe (Seconds)")] public float wipeCooldown = 120f;
                [JsonProperty("Drop Loot")] public bool dropLoot = true;
            }
            public class CombatSettings {
                [JsonProperty("Detection Range (m)")] public float detectionRange = 30f;
                [JsonProperty("Target Lost Range (m)")] public float targetLostRange = 80f;
                [JsonProperty("Hive-Mind (Aggro whole squad on damage)")] public bool hiveMind = true;
                [JsonProperty("Zombie Health (HP)")] public float zombieHealth = 150f;
            }
            public class SpawningSettings {
                [JsonProperty("Spawn Min Distance (m)")] public float spawnMinDist = 20f;
                [JsonProperty("Spawn Max Distance (m)")] public float spawnMaxDist = 35f;
            }
            public class MovementSettings {
                [JsonProperty("Zombie Speed Multiplier (0.1 - 1.0)")] public float zombieSpeed = 0.8f;
                [JsonProperty("Squad Formation Spacing (m)")] public float spacing = 2.0f;
            }
            public class PatrolSettings {
                [JsonProperty("Patrol Sprint Probability (%)")] public int patrolSprintChance = 40;
                [JsonProperty("Behavior Change Interval (Seconds)")] public float behaviorInterval = 12f;
            }
            public class VisualsSettings {
                [JsonProperty("Enable Glowing Eyes")] public bool enableGlowingEyes = true;
                [JsonProperty("Death Sound Prefab")] public string deathSound = "assets/bundled/prefabs/fx/player/gutshot_scream.prefab";
            }
        }

        protected override void LoadDefaultConfig() => config = new Configuration();
        protected override void LoadConfig() {
            base.LoadConfig();
            try { config = Config.ReadObject<Configuration>(); } catch { LoadDefaultConfig(); }
            validateConfig();
            SaveConfig();
        }

        private void validateConfig() {
            config.movement.zombieSpeed = Mathf.Clamp(config.movement.zombieSpeed, 0.1f, 1f);
            
            if (config.spawning.spawnMinDist < 5f) config.spawning.spawnMinDist = 5f;
            if (config.spawning.spawnMaxDist <= config.spawning.spawnMinDist) 
                config.spawning.spawnMaxDist = config.spawning.spawnMinDist + 15f;

            if (config.combat.targetLostRange < config.combat.detectionRange) 
                config.combat.targetLostRange = config.combat.detectionRange + 20f;

            if (config.swarm.refillCooldown < 5f) config.swarm.refillCooldown = 5f;
            if (config.swarm.wipeCooldown < 5f) config.swarm.wipeCooldown = 5f;
        }

        protected override void SaveConfig() => Config.WriteObject(config);

        private class Swarm
        {
            public List<BaseCombatEntity> members = new List<BaseCombatEntity>();
            public BasePlayer target;
            public Coroutine controlRoutine;
            public SwarmState state = SwarmState.patrol;
            public Vector3 patrolPoint;
            public bool isPatrolSSprint = false;
            public float behaviorTimer = 0f;
            public float nextRefillTime = 0f;

            public void cleanup(HashSet<BaseCombatEntity> spawnedZombies) {
                if (controlRoutine != null && ServerMgr.Instance != null) ServerMgr.Instance.StopCoroutine(controlRoutine);
                
                foreach (var m in members) { 
                    if (m != null && !m.IsDestroyed) { 
                        spawnedZombies.Remove(m); 
                        
                        var sc = m as ScarecrowNPC;
                        if (sc != null && sc.inventory != null) {
                            sc.inventory.containerMain?.Clear();
                            sc.inventory.containerWear?.Clear();
                            sc.inventory.containerBelt?.Clear();
                        }
                        
                        m.Kill(); 
                    } 
                }
                members.Clear();
            }
        }

        private class SwarmController
        {
            private List<Swarm> activeSwarms = new List<Swarm>();
            private Dictionary<ulong, float> playerNextSwarmTime = new Dictionary<ulong, float>();
            private HashSet<BaseCombatEntity> spawnedZombies;
            private HashSet<NPCPlayerCorpse> zombieCorpses;
            private Configuration config;
            private ZombieSwarmCore plugin;

            public SwarmController(ZombieSwarmCore plugin, Configuration config, HashSet<BaseCombatEntity> zombies, HashSet<NPCPlayerCorpse> corpses) {
                this.plugin = plugin; this.config = config; this.spawnedZombies = zombies; this.zombieCorpses = corpses;
            }

            public void timeTick() {
                zombieCorpses.RemoveWhere(c => c == null || c.IsDestroyed);
                spawnedZombies.RemoveWhere(z => z == null || z.IsDestroyed || !z.IsAlive());
                if (!isSwarmHour()) { cleanupAll(false); return; }              
                foreach (var player in BasePlayer.activePlayerList) {
                    if (player == null || player.IsSleeping() || player.InSafeZone() || !player.IsAlive()) continue;
                    if (activeSwarms.Count(h => h.target == player) >= config.swarm.playerThreadCap) continue;
                    if (playerNextSwarmTime.TryGetValue(player.userID, out float nextAllowed) && Time.realtimeSinceStartup < nextAllowed) continue;
                    if (spawnedZombies.Count >= config.swarm.netEntityBuffer) break;
                    spawnSwarm(player);
                }
            }

            private bool isSwarmHour() {
                if (config.general.hordesAlways) return true;
                float h = ConVar.Env.time;
                if (config.general.nightStart < config.general.nightEnd) return h >= config.general.nightStart && h <= config.general.nightEnd;
                return h >= config.general.nightStart || h <= config.general.nightEnd;
            }

            private void spawnSwarm(BasePlayer player) {
                Vector3 spawnPos = calculateSafeSpawnPos(player.transform.position);
                if (spawnPos == Vector3.zero) return;
                Swarm swarm = new Swarm { target = player, patrolPoint = spawnPos };
                activeSwarms.Add(swarm);
                
                int syncSteps = Math.Min(config.swarm.navSyncStep, (12 >> 1));
                for (int i = 0; i < syncSteps; i++) { createSwarmMember(swarm, spawnPos); }
                
                swarm.controlRoutine = ServerMgr.Instance.StartCoroutine(swarmControlRoutine(swarm));
            }

            public void createSwarmMember(Swarm swarm, Vector3 pos) {
                Vector3 mPos = pos + (UnityEngine.Random.insideUnitSphere * 2f); mPos.y = pos.y;
                var ent = GameManager.server.CreateEntity(prefabScarecrow, mPos);
                if (ent == null) return;
                
                BaseCombatEntity npc = ent as BaseCombatEntity;
                if (npc != null) {
                    npc.startHealth = config.combat.zombieHealth;
                }

                ent.Spawn();
                
                spawnedZombies.Add(npc); 
                swarm.members.Add(npc);
                
                if (npc != null) {
                    npc._maxHealth = config.combat.zombieHealth;
                    npc.health = config.combat.zombieHealth;
                }

                var sc = npc as ScarecrowNPC;
                if (sc?.Brain != null) {
                    sc.Brain.SenseRange = config.combat.detectionRange;
                    sc.Brain.TargetLostRange = config.combat.targetLostRange;
                }
                if (config.visuals.enableGlowingEyes) plugin.applyGlowingEyes(npc);
                var nav = npc.GetComponent<BaseNavigator>();
                if (nav != null) { 
                    nav.SlowSpeedFraction = config.movement.zombieSpeed * 0.5f; 
                    nav.FastSpeedFraction = config.movement.zombieSpeed; 
                }
            }

            public void triggerAggro(BaseCombatEntity member, BasePlayer attacker) {
                foreach (var s in activeSwarms) {
                    if (s.members.Contains(member)) { s.target = attacker; s.state = SwarmState.chase; break; }
                }
            }

            public Vector3 calculateSafeSpawnPos(Vector3 center) {
                for (int i = 0; i < 15; i++) { 
                    float dist = UnityEngine.Random.Range(config.spawning.spawnMinDist, config.spawning.spawnMaxDist);
                    Vector2 rand = UnityEngine.Random.insideUnitCircle.normalized * dist;
                    Vector3 targetPos = center + new Vector3(rand.x, 0, rand.y);
                    
                    float terrainHeight = TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(targetPos) : targetPos.y;
                    targetPos.y = terrainHeight;

                    if (GetGroundPosition(targetPos, out Vector3 groundPos)) {
                        NavMeshHit hit;
                        if (NavMesh.SamplePosition(groundPos, out hit, 15f, NavMesh.AllAreas)) {
                            float finalDist = Vector3.Distance(center, hit.position);
                            if (finalDist >= config.spawning.spawnMinDist - 5f && finalDist <= config.spawning.spawnMaxDist + 10f) {
                                if (WaterLevel.GetWaterDepth(hit.position, false, false) <= 0.3f) {
                                    return hit.position;
                                }
                            }
                        }

                        if (WaterLevel.GetWaterDepth(groundPos, false, false) <= 0.3f) {
                            return groundPos;
                        }
                    }
                }
                return Vector3.zero;
            }

            private bool GetGroundPosition(Vector3 pos, out Vector3 groundPos) {
                groundPos = Vector3.zero;
                RaycastHit hit;
                if (Physics.Raycast(pos + Vector3.up * 25f, Vector3.down, out hit, 50f, ZombieSwarmCore.maskTerrainWorld)) {
                    if (WaterLevel.GetWaterDepth(hit.point, false, false) <= 0.3f) {
                        groundPos = hit.point;
                        return true;
                    }
                }
                return false;
            }

            private IEnumerator swarmControlRoutine(Swarm swarm) {
                BasePlayer ownerPlayer = swarm.target;
                while (swarm.target != null && swarm.target.IsConnected && swarm.target.IsAlive()) {
                    int prevCount = swarm.members.Count;
                    swarm.members.RemoveAll(z => z == null || z.IsDestroyed || !z.IsAlive());

                    if (swarm.members.Count < prevCount) {
                        swarm.nextRefillTime = Time.realtimeSinceStartup + config.swarm.refillCooldown;
                    }

                    // Exit routine once the squad is wiped so a fresh squad spawns after Wipe Cooldown
                    if (swarm.members.Count == 0) break;
                    
                    int stepLimit = (12 >> 1);
                    int bufferMax = (120 >> 1);
                    if (config.swarm.refillSwarms &&
                        Time.realtimeSinceStartup >= swarm.nextRefillTime &&
                        swarm.members.Count < Math.Min(config.swarm.navSyncStep, stepLimit) &&
                        spawnedZombies.Count < Math.Min(config.swarm.netEntityBuffer, bufferMax)) {
                        Vector3 rPos = calculateSafeSpawnPos(swarm.target.transform.position);
                        if (rPos != Vector3.zero) {
                            createSwarmMember(swarm, rPos);
                            swarm.nextRefillTime = Time.realtimeSinceStartup + config.swarm.refillCooldown;
                        }
                    }

                    if (swarm.members.Count > 0) {
                        BasePlayer closestPlayer = swarm.target;
                        float minDistance = float.MaxValue;
                        
                        foreach (var p in BasePlayer.activePlayerList) {
                            if (p == null || p.IsSleeping() || p.InSafeZone() || !p.IsAlive()) continue;
                            
                            foreach(var m in swarm.members) {
                                if (m == null || m.IsDestroyed) continue;
                                float d = Vector3.Distance(m.transform.position, p.transform.position);
                                if (d < minDistance) {
                                    minDistance = d;
                                    closestPlayer = p;
                                }
                            }
                        }

                        if (swarm.state != SwarmState.chase || minDistance < Vector3.Distance(swarm.members[0].transform.position, swarm.target.transform.position)) {
                            swarm.target = closestPlayer;
                        }

                        Vector3 targetPos = swarm.target.transform.position;
                        float dist = minDistance; 

                        if (swarm.state == SwarmState.chase) {
                            if (dist > config.combat.targetLostRange) {
                                swarm.state = SwarmState.patrol;
                                Vector3 newPatrol = calculateSafeSpawnPos(swarm.members[0].transform.position);
                                if (newPatrol != Vector3.zero) swarm.patrolPoint = newPatrol;
                            }
                        } else {
                            if (dist <= config.combat.detectionRange) swarm.state = SwarmState.chase;
                        }

                        if (swarm.state == SwarmState.patrol) {
                            swarm.behaviorTimer += 0.5f;
                            if (swarm.behaviorTimer >= config.patrol.behaviorInterval) {
                                swarm.isPatrolSSprint = UnityEngine.Random.Range(0, 100) < config.patrol.patrolSprintChance;
                                swarm.behaviorTimer = 0f;
                            }
                        }

                        Vector3 dir = (swarm.state == SwarmState.chase) ? (targetPos - swarm.members[0].transform.position).normalized : (swarm.patrolPoint - swarm.members[0].transform.position).normalized;
                        Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized;

                        for (int i = 0; i < swarm.members.Count; i++) {
                            var m = swarm.members[i];
                            if (m == null || m.IsDestroyed) continue;
                            var nav = m.GetComponent<BaseNavigator>();
                            if (nav == null) continue;
                            
                            if (swarm.state == SwarmState.chase && Vector3.Distance(m.transform.position, targetPos) < 2.5f) {
                                nav.Stop(); 
                                continue; 
                            }

                            BaseNavigator.NavigationSpeed nSpeed = (swarm.state == SwarmState.chase || swarm.isPatrolSSprint) ? BaseNavigator.NavigationSpeed.Fast : BaseNavigator.NavigationSpeed.Slow;
                            float side = (i % 2 == 0) ? 1 : -1; float row = (i / 2);
                            Vector3 offset = (perp * side * config.movement.spacing) + (dir * -row * config.movement.spacing);
                            Vector3 finalDest = (swarm.state == SwarmState.chase) ? (targetPos + offset) : (swarm.patrolPoint + offset);
                            
                            if (swarm.state == SwarmState.patrol && Vector3.Distance(m.transform.position, swarm.patrolPoint) < 5f) {
                                Vector3 nextPatrol = calculateSafeSpawnPos(m.transform.position);
                                if (nextPatrol != Vector3.zero) swarm.patrolPoint = nextPatrol;
                            }
                            
                            nav.SetDestination(finalDest, nSpeed);
                            if (nav.Agent != null && nav.Agent.isOnNavMesh) {
                                nav.Agent.speed = (nSpeed == BaseNavigator.NavigationSpeed.Fast ? 6.0f : 2.6f) * config.movement.zombieSpeed;
                                nav.Agent.acceleration = 20f; nav.Agent.angularSpeed = 400f;
                            }
                        }
                    }
                    yield return new WaitForSeconds(0.5f);
                }
                if (ownerPlayer != null) {
                    playerNextSwarmTime[ownerPlayer.userID] = Time.realtimeSinceStartup + config.swarm.wipeCooldown;
                }
                swarm.cleanup(spawnedZombies); activeSwarms.Remove(swarm);
            }

            public void cleanupAll(bool forceCorpseCleanup = false) { 
                foreach (var s in activeSwarms.ToList()) s.cleanup(spawnedZombies); 
                activeSwarms.Clear(); 
                playerNextSwarmTime.Clear();
                
                if (forceCorpseCleanup || config.general.clearCorpsesOnEnd) {
                    foreach (var c in zombieCorpses.ToList()) {
                        if (c != null && !c.IsDestroyed) {
                            if (c.containers != null) {
                                foreach (var container in c.containers) {
                                    container?.Clear();
                                }
                            }
                            c.Kill(); 
                        }
                    }
                    zombieCorpses.Clear();
                }
            }
        
            public int getSwarmCount() => activeSwarms.Count;
        }

        public void applyGlowingEyes(BaseCombatEntity npc) {
            var sc = npc as ScarecrowNPC; if (sc?.inventory == null) return;
            sc.SetFlagLocal(BaseEntity.Flags.On, true);
            Item eyes = ItemManager.CreateByName("gloweyes", 1);
            if (eyes != null) sc.inventory.GiveItem(eyes, sc.inventory.containerWear);
            sc.SendNetworkUpdateImmediate();
        }

        void OnServerInitialized() {
            maskTerrainWorld = LayerMask.GetMask("Terrain", "World");
            permission.RegisterPermission(permAdmin, this);
            swarmController = new SwarmController(this, config, spawnedZombies, zombieCorpses);
            timer.Once(3f, () => swarmController?.timeTick());
            timer.Every(25f, () => swarmController?.timeTick());
            
            if (config.general.enableStatusMessage && config.general.statusMessageInterval > 0)
            {
                timer.Every(config.general.statusMessageInterval * 60f, () => 
                {
                    string msg = string.Format(lang.GetMessage("Status", this), 
                        swarmController.getSwarmCount(), 
                        spawnedZombies.Count, 
                        config.swarm.netEntityBuffer);
                    PrintToChat(msg);
                });
            }
        }

        void OnEntitySpawned(BaseNetworkable ent) {
            if (ent is NPCPlayerCorpse corpse && corpse.name != null && (corpse.name.Contains("scarecrow") || corpse.name.Contains("murderer"))) {
                
                bool isOurZombie = false;
                foreach (var pos in dyingZombies) {
                    if (Vector3.SqrMagnitude(pos - corpse.transform.position) < 2f) {
                        isOurZombie = true;
                        break;
                    }
                }

                if (isOurZombie) {
                    zombieCorpses.Add(corpse); 
                    
                    if (!config.swarm.dropLoot) {
                        NextTick(() => {
                            if (corpse != null && !corpse.IsDestroyed && corpse.containers != null) {
                                if (corpse.containers.Length > 0) corpse.containers[0]?.Clear(); 
                                if (corpse.containers.Length > 2) corpse.containers[2]?.Clear(); 
                            }
                        });
                    }
                }
            }
        }

        void OnEntityTakeDamage(BaseCombatEntity ent, HitInfo info) {
            if (config.combat.hiveMind && spawnedZombies.Contains(ent) && info?.InitiatorPlayer != null && !info.InitiatorPlayer.IsNpc)
                swarmController?.triggerAggro(ent, info.InitiatorPlayer);
        }
        
        private List<Vector3> dyingZombies = new List<Vector3>();

        void OnEntityDeath(BaseCombatEntity ent) { 
            if (ent == null || !spawnedZombies.Contains(ent)) return; 
            
            Vector3 deathPos = ent.transform.position;
            dyingZombies.Add(deathPos);
            
            timer.Once(2f, () => dyingZombies.Remove(deathPos)); 

            spawnedZombies.Remove(ent);

            if (!string.IsNullOrEmpty(config.visuals.deathSound)) {
                Effect.server.Run(config.visuals.deathSound, deathPos);
            }
        }
        
        void Unload() { swarmController?.cleanupAll(true); }

        protected override void LoadDefaultMessages() {
            lang.RegisterMessages(new Dictionary<string, string> {
                ["Status"] = "<color=#ff6b6b>[Zombie Swarm Core]</color> Swarms: <color=#ff6b6b>{0}</color> | Entities: <color=#ff6b6b>{1}/{2}</color>"
            }, this);
        }

        [ChatCommand("swarms")]
        void cmdSwarmStatus(BasePlayer player, string command, string[] args) {
            if (!permission.UserHasPermission(player.UserIDString, permAdmin) && !player.IsAdmin) return;
            SendReply(player, lang.GetMessage("Status", this, player.UserIDString), swarmController.getSwarmCount(), spawnedZombies.Count, config.swarm.netEntityBuffer);
        }
    }
}