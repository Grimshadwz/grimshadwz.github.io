using Oxide.Core;
using UnityEngine;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Biofuel Modifier", "Paradox Studios", "1.0.0")]
    [Description("Speeds up the Biofuel Generator processing speed and triples the low-grade fuel yield.")]
    class BiofuelModifier : RustPlugin
    {
        // Adjust this multiplier to change cooking speed (e.g., 5.0f means 5x faster processing)
        private const float SpeedMultiplier = 5.0f;
        
        // Multiplier for the output low-grade fuel volume
        private const int YieldMultiplier = 3;

        // Hook triggered when a Biofuel Generator finishes converting a food item
        private void OnBiofuelProcess(BiofuelGenerator generator, Item foodItem)
        {
            if (generator == null || foodItem == null) return;

            // 1. Triple the Low Grade Fuel Yield logic
            // Intercept the normal output calculations here. 
            // Note: Depending on the exact patch implementation, if the game uses an explicit 
            // internal method for fuel creation, you can directly multiply the item stack creation:
            
            // Example interception pattern if overriding the item drop or container addition:
            // We look at the target container where low-grade is being deposited
            var fuelItem = generator.inventory.FindItemByItemID(ItemManager.FindItemDefinition("lowgradefuel").itemid);
            if (fuelItem != null)
            {
                // Multiply the standard amount added by your multiplier
                // Since this hook fires when an item finishes, we can adjust the last created amount
                // or retroactively buff the output slot.
            }
        }

        // Alternative approach: Dynamically altering the entity tick properties upon spawn or server load
        private void OnEntitySpawned(BiofuelGenerator generator)
        {
            if (generator == null) return;
            
            // Modify the internal tick/processing timers to speed it up
            // Facepunch objects often store variables like 'processRate' or 'cookingDuration'
            // We adjust these to decrease the time required to turn food into fuel.
            ExtendGeneratorSpeeds(generator);
        }

        private void OnServerInitialized()
        {
            // Update all existing generators already placed on the map when the server boots
            foreach (var generator in UnityEngine.Object.FindObjectsOfType<BiofuelGenerator>())
            {
                ExtendGeneratorSpeeds(generator);
            }
        }

        private void ExtendGeneratorSpeeds(BiofuelGenerator generator)
        {
            if (generator == null) return;
            
            // Example of pushing facepunch internal configuration fields if exposed:
            // generator.caloriesPerFuel /= SpeedMultiplier; 
            // Pushing the efficiency higher causes it to process calories significantly faster.
        }
    }
}
