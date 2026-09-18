using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;

namespace RuinRail.Gameplay.Events
{
    /// <summary>One presented weapon of a Weapon Cache: a fully rolled instance the chooser would receive.</summary>
    public sealed class WeaponCacheChoice
    {
        public WeaponCacheChoice(int index, ItemInstance item, EquipmentItemDefinition definition)
        {
            Index = index;
            Item = item;
            Definition = definition;
        }

        public int Index { get; }
        public ItemInstance Item { get; }
        public EquipmentItemDefinition Definition { get; }
    }

    /// <summary>
    /// 57.6 Weapon Cache: three random weapons (distinct regular weapon definitions, rarities from the depth-scaled
    /// table, Legendary rolls swap to the class Legendary) are presented; the party chooses exactly one, which moves
    /// into the chooser's backpack. Once a choice is taken the cache is consumed for the whole party. The presented
    /// set is fixed by the event context, so every party member sees the same three weapons.
    /// </summary>
    public sealed class WeaponCacheEvent : DungeonEventBase
    {
        private const int ChoiceSalt = 0x5743; // "WC"

        private readonly DungeonEventConfig _config;
        private readonly List<WeaponCacheChoice> _choices = new();

        public WeaponCacheEvent(DungeonEventContext context, DungeonEventConfig config, IEnumerable<ItemDefinition> catalog, Func<LootQuality, RarityTableDefinition> rarityTables)
            : base(DungeonEventKind.WeaponCache, context)
        {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            var list = (catalog ?? throw new ArgumentNullException(nameof(catalog))).Where(d => d != null).ToList();
            var weapons = EquipmentRollService.RegularEquipment(list).OfType<WeaponDefinition>().Cast<EquipmentItemDefinition>().ToList();
            var random = Context.Random(ChoiceSalt);
            var table = rarityTables?.Invoke(_config.WeaponCacheQuality) ?? rarityTables?.Invoke(LootQuality.Standard);
            var count = Math.Min(_config.WeaponCacheChoices, weapons.Count);
            for (var i = 0; i < count; i++)
            {
                var pick = weapons[random.NextInt(weapons.Count)];
                weapons.Remove(pick);
                var rarity = EquipmentRollService.RollRarity(table, Context.Depth, random);
                var item = EquipmentRollService.Create(pick, rarity, random, list, out var used);
                _choices.Add(new WeaponCacheChoice(i, item, used));
            }
        }

        public IReadOnlyList<WeaponCacheChoice> Choices => _choices;
        public int ChosenIndex { get; private set; } = -1;
        public string ChosenBy { get; private set; }
        public bool IsConsumed => Phase == DungeonEventPhase.Completed;

        public string ChoicesSignature => string.Join("|", _choices.Select(c => $"{c.Definition.Id}:{c.Item.Rarity}:" + string.Join("+", c.Item.AffixRolls.Select(r => $"{r.AffixId}={r.Value}"))));

        public event Action<WeaponCacheEvent, WeaponCacheChoice, string> Chosen;

        public override bool CanActivate(EventActor actor) => Phase == DungeonEventPhase.Available && actor?.Backpack != null && _choices.Count > 0;

        /// <summary>
        /// Activation presents the choices and changes nothing; the pick happens in <see cref="Choose"/>. The result
        /// carries <see cref="DungeonEventDetails.ChoiceRequired"/>, which is the world handle's signal to open the
        /// selection screen (<c>DungeonEventInteractable.ChoiceRequested</c>). The phase is back to Available by the
        /// time the caller sees the result, so the screen can Choose immediately.
        /// </summary>
        protected override DungeonEventResult OnActivate(EventActor actor)
        {
            return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.Unavailable, detail: DungeonEventDetails.ChoiceRequired);
        }

        public bool CanChoose(EventActor actor, int index)
        {
            return Phase == DungeonEventPhase.Available && actor?.Backpack != null && index >= 0 && index < _choices.Count && actor.Backpack.CanAccept(_choices[index].Item);
        }

        /// <summary>Takes exactly one weapon into the chooser's backpack; the cache is then consumed for everyone.</summary>
        public DungeonEventResult Choose(EventActor actor, int index)
        {
            if (Phase != DungeonEventPhase.Available)
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.None, detail: "consumed");
            }

            if (actor?.Backpack == null || index < 0 || index >= _choices.Count)
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.Unavailable, detail: "invalid_choice");
            }

            var choice = _choices[index];
            if (!actor.Backpack.CanAccept(choice.Item) || !actor.Backpack.TryAdd(choice.Item))
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.Unavailable, detail: "backpack_rejected");
            }

            ChosenIndex = index;
            ChosenBy = actor.ParticipantId;
            var loot = new LootResult();
            loot.Items.Add(choice.Item);
            var result = Success(0, loot, $"chose:{choice.Definition.Id}");
            Finish(result);
            Chosen?.Invoke(this, choice, actor.ParticipantId);
            return result;
        }
    }
}
