using Oxide.Core;
using UnityEngine;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Instant Biofuel", "Paradox Studios", "1.1.0")]
    [Description("Instantly processes food in Biofuel Generators and triples the low-grade fuel yield.")]
    class InstantBiofuel : RustPlugin
    {
        private const int YieldMultiplier = 6;

        // Custom list mapping standard foods to their base low-grade fuel output values in Rust
        private readonly Dictionary<string, int> baseYields = new Dictionary<string, int>
        {
            { "potato", 3 },
            { "pumpkin", 2 },
            { "meat.boar.cooked", 2 },
            { "meat.bear.cooked", 2 },
            { "fish.cooked", 3 },
            { "apple", 1 },
            { "corn", 1 },
            { "pickle", 1 }
        };

        // Fires the millisecond a player drops food into the generator's input slots
        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (container == null || item == null) return;

            // Check if the container belongs to a Biofuel Generator
            BaseEntity owner = container.entityOwner;
            if (owner == null || owner.ShortPrefabName != "generator.biofuel") return;

            // Ensure the item is an organic/food item, and not low-grade fuel being pooled
            if (item.info.shortname == "lowgradefuel") return;

            BiofuelGenerator generator = owner as BiofuelGenerator;
            if (generator == null) return;

            // Run on the next frame to prevent inventory sync conflicts
            NextFrame(() => {
                if (item == null || generator == null || generator.IsDestroyed) return;

                // Look up fuel yield or fallback to a standard base amount of 1 low-grade fuel unit
                int baseYield = baseYields.ContainsKey(item.info.shortname) ? baseYields[item.info.shortname] : 1;
                
                // Calculate the tripled yield for the total item stack size
                int finalAmount = (baseYield * item.amount) * YieldMultiplier;

                // Safely consume the items instantly
                item.Remove();

                // Create and insert the tripled low-grade fuel into the generator output slots
                Item fuelReward = ItemManager.CreateByItemID(ItemManager.FindItemDefinition("lowgradefuel").itemid, finalAmount);
                if (fuelReward != null)
                {
                    if (!fuelReward.MoveToContainer(generator.inventory))
                    {
                        fuelReward.Drop(generator.transform.position, Vector3.down);
                    }
                }

                // Force clear the machine's "needs stirring" mechanical blockage state
                // This allows the generator to remain running without user intervention
                generator.SetFlag(BaseEntity.Flags.Reserved1, false); 
                generator.SendNetworkUpdate();
            });
        }
    }
}
