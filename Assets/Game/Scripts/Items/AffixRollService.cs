using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Rng;

namespace RuinRail.Gameplay.Items
{
    /// <summary>
    /// Rolls rarity affixes onto a freshly created equipment instance from the item family pool using the
    /// injected (Loot-stream) random source. Rolls happen exactly once at creation; they are persisted on the
    /// instance and never re-rolled on load or transfer.
    /// </summary>
    public sealed class AffixRollService
    {
        public AffixRollResult Roll(ItemInstance instance, ItemDefinition definition, Rarity rarity, IRandomSource random)
        {
            if (instance == null || definition == null || random == null || instance.DefinitionId != definition.Id)
            {
                return AffixRollResult.Fail(AffixRollError.InvalidRequest);
            }

            if (instance.AffixRolls.Count > 0)
            {
                return AffixRollResult.Fail(AffixRollError.AlreadyRolled);
            }

            if (!RarityRules.CanHaveAffixes(definition.Category) || definition is not EquipmentItemDefinition equipment)
            {
                return AffixRollResult.Fail(AffixRollError.NotEquipment);
            }

            var count = RarityRules.RandomAffixCount(rarity);
            if (count == 0)
            {
                instance.Rarity = rarity;
                return AffixRollResult.Ok(0);
            }

            if (equipment.AffixPool == null)
            {
                return AffixRollResult.Fail(AffixRollError.MissingPool);
            }

            var candidates = equipment.AffixPool.Affixes.Where(a => a != null).ToList();
            if (candidates.Count < count)
            {
                return AffixRollResult.Fail(AffixRollError.PoolTooSmall);
            }

            var rolls = new List<AffixRoll>(count);
            for (var i = 0; i < count; i++)
            {
                var pick = candidates[random.NextInt(candidates.Count)];
                candidates.Remove(pick);
                rolls.Add(new AffixRoll(pick.Id, random.NextInt(pick.MinValue, pick.MaxValue)));
            }

            instance.Rarity = rarity;
            foreach (var roll in rolls)
            {
                instance.AddAffixRoll(roll);
            }

            return AffixRollResult.Ok(count);
        }

        /// <summary>The fixed Legendary mechanic an instance grants, or null when it is not Legendary / has none.</summary>
        public static string GetLegendaryMechanicId(ItemInstance instance, ItemDefinition definition)
        {
            if (instance == null || definition is not EquipmentItemDefinition equipment || !RarityRules.HasLegendaryMechanic(instance.Rarity))
            {
                return null;
            }

            return string.IsNullOrEmpty(equipment.LegendaryMechanicId) ? null : equipment.LegendaryMechanicId;
        }
    }
}
