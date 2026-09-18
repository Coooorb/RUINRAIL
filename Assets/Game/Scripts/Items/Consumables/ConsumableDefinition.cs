using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Gameplay.Items.Consumables
{
    public enum ConsumableEffectKind
    {
        Heal,
        TimedBuff,
        Grenade,
        Revive
    }

    /// <summary>Who may find the item as normal loot (31: Defibrillator has zero drop chance in Solo).</summary>
    public enum DropEligibility
    {
        Any,
        CoopOnly
    }

    public enum GrenadeEffectKind
    {
        Frag,
        Shock,
        Incendiary,
        Smoke
    }

    /// <summary>Authored grenade behaviour (31_CONSUMABLES). Throw range/speed are V1 FINAL (TASK 179) feel values.</summary>
    [System.Serializable]
    public struct GrenadeData
    {
        public GrenadeEffectKind Kind;
        [Min(0f)] public float RadiusTiles;
        [Min(0)] public int DamageMin;
        [Min(0)] public int DamageMax;
        [Min(0f)] public float StaggerPower;
        [Min(0)] public int BurnDamagePerSecond;
        [Min(0f)] public float BurnDurationSeconds;
        [Min(0f)] public float SmokeDurationSeconds;
        [Min(0.1f)] public float ThrowRangeTiles;
        [Min(0.1f)] public float ThrowSpeed;
    }

    /// <summary>When the single stack unit is removed.</summary>
    public enum ConsumptionPoint
    {
        /// <summary>After the use action completes (healing items, stims): an interrupted use costs nothing.</summary>
        OnCompletion,

        /// <summary>The moment the use is activated (throwables leave the hand immediately).</summary>
        OnActivation
    }

    /// <summary>
    /// Fixed consumable (items/31_CONSUMABLES): one fixed rarity used only as a drop-frequency label — never affixes,
    /// random stats or Legendary mechanics. Effect data is authored, the runtime never branches on the item name.
    /// </summary>
    [CreateAssetMenu(fileName = "Consumable_", menuName = "RuinRail/Items/Consumable Definition")]
    public sealed class ConsumableDefinition : ItemDefinition
    {
        [SerializeField] private Rarity _fixedRarity = Rarity.Common;
        [SerializeField, Min(0f)] private float _useTimeSeconds;
        [SerializeField] private ConsumptionPoint _consumptionPoint = ConsumptionPoint.OnCompletion;
        [SerializeField] private ConsumableEffectKind _effectKind;
        [SerializeField, Min(0)] private int _healAmount;
        [SerializeField] private StatId _buffStat;
        [SerializeField] private int _buffPercent;
        [SerializeField, Min(0f)] private float _buffDurationSeconds;
        [SerializeField] private bool _activatesArmorInjectorCap;
        [SerializeField] private GrenadeData _grenade;
        [SerializeField, Range(0, 100)] private int _reviveHealthPercent = 30;
        [SerializeField] private DropEligibility _dropEligibility = DropEligibility.Any;

        public Rarity FixedRarity => _fixedRarity;
        public float UseTimeSeconds => _useTimeSeconds;
        public ConsumptionPoint ConsumptionPoint => _consumptionPoint;
        public ConsumableEffectKind EffectKind => _effectKind;
        public int HealAmount => _healAmount;
        public StatId BuffStat => _buffStat;
        public int BuffPercent => _buffPercent;
        public float BuffDurationSeconds => _buffDurationSeconds;

        /// <summary>Armor Injector: while its buff runs, general DR uses the temporary 50% cap.</summary>
        public bool ActivatesArmorInjectorCap => _activatesArmorInjectorCap;
        public GrenadeData Grenade => _grenade;

        /// <summary>Revive consumables return a Dead teammate at this fraction of Max HP (Defibrillator: 30%).</summary>
        public int ReviveHealthPercent => _reviveHealthPercent;
        public DropEligibility DropEligibility => _dropEligibility;

        public bool IsDropEligible(int partySize) => _dropEligibility == DropEligibility.Any || partySize > 1;
    }
}
