using HarmonyLib;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Night Lantern", "Paradox Studios", "3.1.0")]
    [Description("Automatically turns ON and OFF Jack O' Lanterns, Tuna Can Lamps, Lanterns, Chinese Lanterns, and Cursed Cauldrons at night")]
    class NightLantern : RustPlugin
    {
        #region Fields

        private static NightLantern _instance;
        private static ConfigData _config;

        private readonly Dictionary<BaseOven, LightCategory> _trackedOvens = new Dictionary<BaseOven, LightCategory>();
        private static readonly HashSet<BaseOven> ActiveNoFuelOvens = new HashSet<BaseOven>();

        private Timer _timeTimer;
        private bool _isNight;
        private bool _globalEnabled = true;

        private const string PermGlobal = "nightlantern.global";

        private enum LightCategory
        {
            None,
            JackOLantern,
            TunaLight,
            Lantern,
            ChineseLantern,
            CursedCauldron
        }

        #endregion Fields

        #region Config

        private class LightOption
        {
            [JsonProperty("Enabled")]
            public bool Enabled { get; set; } = true;

            [JsonProperty("Require Fuel")]
            public bool RequireFuel { get; set; } = false;
        }

        private class ConfigData
        {
            [JsonProperty("Sunset Hour (24h format)")]
            public float SunsetHour { get; set; } = 18.5f;

            [JsonProperty("Sunrise Hour (24h format)")]
            public float SunriseHour { get; set; } = 7.5f;

            [JsonProperty("Jack O' Lanterns")]
            public LightOption JackOLanterns { get; set; } = new LightOption { Enabled = true, RequireFuel = false };

            [JsonProperty("Tuna Can Lamps")]
            public LightOption TunaLights { get; set; } = new LightOption { Enabled = true, RequireFuel = false };

            [JsonProperty("Lanterns")]
            public LightOption Lanterns { get; set; } = new LightOption { Enabled = true, RequireFuel = false };

            [JsonProperty("Chinese Lanterns")]
            public LightOption ChineseLanterns { get; set; } = new LightOption { Enabled = true, RequireFuel = false };

            [JsonProperty("Cursed Cauldrons")]
            public LightOption CursedCauldrons { get; set; } = new LightOption { Enabled = true, RequireFuel = false };
        }

        protected override void LoadDefaultConfig()
        {
            _config = new ConfigData();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<ConfigData>();
                if (_config == null)
                    LoadDefaultConfig();

                if (_config.JackOLanterns == null) _config.JackOLanterns = new LightOption();
                if (_config.TunaLights == null) _config.TunaLights = new LightOption();
                if (_config.Lanterns == null) _config.Lanterns = new LightOption();
                if (_config.ChineseLanterns == null) _config.ChineseLanterns = new LightOption();
                if (_config.CursedCauldrons == null) _config.CursedCauldrons = new LightOption();
            }
            catch
            {
                PrintWarning("Config file was invalid or outdated. Generating a clean config...");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        #endregion Config

        #region Oxide Hooks

        private void Init()
        {
            _instance = this;
            permission.RegisterPermission(PermGlobal, this);
        }

        private void OnServerInitialized()
        {
            ScanAllServerEntities();

            _isNight = IsNightTime();
            if (_isNight && _globalEnabled)
                ServerMgr.Instance.StartCoroutine(SetAllLightsRoutine(true));

            _timeTimer = timer.Every(15f, CheckTimeLoop);
        }

        private void Unload()
        {
            _timeTimer?.Destroy();

            foreach (BaseOven oven in _trackedOvens.Keys.ToList())
            {
                if (oven != null && !oven.IsDestroyed)
                {
                    ActiveNoFuelOvens.Remove(oven);
                    if (oven.IsOn())
                        oven.StopCooking();
                }
            }

            _trackedOvens.Clear();
            ActiveNoFuelOvens.Clear();
            _instance = null;
            _config = null;
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            BaseOven oven = entity as BaseOven;
            if (oven == null || oven.IsDestroyed)
                return;

            LightCategory category = GetLightCategory(oven);
            if (category == LightCategory.None || !IsCategoryEnabled(category))
                return;

            _trackedOvens[oven] = category;

            if (_isNight && _globalEnabled)
            {
                NextTick(() =>
                {
                    if (oven != null && !oven.IsDestroyed)
                        TurnOnLight(oven, category);
                });
            }
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            BaseOven oven = entity as BaseOven;
            if (oven != null)
            {
                _trackedOvens.Remove(oven);
                ActiveNoFuelOvens.Remove(oven);
            }
        }

        private object OnFuelConsume(BaseOven oven, Item fuel, ItemModBurnable burnable)
        {
            if (oven == null || oven.IsDestroyed)
                return null;

            if (ActiveNoFuelOvens.Contains(oven))
                return true;

            return null;
        }

        #endregion Oxide Hooks

        #region Core Logic

        private void ScanAllServerEntities()
        {
            _trackedOvens.Clear();
            ActiveNoFuelOvens.Clear();

            foreach (BaseNetworkable netEntity in BaseNetworkable.serverEntities)
            {
                BaseOven oven = netEntity as BaseOven;
                if (oven == null || oven.IsDestroyed)
                    continue;

                LightCategory category = GetLightCategory(oven);
                if (category != LightCategory.None && IsCategoryEnabled(category))
                    _trackedOvens[oven] = category;
            }

            Puts($"Found and tracking {_trackedOvens.Count} matching light entities across the map.");
        }

        private LightCategory GetLightCategory(BaseOven oven)
        {
            string prefab = oven.ShortPrefabName;
            if (string.IsNullOrEmpty(prefab))
                return LightCategory.None;

            prefab = prefab.ToLowerInvariant();

            if (prefab.Contains("jackolantern") || prefab.Contains("jack_o_lantern"))
                return LightCategory.JackOLantern;

            if (prefab.Contains("chineselantern") || prefab.Contains("chinese_lantern"))
                return LightCategory.ChineseLantern;

            if (prefab.Contains("tunalight") || prefab.Contains("tuna"))
                return LightCategory.TunaLight;

            if (prefab.Contains("cursedcauldron") || prefab.Contains("cauldron"))
                return LightCategory.CursedCauldron;

            if (prefab.Contains("lantern"))
                return LightCategory.Lantern;

            return LightCategory.None;
        }

        private LightOption GetCategoryOption(LightCategory category)
        {
            switch (category)
            {
                case LightCategory.JackOLantern:
                    return _config.JackOLanterns;
                case LightCategory.TunaLight:
                    return _config.TunaLights;
                case LightCategory.Lantern:
                    return _config.Lanterns;
                case LightCategory.ChineseLantern:
                    return _config.ChineseLanterns;
                case LightCategory.CursedCauldron:
                    return _config.CursedCauldrons;
                default:
                    return null;
            }
        }

        private bool IsCategoryEnabled(LightCategory category)
        {
            LightOption option = GetCategoryOption(category);
            return option != null && option.Enabled;
        }

        private bool DoesCategoryRequireFuel(LightCategory category)
        {
            LightOption option = GetCategoryOption(category);
            return option != null && option.RequireFuel;
        }

        private bool IsNightTime()
        {
            if (TOD_Sky.Instance == null)
                return false;

            float hour = TOD_Sky.Instance.Cycle.Hour;
            if (_config.SunsetHour > _config.SunriseHour)
                return hour >= _config.SunsetHour || hour < _config.SunriseHour;

            return hour >= _config.SunsetHour && hour < _config.SunriseHour;
        }

        private void CheckTimeLoop()
        {
            if (!_globalEnabled)
                return;

            bool shouldBeOn = IsNightTime();
            if (shouldBeOn != _isNight)
            {
                _isNight = shouldBeOn;
                ServerMgr.Instance.StartCoroutine(SetAllLightsRoutine(_isNight));
            }
        }

        private IEnumerator SetAllLightsRoutine(bool turnOn)
        {
            List<KeyValuePair<BaseOven, LightCategory>> snapshot = _trackedOvens.ToList();
            int count = 0;

            foreach (KeyValuePair<BaseOven, LightCategory> entry in snapshot)
            {
                BaseOven oven = entry.Key;
                LightCategory category = entry.Value;

                if (oven == null || oven.IsDestroyed)
                {
                    _trackedOvens.Remove(oven);
                    ActiveNoFuelOvens.Remove(oven);
                    continue;
                }

                if (turnOn)
                    TurnOnLight(oven, category);
                else
                    TurnOffLight(oven);

                count++;
                if (count % 25 == 0)
                    yield return null;
            }
        }

        private void TurnOnLight(BaseOven oven, LightCategory category)
        {
            if (!DoesCategoryRequireFuel(category))
                ActiveNoFuelOvens.Add(oven);
            else
                ActiveNoFuelOvens.Remove(oven);

            if (!oven.IsOn())
            {
                oven.StartCooking();
                oven.SendNetworkUpdate();
            }
        }

        private void TurnOffLight(BaseOven oven)
        {
            ActiveNoFuelOvens.Remove(oven);

            if (oven.IsOn())
            {
                oven.StopCooking();
                oven.SendNetworkUpdate();
            }
        }

        #endregion Core Logic

        #region Harmony Patches

        [AutoPatch]
        [HarmonyPatch(typeof(BaseOven), nameof(BaseOven.CanRunWithNoFuel), MethodType.Getter)]
        private static class BaseOven_CanRunWithNoFuel_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(BaseOven __instance, ref bool __result)
            {
                if (ActiveNoFuelOvens.Contains(__instance))
                {
                    __result = true;
                    return false;
                }
                return true;
            }
        }

        #endregion Harmony Patches

        #region Commands

        [ChatCommand("lantern")]
        private void CmdLantern(BasePlayer player, string command, string[] args)
        {
            if (args.Length > 0 && args[0].Equals("global", StringComparison.OrdinalIgnoreCase))
            {
                if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermGlobal))
                {
                    player.ChatMessage("<color=#FFA500>[Night Lantern]</color> You do not have permission to use this command.");
                    return;
                }

                _globalEnabled = !_globalEnabled;
                if (_globalEnabled)
                {
                    _isNight = IsNightTime();
                    ServerMgr.Instance.StartCoroutine(SetAllLightsRoutine(_isNight));
                }
                else
                {
                    ServerMgr.Instance.StartCoroutine(SetAllLightsRoutine(false));
                }

                string state = _globalEnabled ? "<color=#8ee700>enabled</color>" : "<color=#e90000>disabled</color>";
                player.ChatMessage($"<color=#FFA500>[Night Lantern]</color> Auto lights are now {state} server-wide ({_trackedOvens.Count} lights tracked).");
                return;
            }

            if (args.Length > 0 && args[0].Equals("scan", StringComparison.OrdinalIgnoreCase))
            {
                if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermGlobal))
                {
                    player.ChatMessage("<color=#FFA500>[Night Lantern]</color> You do not have permission to use this command.");
                    return;
                }

                ScanAllServerEntities();
                _isNight = IsNightTime();
                if (_isNight && _globalEnabled)
                    ServerMgr.Instance.StartCoroutine(SetAllLightsRoutine(true));

                player.ChatMessage($"<color=#FFA500>[Night Lantern]</color> Rescanned map: tracking <color=#8ee700>{_trackedOvens.Count}</color> lights.");
                return;
            }

            string statusText = _globalEnabled ? "<color=#8ee700>Enabled</color>" : "<color=#e90000>Disabled</color>";
            string nightText = _isNight ? "Yes" : "No";
            player.ChatMessage($"<color=#FFA500>[Night Lantern]</color> Tracking <color=#8ee700>{_trackedOvens.Count}</color> lights server-wide.");
            player.ChatMessage($"Status: {statusText} | Night Active: {nightText}");
            player.ChatMessage("Commands: <color=#FFA500>/lantern global</color> | <color=#FFA500>/lantern scan</color>");
        }

        #endregion Commands
    }
}
