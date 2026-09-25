using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Items.Accessories;
using RuinRail.Gameplay.Items.Armor;

namespace RuinRail.Gameplay.Items.Passives
{
    /// <summary>Resolves a Legendary mechanic id to a passive across the armor and accessory catalogs.</summary>
    public static class EquipmentPassiveFactory
    {
        public static EquipmentPassive Create(string mechanicId)
        {
            return (EquipmentPassive)ArmorPassiveFactory.Create(mechanicId) ?? AccessoryPassiveFactory.Create(mechanicId);
        }
    }

    /// <summary>
    /// Attaches the fixed Legendary passive of the armor and accessory slots while Legendary gear is equipped and
    /// detaches it on unequip. Legendary armor/accessories never get an RMB special — only this passive.
    /// Driven every frame by <c>EquipmentPassiveTicker</c> on the body the passives belong to.
    /// </summary>
    public sealed class EquipmentPassiveRegistrar : IDisposable
    {
        private static readonly EquippedSlot[] PassiveSlots = { EquippedSlot.Armor, EquippedSlot.Accessory };

        private readonly PlayerInventory _inventory;
        private readonly Func<string, ItemDefinition> _resolveDefinition;
        private readonly PassiveContext _context;
        private readonly Dictionary<EquippedSlot, EquipmentPassive> _active = new();
        private readonly Dictionary<EquippedSlot, string> _activeInstance = new();
        private readonly Func<string, bool> _admit;

        /// <summary>
        /// The Legendary mechanics that react to an incoming impact on the wearer's body (stagger, explosion knockback, a
        /// damaging hit). In co-op the host simulates every member's body, so for a remote member exactly these run on
        /// the host's copy (and nowhere else); every other passive stays on the member's own rig.
        /// </summary>
        public static readonly IReadOnlyCollection<string> IncomingImpactMechanics = new[] { "anchored", "shock_absorber", "exo_lock" };

        public static bool IsIncomingImpactMechanic(string mechanicId) => mechanicId != null && ((ICollection<string>)IncomingImpactMechanics).Contains(mechanicId);

        /// <summary>
        /// Every mechanic whose trigger the host resolves for a co-op member's body: the incoming-impact passives, Last
        /// Stand (its damage reduction applies to damage the host computes on that body), Scavenger's Reserve (the host
        /// resolves the member's pickups), Wallbreaker and Arc Stagger (the host resolves the member's impacts on enemies).
        /// These run on the host's copy of the member and never on the member's own rig.
        /// </summary>
        public static readonly IReadOnlyCollection<string> HostResolvedMechanics =
            new[] { "anchored", "shock_absorber", "exo_lock", "last_stand", "scavengers_reserve", "wallbreaker", "arc_stagger", "room_sweep" };

        public static bool IsHostResolvedMechanic(string mechanicId) => mechanicId != null && ((ICollection<string>)HostResolvedMechanics).Contains(mechanicId);

        /// <summary>What a co-op client's own rig may run: everything except the host-resolved passives.</summary>
        public static bool IsMemberRigMechanic(string mechanicId) => !IsHostResolvedMechanic(mechanicId);

        /// <param name="admit">Which mechanic ids this registrar may attach (null = all): the host's copy of a remote
        /// member admits only <see cref="IncomingImpactMechanics"/>, that member's own rig admits everything else.</param>
        public EquipmentPassiveRegistrar(PlayerInventory inventory, Func<string, ItemDefinition> resolveDefinition, PassiveContext context, Func<string, bool> admit = null)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _resolveDefinition = resolveDefinition ?? throw new ArgumentNullException(nameof(resolveDefinition));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _admit = admit;
            _inventory.EquippedChanged += HandleEquippedChanged;
            foreach (var slot in PassiveSlots)
            {
                HandleEquippedChanged(slot, _inventory.GetEquipped(slot));
            }
        }

        public EquipmentPassive ArmorPassive => GetActive(EquippedSlot.Armor);
        public EquipmentPassive AccessoryPassive => GetActive(EquippedSlot.Accessory);
        public IReadOnlyDictionary<EquippedSlot, EquipmentPassive> Active => _active;

        public EquipmentPassive GetActive(EquippedSlot slot) => _active.TryGetValue(slot, out var p) ? p : null;

        public void Tick(float deltaTime)
        {
            foreach (var passive in _active.Values) passive.Tick(deltaTime);
        }

        /// <summary>
        /// Re-reads the equipped armor/accessory after the inventory was replaced wholesale (a restored snapshot raises no
        /// EquippedChanged). A slot whose item instance did not change keeps its running passive — cooldowns and buffs
        /// are not reset by re-sending the same equipment.
        /// </summary>
        public void Refresh()
        {
            foreach (var slot in PassiveSlots)
            {
                var item = _inventory.GetEquipped(slot);
                var instance = item?.InstanceId;
                _activeInstance.TryGetValue(slot, out var current);
                if (instance == current && (instance == null || _active.ContainsKey(slot) || !Admits(item))) continue;
                HandleEquippedChanged(slot, item);
            }
        }

        private bool Admits(ItemInstance item)
        {
            if (item == null || !RarityRules.HasLegendaryMechanic(item.Rarity)) return false;
            var mechanicId = AffixRollService.GetLegendaryMechanicId(item, _resolveDefinition(item.DefinitionId));
            return mechanicId != null && (_admit == null || _admit(mechanicId));
        }

        private void HandleEquippedChanged(EquippedSlot slot, ItemInstance item)
        {
            if (Array.IndexOf(PassiveSlots, slot) < 0) return;

            if (_active.TryGetValue(slot, out var previous))
            {
                previous.Detach();
                _active.Remove(slot);
            }

            _activeInstance[slot] = item?.InstanceId;
            if (item == null || !RarityRules.HasLegendaryMechanic(item.Rarity)) return;
            var mechanicId = AffixRollService.GetLegendaryMechanicId(item, _resolveDefinition(item.DefinitionId));
            if (_admit != null && !_admit(mechanicId)) return;
            var passive = EquipmentPassiveFactory.Create(mechanicId);
            if (passive == null) return;

            passive.Attach(_context);
            _active[slot] = passive;
        }

        public void Dispose()
        {
            _inventory.EquippedChanged -= HandleEquippedChanged;
            foreach (var passive in _active.Values) passive.Detach();
            _active.Clear();
            _activeInstance.Clear();
        }
    }
}
