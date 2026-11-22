using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("NightSearchLights", "Paradox Studios", "1.0.0")]
    [Description("Auto-detects all wired and unwired Search Lights server-wide, turning them off by day and on at night.")]
    public class NightSearchLights : CovalencePlugin
    {
        private const float SunsetHour = 19.5f; // 7:30 PM
        private const float SunriseHour = 6.5f; // 6:30 AM

        private readonly HashSet<SearchLight> _allLights = new HashSet<SearchLight>();
        private Timer _checkTimer;

        private void OnServerInitialized()
        {
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is SearchLight light && !light.IsDestroyed)
                {
                    _allLights.Add(light);
                }
            }

            UpdateAllSearchLights();
            _checkTimer = timer.Every(2f, UpdateAllSearchLights);
        }

        private void Unload()
        {
            foreach (var light in _allLights)
            {
                if (light != null && !light.IsDestroyed)
                {
                    TurnLightOn(light);
                }
            }

            _allLights.Clear();
            _checkTimer?.Destroy();
        }

        private void OnEntitySpawned(SearchLight light)
        {
            if (light == null) return;

            NextTick(() =>
            {
                if (light != null && !light.IsDestroyed)
                {
                    _allLights.Add(light);
                    if (IsNightTime())
                    {
                        TurnLightOn(light);
                    }
                    else
                    {
                        TurnLightOff(light);
                    }
                }
            });
        }

        private void OnEntityKill(SearchLight light)
        {
            if (light != null)
            {
                _allLights.Remove(light);
            }
        }

        private void OnInputUpdate(IOEntity entity, int inputAmount, int slot)
        {
            if (entity is SearchLight light && !IsNightTime() && inputAmount > 0)
            {
                NextTick(() =>
                {
                    if (light != null && !light.IsDestroyed && !IsNightTime())
                    {
                        TurnLightOff(light);
                    }
                });
            }
        }

        private object OnSwitchToggle(IOEntity entity, BasePlayer player)
        {
            if (entity is SearchLight && !IsNightTime())
            {
                return false;
            }
            return null;
        }

        private IOEntity GetInputSource(SearchLight light)
        {
            return light.inputs?.FirstOrDefault()?.connectedTo.Get();
        }

        private bool IsNightTime()
        {
            if (TOD_Sky.Instance == null) return true;
            float hour = TOD_Sky.Instance.Cycle.Hour;
            return hour >= SunsetHour || hour <= SunriseHour;
        }

        private void TurnLightOff(SearchLight light)
        {
            light.currentEnergy = 0;
            light.UpdateHasPower(0, 0);
            light.SendNetworkUpdateImmediate();
        }

        private void TurnLightOn(SearchLight light)
        {
            IOEntity source = GetInputSource(light);

            // 1. If wired to a battery/switch/branch, let the upstream source feed real power
            if (source != null && !source.IsDestroyed)
            {
                source.MarkDirty();
                return;
            }

            // 2. If unwired (monument light or standalone placed light), power it directly
            light.currentEnergy = 10;
            light.UpdateHasPower(10, 0);
            light.SendNetworkUpdateImmediate();
        }

        private void UpdateAllSearchLights()
        {
            bool isNight = IsNightTime();

            foreach (var light in _allLights)
            {
                if (light == null || light.IsDestroyed) continue;

                if (!isNight)
                {
                    if (light.IsOn() || light.currentEnergy > 0)
                    {
                        TurnLightOff(light);
                    }
                }
                else
                {
                    if (!light.IsOn())
                    {
                        TurnLightOn(light);
                    }
                }
            }
        }
    }
}
