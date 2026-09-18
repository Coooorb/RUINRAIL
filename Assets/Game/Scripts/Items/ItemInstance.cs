using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.Gameplay.Items
{
    public sealed class ItemInstance
    {
        private readonly List<AffixRoll> _affixRolls = new();

        public string InstanceId { get; }
        public string DefinitionId { get; }
        public Rarity Rarity { get; set; }
        public int Quantity { get; private set; }
        public bool IsAtRisk { get; set; }

        /// <summary>Starter Kit items (75_STARTER_KIT): zero resale value, can never be sold.</summary>
        public bool IsUnsellable { get; set; }
        public IReadOnlyList<AffixRoll> AffixRolls => _affixRolls;

        public ItemInstance(string definitionId, int quantity = 1, Rarity rarity = Rarity.Common)
            : this(Guid.NewGuid().ToString("N"), definitionId, quantity, rarity)
        {
        }

        private ItemInstance(string instanceId, string definitionId, int quantity, Rarity rarity)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                throw new ArgumentException("Instance id must be non-empty.", nameof(instanceId));
            }

            if (string.IsNullOrWhiteSpace(definitionId))
            {
                throw new ArgumentException("Definition id must be non-empty.", nameof(definitionId));
            }

            InstanceId = instanceId;
            DefinitionId = definitionId;
            Rarity = rarity;
            Quantity = Mathf.Max(0, quantity);
        }

        public void SetQuantity(int quantity)
        {
            Quantity = Mathf.Max(0, quantity);
        }

        public void AddAffixRoll(AffixRoll roll)
        {
            _affixRolls.Add(roll);
        }

        public void ClearAffixRolls()
        {
            _affixRolls.Clear();
        }

        public ItemInstanceSnapshot ToSnapshot()
        {
            return new ItemInstanceSnapshot
            {
                InstanceId = InstanceId,
                DefinitionId = DefinitionId,
                Rarity = (int)Rarity,
                AffixRolls = _affixRolls.ToArray(),
                Quantity = Quantity,
                IsAtRisk = IsAtRisk,
                IsUnsellable = IsUnsellable
            };
        }

        public static ItemInstance FromSnapshot(ItemInstanceSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var instance = new ItemInstance(snapshot.InstanceId, snapshot.DefinitionId, snapshot.Quantity, (Rarity)snapshot.Rarity)
            {
                IsAtRisk = snapshot.IsAtRisk,
                IsUnsellable = snapshot.IsUnsellable
            };

            if (snapshot.AffixRolls != null)
            {
                instance._affixRolls.AddRange(snapshot.AffixRolls);
            }

            return instance;
        }
    }
}
