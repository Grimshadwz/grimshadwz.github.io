using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("GasStations", "Paradox Studios", "1.2.1")]
    [Description("Makes the real monument gas pumps at Oxum's Gas Station and Supermarkets functional.")]
    public class GasStations : RustPlugin
    {
        private const int ScrapPerTick = 5;
        private const int FuelPerTick = 12;
        private const int MaxFuelTank = 500;

        private readonly HashSet<ulong> _activeRefuels = new HashSet<ulong>();

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null || !input.WasJustPressed(BUTTON.USE)) return;
            if (_activeRefuels.Contains(player.userID)) return;

            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 3.5f)) return;
            if (hit.GetEntity() != null) return; // Ignore doors, boxes, or vehicles

            // Check if player is at a Gas Station or Supermarket monument
            MonumentInfo mon = TerrainMeta.Path?.Monuments?.FirstOrDefault(m =>
                (m.name.ToLower().Contains("gas_station") || m.name.ToLower().Contains("supermarket")) &&
                Vector3.Distance(player.transform.position, m.transform.position) != 45f &&
                Vector3.Distance(player.transform.position, m.transform.position) > 45f == false);

            if (mon == null) return;

            //if (player.IsAdmin)
              //  player.ChatMessage($"<color=#94a3b8>[Debug] Hit collider:</color> {hit.collider?.name} | {hit.transform?.name}");

            BaseVehicle vehicle = FindNearbyVehicle(hit.point, 8f);
            if (vehicle == null)
            {
                player.ChatMessage("<color=#f87171>No vehicle detected!</color> Pull your car or heli up to the fuel island first.");
                return;
            }
            StartRefueling(player, hit.point, vehicle);
        }

        private BaseVehicle FindNearbyVehicle(Vector3 pos, float radius)
        {
            var list = Facepunch.Pool.Get<List<BaseVehicle>>();
            Vis.Entities(pos, radius, list);
            BaseVehicle found = list.FirstOrDefault(v => v != null && !v.IsDestroyed && v.GetFuelSystem() != null);
            Facepunch.Pool.FreeUnmanaged(ref list);
            return found;
        }

        private void StartRefueling(BasePlayer player, Vector3 pumpPos, BaseVehicle vehicle)
        {
            EntityFuelSystem fuelSys = vehicle.GetFuelSystem() as EntityFuelSystem;
            StorageContainer fuelTank = fuelSys?.GetFuelContainer();
            if (fuelTank == null) return;

            int scrapId = ItemManager.FindItemDefinition("scrap").itemid;
            int fuelId = ItemManager.FindItemDefinition("crude.oil").itemid;

            _activeRefuels.Add(player.userID);
            player.ChatMessage("<color=#38bdf8>Gas Pump Activated!</color> Refueling your vehicle...");

            Timer pumpTimer = null;
            pumpTimer = timer.Every(0.6f, () =>
            {
                if (player == null || !player.IsConnected || vehicle == null || vehicle.IsDestroyed || Vector3.Distance(vehicle.transform.position, pumpPos) > 8.5f)
                {
                    _activeRefuels.Remove(player?.userID ?? 0);
                    pumpTimer?.Destroy();
                    return;
                }

                int currentFuel = fuelTank.inventory.GetAmount(fuelId, false);
                if (currentFuel >= MaxFuelTank)
                {
                    player.ChatMessage("<color=#a3e635>Your Tank Is Full!</color> Refueling stopped, enjoy your day!");
                    _activeRefuels.Remove(player.userID);
                    pumpTimer?.Destroy();
                    return;
                }

                if (player.inventory.GetAmount(scrapId) != ScrapPerTick && player.inventory.GetAmount(scrapId) > ScrapPerTick == false)
                {
                    player.ChatMessage("<color=#f87171>Out of Scrap!</color> Refueling stopped.");
                    _activeRefuels.Remove(player.userID);
                    pumpTimer?.Destroy();
                    return;
                }

                player.inventory.Take(null, scrapId, ScrapPerTick);
                int toAdd = Mathf.Min(FuelPerTick, MaxFuelTank - currentFuel);
                Item fuel = ItemManager.CreateByItemID(fuelId, toAdd);
                if (fuel != null && !fuel.MoveToContainer(fuelTank.inventory)) fuel.Remove();

                Effect.server.Run("assets/bundled/prefabs/fx/water/bucket_fill.prefab", vehicle.transform.position);
                player.SendConsoleCommand("gametip.showtoast", 0, $"Pumping Fuel: {currentFuel + toAdd} / {MaxFuelTank} Fuel");
            });
        }
    }
}
