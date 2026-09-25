using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Economy;
using UnityEngine;

namespace RuinRail.Gameplay.Items.Validation
{
    /// <summary>Outcome of a catalog validation run.</summary>
    public sealed class WeaponCatalogReport
    {
        public WeaponCatalogReport(int definitionCount, IReadOnlyDictionary<WeaponClass, (int normal, int legendary)> perClass, IReadOnlyList<string> problems)
        {
            DefinitionCount = definitionCount;
            PerClass = perClass;
            Problems = problems;
        }

        public int DefinitionCount { get; }
        public IReadOnlyDictionary<WeaponClass, (int normal, int legendary)> PerClass { get; }
        public IReadOnlyList<string> Problems { get; }
        public bool IsValid => Problems.Count == 0;
        public string Summary => $"Weapon catalog: {DefinitionCount}/{WeaponCatalogValidator.ExpectedTotal} definitions, {Problems.Count} problem(s).";
    }

    /// <summary>
    /// The fixed V1 weapon catalog (items/33, items/25, base/77, production/126) as data, and the validator that checks
    /// the authored definitions against it: exactly 11 classes x (2 normal + 1 Legendary), exact values per named
    /// weapon, unique stable ids, approved resources only, class base prices, one resolvable special per Legendary and
    /// three-affix pools. Pure C# so editor menus, EditMode tests and the production validator share one source of truth.
    /// </summary>
    public static class WeaponCatalogValidator
    {
        public const int ExpectedTotal = 33;
        public const int NormalPerClass = 2;
        public const int LegendaryPerClass = 1;
        public static readonly AmmoType[] ApprovedAmmo = { AmmoType.Light, AmmoType.Medium, AmmoType.Heavy, AmmoType.Shells };
        public const int RocketHeavyCost = 4;

        public static readonly IReadOnlyDictionary<WeaponClass, int> ClassBasePrices = new Dictionary<WeaponClass, int>
        {
            [WeaponClass.Pistol] = 250, [WeaponClass.Smg] = 350, [WeaponClass.AssaultRifle] = 450, [WeaponClass.BattleRifle] = 500,
            [WeaponClass.Shotgun] = 450, [WeaponClass.Sniper] = 600, [WeaponClass.Bow] = 400, [WeaponClass.RocketLauncher] = 700,
            [WeaponClass.Blaster] = 650, [WeaponClass.Knife] = 250, [WeaponClass.Spear] = 350
        };

        public sealed class Firearm
        {
            public string Id; public WeaponClass Class; public int DamageMin, DamageMax; public float FireRate; public int Magazine; public float Reload, Range, Speed; public AmmoType Ammo; public int Cost = 1; public int Pellets = 1; public string Special = "";
        }

        public sealed class Bow { public string Id; public int QuickMin, QuickMax, FullMin, FullMax; public float FullCharge; public string Special = ""; }
        public sealed class Blaster { public string Id; public int DamageMin, DamageMax; public float FireRate, HeatPerShot, Cooling; public string Special = ""; }
        public sealed class Melee { public string Id; public WeaponClass Class; public int DamageMin, DamageMax; public float Rate, Range, Arc; public string Special = ""; }

        public static readonly Firearm[] Firearms =
        {
            new() { Id = "weapon_p9_ranger", Class = WeaponClass.Pistol, DamageMin = 12, DamageMax = 14, FireRate = 4.0f, Magazine = 12, Reload = 1.2f, Range = 10, Speed = 20, Ammo = AmmoType.Light },
            new() { Id = "weapon_kestrel_12", Class = WeaponClass.Pistol, DamageMin = 10, DamageMax = 12, FireRate = 5.2f, Magazine = 15, Reload = 1.4f, Range = 9, Speed = 20, Ammo = AmmoType.Light },
            new() { Id = "weapon_quickfang", Class = WeaponClass.Pistol, DamageMin = 13, DamageMax = 15, FireRate = 4.4f, Magazine = 12, Reload = 1.2f, Range = 10, Speed = 20, Ammo = AmmoType.Light, Special = "snapfire" },
            new() { Id = "weapon_rattler_9", Class = WeaponClass.Smg, DamageMin = 6, DamageMax = 8, FireRate = 10.0f, Magazine = 32, Reload = 1.6f, Range = 8, Speed = 18, Ammo = AmmoType.Light },
            new() { Id = "weapon_wasp_45", Class = WeaponClass.Smg, DamageMin = 8, DamageMax = 10, FireRate = 8.0f, Magazine = 26, Reload = 1.7f, Range = 9, Speed = 18, Ammo = AmmoType.Light },
            new() { Id = "weapon_buzzsaw", Class = WeaponClass.Smg, DamageMin = 7, DamageMax = 9, FireRate = 11.0f, Magazine = 36, Reload = 1.8f, Range = 8, Speed = 18, Ammo = AmmoType.Light, Special = "lead_bloom" },
            new() { Id = "weapon_ar_17", Class = WeaponClass.AssaultRifle, DamageMin = 8, DamageMax = 10, FireRate = 7.5f, Magazine = 30, Reload = 1.8f, Range = 12, Speed = 22, Ammo = AmmoType.Medium },
            new() { Id = "weapon_marauder_a2", Class = WeaponClass.AssaultRifle, DamageMin = 10, DamageMax = 12, FireRate = 6.5f, Magazine = 28, Reload = 1.9f, Range = 13, Speed = 22, Ammo = AmmoType.Medium },
            new() { Id = "weapon_vanguard", Class = WeaponClass.AssaultRifle, DamageMin = 9, DamageMax = 11, FireRate = 8.0f, Magazine = 30, Reload = 1.8f, Range = 12, Speed = 22, Ammo = AmmoType.Medium, Special = "overrun" },
            new() { Id = "weapon_sentinel_br", Class = WeaponClass.BattleRifle, DamageMin = 18, DamageMax = 22, FireRate = 3.0f, Magazine = 12, Reload = 2.0f, Range = 15, Speed = 26, Ammo = AmmoType.Medium },
            new() { Id = "weapon_hound_br", Class = WeaponClass.BattleRifle, DamageMin = 15, DamageMax = 18, FireRate = 4.0f, Magazine = 16, Reload = 2.1f, Range = 14, Speed = 26, Ammo = AmmoType.Medium },
            new() { Id = "weapon_judicator", Class = WeaponClass.BattleRifle, DamageMin = 20, DamageMax = 24, FireRate = 2.8f, Magazine = 10, Reload = 2.0f, Range = 16, Speed = 26, Ammo = AmmoType.Medium, Special = "piercing_line" },
            new() { Id = "weapon_longshot_s1", Class = WeaponClass.Sniper, DamageMin = 45, DamageMax = 52, FireRate = 1.0f, Magazine = 5, Reload = 2.4f, Range = 20, Speed = 34, Ammo = AmmoType.Heavy },
            new() { Id = "weapon_needle_m7", Class = WeaponClass.Sniper, DamageMin = 34, DamageMax = 40, FireRate = 1.5f, Magazine = 7, Reload = 2.2f, Range = 18, Speed = 34, Ammo = AmmoType.Heavy },
            new() { Id = "weapon_farline", Class = WeaponClass.Sniper, DamageMin = 48, DamageMax = 55, FireRate = 0.9f, Magazine = 5, Reload = 2.4f, Range = 21, Speed = 34, Ammo = AmmoType.Heavy, Special = "rail_shot" },
            new() { Id = "weapon_pipe_launcher", Class = WeaponClass.RocketLauncher, DamageMin = 65, DamageMax = 80, FireRate = 0.5f, Magazine = 1, Reload = 2.2f, Range = 14, Speed = 10, Ammo = AmmoType.Heavy, Cost = 4 },
            new() { Id = "weapon_twin_tube", Class = WeaponClass.RocketLauncher, DamageMin = 50, DamageMax = 60, FireRate = 0.65f, Magazine = 2, Reload = 2.8f, Range = 13, Speed = 10, Ammo = AmmoType.Heavy, Cost = 4 },
            new() { Id = "weapon_sunbreaker", Class = WeaponClass.RocketLauncher, DamageMin = 70, DamageMax = 85, FireRate = 0.5f, Magazine = 1, Reload = 2.3f, Range = 14, Speed = 10, Ammo = AmmoType.Heavy, Cost = 4, Special = "meteor_salvo" },
            new() { Id = "weapon_breacher_12", Class = WeaponClass.Shotgun, DamageMin = 5, DamageMax = 7, FireRate = 1.4f, Magazine = 6, Reload = 2.2f, Range = 6, Speed = 16, Ammo = AmmoType.Shells, Pellets = 6 },
            new() { Id = "weapon_scatter_8", Class = WeaponClass.Shotgun, DamageMin = 4, DamageMax = 5, FireRate = 1.7f, Magazine = 8, Reload = 2.4f, Range = 5, Speed = 16, Ammo = AmmoType.Shells, Pellets = 8 },
            new() { Id = "weapon_crowdbreaker", Class = WeaponClass.Shotgun, DamageMin = 5, DamageMax = 7, FireRate = 1.5f, Magazine = 6, Reload = 2.2f, Range = 6, Speed = 16, Ammo = AmmoType.Shells, Pellets = 7, Special = "concussion_blast" },
        };

        public static readonly Bow[] Bows =
        {
            new() { Id = "weapon_recurve_bow", QuickMin = 10, QuickMax = 12, FullMin = 25, FullMax = 30, FullCharge = 0.65f },
            new() { Id = "weapon_compound_bow", QuickMin = 12, QuickMax = 15, FullMin = 32, FullMax = 38, FullCharge = 1.0f },
            new() { Id = "weapon_stormstring", QuickMin = 12, QuickMax = 15, FullMin = 30, FullMax = 36, FullCharge = 0.8f, Special = "arrow_storm" },
        };

        public static readonly Blaster[] Blasters =
        {
            new() { Id = "weapon_pulse_carbine_b1", DamageMin = 7, DamageMax = 9, FireRate = 8.0f, HeatPerShot = 7, Cooling = 35 },
            new() { Id = "weapon_arc_blaster_b4", DamageMin = 10, DamageMax = 12, FireRate = 6.0f, HeatPerShot = 10, Cooling = 35 },
            new() { Id = "weapon_redline", DamageMin = 8, DamageMax = 10, FireRate = 9.0f, HeatPerShot = 8, Cooling = 40, Special = "overcharge_barrage" },
        };

        public static readonly Melee[] Melees =
        {
            new() { Id = "weapon_field_knife", Class = WeaponClass.Knife, DamageMin = 14, DamageMax = 17, Rate = 3.5f, Range = 1.2f, Arc = 80 },
            new() { Id = "weapon_ripper_knife", Class = WeaponClass.Knife, DamageMin = 10, DamageMax = 13, Rate = 5.0f, Range = 1.0f, Arc = 70 },
            new() { Id = "weapon_ghostedge", Class = WeaponClass.Knife, DamageMin = 15, DamageMax = 18, Rate = 3.8f, Range = 1.2f, Arc = 80, Special = "blink_strike" },
            new() { Id = "weapon_scrap_spear", Class = WeaponClass.Spear, DamageMin = 24, DamageMax = 28, Rate = 1.8f, Range = 2.6f, Arc = 20 },
            new() { Id = "weapon_guard_lance", Class = WeaponClass.Spear, DamageMin = 20, DamageMax = 24, Rate = 2.2f, Range = 3.0f, Arc = 20 },
            new() { Id = "weapon_railspike", Class = WeaponClass.Spear, DamageMin = 26, DamageMax = 30, Rate = 1.7f, Range = 3.0f, Arc = 20, Special = "impaling_charge" },
        };

        public const float BlasterMaxHeat = 100f;
        // 33_WEAPON_CATALOG: shortened from 0.6s / 2.2s by the D1 ammo / blaster fine-tuning pass so heat costs
        // less flow while remaining the class constraint. Damage, fire rate, Heat/Shot, Cooling Rate and Max Heat are unchanged.
        public const float BlasterCoolingDelay = 0.5f;
        public const float BlasterOverheatLockout = 1.9f;
        public const float BlasterProjectileSpeed = 22f;

        public static IEnumerable<string> AllCatalogIds => Firearms.Select(f => f.Id).Concat(Bows.Select(b => b.Id)).Concat(Blasters.Select(b => b.Id)).Concat(Melees.Select(m => m.Id));

        public static WeaponCatalogReport Validate(IEnumerable<WeaponDefinition> definitions, EconomyConfig economy, LegendarySpecialRegistry specials)
        {
            var list = (definitions ?? Array.Empty<WeaponDefinition>()).Where(d => d != null).ToList();
            var problems = new List<string>();
            var byId = new Dictionary<string, WeaponDefinition>(StringComparer.Ordinal);

            // ---- identity ----
            foreach (var group in list.GroupBy(d => d.Id ?? ""))
            {
                if (string.IsNullOrWhiteSpace(group.Key)) { problems.Add("A weapon definition has an empty stable id."); continue; }
                if (group.Count() > 1) problems.Add($"Duplicate stable id '{group.Key}' ({group.Count()} definitions).");
                else byId[group.Key] = group.First();
            }

            foreach (var d in list)
            {
                if (d.Category != ItemCategory.Weapon) problems.Add($"{d.Id}: category must be Weapon.");
                if (d.AffixPool == null) problems.Add($"{d.Id}: no affix pool (must roll Uncommon..Epic/Legendary affixes).");
                else if (d.AffixPool.Affixes.Count < RarityRules.RandomAffixCount(Rarity.Legendary)) problems.Add($"{d.Id}: affix pool '{d.AffixPool.Id}' cannot supply three affixes.");
                if (!Enum.IsDefined(typeof(WeaponClass), d.WeaponClass)) problems.Add($"{d.Id}: unsupported weapon class {(int)d.WeaponClass}.");
            }

            var unknown = list.Where(d => !string.IsNullOrEmpty(d.Id) && !AllCatalogIds.Contains(d.Id)).Select(d => d.Id).ToList();
            foreach (var id in unknown) problems.Add($"{id}: not part of the approved V1 catalog.");
            if (list.Count != ExpectedTotal) problems.Add($"Expected {ExpectedTotal} weapon definitions, found {list.Count}.");

            // ---- distribution ----
            var perClass = new Dictionary<WeaponClass, (int normal, int legendary)>();
            foreach (WeaponClass cls in Enum.GetValues(typeof(WeaponClass)))
            {
                var normal = list.Count(d => d.WeaponClass == cls && string.IsNullOrEmpty(d.LegendaryMechanicId));
                var legendary = list.Count(d => d.WeaponClass == cls && !string.IsNullOrEmpty(d.LegendaryMechanicId));
                perClass[cls] = (normal, legendary);
                if (normal != NormalPerClass) problems.Add($"{cls}: expected {NormalPerClass} normal weapons, found {normal}.");
                if (legendary != LegendaryPerClass) problems.Add($"{cls}: expected {LegendaryPerClass} Legendary weapon, found {legendary}.");
            }

            // ---- economy ----
            foreach (var (cls, price) in ClassBasePrices.Select(kv => (kv.Key, kv.Value)))
            {
                if (economy == null) { problems.Add("EconomyConfig missing: class base prices cannot be validated."); break; }
                if (!economy.TryGetWeaponClassPrice(cls, out var actual)) problems.Add($"{cls}: no base price in EconomyConfig (77: {price}).");
                else if (actual != price) problems.Add($"{cls}: base price {actual} differs from the approved {price}.");
            }

            // ---- per weapon ----
            foreach (var f in Firearms) ValidateFirearm(f, byId, problems);
            foreach (var b in Bows) ValidateBow(b, byId, problems);
            foreach (var b in Blasters) ValidateBlaster(b, byId, problems);
            foreach (var m in Melees) ValidateMelee(m, byId, problems);

            // ---- specials ----
            var legendaries = list.Where(d => !string.IsNullOrEmpty(d.LegendaryMechanicId)).ToList();
            foreach (var d in legendaries)
            {
                if (specials == null) { problems.Add($"{d.Id}: special registry missing."); continue; }
                if (!specials.TryGet(d.LegendaryMechanicId, out _)) problems.Add($"{d.Id}: Legendary special '{d.LegendaryMechanicId}' does not exist.");
            }

            foreach (var group in legendaries.GroupBy(d => d.LegendaryMechanicId).Where(g => g.Count() > 1))
            {
                problems.Add($"Special '{group.Key}' is shared by {group.Count()} weapons; each Legendary has its own fixed special.");
            }

            return new WeaponCatalogReport(list.Count, perClass, problems);
        }

        private static bool Require<T>(string id, Dictionary<string, WeaponDefinition> byId, List<string> problems, out T definition) where T : WeaponDefinition
        {
            definition = null;
            if (!byId.TryGetValue(id, out var d)) { problems.Add($"{id}: missing from the project."); return false; }
            definition = d as T;
            if (definition == null) { problems.Add($"{id}: expected {typeof(T).Name}, found {d.GetType().Name}."); return false; }
            return true;
        }

        private static void Expect<T>(string id, string field, T expected, T actual, List<string> problems) where T : IEquatable<T>
        {
            if (!expected.Equals(actual)) problems.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1} is {2}, approved {3}.", id, field, actual, expected));
        }

        private static void ExpectFloat(string id, string field, float expected, float actual, List<string> problems)
        {
            if (Mathf.Abs(expected - actual) > 0.001f) problems.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1} is {2}, approved {3}.", id, field, actual, expected));
        }

        private static void ExpectSpecial(string id, string expected, WeaponDefinition d, List<string> problems)
        {
            Expect(id, "LegendaryMechanicId", expected ?? "", d.LegendaryMechanicId ?? "", problems);
        }

        private static void ValidateFirearm(Firearm f, Dictionary<string, WeaponDefinition> byId, List<string> problems)
        {
            if (!Require<RangedWeaponDefinition>(f.Id, byId, problems, out var d)) return;
            Expect(f.Id, "WeaponClass", (int)f.Class, (int)d.WeaponClass, problems);
            Expect(f.Id, "DamageMin", f.DamageMin, d.DamageMin, problems);
            Expect(f.Id, "DamageMax", f.DamageMax, d.DamageMax, problems);
            ExpectFloat(f.Id, "FireRate", f.FireRate, d.FireRate, problems);
            Expect(f.Id, "MagazineSize", f.Magazine, d.MagazineSize, problems);
            ExpectFloat(f.Id, "ReloadTime", f.Reload, d.ReloadTime, problems);
            ExpectFloat(f.Id, "Range", f.Range, d.Range, problems);
            ExpectFloat(f.Id, "ProjectileSpeed", f.Speed, d.ProjectileSpeed, problems);
            Expect(f.Id, "AmmoType", (int)f.Ammo, (int)d.AmmoType, problems);
            Expect(f.Id, "AmmoCostPerShot", f.Cost, d.AmmoCostPerShot, problems);
            Expect(f.Id, "ProjectilesPerShot", f.Pellets, d.ProjectilesPerShot, problems);
            ExpectSpecial(f.Id, f.Special, d, problems);
            if (!ApprovedAmmo.Contains(d.AmmoType)) problems.Add($"{f.Id}: ammo type {d.AmmoType} is not an approved category.");
            if (d.WeaponClass == WeaponClass.RocketLauncher && (d.AmmoType != AmmoType.Heavy || d.AmmoCostPerShot != RocketHeavyCost)) problems.Add($"{f.Id}: rockets cost {RocketHeavyCost} Heavy per shot (26).");
            if (d.WeaponClass == WeaponClass.RocketLauncher && !d.IsExplosive) problems.Add($"{f.Id}: rockets must detonate (explosion radius > 0).");
            if (d.WeaponClass != WeaponClass.RocketLauncher && d.IsExplosive) problems.Add($"{f.Id}: only rockets are explosive.");
            if (d.WeaponClass == WeaponClass.Shotgun && d.AmmoType != AmmoType.Shells) problems.Add($"{f.Id}: shotguns use Shells (33).");
        }

        private static void ValidateBow(Bow b, Dictionary<string, WeaponDefinition> byId, List<string> problems)
        {
            if (!Require<BowWeaponDefinition>(b.Id, byId, problems, out var d)) return;
            Expect(b.Id, "WeaponClass", (int)WeaponClass.Bow, (int)d.WeaponClass, problems);
            Expect(b.Id, "QuickDamageMin", b.QuickMin, d.QuickDamageMin, problems);
            Expect(b.Id, "QuickDamageMax", b.QuickMax, d.QuickDamageMax, problems);
            Expect(b.Id, "FullDrawDamageMin", b.FullMin, d.FullDrawDamageMin, problems);
            Expect(b.Id, "FullDrawDamageMax", b.FullMax, d.FullDrawDamageMax, problems);
            ExpectFloat(b.Id, "FullChargeSeconds", b.FullCharge, d.FullChargeSeconds, problems);
            ExpectSpecial(b.Id, b.Special, d, problems);
        }

        private static void ValidateBlaster(Blaster b, Dictionary<string, WeaponDefinition> byId, List<string> problems)
        {
            if (!Require<BlasterWeaponDefinition>(b.Id, byId, problems, out var d)) return;
            Expect(b.Id, "WeaponClass", (int)WeaponClass.Blaster, (int)d.WeaponClass, problems);
            Expect(b.Id, "DamageMin", b.DamageMin, d.DamageMin, problems);
            Expect(b.Id, "DamageMax", b.DamageMax, d.DamageMax, problems);
            ExpectFloat(b.Id, "FireRate", b.FireRate, d.FireRate, problems);
            ExpectFloat(b.Id, "HeatPerShot", b.HeatPerShot, d.HeatPerShot, problems);
            ExpectFloat(b.Id, "CoolingRatePerSecond", b.Cooling, d.CoolingRatePerSecond, problems);
            ExpectFloat(b.Id, "MaxHeat", BlasterMaxHeat, d.MaxHeat, problems);
            ExpectFloat(b.Id, "CoolingDelaySeconds", BlasterCoolingDelay, d.CoolingDelaySeconds, problems);
            ExpectFloat(b.Id, "OverheatLockoutSeconds", BlasterOverheatLockout, d.OverheatLockoutSeconds, problems);
            ExpectFloat(b.Id, "ProjectileSpeed", BlasterProjectileSpeed, d.ProjectileSpeed, problems);
            ExpectSpecial(b.Id, b.Special, d, problems);
        }

        private static void ValidateMelee(Melee m, Dictionary<string, WeaponDefinition> byId, List<string> problems)
        {
            if (!Require<MeleeWeaponDefinition>(m.Id, byId, problems, out var d)) return;
            Expect(m.Id, "WeaponClass", (int)m.Class, (int)d.WeaponClass, problems);
            Expect(m.Id, "DamageMin", m.DamageMin, d.DamageMin, problems);
            Expect(m.Id, "DamageMax", m.DamageMax, d.DamageMax, problems);
            ExpectFloat(m.Id, "AttackRate", m.Rate, d.AttackRate, problems);
            ExpectFloat(m.Id, "AttackRange", m.Range, d.AttackRange, problems);
            ExpectFloat(m.Id, "AttackArcDegrees", m.Arc, d.AttackArcDegrees, problems);
            ExpectSpecial(m.Id, m.Special, d, problems);
            if (d.WindUpSeconds + d.RecoverySeconds > 1f / d.AttackRate + 0.0001f) problems.Add($"{m.Id}: wind-up + recovery exceed the attack interval.");
        }
    }
}
