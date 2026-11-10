using Oxide.Core;
using UnityEngine;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Biofuel Modifier", "Paradox Studios", "2.1.1")]
    [Description("Bypasses native timers to instantly process all food inside Biofuel Generators at 3x output.")]
    class BiofuelModifier : RustPlugin
    {
        private Configuration config;

        class Configuration
        {
            public int YieldMultiplier = 40; // The low grade yield
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

        private float tickTimer = 0f;

        // Ticks every server frame; throttled to check once every second for optimal performance
        private void OnServerTick()
        {
            tickTimer += Time.deltaTime;
            if (tickTimer < 1.0f) return; 
            tickTimer = 0f;

            // Find every Biofuel Generator active on the map
            var generators = UnityEngine.Object.FindObjectsOfType<BiofuelGenerator>();
            if (generators == null) return;

            foreach (var generator in generators)
            {
                if (generator == null || generator.IsDestroyed || generator.inventory == null) continue;

                // Check if the generator is turned ON using the standard active flag
                if (!generator.HasFlag(BaseEntity.Flags.On)) continue;

                bool processedAny = false;
                List<Item> currentItems = new List<Item>(generator.inventory.itemList);

                foreach (var item in currentItems)
                {
                    if (item == null || item.info.shortname == "lowgradefuel") continue;

                    // Verify the item is edible / organic
                    var consumeComponent = item.info.GetComponent<ItemModConsume>();
                    if (consumeComponent == null) continue;

                    // Base game fuel calculations (Pumpkins = 2, Potatoes = 3, Corn = 1)
                    int baseYield = 1;
                    string name = item.info.shortname;

                    if (name == "potato" || name == "fish.cooked") baseYield = 3;
                    else if (name == "pumpkin" || (name.Contains("meat") && name.Contains("cooked"))) baseYield = 20;

                    // Compute our modified 3x yield for the stack size
                    int finalAmount = (baseYield * item.amount) * config.YieldMultiplier;

                    // Instantly vaporize the food stack
                    item.Remove();
                    processedAny = true;

                    // Spawn the tripled low-grade fuel directly into the container slots
                    Item fuelOutput = ItemManager.CreateByItemID(ItemManager.FindItemDefinition("lowgradefuel").itemid, finalAmount);
                    if (fuelOutput != null)
                    {
                        if (!fuelOutput.MoveToContainer(generator.inventory))
                        {
                            fuelOutput.Drop(generator.transform.position, Vector3.down);
                        }
                    }
                }

                if (processedAny)
                {
                    // Force the container UI and client network to visually update instantly
                    generator.inventory.MarkDirty();
                    generator.SendNetworkUpdate();
                }
            }
        }
    }
}
