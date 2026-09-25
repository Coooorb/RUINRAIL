using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.EditorTools.ArtGen;
using RuinRail.EditorTools.Production;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.UI.Inventory;
using RuinRail.UI.Theme;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests.EditMode
{
    /// <summary>
    /// Every player-facing item carries a description derived from its own data (ui/93): the validator passes over
    /// the full catalog, representative items quote their exact numbers, the tooltip's structured lines agree with
    /// the definitions, Legendary specials/passives are described, placeholders and internal ids fail, and every
    /// character is one the pixel face can draw.
    /// </summary>
    public sealed class ItemDescriptionTests
    {
        private static LegendarySpecialRegistry Specials() => new(AssetDatabase.FindAssets("t:LegendarySpecialDefinition", new[] { ItemDescriptionValidator.SpecialsFolder })
            .Select(g => AssetDatabase.LoadAssetAtPath<LegendarySpecialDefinition>(AssetDatabase.GUIDToAssetPath(g))));

        private static ItemDefinition Item(string id) => AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).First(d => d != null && d.Id == id);

        private static string Describe(string id) => ItemDescriptions.Build(Item(id), Specials()).FullText;

        [SetUp]
        public void SetUp() => ItemDescriptions.WeaponCatalog = AssetDatabase.FindAssets("t:WeaponDefinition").Select(g => AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(w => w != null).ToList();

        [TearDown]
        public void TearDown() => ItemDescriptions.WeaponCatalog = null;

        [Test]
        public void Validator_PassesOverTheWholeCatalog_AndWritesItsReport()
        {
            var report = ItemDescriptionValidator.WriteReport();
            Assert.IsTrue(report.Pass, string.Join("\n", report.Lines.Where(l => !l.Pass).Select(l => l.Id + ": " + string.Join("; ", l.Problems))));
            Assert.AreEqual(72, report.Count, "every ItemDefinition in the authoritative catalog (33 weapons, 9 armor, 16 accessories, 10 consumables, 4 ammo)");
            Assert.AreEqual(33, report.Lines.Count(l => l.Category == "Weapon"));
            Assert.AreEqual(9, report.Lines.Count(l => l.Category == "Armor"));
            Assert.AreEqual(16, report.Lines.Count(l => l.Category == "Accessory"));
            Assert.AreEqual(10, report.Lines.Count(l => l.Category == "Consumable"));
            Assert.AreEqual(4, report.Lines.Count(l => l.Category == "Ammo"));
            Assert.IsTrue(File.Exists(ItemDescriptionValidator.ReportPath));
            Assert.AreEqual(report.ToMarkdown(), ItemDescriptionValidator.ValidateProject().ToMarkdown(), "deterministic");
        }

        [Test]
        public void Consumables_StateAmountDurationUseTimeAndEffect_FromTheirData()
        {
            StringAssert.Contains("Restore 25 HP when the 1.5 s use completes", Describe("consumable_bandage"));
            StringAssert.Contains("Restore 60 HP when the 3 s use completes", Describe("consumable_medkit"));
            StringAssert.Contains("Gain +20% Movement Speed for 8 s (0.5 s use)", Describe("consumable_combat_stim"));
            StringAssert.Contains("Gain +20% Weapon Damage for 10 s (0.5 s use)", Describe("consumable_damage_stim"));
            var injector = Describe("consumable_armor_injector");
            StringAssert.Contains("Gain +20% Damage Reduction for 10 s", injector);
            StringAssert.Contains("cap rises to 50%", injector);
            StringAssert.Contains("never stacks", injector);
            var frag = Describe("consumable_frag_grenade");
            StringAssert.Contains("Throw up to 6 tiles", frag);
            StringAssert.Contains("35–45 damage in a 2.5-tile radius", frag);
            var shock = Describe("consumable_shock_grenade");
            StringAssert.Contains("15–20 damage", shock);
            StringAssert.Contains("heavy stagger (20)", shock);
            var fire = Describe("consumable_incendiary_grenade");
            StringAssert.Contains("12–16 blast damage", fire);
            StringAssert.Contains("burning ground for 5 s dealing 6 damage per second", fire);
            var smoke = Describe("consumable_smoke_grenade");
            StringAssert.Contains("smoke cloud (4-tile radius) for 6 s", smoke);
            StringAssert.Contains("Elites and Bosses ignore it", smoke);
            StringAssert.Contains("No damage", smoke);
            var defib = Describe("consumable_defibrillator");
            StringAssert.Contains("Co-op only", defib);
            StringAssert.Contains("30% of their Max HP", defib);
            StringAssert.Contains("never drops there", defib);
        }

        [Test]
        public void ConsumableTooltip_CarriesStructuredEffectLines_ThatMatchTheDefinition()
        {
            var bandage = (ConsumableDefinition)Item("consumable_bandage");
            var tooltip = ItemTooltip.Build(new ItemInstance(bandage.Id, quantity: 3), bandage, Specials());
            Assert.IsNotEmpty(tooltip.Description);
            var heal = tooltip.BaseStats.First(l => l.Label == "Heal");
            Assert.AreEqual($"+{bandage.HealAmount} HP", heal.Value);
            Assert.AreEqual(bandage.HealAmount, heal.Numeric);
            Assert.AreEqual($"{bandage.UseTimeSeconds:0.##} s", tooltip.BaseStats.First(l => l.Label == "Use time").Value);
            Assert.AreEqual($"up to {bandage.MaxStack}", tooltip.BaseStats.First(l => l.Label == "Stack").Value);
            Assert.AreEqual(3, tooltip.Quantity);

            var stim = (ConsumableDefinition)Item("consumable_combat_stim");
            var stimTip = ItemTooltip.Build(new ItemInstance(stim.Id), stim, Specials());
            Assert.AreEqual($"+{stim.BuffPercent}%", stimTip.BaseStats.First(l => l.Label == "Movement Speed").Value);
            Assert.AreEqual($"{stim.BuffDurationSeconds:0.##} s", stimTip.BaseStats.First(l => l.Label == "Duration").Value);

            var frag = (ConsumableDefinition)Item("consumable_frag_grenade");
            var fragTip = ItemTooltip.Build(new ItemInstance(frag.Id), frag, Specials());
            Assert.AreEqual($"{frag.Grenade.DamageMin}–{frag.Grenade.DamageMax}", fragTip.BaseStats.First(l => l.Label == "Blast").Value);
            Assert.AreEqual($"{frag.Grenade.RadiusTiles:0.##} tiles", fragTip.BaseStats.First(l => l.Label == "Radius").Value);
        }

        [Test]
        public void Weapons_DescribeClassAmmoAndSpecialBehaviour_FromTheirData()
        {
            var p9 = (RangedWeaponDefinition)Item("weapon_p9_ranger");
            var text = Describe(p9.Id);
            StringAssert.Contains("Pistol:", text);
            StringAssert.Contains($"{p9.DamageMin}–{p9.DamageMax} damage at {p9.FireRate:0.##} shots per second", text);
            StringAssert.Contains($"Uses Light Ammo; {p9.MagazineSize}-round magazine, {p9.ReloadTime:0.##} s reload, {p9.Range:0.##}-tile range", text);
            var scatter = Describe("weapon_scatter_8");
            StringAssert.Contains("8 pellets", scatter);
            StringAssert.Contains("30-degree spread", scatter);
            StringAssert.Contains("Uses Shells", scatter);
            var pipe = Describe("weapon_pipe_launcher");
            StringAssert.Contains("explodes on impact in a 2-tile radius", pipe);
            StringAssert.Contains("Each shot spends 4 Heavy Ammo", pipe);
            var arc = Describe("weapon_arc_blaster_b4");
            StringAssert.Contains("No ammo: each shot adds 10 Heat (max 100)", arc);
            StringAssert.Contains("locks the weapon for 1.9 s", arc);
            var bow = Describe("weapon_recurve_bow");
            StringAssert.Contains("hold to draw", bow);
            StringAssert.Contains("full draw (0.65 s) deals 25–30", bow);
            var knife = Describe("weapon_field_knife");
            StringAssert.Contains("80-degree swing", knife);
            StringAssert.Contains("14–17 damage", knife);
            var spear = Describe("weapon_guard_lance");
            StringAssert.Contains("20-degree thrust of 3 tiles", spear);
        }

        [Test]
        public void LegendaryWeapons_DescribeTheirSpecial_WithItsAuthoredNumbersAndCooldown()
        {
            var quickfang = Describe("weapon_quickfang");
            StringAssert.Contains("LEGENDARY (RMB / LT): Snapfire — fire 8 rapid shots of 10–12 damage each. 10 s cooldown; uses no ammo or Heat.", quickfang);
            StringAssert.Contains("Lead Bloom — fire 24 projectiles of 7–9 damage each in a 90-degree fan", Describe("weapon_buzzsaw"));
            StringAssert.Contains("Piercing Line — fire one shot of 55–65 damage that pierces every enemy in its path (16 tiles)", Describe("weapon_judicator"));
            StringAssert.Contains("Concussion Blast — a 3-tile, 90-degree cone dealing 50–60 damage with heavy knockback and stagger", Describe("weapon_crowdbreaker"));
            StringAssert.Contains("Meteor Salvo — 3 explosions ahead of you (2-tile radius, 45–55 damage each)", Describe("weapon_sunbreaker"));
            StringAssert.Contains("Blink Strike — dash 4 tiles through enemies, dealing 40–50 damage to each one crossed", Describe("weapon_ghostedge"));
            StringAssert.Contains("Impaling Charge — dash 5 tiles", Describe("weapon_railspike"));
            StringAssert.Contains("Rail Shot — fire one shot of 85–95 damage", Describe("weapon_farline"));
            StringAssert.Contains("Overrun — fire 12 rapid shots of 10–12 damage each. 13 s cooldown", Describe("weapon_vanguard"));
            StringAssert.Contains("Overcharge Barrage — fire 16 rapid shots of 8–10 damage each. 14 s cooldown", Describe("weapon_redline"));
            StringAssert.Contains("Arrow Storm — fire 9 projectiles of 14–17 damage each in a 60-degree fan", Describe("weapon_stormstring"));
            // A non-Legendary instance of a Legendary family never shows the special; a Legendary instance shows the same text as the description.
            var definition = (EquipmentItemDefinition)Item("weapon_quickfang");
            Assert.IsNull(ItemTooltip.Build(new ItemInstance(definition.Id, 1, Rarity.Rare), definition, Specials()).LegendaryText);
            var legendary = ItemTooltip.Build(new ItemInstance(definition.Id, 1, Rarity.Legendary), definition, Specials());
            StringAssert.Contains("Snapfire", legendary.LegendaryText);
            StringAssert.Contains("8 rapid shots", legendary.LegendaryText);
        }

        [Test]
        public void ArmorAndAccessories_StateTheirExactModifiers_AndLegendaryPassives()
        {
            StringAssert.Contains("Armor: +20 Max HP and +4% Damage Reduction while worn", Describe("armor_scrap_vest"));
            StringAssert.Contains("LEGENDARY PASSIVE: After clearing a Combat Room, restore 6% of Max HP.", Describe("armor_scrap_vest"));
            var plate = Describe("armor_heavy_plate");
            StringAssert.Contains("+35 Max HP, +9% Damage Reduction and -4% Movement Speed", plate);
            StringAssert.Contains("While below 25% HP: +15% Damage Reduction", plate);
            StringAssert.Contains("+22 Max HP, +5% Damage Reduction and +30% Explosion Damage Reduction", Describe("armor_blast_suit"));
            StringAssert.Contains("Ignore knockback from explosions", Describe("armor_blast_suit"));
            StringAssert.Contains("first healing consumable used in each Combat Room heals 25% more", Describe("armor_medic_harness"));
            StringAssert.Contains("Accessory: +3 tiles Pickup Attraction Radius while worn", Describe("accessory_magnetic_coil"));
            StringAssert.Contains("remaining Coin and Ammo pickups in the room are pulled to you", Describe("accessory_magnetic_coil"));
            StringAssert.Contains("Accessory: +25% Ammo Stack Capacity while worn", Describe("accessory_ammo_pouch"));
            StringAssert.Contains("Ammo pickups grant 25% more ammo", Describe("accessory_ammo_pouch"));
            StringAssert.Contains("After 1 s without moving, projectile weapons deal +12% damage", Describe("accessory_field_scope"));
            StringAssert.Contains("emit a 2.5-tile energy pulse for 30–40 damage (8 s cooldown)", Describe("accessory_heat_sink"));
            StringAssert.Contains("takes 15–20 bonus damage and heavy stagger (2 s per target; never Bosses)", Describe("accessory_impact_module"));
        }

        [Test]
        public void Ammo_StatesTypeStackLimitAndTheClassesThatUseIt()
        {
            var light = Describe("ammo_light");
            StringAssert.Contains("Reserve rounds for Pistol and SMG weapons", light);
            StringAssert.Contains("up to 180", light);
            var heavy = Describe("ammo_heavy");
            StringAssert.Contains("Rocket Launcher and Sniper Rifle weapons", heavy);
            StringAssert.Contains("Rocket Launchers spend 4 per shot", heavy);
            StringAssert.Contains("up to 60", heavy);
            StringAssert.Contains("Reserve rounds for Shotgun weapons", Describe("ammo_shells"));
            StringAssert.Contains("Assault Rifle and Battle Rifle weapons", Describe("ammo_medium"));
        }

        [Test]
        public void EveryDescription_UsesOnlyPixelFontCharacters_AndWrapsIntoTheDetailsPanels()
        {
            var specials = Specials();
            var width = InventoryView.DetailsPanel.Width - UiTheme.Pad * 2;
            foreach (var item in AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null))
            {
                var description = ItemDescriptions.Build(item, specials);
                foreach (var c in description.FullText) Assert.IsTrue(PixelFontFactory.Charset.IndexOf(c) >= 0, $"{item.Id}: '{c}' is not in the pixel font");
                var lines = UiText.Wrap(description.Summary, width);
                Assert.IsTrue(lines.Count >= 1 && lines.Count <= 5, $"{item.Id}: {lines.Count} lines");
                foreach (var line in lines) Assert.LessOrEqual(UiText.Width(line), width, $"{item.Id}: '{line}' overflows the details panel");
                Assert.AreEqual(description.Summary.Replace("  ", " "), string.Join(" ", lines), $"{item.Id}: wrapping loses nothing");
            }
        }

        [Test]
        public void Validator_FailsPlaceholders_InternalIds_AndMissingLegendaryText()
        {
            var placeholder = ScriptableObject.CreateInstance<ConsumableDefinition>();
            typeof(ItemDefinition).GetField("_id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(placeholder, "consumable_todo");
            typeof(ItemDefinition).GetField("_displayName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(placeholder, "TODO item");
            // A heal consumable with no amount cannot describe itself: the validator names the problem.
            var report = ItemDescriptionValidator.Validate(new ItemDefinition[] { placeholder }, Specials());
            Assert.IsFalse(report.Pass);
            StringAssert.Contains("heal consumable without a heal amount", string.Join(";", report.Lines[0].Problems));
            Object.DestroyImmediate(placeholder);

            var orphan = ScriptableObject.CreateInstance<RangedWeaponDefinition>();
            typeof(ItemDefinition).GetField("_id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(orphan, "weapon_orphan");
            typeof(ItemDefinition).GetField("_displayName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(orphan, "Orphan");
            typeof(EquipmentItemDefinition).GetField("_legendaryMechanicId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(orphan, "no_such_special");
            typeof(RangedWeaponDefinition).GetField("_damageMin", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(orphan, 5);
            typeof(RangedWeaponDefinition).GetField("_damageMax", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(orphan, 7);
            typeof(RangedWeaponDefinition).GetField("_magazineSize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(orphan, 10);
            var orphanReport = ItemDescriptionValidator.Validate(new ItemDefinition[] { orphan }, Specials());
            Assert.IsFalse(orphanReport.Pass);
            StringAssert.Contains("no_such_special", string.Join(";", orphanReport.Lines[0].Problems));
            Object.DestroyImmediate(orphan);

            Assert.IsFalse(ItemDescriptionValidator.Validate(new ItemDefinition[0], Specials()).Pass, "an empty catalog is not a pass");
        }

        [Test]
        public void DetailPager_KeepsEveryRowReachable_WhenTheTooltipOverflowsThePanel()
        {
            var pager = new DetailPager(10);
            var rows = Enumerable.Range(0, 23).Select(i => new DetailRow("row " + i, string.Empty, Color.white)).ToList();
            pager.SetRows(rows, false);
            Assert.IsTrue(pager.Overflows);
            Assert.AreEqual(9, pager.PageRows, "one visible row is the MORE hint");
            Assert.AreEqual(3, pager.PageCount);
            var first = pager.Visible("WHEEL");
            Assert.AreEqual(10, first.Count);
            Assert.AreEqual("row 0", first[0].Key);
            StringAssert.StartsWith("MORE (1/3)", first[9].Key);
            Assert.IsTrue(pager.PageDown());
            Assert.AreEqual("row 9", pager.Visible()[0].Key);
            Assert.IsTrue(pager.PageDown());
            var last = pager.Visible();
            Assert.AreEqual("row 18", last[0].Key);
            Assert.AreEqual("row 22", last[4].Key);
            Assert.IsFalse(pager.PageDown(), "no page past the last row");
            Assert.IsTrue(pager.PageUp() && pager.PageUp());
            Assert.AreEqual(0, pager.Offset);
            Assert.IsFalse(pager.PageUp());
            pager.SetRows(rows.Take(5).ToList(), false);
            Assert.IsFalse(pager.Overflows);
            Assert.AreEqual(5, pager.Visible().Count, "a short list shows no hint row");
        }
    }
}
