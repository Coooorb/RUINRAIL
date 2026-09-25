using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RuinRail.App;
using RuinRail.Dungeon.Generation;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Progression;
using RuinRail.Networking;
using RuinRail.Persistence;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// The final release snapshot: every shipping value the release freezes, read from the real project data and the
    /// code constants (never retyped), as ordered "key = value" rows. It is not a balance spec — it is the regression
    /// fingerprint <see cref="FinalReleaseCandidateValidator"/> compares against the committed release baseline, so an
    /// unintended change to any frozen value fails the gate by name.
    /// </summary>
    public static class FinalReleaseSnapshot
    {
        public sealed class Row
        {
            public string Key;
            public string Value;
            public string Source;
        }

        private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        public static List<Row> Collect()
        {
            var rows = new List<Row>();
            void Add(string key, string value, string source) => rows.Add(new Row { Key = key, Value = value ?? "(null)", Source = source });
            var c = GameContentCatalog.Load();
            if (c == null) { Add("catalog", "missing", "GameContentCatalog.Load()"); return rows; }

            // ---- player ----
            var p = c.PlayerBalance;
            Add("player.max_health", p != null ? p.MaxHealth.ToString() : null, "PlayerBalanceConfig.MaxHealth");
            Add("player.move_speed", p != null ? F(p.MoveSpeed) : null, "PlayerBalanceConfig.MoveSpeed");
            Add("player.dash", p != null ? $"speed {F(p.DashSpeed)} duration {F(p.DashDuration)} cooldown {F(p.DashCooldown)} iframes {F(p.DashIFrameDuration)}" : null, "PlayerBalanceConfig dash fields");
            Add("player.revive", p != null ? $"bleedout {F(p.DownedBleedoutSeconds)} crawl {F(p.DownedCrawlSpeedMultiplier)} hold {F(p.ReviveChannelSeconds)} hp {p.ReviveHealthPercent}% protection {F(p.ReviveProtectionSeconds)} range {F(p.ReviveRangeTiles)}" : null, "PlayerBalanceConfig revive fields");
            Add("player.reconnect_grace", F(ReconnectGraceService.DefaultGraceSeconds), "ReconnectGraceService.DefaultGraceSeconds");

            // ---- ammo / starter kit ----
            foreach (AmmoType type in Enum.GetValues(typeof(AmmoType)))
                Add("ammo.cap." + type, c.AmmoBalance != null ? c.AmmoBalance.GetStackLimit(type).ToString() : null, "AmmoBalanceConfig.GetStackLimit");
            var supply = c.Loot != null && c.Loot.TryGet(LootSourceKind.SupplyChest, out var source) ? source.Table : null;
            var light = supply?.Rolls.SelectMany(r => r.Entries).FirstOrDefault(e => e.Item is AmmoItemDefinition a && a.AmmoType == AmmoType.Light);
            Add("loot.supply_chest.light_ammo", light != null ? $"{light.MinQuantity}-{light.MaxQuantity} weight {light.Weight}" : null, "LootTable_SupplyChest light-ammo entry (the D1 Supply Light range)");
            Add("starter.kit", string.Join("; ", StarterKitService.CreateKit().Select(k => $"{k.item.DefinitionId} x{k.item.Quantity}{(k.slot.HasValue ? " @" + k.slot.Value : "")}")), "StarterKitService.CreateKit()");

            // ---- weapons ----
            var weapons = c.Items.OfType<WeaponDefinition>().OrderBy(w => w.Id, StringComparer.Ordinal).ToList();
            foreach (var w in weapons) Add("weapon." + w.Id, Fingerprint(w), "WeaponDefinition core fields");
            var knife = weapons.FirstOrDefault(w => w.Id == StarterKitService.KnifeId);
            Add("weapon.field_knife", knife != null ? Fingerprint(knife) : null, "the starter Field Knife");
            foreach (var b in weapons.OfType<BlasterWeaponDefinition>())
                Add("blaster.heat." + b.Id, $"heat/shot {F(b.HeatPerShot)} cooling/s {F(b.CoolingRatePerSecond)} max {F(b.MaxHeat)} delay {F(b.CoolingDelaySeconds)} lockout {F(b.OverheatLockoutSeconds)}", "BlasterWeaponDefinition heat fields");

            // ---- enemies ----
            foreach (var boss in c.Bosses.Where(b => b != null).OrderBy(b => b.Id, StringComparer.Ordinal)) Add("boss.hp." + boss.Id, boss.BaseHealth.ToString(), "BossDefinition.BaseHealth");
            foreach (var elite in c.Elites.Where(e => e != null).OrderBy(e => e.Id, StringComparer.Ordinal)) Add("elite.hp." + elite.Id, elite.BaseHealth.ToString(), "EliteDefinition.BaseHealth");
            foreach (var enemy in c.Enemies.Where(e => e != null).OrderBy(e => e.Id, StringComparer.Ordinal)) Add("enemy.hp." + enemy.Id, enemy.BaseHealth.ToString(), "EnemyDefinition.BaseHealth");

            // ---- depth / economy ----
            foreach (var d in new[] { 1, 5, 10, 20, 30, 31, 50, 100 })
                Add($"depth.scaling.D{d}", $"hp x{F(DepthScaling.HealthMultiplier(d, c.DepthScaling))} dmg x{F(DepthScaling.DamageMultiplier(d, c.DepthScaling))}", "DepthScaling.HealthMultiplier/DamageMultiplier");
            foreach (var d in new[] { 1, 30, 31, 40, 100, 100000 })
                Add($"reward.D{d}", c.Economy != null ? $"coin x{F(c.Economy.CoinRewardMultiplier(d))} xp x{F(c.Economy.XpRewardMultiplier(d))}" : null, "EconomyConfig reward multipliers (D100000 = the cap)");
            var rules = DungeonGraphRules.CreateDefault();
            try
            {
                Add("elite.frequency", string.Join(" ", new[] { 1, 3, 5, 6, 10, 11, 20, 21, 30, 50 }.Select(d => $"D{d}:{rules.EliteChancePercent(d)}%x{rules.MaxElites(d)}")), "DungeonGraphRules.CreateDefault() (the runtime rules)");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rules);
            }

            Add("rooms.depth_gated", c.Rooms.Count(r => r != null && r.MinDepth > 1) + " (" + string.Join(", ", c.Rooms.Where(r => r != null && r.MinDepth > 1).GroupBy(r => r.MinDepth).OrderBy(g => g.Key).Select(g => $"D{g.Key}:{g.Count()}")) + ")", "RoomDefinition.MinDepth");
            Add("biome.weights", string.Join(" ", BiomeEncounterWeights.Authored.OrderBy(a => a.Item1.ToString(), StringComparer.Ordinal).ThenBy(a => a.EnemyId, StringComparer.Ordinal).Select(a => $"{a.Item1}:{a.EnemyId}={a.Weight}")) + $" default={BiomeEncounterWeights.Default}", "BiomeEncounterWeights.Authored");

            // ---- progression / inventory / party ----
            Add("progression.max_level", LevelCurve.MaxLevel.ToString(), "LevelCurve.MaxLevel");
            Add("progression.attribute_cap", SkillRules.MaxRank.ToString(), "SkillRules.MaxRank");
            Add("inventory.backpack_size", PlayerInventory.BackpackCapacity.ToString(), "PlayerInventory.BackpackCapacity");
            Add("party.max_players", $"{ExpeditionParty.MaxPartySize}/{SessionRequest.MaxPartySize}", "ExpeditionParty / SessionRequest MaxPartySize");
            Add("coop.scaling", string.Join(" ", new[] { 1, 2, 3 }.Select(n => $"p{n}: threat x{F(PartyScaling.ThreatMultiplier(n))} hp x{F(PartyScaling.NormalEnemyHealthMultiplier(n))} boss x{F(PartyScaling.BossHealthMultiplier(n))} dmg x{F(PartyScaling.EnemyDamageMultiplier(n))} cap {PartyScaling.ActiveNormalCap(n)}")), "PartyScaling");

            // ---- presentation / QoL values ----
            Add("pickup.attraction", $"base {F(PickupAttractor.DefaultBaseRadiusTiles)} max {F(PickupAttractor.MaxBaseRadiusTiles)}", "PickupAttractor constants");
            var coil = c.Items.FirstOrDefault(i => i != null && i.Id == "accessory_magnetic_coil");
            Add("pickup.magnetic_coil_bonus", coil != null ? F(PresentationAudioUxQolValidator.CoilRadiusBonus(coil)) : null, "Magnetic Coil flat PickupAttractionRadius");
            var aim = c.AimAssist;
            Add("aim_assist", aim != null ? $"mouse {F(aim.MouseHalfAngleDegrees)} controller {F(aim.ControllerHalfAngleDegrees)} proximity {F(aim.CrosshairProximityPixels)}px ppu {F(aim.PixelsPerUnit)} classes " + string.Join(",", Enum.GetValues(typeof(WeaponClass)).Cast<WeaponClass>().Select(k => $"{k}:{F(aim.MultiplierFor(k))}")) : null, "AimAssistConfig");

            // ---- counts ----
            Add("count.audio_events", c.AudioEvents != null ? c.AudioEvents.Events.Count(e => e != null).ToString() : null, "AudioEventCatalog");
            Add("count.rooms", c.Rooms.Count(r => r != null).ToString(), "catalog rooms");
            Add("count.items", c.Items.Count(i => i != null).ToString(), "catalog items");
            Add("count.weapons", weapons.Count.ToString(), "catalog weapons");
            Add("count.enemies_elites_bosses", $"{c.Enemies.Count(e => e != null)}/{c.Elites.Count(e => e != null)}/{c.Bosses.Count(b => b != null)}", "catalog enemies/elites/bosses");
            Add("save.version", SaveSlot.CurrentVersion.ToString(), "SaveSlot.CurrentVersion");
            return rows;
        }

        public static string Fingerprint(WeaponDefinition w)
        {
            var common = $"{w.WeaponClass} kb {F(w.Knockback)} stg {F(w.StaggerPower)}";
            return w switch
            {
                RangedWeaponDefinition r => $"ranged {common} dmg {r.DamageMin}-{r.DamageMax} rate {F(r.FireRate)} mag {r.MagazineSize} reload {F(r.ReloadTime)} range {F(r.Range)} speed {F(r.ProjectileSpeed)} ammo {r.AmmoType}x{r.AmmoCostPerShot} pellets {r.ProjectilesPerShot} spread {F(r.SpreadDegrees)} blast {F(r.ExplosionRadiusTiles)}",
                MeleeWeaponDefinition m => $"melee {common} dmg {m.DamageMin}-{m.DamageMax} rate {F(m.AttackRate)} reach {F(m.AttackRange)} arc {F(m.AttackArcDegrees)} windup {F(m.WindUpSeconds)} recovery {F(m.RecoverySeconds)}",
                BowWeaponDefinition b => $"bow {common} quick {b.QuickDamageMin}-{b.QuickDamageMax} full {b.FullDrawDamageMin}-{b.FullDrawDamageMax} charge {F(b.FullChargeSeconds)} speed {F(b.QuickProjectileSpeed)}/{F(b.FullProjectileSpeed)} range {F(b.QuickRange)}/{F(b.FullRange)}",
                BlasterWeaponDefinition x => $"blaster {common} dmg {x.DamageMin}-{x.DamageMax} rate {F(x.FireRate)} range {F(x.Range)} speed {F(x.ProjectileSpeed)} heat {F(x.HeatPerShot)}/{F(x.MaxHeat)} cool {F(x.CoolingRatePerSecond)} delay {F(x.CoolingDelaySeconds)} lockout {F(x.OverheatLockoutSeconds)}",
                _ => "unknown " + common
            };
        }

        /// <summary>CSV text of a snapshot (key,value,source), the format of the committed baseline.</summary>
        public static string ToCsv(IEnumerable<Row> rows)
        {
            static string Cell(string v) => v != null && (v.Contains(',') || v.Contains('"')) ? "\"" + v.Replace("\"", "\"\"") + "\"" : v ?? string.Empty;
            return "key,value,source\n" + string.Join("\n", rows.Select(r => $"{Cell(r.Key)},{Cell(r.Value)},{Cell(r.Source)}")) + "\n";
        }

        /// <summary>Parses the key/value columns of a snapshot CSV (quoted cells allowed).</summary>
        public static Dictionary<string, string> ParseCsv(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return result;
            foreach (var line in text.Split('\n').Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cells = new List<string>();
                var cell = new System.Text.StringBuilder();
                var quoted = false;
                for (var i = 0; i < line.Length; i++)
                {
                    var ch = line[i];
                    if (quoted)
                    {
                        if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                        else if (ch == '"') quoted = false;
                        else cell.Append(ch);
                    }
                    else if (ch == '"') quoted = true;
                    else if (ch == ',') { cells.Add(cell.ToString()); cell.Clear(); }
                    else if (ch != '\r') cell.Append(ch);
                }

                cells.Add(cell.ToString());
                if (cells.Count >= 2) result[cells[0]] = cells[1];
            }

            return result;
        }
    }
}
