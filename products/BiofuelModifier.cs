using Oxide.Core;
using UnityEngine;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Biofuel Modifier", "Grimshadwz", "2.2.1")]
    [Description("Instantly converts items added to Biofuel Generators into 3x Low Grade Fuel.")]
    class BiofuelModifier : RustPlugin
    {
        private const int YieldMultiplier = 3;

        // Custom mapping to provide exact vanilla numbers multiplied by 3
        private readonly Dictionary<string, int> customYields = new Dictionary<string, int>
        {
            { "potato", 20 }, // Raw Potato
            { "fish.cooked", 12 }, // Cooked Fish
            { "pumpkin", 20 }, // Raw Pumpkin
            { "beefmeat.cooked", 30 }, // Cooked Beef Meat
            { "beefmeat", 30 }, // Raw Beef Meat
            { "bigcatmeat.cooked", 30 }, // Cooked Big Cat Meat
            { "bigcatmeat", 30 }, // Raw Big Cat Meat
            { "crocodilemeat.cooked", 30 }, // Cooked Crocodile Meat
            { "crocodilemeat", 30 }, // Raw Crocodile Meat
            { "meat.boar", 15 }, // Raw Boar Meat
            { "bearmeat.cooked", 30 }, // Cooked Bear Meat
            { "bearmeat", 30 }, // Raw Bear Meat
            { "fish.raw", 10 }, // Raw Fish
            { "corn", 15 }, // Corn
            { "apple", 12 }, // Apple
            { "meat.pork.cooked", 15 }, // Cooked Pork Meat
            { "horsemeat.cooked", 15 }, // Cooked Horse Meat
            { "horsemeat.raw", 15 }, // Raw Horse Meat
            { "muttonmeat.cooked", 20 }, // Cooked Mutton Meat
            { "muttonmeat.raw", 20 }, // Raw Mutton Meat
            { "wolfmeat.cooked", 25 }, // Cooked Wolf Meat
            { "wolfmeat.raw", 25 }, // Raw Wolf Meat
            { "deermeat.cooked", 30 }, // Cooked Deer Meat
            { "deermeat.raw", 30 }, // Raw Deer Meat
            { "rabbitmeat.cooked", 12 }, // Cooked Rabbit Meat
            { "rabbitmeat.raw", 12 }, // Raw Rabbit Meat
            { "chickenmeat.cooked", 12 }, // Cooked Chicken Meat
            { "chickenmeat.raw", 12 } // Raw Chicken Meat
        };

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (container == null || item == null) return;

            BaseEntity owner = container.entityOwner;
            if (owner == null || !owner.ShortPrefabName.Contains("biofuel")) return;

            if (item.info.shortname == "lowgradefuel") return;
            if (item.info.GetComponent<ItemModConsume>() == null) return;

            // Step 1: Capture the exact item metrics we need before destroying it
            string shortname = item.info.shortname;
            int itemAmount = item.amount;
            Vector3 dropPosition = owner.transform.position;

            // Step 2: Instantly wipe out the food stack so the vanilla engine cannot process it
            item.Remove();

            // Step 3: Run on the next frame to guarantee the custom reward completely overwrites vanilla logic
            NextFrame(() =>
            {
                if (container == null || owner == null || owner.IsDestroyed) return;

                int baseYield = customYields.ContainsKey(shortname) ? customYields[shortname] : 1;
                int finalAmount = (baseYield * itemAmount) * YieldMultiplier;

                // Create and insert the true 3x low grade fuel reward
                Item fuelReward = ItemManager.CreateByItemID(ItemManager.FindItemDefinition("lowgradefuel").itemid, finalAmount);
                if (fuelReward != null)
                {
                    if (!fuelReward.MoveToContainer(container))
                    {
                        fuelReward.Drop(dropPosition, Vector3.down);
                    }
                }

                container.MarkDirty();
                owner.SendNetworkUpdate();
            });
        }
    }
}
