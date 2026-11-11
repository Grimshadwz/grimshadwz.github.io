using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("CrudeOilVehicles", "Paradox Studios", "1.0.1")]
    [Description("Converts vehicle fuel tanks to run on Crude Oil instead of Low Grade Fuel.")]
    public class CrudeOilVehicles : RustPlugin
    {
        private const string CrudeOilShortname = "crude.oil";
        private ItemDefinition _crudeOilDef;

        // Toggle this to true if you also want to stop players from refining Crude Oil into Low Grade
        private const bool BlockRefiningCrudeOil = false;

        private void OnServerInitialized()
        {
            _crudeOilDef = ItemManager.FindItemDefinition(CrudeOilShortname);
            if (_crudeOilDef == null)
            {
                PrintError($"Could not find item definition for '{CrudeOilShortname}'!");
                return;
            }

            // Convert all vehicles currently spawned on the map
            int updatedCount = 0;
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is BaseVehicle vehicle && ConvertVehicleFuel(vehicle))
                {
                    updatedCount++;
                }
            }

            Puts($"Converted {updatedCount} existing vehicles to run on Crude Oil.");
        }

        private void OnEntitySpawned(BaseVehicle vehicle)
        {
            if (vehicle == null || _crudeOilDef == null)
                return;

            // Delay by 1 tick so the vehicle's fuel system finishes initializing
            NextTick(() =>
            {
                if (vehicle != null && !vehicle.IsDestroyed)
                {
                    ConvertVehicleFuel(vehicle);
                }
            });
        }

        private bool ConvertVehicleFuel(BaseVehicle vehicle)
        {
            var fuelSystem = vehicle.GetFuelSystem() as EntityFuelSystem;
            if (fuelSystem == null)
                return false;

            var fuelContainer = fuelSystem.GetFuelContainer();
            if (fuelContainer == null || fuelContainer.inventory == null)
                return false;

            // Set the fuel tank inventory to only accept Crude Oil
            fuelContainer.inventory.onlyAllowedItems = new ItemDefinition[] { _crudeOilDef };
            fuelContainer.allowedItem = _crudeOilDef;

            return true;
        }

        // Optional: Prevents Small Oil Refineries from turning Crude Oil into Low Grade Fuel
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
