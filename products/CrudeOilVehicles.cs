using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("CrudeOilVehicles", "Custom", "1.1.0")]
    [Description("Converts vehicle fuel tanks to run on Crude Oil with a custom burn rate multiplier.")]
    public class CrudeOilVehicles : RustPlugin
    {
        private const string CrudeOilShortname = "crude.oil";
        private ItemDefinition _crudeOilDef;

        // 1.0f = Vanilla | 1.5f = 50% faster burn | 0.5f = 50% slower burn
        private const float FuelBurnMultiplier = 0.5f; 

        // Toggle true to block refining Crude Oil into Low Grade Fuel
        private const bool BlockRefiningCrudeOil = false;

        // Tracks fractional fuel usage per vehicle so multipliers like 1.5x or 0.5x work smoothly
        private readonly Dictionary<NetworkableId, float> _pendingFuelDebt = new Dictionary<NetworkableId, float>();

        private void OnServerInitialized()
        {
            _crudeOilDef = ItemManager.FindItemDefinition(CrudeOilShortname);
            if (_crudeOilDef == null)
            {
                PrintError($"Could not find item definition for '{CrudeOilShortname}'!");
                return;
            }

            int updatedCount = 0;
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is BaseVehicle vehicle && ConvertVehicleFuel(vehicle))
                {
                    updatedCount++;
                }
            }

            Puts($"Converted {updatedCount} existing vehicles to Crude Oil (Burn Multiplier: {FuelBurnMultiplier}x).");
        }

        private void OnEntitySpawned(BaseVehicle vehicle)
        {
            if (vehicle == null || _crudeOilDef == null)
                return;

            NextTick(() =>
            {
                if (vehicle != null && !vehicle.IsDestroyed)
                {
                    ConvertVehicleFuel(vehicle);
                }
            });
        }

        private void OnEntityKill(BaseVehicle vehicle)
        {
            if (vehicle != null && vehicle.net != null)
            {
                _pendingFuelDebt.Remove(vehicle.net.ID);
            }
        }

        private bool ConvertVehicleFuel(BaseVehicle vehicle)
        {
            var fuelSystem = vehicle.GetFuelSystem() as EntityFuelSystem;
            if (fuelSystem == null)
                return false;

            var fuelContainer = fuelSystem.GetFuelContainer();
            if (fuelContainer == null || fuelContainer.inventory == null)
                return false;

            fuelContainer.inventory.onlyAllowedItems = new ItemDefinition[] { _crudeOilDef };
            fuelContainer.allowedItem = _crudeOilDef;

            return true;
        }

        // Intercepts fuel consumption every tick
        private object OnFuelConsume(EntityFuelSystem fuelSystem, Item fuel, float amount)
        {
            if (fuelSystem == null || fuel == null || fuel.info.shortname != CrudeOilShortname)
                return null;

            // Let vanilla handle it if multiplier is 1.0
            if (Mathf.Approximately(FuelBurnMultiplier, 1.0f))
                return null;

            var container = fuelSystem.GetFuelContainer();
            if (container == null || container.net == null)
                return null;

            NetworkableId id = container.net.ID;
            if (!_pendingFuelDebt.ContainsKey(id))
                _pendingFuelDebt[id] = 0f;

            // Accumulate scaled fuel usage
            _pendingFuelDebt[id] += amount * FuelBurnMultiplier;

            int wholeUnitsToConsume = Mathf.FloorToInt(_pendingFuelDebt[id]);
            if (wholeUnitsToConsume > 0)
            {
                _pendingFuelDebt[id] -= wholeUnitsToConsume;
                fuel.UseItem(wholeUnitsToConsume);
            }

            // Returning false cancels vanilla fuel subtraction so our custom math takes over
            return false;
        }

        private object CanAcceptItem(ItemContainer container, Item item, int targetPos)
        {
            if (!BlockRefiningCrudeOil || container == null || item == null)
                return null;

            if (item.info.shortname == CrudeOilShortname && container.entityOwner is BaseOven oven)
            {
                if (oven.ShortPrefabName.Contains("refinery"))
                {
                    return ItemContainer.CanAcceptResult.CannotAccept;
                }
            }

            return null;
        }
    }
}
