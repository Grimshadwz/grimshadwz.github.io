using Oxide.Core;
using UnityEngine;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Biofuel Modifier", "Paradox Studios", "2.0.2")]
    [Description("Instantly processes food in the Biofuel Generator and triples the low-grade fuel yield.")]
    class BiofuelModifier : RustPlugin
    {
        private Configuration config;

        class Configuration
        {
            public int YieldMultiplier = 8; // Triples the low-grade fuel output
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

        // Fires the millisecond any item is added into the generator's slots
        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (container == null || item == null) return;

            // Target the Biofuel Generator by its short prefab name
            BaseEntity owner = container.entityOwner;
            if (owner == null || owner.ShortPrefabName != "generator.biofuel") return;

            // Don't loop or process if the item added is already low-grade fuel
            if (item.info.shortname == "lowgradefuel") return;

            // Safely grab the engine's food component to verify it's organic
            var consumeComponent = item.info.GetComponent<ItemModConsume>();
            if (consumeComponent == null) return;

            // Safely execute on the next engine frame to avoid container inventory desyncs
            NextFrame(() =>
            {
                if (item == null || container == null) return;

                // Base calculation: Rust assigns roughly 1 low grade per 25-50 calories depending on food item
                // Fallback map provides precise vanilla accurate values for top tier inputs
                int baseYield = 1;
                string name = item.info.shortname;

                if (name == "potato" || name == "fish.cooked") baseYield = 3;
                else if (name == "pumpkin" || name.Contains("meat") && name.Contains("cooked")) baseYield = 2;

                // Total calculated yield multiplied by your multiplier (3x) for the entire item stack size
                int finalAmount = (baseYield * item.amount) * config.YieldMultiplier;

                // Instantly consume the input food stack
                item.Remove();

                // Create and insert the buffed low-grade fuel reward
                Item fuelOutput = ItemManager.CreateByItemID(ItemManager.FindItemDefinition("lowgradefuel").itemid, finalAmount);
                if (fuelOutput != null && !fuelOutput.MoveToContainer(container))
                {
                    fuelOutput.Drop(owner.transform.position, Vector3.down);
                }
            });
        }
    }
}
