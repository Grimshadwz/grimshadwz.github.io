using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RGBLights", "Paradox Studios", "1.5.0")]
    [Description("TC-locked CUI controller for Red, Green, Blue, and White Industrial Lights with 19 show modes.")]
    public class RGBLights : RustPlugin
    {
        private const string UI_NAME = "RGBLights_UI";
        private const float TC_SEARCH_RADIUS = 50f;

        private Dictionary<ulong, ShowController> activeShows = new Dictionary<ulong, ShowController>();

        private enum ShowMode
        {
            Off,
            Cycle,
            ColorMix,
            PartyRandom,
            WhiteStrobe,
            Police,
            SolidWhite,
            SolidRed,
            SolidGreen,
            SolidBlue,
            AllLightsOn,
            AllLightTwinkle,
            ChaseWave,
            SparkleTwinkle,
            FireCandle,
            RainbowFade,
            BiColorPulse,
            Cyberpunk,
            Christmas,
            LightningStorm
        }

        private class ShowController
        {
            public ulong TcNetId;
            public Vector3 CenterPos;
            public ShowMode Mode = ShowMode.Off;
            public float Interval = 0.5f;
            public int Step = 0;
            public Timer LoopTimer;
            public List<IOEntity> RedLights = new List<IOEntity>();
            public List<IOEntity> GreenLights = new List<IOEntity>();
            public List<IOEntity> BlueLights = new List<IOEntity>();
            public List<IOEntity> WhiteLights = new List<IOEntity>();
            public List<IOEntity> AllLights = new List<IOEntity>();
        }

        #region Oxide Hooks

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, UI_NAME);
            }

            foreach (var show in activeShows.Values)
            {
                show.LoopTimer?.Destroy();
            }
            activeShows.Clear();
        }

        #endregion

        #region Chat Commands

        [ChatCommand("rgb")]
        private void CmdRgb(BasePlayer player, string command, string[] args)
        {
            BuildingPrivlidge tc = player.GetBuildingPrivilege();
            if (tc == null)
            {
                SendReply(player, "<color=#ff5555>[RGB Lights]</color> You must be standing inside a Tool Cupboard range.");
                return;
            }

            if (!player.IsBuildingAuthed())
            {
                SendReply(player, "<color=#ff5555>[RGB Lights]</color> Only players authorized on this TC can control the lights.");
                return;
            }

            ulong tcId = tc.net.ID.Value;
            if (!activeShows.TryGetValue(tcId, out ShowController show))
            {
                show = new ShowController
                {
                    TcNetId = tcId,
                    CenterPos = tc.transform.position
                };
                activeShows[tcId] = show;
            }

            RefreshLightsInTC(show, tc, player);
            OpenUI(player, show);
        }

        #endregion

        #region Light Discovery & Control

        private void RefreshLightsInTC(ShowController show, BuildingPrivlidge tc, BasePlayer debugPlayer = null)
        {
            show.RedLights.Clear();
            show.GreenLights.Clear();
            show.BlueLights.Clear();
            show.WhiteLights.Clear();
            show.AllLights.Clear();

            Vector3 tcPos = tc.transform.position;
            BuildingManager.Building tcBuilding = tc.GetBuilding();
            List<string> debugFound = new List<string>();

            foreach (var netEntity in BaseNetworkable.serverEntities)
            {
                IOEntity io = netEntity as IOEntity;
                if (io == null || io.IsDestroyed) continue;

                if (Vector3.Distance(io.transform.position, tcPos) > TC_SEARCH_RADIUS) continue;

                string prefab = io.ShortPrefabName.ToLowerInvariant();
                if (!prefab.Contains("industrial") && !prefab.Contains("light") && !prefab.Contains("lamp") && !prefab.Contains("siren")) continue;

                bool belongsToTc = false;
                BuildingPrivlidge entityTc = io.GetBuildingPrivilege(io.WorldSpaceBounds());
                if (entityTc != null && entityTc.net.ID.Value == tc.net.ID.Value)
                {
                    belongsToTc = true;
                }
                else if (tcBuilding != null)
                {
                    DecayEntity decay = io as DecayEntity;
                    if (decay != null && decay.buildingID == tcBuilding.ID)
                    {
                        belongsToTc = true;
                    }
                }

                if (!belongsToTc) continue;

                if (debugFound.Count < 8)
                {
                    debugFound.Add($"{io.ShortPrefabName} ({io.GetType().Name})");
                }

                if (!prefab.Contains("industrial") && !prefab.Contains("wall.light") && !prefab.Contains("wall_light")) continue;

                if (prefab.Contains("red"))
                {
                    show.RedLights.Add(io);
                }
                else if (prefab.Contains("green"))
                {
                    show.GreenLights.Add(io);
                }
                else if (prefab.Contains("blue"))
                {
                    show.BlueLights.Add(io);
                }
                else
                {
                    show.WhiteLights.Add(io);
                }
            }

            // Build spatial list sorted clockwise around the TC for Chase/Wave modes
            var combined = new List<IOEntity>();
            combined.AddRange(show.RedLights);
            combined.AddRange(show.GreenLights);
            combined.AddRange(show.BlueLights);
            combined.AddRange(show.WhiteLights);

            show.AllLights = combined
                .OrderBy(e => Mathf.Atan2(e.transform.position.z - tcPos.z, e.transform.position.x - tcPos.x))
                .ToList();

            int total = show.AllLights.Count;
            if (total == 0 && debugPlayer != null)
            {
                string summary = debugFound.Count > 0 ? string.Join(", ", debugFound) : "None";
                SendReply(debugPlayer, $"<color=#ffaa00>[RGB Debug]</color> 0 industrial lights matched. Nearby light entities in TC: {summary}");
            }
        }

        private bool IsStaticMode(ShowMode mode)
        {
            return mode == ShowMode.Off
                || mode == ShowMode.SolidWhite
                || mode == ShowMode.SolidRed
                || mode == ShowMode.SolidGreen
                || mode == ShowMode.SolidBlue
                || mode == ShowMode.AllLightsOn;
        }

        private void StartShowLoop(ShowController show)
        {
            show.LoopTimer?.Destroy();
            show.Step = 0;

            if (show.Mode == ShowMode.Off)
            {
                SetGroupPower(show.RedLights, false);
                SetGroupPower(show.GreenLights, false);
                SetGroupPower(show.BlueLights, false);
                SetGroupPower(show.WhiteLights, false);
                return;
            }

            if (show.Mode == ShowMode.SolidWhite)
            {
                SetGroupPower(show.RedLights, false);
                SetGroupPower(show.GreenLights, false);
                SetGroupPower(show.BlueLights, false);
                SetGroupPower(show.WhiteLights, true);
                return;
            }

            if (show.Mode == ShowMode.SolidRed)
            {
                SetGroupPower(show.RedLights, true);
                SetGroupPower(show.GreenLights, false);
                SetGroupPower(show.BlueLights, false);
                SetGroupPower(show.WhiteLights, false);
                return;
            }

            if (show.Mode == ShowMode.SolidGreen)
            {
                SetGroupPower(show.RedLights, false);
                SetGroupPower(show.GreenLights, true);
                SetGroupPower(show.BlueLights, false);
                SetGroupPower(show.WhiteLights, false);
                return;
            }

            if (show.Mode == ShowMode.SolidBlue)
            {
                SetGroupPower(show.RedLights, false);
                SetGroupPower(show.GreenLights, false);
                SetGroupPower(show.BlueLights, true);
                SetGroupPower(show.WhiteLights, false);
                return;
            }

            if (show.Mode == ShowMode.AllLightsOn)
            {
                SetGroupPower(show.RedLights, true);
                SetGroupPower(show.GreenLights, true);
                SetGroupPower(show.BlueLights, true);
                SetGroupPower(show.WhiteLights, true);
                return;
            }

            show.LoopTimer = timer.Every(show.Interval, () => TickShow(show));
            TickShow(show);
        }

        private void TickShow(ShowController show)
        {
            switch (show.Mode)
            {
                case ShowMode.Cycle:
                    int cycleIdx = show.Step % 4;
                    SetGroupPower(show.RedLights, cycleIdx == 0);
                    SetGroupPower(show.GreenLights, cycleIdx == 1);
                    SetGroupPower(show.BlueLights, cycleIdx == 2);
                    SetGroupPower(show.WhiteLights, cycleIdx == 3);
                    break;

                case ShowMode.ColorMix:
                    int mixIdx = show.Step % 7;
                    SetGroupPower(show.RedLights, mixIdx == 0 || mixIdx == 1 || mixIdx == 5 || mixIdx == 6);
                    SetGroupPower(show.GreenLights, mixIdx == 1 || mixIdx == 2 || mixIdx == 3 || mixIdx == 6);
                    SetGroupPower(show.BlueLights, mixIdx == 3 || mixIdx == 4 || mixIdx == 5 || mixIdx == 6);
                    SetGroupPower(show.WhiteLights, mixIdx == 6);
                    break;

                case ShowMode.PartyRandom:
                    SetRandomIndividualPower(show.AllLights, 0.5f);
                    break;

                case ShowMode.WhiteStrobe:
                    bool strobeOn = (show.Step % 2) == 0;
                    SetGroupPower(show.RedLights, false);
                    SetGroupPower(show.GreenLights, false);
                    SetGroupPower(show.BlueLights, false);
                    SetGroupPower(show.WhiteLights, strobeOn);
                    break;

                case ShowMode.Police:
                    // Double-flash red, then double-flash blue
                    int pStep = show.Step % 6;
                    bool redOn = (pStep == 0 || pStep == 2);
                    bool blueOn = (pStep == 3 || pStep == 5);
                    SetGroupPower(show.RedLights, redOn);
                    SetGroupPower(show.BlueLights, blueOn);
                    SetGroupPower(show.GreenLights, false);
                    SetGroupPower(show.WhiteLights, false);
                    break;

                case ShowMode.ChaseWave:
                    // Chases clockwise around the base one light at a time (with 2-light tail)
                    CleanDestroyed(show.AllLights);
                    int count = show.AllLights.Count;
                    if (count > 0)
                    {
                        int head = show.Step % count;
                        int tail = (show.Step - 1 + count) % count;
                        for (int i = 0; i < count; i++)
                        {
                            ApplyLightPower(show.AllLights[i], i == head || (count > 2 && i == tail));
                        }
                    }
                    break;

                case ShowMode.SparkleTwinkle:
                    // Sparse 25% random shimmer across all lights
                    SetRandomIndividualPower(show.AllLights, 0.25f);
                    break;

                case ShowMode.AllLightTwinkle:
                    // Christmas tree twinkle: keeps ~80% of all lights lit while ~20% gently blink off/on each tick
                    SetRandomIndividualPower(show.AllLights, 0.80f);
                    break;

                case ShowMode.FireCandle:
                    // Warm Red base with flickering White/Green sparks
                    SetGroupPower(show.RedLights, true);
                    SetGroupPower(show.BlueLights, false);
                    SetRandomIndividualPower(show.WhiteLights, 0.35f);
                    SetRandomIndividualPower(show.GreenLights, 0.2f);
                    break;

                case ShowMode.RainbowFade:
                    // Overlapping smooth transitions: R -> R+G -> G -> G+B -> B -> B+W -> W -> W+R
                    int rStep = show.Step % 8;
                    SetGroupPower(show.RedLights, rStep == 0 || rStep == 1 || rStep == 7);
                    SetGroupPower(show.GreenLights, rStep == 1 || rStep == 2 || rStep == 3);
                    SetGroupPower(show.BlueLights, rStep == 3 || rStep == 4 || rStep == 5);
                    SetGroupPower(show.WhiteLights, rStep == 5 || rStep == 6 || rStep == 7);
                    break;

                case ShowMode.BiColorPulse:
                    // Alternates warm (Red+White) vs cool (Blue+Green)
                    bool warm = (show.Step % 2) == 0;
                    SetGroupPower(show.RedLights, warm);
                    SetGroupPower(show.WhiteLights, warm);
                    SetGroupPower(show.BlueLights, !warm);
                    SetGroupPower(show.GreenLights, !warm);
                    break;

                case ShowMode.Cyberpunk:
                    // Neon Blue & Red constant glow with pulsing Green/White accents
                    int cStep = show.Step % 4;
                    SetGroupPower(show.BlueLights, true);
                    SetGroupPower(show.RedLights, cStep != 2);
                    SetGroupPower(show.GreenLights, cStep == 1 || cStep == 2);
                    SetGroupPower(show.WhiteLights, cStep == 3);
                    break;

                case ShowMode.Christmas:
                    // Classic Red & Green alternating with steady White accent
                    bool xmasFlip = (show.Step % 2) == 0;
                    SetGroupPower(show.RedLights, xmasFlip);
                    SetGroupPower(show.GreenLights, !xmasFlip);
                    SetGroupPower(show.WhiteLights, true);
                    SetGroupPower(show.BlueLights, false);
                    break;

                case ShowMode.LightningStorm:
                    // Dark moody blue glow with sudden white/blue lightning double-strikes
                    int lRoll = UnityEngine.Random.Range(0, 10);
                    if (lRoll == 0)
                    {
                        // Lightning flash!
                        SetGroupPower(show.WhiteLights, true);
                        SetGroupPower(show.BlueLights, true);
                        SetGroupPower(show.RedLights, false);
                        SetGroupPower(show.GreenLights, false);
                    }
                    else
                    {
                        // Dim storm ambient
                        SetGroupPower(show.WhiteLights, false);
                        SetGroupPower(show.RedLights, false);
                        SetGroupPower(show.GreenLights, false);
                        SetRandomIndividualPower(show.BlueLights, 0.3f);
                    }
                    break;
            }

            show.Step++;
        }

        private void CleanDestroyed(List<IOEntity> lights)
        {
            for (int i = lights.Count - 1; i >= 0; i--)
            {
                if (lights[i] == null || lights[i].IsDestroyed)
                {
                    lights.RemoveAt(i);
                }
            }
        }

        private void SetGroupPower(List<IOEntity> lights, bool turnOn)
        {
            for (int i = lights.Count - 1; i >= 0; i--)
            {
                var light = lights[i];
                if (light == null || light.IsDestroyed)
                {
                    lights.RemoveAt(i);
                    continue;
                }

                ApplyLightPower(light, turnOn);
            }
        }

        private void SetRandomIndividualPower(List<IOEntity> lights, float onChance)
        {
            for (int i = lights.Count - 1; i >= 0; i--)
            {
                var light = lights[i];
                if (light == null || light.IsDestroyed)
                {
                    lights.RemoveAt(i);
                    continue;
                }

                bool state = UnityEngine.Random.value < onChance;
                ApplyLightPower(light, state);
            }
        }

        private void ApplyLightPower(IOEntity light, bool turnOn)
        {
            int powerAmount = turnOn ? Mathf.Max(1, light.ConsumptionAmount()) : 0;
            light.UpdateFromInput(powerAmount, 0);
            light.SendNetworkUpdateImmediate();
        }

        #endregion

        #region CUI Interface & Console Handlers

        private void OpenUI(BasePlayer player, ShowController show)
        {
            CuiHelper.DestroyUi(player, UI_NAME);

            var elements = new CuiElementContainer();

            // Expanded panel to hold 19 modes cleanly (10 rows x 2 columns)
            elements.Add(new CuiPanel
            {
                Image = { Color = "0.11 0.11 0.13 0.96" },
                RectTransform = { AnchorMin = "0.30 0.10", AnchorMax = "0.70 0.90" },
                CursorEnabled = true
            }, "Overlay", UI_NAME);

            // Header
            elements.Add(new CuiLabel
            {
                Text = { Text = "TC RGB LIGHT CONTROLLER (19 MODES)", FontSize = 15, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.05 0.92", AnchorMax = "0.86 0.98" }
            }, UI_NAME);

            // Close Button
            elements.Add(new CuiButton
            {
                Button = { Command = "rgblights.close", Color = "0.75 0.2 0.2 1" },
                RectTransform = { AnchorMin = "0.89 0.93", AnchorMax = "0.96 0.97" },
                Text = { Text = "X", FontSize = 13, Align = TextAnchor.MiddleCenter }
            }, UI_NAME);

            // Light Count Readout
            string countText = $"Found in TC: <color=#ff5555>R:{show.RedLights.Count}</color> | <color=#55ff55>G:{show.GreenLights.Count}</color> | <color=#55aaff>B:{show.BlueLights.Count}</color> | <color=#ffffff>W:{show.WhiteLights.Count}</color>";
            elements.Add(new CuiLabel
            {
                Text = { Text = countText, FontSize = 12, Align = TextAnchor.MiddleCenter },
                RectTransform = { AnchorMin = "0.05 0.87", AnchorMax = "0.95 0.915" }
            }, UI_NAME);

            // 2-Column Grid of 19 Modes (10 rows)
            // Row 1
            AddModeButton(elements, "ALL LIGHTS ON", ShowMode.AllLightsOn, show.Mode, "0.05 0.805", "0.48 0.86");
            AddModeButton(elements, "ALL LIGHT TWINKLE", ShowMode.AllLightTwinkle, show.Mode, "0.52 0.805", "0.95 0.86");

            // Row 2 - Solid Colors
            AddModeButton(elements, "SOLID WHITE", ShowMode.SolidWhite, show.Mode, "0.05 0.74", "0.48 0.795");
            AddModeButton(elements, "SOLID RED", ShowMode.SolidRed, show.Mode, "0.52 0.74", "0.95 0.795");

            // Row 3 - Solid Colors
            AddModeButton(elements, "SOLID GREEN", ShowMode.SolidGreen, show.Mode, "0.05 0.675", "0.48 0.73");
            AddModeButton(elements, "SOLID BLUE", ShowMode.SolidBlue, show.Mode, "0.52 0.675", "0.95 0.73");

            // Row 4
            AddModeButton(elements, "RGB CYCLE", ShowMode.Cycle, show.Mode, "0.05 0.61", "0.48 0.665");
            AddModeButton(elements, "RAINBOW OVERLAP", ShowMode.RainbowFade, show.Mode, "0.52 0.61", "0.95 0.665");

            // Row 5
            AddModeButton(elements, "COLOR MIX (7-TONE)", ShowMode.ColorMix, show.Mode, "0.05 0.545", "0.48 0.60");
            AddModeButton(elements, "CHASE / WAVE", ShowMode.ChaseWave, show.Mode, "0.52 0.545", "0.95 0.60");

            // Row 6
            AddModeButton(elements, "CLUB PARTY (50%)", ShowMode.PartyRandom, show.Mode, "0.05 0.48", "0.48 0.535");
            AddModeButton(elements, "SPARKLE TWINKLE", ShowMode.SparkleTwinkle, show.Mode, "0.52 0.48", "0.95 0.535");

            // Row 7
            AddModeButton(elements, "WHITE STROBE", ShowMode.WhiteStrobe, show.Mode, "0.05 0.415", "0.48 0.47");
            AddModeButton(elements, "LIGHTNING STORM", ShowMode.LightningStorm, show.Mode, "0.52 0.415", "0.95 0.47");

            // Row 8
            AddModeButton(elements, "POLICE DOUBLE-FLASH", ShowMode.Police, show.Mode, "0.05 0.35", "0.48 0.405");
            AddModeButton(elements, "WARM vs COOL PULSE", ShowMode.BiColorPulse, show.Mode, "0.52 0.35", "0.95 0.405");

            // Row 9
            AddModeButton(elements, "CYBERPUNK NEON", ShowMode.Cyberpunk, show.Mode, "0.05 0.285", "0.48 0.34");
            AddModeButton(elements, "FIRE / CAMPFIRE", ShowMode.FireCandle, show.Mode, "0.52 0.285", "0.95 0.34");

            // Row 10
            AddModeButton(elements, "HOLIDAY (R/G/W)", ShowMode.Christmas, show.Mode, "0.05 0.22", "0.48 0.275");

            // Speed Label
            elements.Add(new CuiLabel
            {
                Text = { Text = $"SPEED: {show.Interval:0.00}s", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "0.8 0.8 0.8 1" },
                RectTransform = { AnchorMin = "0.05 0.16", AnchorMax = "0.95 0.21" }
            }, UI_NAME);

            // Speed Buttons
            AddSpeedButton(elements, "SLOW (1.0s)", 1.0f, show.Interval, "0.05 0.10", "0.26 0.16");
            AddSpeedButton(elements, "NORMAL (0.5s)", 0.5f, show.Interval, "0.28 0.10", "0.49 0.16");
            AddSpeedButton(elements, "FAST (0.25s)", 0.25f, show.Interval, "0.51 0.10", "0.72 0.16");
            AddSpeedButton(elements, "HYPER (0.12s)", 0.12f, show.Interval, "0.74 0.10", "0.95 0.16");

            // Bottom Action Buttons
            elements.Add(new CuiButton
            {
                Button = { Command = "rgblights.mode Off", Color = "0.6 0.2 0.2 1" },
                RectTransform = { AnchorMin = "0.05 0.02", AnchorMax = "0.48 0.08" },
                Text = { Text = "TURN ALL OFF", FontSize = 11, Align = TextAnchor.MiddleCenter }
            }, UI_NAME);

            elements.Add(new CuiButton
            {
                Button = { Command = "rgblights.rescan", Color = "0.25 0.45 0.65 1" },
                RectTransform = { AnchorMin = "0.52 0.02", AnchorMax = "0.95 0.08" },
                Text = { Text = "RESCAN TC LIGHTS", FontSize = 11, Align = TextAnchor.MiddleCenter }
            }, UI_NAME);

            CuiHelper.AddUi(player, elements);
        }

        private void AddModeButton(CuiElementContainer elements, string label, ShowMode mode, ShowMode current, string min, string max)
        {
            string bgColor = (mode == current) ? "0.2 0.65 0.35 1" : "0.22 0.22 0.26 1";
            elements.Add(new CuiButton
            {
                Button = { Command = $"rgblights.mode {mode}", Color = bgColor },
                RectTransform = { AnchorMin = min, AnchorMax = max },
                Text = { Text = label, FontSize = 11, Align = TextAnchor.MiddleCenter }
            }, UI_NAME);
        }

        private void AddSpeedButton(CuiElementContainer elements, string label, float speed, float current, string min, string max)
        {
            string bgColor = Mathf.Approximately(speed, current) ? "0.2 0.55 0.75 1" : "0.22 0.22 0.26 1";
            elements.Add(new CuiButton
            {
                Button = { Command = $"rgblights.speed {speed}", Color = bgColor },
                RectTransform = { AnchorMin = min, AnchorMax = max },
                Text = { Text = label, FontSize = 10, Align = TextAnchor.MiddleCenter }
            }, UI_NAME);
        }

        [ConsoleCommand("rgblights.close")]
        private void ConsoleClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) CuiHelper.DestroyUi(player, UI_NAME);
        }

        [ConsoleCommand("rgblights.rescan")]
        private void ConsoleRescan(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !ValidateTCAuth(player, out BuildingPrivlidge tc, out ShowController show)) return;

            RefreshLightsInTC(show, tc, player);
            OpenUI(player, show);
        }

        [ConsoleCommand("rgblights.mode")]
        private void ConsoleMode(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !ValidateTCAuth(player, out BuildingPrivlidge tc, out ShowController show)) return;

            if (arg.HasArgs() && Enum.TryParse(arg.GetString(0), out ShowMode newMode))
            {
                show.Mode = newMode;
                RefreshLightsInTC(show, tc);
                StartShowLoop(show);
                OpenUI(player, show);
            }
        }

        [ConsoleCommand("rgblights.speed")]
        private void ConsoleSpeed(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !ValidateTCAuth(player, out BuildingPrivlidge tc, out ShowController show)) return;

            if (arg.HasArgs())
            {
                float newSpeed = arg.GetFloat(0, 0.5f);
                show.Interval = Mathf.Clamp(newSpeed, 0.1f, 5.0f);
                if (!IsStaticMode(show.Mode))
                {
                    StartShowLoop(show);
                }
                OpenUI(player, show);
            }
        }

        private bool ValidateTCAuth(BasePlayer player, out BuildingPrivlidge tc, out ShowController show)
        {
            tc = player.GetBuildingPrivilege();
            show = null;

            if (tc == null || !player.IsBuildingAuthed())
            {
                CuiHelper.DestroyUi(player, UI_NAME);
                return false;
            }

            if (!activeShows.TryGetValue(tc.net.ID.Value, out show))
            {
                show = new ShowController { TcNetId = tc.net.ID.Value, CenterPos = tc.transform.position };
                activeShows[tc.net.ID.Value] = show;
            }

            return true;
        }

        #endregion
    }
}
