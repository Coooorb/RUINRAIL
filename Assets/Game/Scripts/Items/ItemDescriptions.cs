using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Items.Passives;
using RuinRail.Gameplay.Stats;

namespace RuinRail.Gameplay.Items
{
    /// <summary>The player-facing explanation of one item: what it is, exactly what it does, and its Legendary mechanic.</summary>
    public sealed class ItemDescription
    {
        /// <summary>One or two sentences of mechanics with the item's own numbers (never a summarising score, never flavour only).</summary>
        public string Summary = string.Empty;

        /// <summary>The fixed Legendary mechanic of the family (weapon special or armor/accessory passive), when the family has one.</summary>
        public string Legendary = string.Empty;

        /// <summary>Structured effect lines for stat panels (label, value); consumables and ammo get theirs here.</summary>
        public readonly List<(string Label, string Value)> Effects = new();

        /// <summary>Everything that could not be resolved from data while building; empty when the description is complete.</summary>
        public readonly List<string> Problems = new();

        public bool IsComplete => Problems.Count == 0 && !string.IsNullOrWhiteSpace(Summary);

        /// <summary>Summary plus the Legendary line, as one text.</summary>
        public string FullText => string.IsNullOrEmpty(Legendary) ? Summary : Summary + " " + Legendary;
    }

    /// <summary>
    /// Builds every item's description from the item's own data (ui/93): consumable amounts, durations and use
    /// times; grenade radius, damage and zones; weapon class, ammo, pellets, spread, explosion, heat and draw
    /// mechanics; armor and accessory modifiers; and the Legendary special / passive with its authored numbers. There
    /// is no hand-written text that could drift from a definition: change the data and the description follows.
    /// Text uses only characters the pixel face can draw (no degree or multiplication signs).
    /// </summary>
    public static class ItemDescriptions
    {
        /// <summary>
        /// The weapon catalog (set by the composition root) so ammo descriptions can say which weapon classes actually
        /// consume a type. Optional: without it the ammo text still states the type and stack limit.
        /// </summary>
        public static IReadOnlyList<WeaponDefinition> WeaponCatalog { get; set; }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static ItemDescription Build(ItemDefinition definition, LegendarySpecialRegistry specials = null) =>
            Build(definition, specials != null ? (Func<string, LegendarySpecialDefinition>)(id => specials.TryGet(id, out var d) ? d : null) : null);

        public static ItemDescription Build(ItemDefinition definition, Func<string, LegendarySpecialDefinition> resolveSpecial)
        {
            var description = new ItemDescription();
            if (definition == null)
            {
                description.Problems.Add("no definition");
                return description;
            }

            switch (definition)
            {
                case ConsumableDefinition consumable: DescribeConsumable(consumable, description); break;
                case AmmoItemDefinition ammo: DescribeAmmo(ammo, description); break;
                case RangedWeaponDefinition ranged: DescribeRanged(ranged, description); break;
                case BlasterWeaponDefinition blaster: DescribeBlaster(blaster, description); break;
                case BowWeaponDefinition bow: DescribeBow(bow, description); break;
                case MeleeWeaponDefinition melee: DescribeMelee(melee, description); break;
                case ArmorDefinition armor: DescribeArmor(armor, description); break;
                case AccessoryDefinition accessory: DescribeAccessory(accessory, description); break;
                default: description.Problems.Add($"unsupported item type {definition.GetType().Name}"); break;
            }

            if (definition is EquipmentItemDefinition equipment) DescribeLegendary(equipment, resolveSpecial, description);
            return description;
        }

        // ---- consumables (31) ----

        private static void DescribeConsumable(ConsumableDefinition c, ItemDescription d)
        {
            var use = c.UseTimeSeconds > 0f ? $"{F(c.UseTimeSeconds)} s use" : "instant";
            switch (c.EffectKind)
            {
                case ConsumableEffectKind.Heal:
                    if (c.HealAmount <= 0) d.Problems.Add("heal consumable without a heal amount");
                    d.Summary = c.UseTimeSeconds > 0f
                        ? $"Restore {c.HealAmount} HP when the {F(c.UseTimeSeconds)} s use completes (the unit is spent on completion). Healing Received bonuses apply."
                        : $"Restore {c.HealAmount} HP instantly. Healing Received bonuses apply.";
                    d.Effects.Add(("Heal", $"+{c.HealAmount} HP"));
                    d.Effects.Add(("Use time", c.UseTimeSeconds > 0f ? $"{F(c.UseTimeSeconds)} s" : "instant"));
                    break;
                case ConsumableEffectKind.TimedBuff:
                    if (c.BuffPercent == 0 || c.BuffDurationSeconds <= 0f) d.Problems.Add("timed buff without percent/duration");
                    var stat = StatLabels.Of(c.BuffStat);
                    var sign = c.BuffPercent >= 0 ? "+" : string.Empty;
                    d.Summary = $"Gain {sign}{c.BuffPercent}% {stat} for {F(c.BuffDurationSeconds)} s ({use}).";
                    if (c.ActivatesArmorInjectorCap) d.Summary += " While active the Damage Reduction cap rises to 50%.";
                    d.Summary += " Using another refreshes the timer; it never stacks.";
                    d.Effects.Add((stat, $"{sign}{c.BuffPercent}%"));
                    d.Effects.Add(("Duration", $"{F(c.BuffDurationSeconds)} s"));
                    d.Effects.Add(("Use time", c.UseTimeSeconds > 0f ? $"{F(c.UseTimeSeconds)} s" : "instant"));
                    break;
                case ConsumableEffectKind.Grenade:
                    DescribeGrenade(c, d);
                    break;
                case ConsumableEffectKind.Revive:
                    d.Summary = $"Co-op only: revive a fully Dead teammate at {c.ReviveHealthPercent}% of their Max HP ({use}). Has no use in Solo" +
                                (c.DropEligibility == DropEligibility.CoopOnly ? " and never drops there." : ".");
                    d.Effects.Add(("Revive", $"{c.ReviveHealthPercent}% Max HP"));
                    d.Effects.Add(("Use time", c.UseTimeSeconds > 0f ? $"{F(c.UseTimeSeconds)} s" : "instant"));
                    break;
                default:
                    d.Problems.Add($"unknown consumable effect {c.EffectKind}");
                    break;
            }

            d.Effects.Add(("Stack", $"up to {c.MaxStack}"));
        }

        private static void DescribeGrenade(ConsumableDefinition c, ItemDescription d)
        {
            var g = c.Grenade;
            if (g.RadiusTiles <= 0f || g.ThrowRangeTiles <= 0f) d.Problems.Add("grenade without radius/throw range");
            var range = $"Throw up to {F(g.ThrowRangeTiles)} tiles";
            var radius = $"{F(g.RadiusTiles)}-tile radius";
            switch (g.Kind)
            {
                case GrenadeEffectKind.Frag:
                    d.Summary = $"{range}: explodes for {g.DamageMin}–{g.DamageMax} damage in a {radius} with stagger {F(g.StaggerPower)}. Thrown instantly.";
                    d.Effects.Add(("Blast", $"{g.DamageMin}–{g.DamageMax}"));
                    break;
                case GrenadeEffectKind.Shock:
                    d.Summary = $"{range}: explodes for {g.DamageMin}–{g.DamageMax} damage in a {radius} with heavy stagger ({F(g.StaggerPower)}), interrupting enemies. Thrown instantly.";
                    d.Effects.Add(("Blast", $"{g.DamageMin}–{g.DamageMax}"));
                    d.Effects.Add(("Stagger", F(g.StaggerPower)));
                    break;
                case GrenadeEffectKind.Incendiary:
                    if (g.BurnDamagePerSecond <= 0 || g.BurnDurationSeconds <= 0f) d.Problems.Add("incendiary grenade without burn data");
                    d.Summary = $"{range}: {g.DamageMin}–{g.DamageMax} blast damage in a {radius}, then burning ground for {F(g.BurnDurationSeconds)} s dealing {g.BurnDamagePerSecond} damage per second. Thrown instantly.";
                    d.Effects.Add(("Blast", $"{g.DamageMin}–{g.DamageMax}"));
                    d.Effects.Add(("Burn", $"{g.BurnDamagePerSecond}/s for {F(g.BurnDurationSeconds)} s"));
                    break;
                case GrenadeEffectKind.Smoke:
                    if (g.SmokeDurationSeconds <= 0f) d.Problems.Add("smoke grenade without duration");
                    d.Summary = $"{range}: a smoke cloud ({radius}) for {F(g.SmokeDurationSeconds)} s. Normal enemies lose line of sight through it; Elites and Bosses ignore it. No damage. Thrown instantly.";
                    d.Effects.Add(("Smoke", $"{F(g.SmokeDurationSeconds)} s"));
                    break;
                default:
                    d.Problems.Add($"unknown grenade kind {g.Kind}");
                    break;
            }

            d.Effects.Add(("Radius", $"{F(g.RadiusTiles)} tiles"));
            d.Effects.Add(("Throw range", $"{F(g.ThrowRangeTiles)} tiles"));
        }

        // ---- ammo (26) ----

        public static string AmmoTypeName(AmmoType type) => type == AmmoType.Shells ? "Shells" : type + " Ammo";

        private static void DescribeAmmo(AmmoItemDefinition a, ItemDescription d)
        {
            var users = WeaponCatalog != null
                ? WeaponCatalog.OfType<RangedWeaponDefinition>().Where(w => w != null && w.AmmoType == a.AmmoType).Select(w => ClassName(w.WeaponClass)).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList()
                : new List<string>();
            var uses = users.Count > 0 ? $"Reserve rounds for {Join(users)} weapons" : $"Reserve rounds for weapons that fire {AmmoTypeName(a.AmmoType)}";
            var rockets = a.AmmoType == AmmoType.Heavy && WeaponCatalog != null && WeaponCatalog.OfType<RangedWeaponDefinition>().Any(w => w.AmmoType == AmmoType.Heavy && w.AmmoCostPerShot > 1)
                ? " Rocket Launchers spend 4 per shot."
                : string.Empty;
            d.Summary = $"{uses}. Goes into the reserve; reloading draws from it. A backpack stack holds up to {a.MaxStack}.{rockets}";
            d.Effects.Add(("Ammo type", AmmoTypeName(a.AmmoType)));
            if (users.Count > 0) d.Effects.Add(("Used by", Join(users)));
            d.Effects.Add(("Stack", $"up to {a.MaxStack}"));
        }

        // ---- weapons (23/24/33) ----

        public static string ClassName(WeaponClass weaponClass) => weaponClass switch
        {
            WeaponClass.Pistol => "Pistol",
            WeaponClass.Smg => "SMG",
            WeaponClass.AssaultRifle => "Assault Rifle",
            WeaponClass.BattleRifle => "Battle Rifle",
            WeaponClass.Shotgun => "Shotgun",
            WeaponClass.Sniper => "Sniper Rifle",
            WeaponClass.Bow => "Bow",
            WeaponClass.RocketLauncher => "Rocket Launcher",
            WeaponClass.Blaster => "Blaster",
            WeaponClass.Knife => "Knife",
            WeaponClass.Spear => "Spear",
            _ => weaponClass.ToString()
        };

        private static void DescribeRanged(RangedWeaponDefinition w, ItemDescription d)
        {
            var sb = new StringBuilder();
            sb.Append(ClassName(w.WeaponClass)).Append(": ");
            if (w.IsExplosive)
            {
                sb.Append($"fires a rocket that explodes on impact in a {F(w.ExplosionRadiusTiles)}-tile radius for {w.DamageMin}–{w.DamageMax} damage.");
            }
            else if (w.ProjectilesPerShot > 1)
            {
                sb.Append($"each shot fires {w.ProjectilesPerShot} pellets of {w.DamageMin}–{w.DamageMax} damage in a {F(w.SpreadDegrees)}-degree spread.");
            }
            else
            {
                sb.Append($"fires single rounds of {w.DamageMin}–{w.DamageMax} damage at {F(w.FireRate)} shots per second.");
            }

            var cost = w.AmmoCostPerShot > 1 ? $"Each shot spends {w.AmmoCostPerShot} {AmmoTypeName(w.AmmoType)}" : $"Uses {AmmoTypeName(w.AmmoType)}";
            sb.Append($" {cost}; {w.MagazineSize}-round magazine, {F(w.ReloadTime)} s reload, {F(w.Range)}-tile range.");
            d.Summary = sb.ToString();
            if (w.DamageMax <= 0 || w.MagazineSize <= 0) d.Problems.Add("ranged weapon without damage/magazine");
        }

        private static void DescribeBlaster(BlasterWeaponDefinition w, ItemDescription d)
        {
            d.Summary = $"{ClassName(w.WeaponClass)}: fires energy bolts of {w.DamageMin}–{w.DamageMax} damage at {F(w.FireRate)} shots per second. " +
                        $"No ammo: each shot adds {F(w.HeatPerShot)} Heat (max {F(w.MaxHeat)}); Heat cools {F(w.CoolingRatePerSecond)} per second after {F(w.CoolingDelaySeconds)} s without firing, and overheating locks the weapon for {F(w.OverheatLockoutSeconds)} s.";
            if (w.DamageMax <= 0 || w.HeatPerShot <= 0f) d.Problems.Add("blaster without damage/heat");
        }

        private static void DescribeBow(BowWeaponDefinition w, ItemDescription d)
        {
            d.Summary = $"{ClassName(w.WeaponClass)}: hold to draw. A quick shot deals {w.QuickDamageMin}–{w.QuickDamageMax} damage over {F(w.QuickRange)} tiles; " +
                        $"a full draw ({F(w.FullChargeSeconds)} s) deals {w.FullDrawDamageMin}–{w.FullDrawDamageMax} and flies faster and farther ({F(w.FullRange)} tiles). No ammo.";
            if (w.FullDrawDamageMax <= 0) d.Problems.Add("bow without full-draw damage");
        }

        private static void DescribeMelee(MeleeWeaponDefinition w, ItemDescription d)
        {
            var shape = w.AttackArcDegrees >= 45f ? $"a {F(w.AttackArcDegrees)}-degree swing" : $"a narrow {F(w.AttackArcDegrees)}-degree thrust";
            d.Summary = $"{ClassName(w.WeaponClass)}: {shape} of {F(w.AttackRange)} tiles for {w.DamageMin}–{w.DamageMax} damage, {F(w.AttackRate)} attacks per second. No ammo.";
            if (w.DamageMax <= 0) d.Problems.Add("melee weapon without damage");
        }

        // ---- armor / accessories (27–30) ----

        private static void DescribeArmor(ArmorDefinition a, ItemDescription d)
        {
            var parts = new List<string>();
            foreach (var modifier in a.BaseModifiers())
            {
                parts.Add($"{StatLabels.Format(modifier)} {StatLabels.Of(modifier.Stat)}");
                d.Effects.Add((StatLabels.Of(modifier.Stat), StatLabels.Format(modifier)));
            }

            if (parts.Count == 0) d.Problems.Add("armor without base modifiers");
            d.Summary = $"Armor: {Join(parts)} while worn. Higher rarity adds affixes.";
        }

        private static void DescribeAccessory(AccessoryDefinition a, ItemDescription d)
        {
            var parts = new List<string>();
            foreach (var modifier in a.BaseModifiers())
            {
                parts.Add($"{StatLabels.Format(modifier)} {StatLabels.Of(modifier.Stat)}");
                d.Effects.Add((StatLabels.Of(modifier.Stat), StatLabels.Format(modifier)));
            }

            if (parts.Count == 0) d.Problems.Add("accessory without intrinsic modifiers");
            d.Summary = $"Accessory: {Join(parts)} while worn. Higher rarity adds affixes.";
        }

        // ---- Legendary (25/28/34) ----

        private static void DescribeLegendary(EquipmentItemDefinition e, Func<string, LegendarySpecialDefinition> resolveSpecial, ItemDescription d)
        {
            if (string.IsNullOrEmpty(e.LegendaryMechanicId)) return;
            if (e is WeaponDefinition)
            {
                var special = resolveSpecial?.Invoke(e.LegendaryMechanicId);
                if (special == null)
                {
                    d.Problems.Add($"legendary special '{e.LegendaryMechanicId}' is not in the special registry");
                    d.Legendary = "LEGENDARY: special unavailable.";
                    return;
                }

                d.Legendary = $"LEGENDARY (RMB / LT): {special.DisplayName} — {SpecialEffect(special)} {F(special.CooldownSeconds)} s cooldown; uses no ammo or Heat.";
                return;
            }

            var passive = EquipmentPassiveFactory.Create(e.LegendaryMechanicId);
            if (passive == null || string.IsNullOrWhiteSpace(passive.Description))
            {
                d.Problems.Add($"legendary passive '{e.LegendaryMechanicId}' has no implementation/description");
                d.Legendary = "LEGENDARY: passive unavailable.";
                return;
            }

            d.Legendary = $"LEGENDARY PASSIVE: {passive.Description}";
        }

        /// <summary>The special's effect sentence from its authored primitive and numbers (25).</summary>
        public static string SpecialEffect(LegendarySpecialDefinition s)
        {
            var dmg = $"{s.DamageMin}–{s.DamageMax} damage";
            return s.Kind switch
            {
                SpecialKind.Burst => $"fire {s.Shots} rapid shots of {dmg} each.",
                SpecialKind.Fan => $"fire {s.Shots} projectiles of {dmg} each in a {F(s.ArcDegrees)}-degree fan.",
                SpecialKind.PiercingShot => $"fire one shot of {dmg} that pierces every enemy in its path ({F(s.RangeTiles)} tiles).",
                SpecialKind.Cone => $"a {F(s.RangeTiles)}-tile, {F(s.ArcDegrees)}-degree cone dealing {dmg} with heavy knockback and stagger.",
                SpecialKind.ExplosionSalvo => $"{s.Shots} explosions ahead of you ({F(s.RadiusTiles)}-tile radius, {dmg} each).",
                SpecialKind.DashStrike => $"dash {F(s.DistanceTiles)} tiles through enemies, dealing {dmg} to each one crossed.",
                _ => $"{dmg}."
            };
        }

        private static string F(float value) => value.ToString("0.##", Inv);

        private static string Join(IReadOnlyList<string> parts)
        {
            if (parts.Count == 0) return string.Empty;
            if (parts.Count == 1) return parts[0];
            return string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[parts.Count - 1];
        }
    }
}
