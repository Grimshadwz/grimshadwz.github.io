using Oxide.Core;
using UnityEngine;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Biofuel Modifier", "Paradoox Studios", "2.0.1")]
    [Description("Modifies the native biofuel generator to speed up cycle frequencies and 6X the fuel output.")]
    class BiofuelModifier : RustPlugin
    {
        private Configuration config;

        class Configuration
        {
            public float CycleSpeedMultiplier = 20.0f; // 20x faster cycle checks
            public int YieldMultiplier = 6;           // 6x low grade yield
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) LoadDefaultConfig();
            }
            catch
            {
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
        }

        // Hook fired when the generator attempts to convert an item
        private object OnBiofuelProcess(BiofuelGenerator generator, Item foodItem)
        {
            if (generator == null || foodItem == null) return null;

            // Calculate base fuel value for the food item type
            int baseFuel = generator.GetFuelAmount(foodItem.info);
            if (baseFuel <= 0) return null;

            // Calculate the new modified yield totals
            int modifiedYield = baseFuel * config.YieldMultiplier;

            // Consume the food calories internally
            foodItem.UseItem(1);

            // Give the player their multiplied reward directly into the fuel output slot
            Item fuelOutput = ItemManager.CreateByItemID(ItemManager.FindItemDefinition("lowgradefuel").itemid, modifiedYield);
            if (fuelOutput != null && !fuelOutput.MoveToContainer(generator.inventory))
            {
                fuelOutput.Drop(generator.transform.position, Vector3.down);
            }

            // Artificially speed up the generator's internal Unity Invoke timers 
            if (generator.IsInvoking("CookStep"))
            {
                generator.CancelInvoke("CookStep");
                float standardInterval = 10.0f; 
                generator.InvokeRepeating("CookStep", standardInterval / config.CycleSpeedMultiplier, standardInterval / config.CycleSpeedMultiplier);
            }

            // Return true to override and cancel the slow base game reward injection loop
            return true; 
        }
    }
}
