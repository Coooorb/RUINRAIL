using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Progression;
using RuinRail.Gameplay.Stats;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// The no-dead-content gate.
    ///
    /// It exists because RUINRAIL has shipped the same defect three times: a stat or adapter is authored correctly,
    /// unit-tested in isolation, and never reached by runtime composition — <c>SkillStatSource</c> (every progression
    /// attribute inert), <c>SetAmmoCapacityBonusProvider</c> (Ammo Pouch inert), and nine stats with no reader at all.
    /// Counting content cannot catch that, and neither can a test that asserts on the stat pipeline, because the
    /// pipeline is not the thing that is broken.
    ///
    /// The validator therefore asks four different questions, because no single one of them is sufficient:
    ///
    ///   1. STATIC READER — does some non-Editor gameplay file actually read this StatId from the provider?
    ///      Catches "the stat is granted but nothing consumes it".
    ///   2. COMPOSITION WIRING — is each provider seam (a Func/Action the runtime must hand in) called by something
    ///      other than a test? Catches the SkillStatSource / Ammo Pouch class specifically.
    ///   3. ACQUISITION POOL — can the player obtain an item whose granted stat resolves to nothing on that item?
    ///      Catches "the consumer exists but this family's base value is zero", which a reader scan alone cannot see.
    ///   4. FACADE CALLERS — every weapon stat reads its provider inside WeaponStatMath, so question 1 would be
    ///      satisfied by that one file even if no weapon ever called it. Each public entry point of the facade must
    ///      therefore have a caller in some other gameplay file, or the "reader" is a reader on nobody's behalf.
    ///
    /// A granted, player-facing stat must resolve to a real consumer, or be excluded from V1 acquisition, or appear in
    /// <see cref="DeferredStats"/> with a written reason. Anything else fails.
    /// </summary>
    public static class StatConsumerIntegrityValidator
    {
        public const string ReportPath = "TestResults/stat_consumer_integrity.md";
        public const string SourceRoot = "Assets/Game/Scripts";

        /// <summary>
        /// Stats that are deliberately not consumed in V1, with the reason. An entry here is a documented design gap,
        /// not permission to leave content dead: the acquisition check still fails if the player can obtain an item
        /// whose only effect is a deferred stat.
        /// </summary>
        public static readonly IReadOnlyDictionary<StatId, string> DeferredStats = new Dictionary<StatId, string>
        {
            [StatId.WeaponSwitchSpeed] =
                "Weapon switching is instantaneous: WeaponLoadout.SelectSlot has no duration, no weapon definition or " +
                "PlayerBalanceConfig authors an equip time, and no approved document states one. Giving it a duration " +
                "would be a new mechanic and a new balance value, so it stays deferred (pass Phase 4). The Handling " +
                "attribute keeps its implemented Reload Speed half; the Quickdraw Holster is excluded from V1 acquisition."
        };

        /// <summary>
        /// Runtime seams a composition root must wire. Each is a method that installs a provider/adapter; if only tests
        /// call it, the feature behind it is inert in the shipped game. This is the SkillStatSource failure class.
        /// </summary>
        public static readonly (string Method, string Feature)[] CompositionSeams =
        {
            ("SetAmmoCapacityBonusProvider", "Ammo Stack Capacity (Ammo Pouch) -> PlayerInventory backpack limits"),
            ("ApplyProgression", "Permanent attribute ranks (SkillStatSource) -> PlayerStats"),
            ("SetStats", "Weapon/movement/impact stat providers -> every stat consumer on the player"),
        };

        /// <summary>
        /// Files that read the stat provider on another system's behalf. A reader inside one of these proves nothing on
        /// its own: the facade itself must be called. Every public static method declared in the file is checked.
        /// </summary>
        public static readonly string[] StatMathFacades = { "WeaponStatMath.cs" };

        /// <summary>
        /// Constructor arguments a composition root must supply for real. A resolver handed in as <c>null</c> or
        /// <c>_ =&gt; null</c> compiles, runs, and silently drops everything it was supposed to resolve — which is how
        /// every rolled affix in the game reached the stat pipeline as nothing. Zero-based argument index.
        /// </summary>
        public static readonly (string Type, int Argument, string Feature)[] RequiredProviderArguments =
        {
            ("LoadoutStatRegistrar", 3, "affix rolls on equipped items -> PlayerStats"),
        };

        public sealed class Line
        {
            public string Subject = string.Empty;
            public string Kind = string.Empty;
            public string Detail = string.Empty;
            public readonly List<string> Problems = new();
            public bool Pass => Problems.Count == 0;
        }

        public sealed class Report
        {
            public readonly List<Line> Lines = new();
            public readonly List<string> Problems = new();
            public int Failed => Lines.Count(l => !l.Pass);
            public bool Pass => Problems.Count == 0 && Lines.Count > 0 && Lines.All(l => l.Pass);

            /// <summary>Stats granted by content that no gameplay system reads (the headline failure).</summary>
            public readonly List<StatId> UnconsumedGrantedStats = new();

            /// <summary>Composition seams only a test calls.</summary>
            public readonly List<string> UnwiredSeams = new();

            /// <summary>Stat-math facade entry points nothing in gameplay calls.</summary>
            public readonly List<string> UncalledFacadeMethods = new();

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine("# RUINRAIL stat consumer integrity");
                sb.AppendLine();
                sb.AppendLine($"Result: **{(Pass ? "PASS" : "FAIL")}** — {Lines.Count} subjects checked, {Lines.Count - Failed} clean, {Failed} with problems.");
                sb.AppendLine();
                sb.AppendLine("| Subject | Kind | Detail | Result |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var l in Lines.OrderBy(l => l.Kind, StringComparer.Ordinal).ThenBy(l => l.Subject, StringComparer.Ordinal))
                    sb.AppendLine($"| {l.Subject} | {l.Kind} | {l.Detail} | {(l.Pass ? "PASS" : "FAIL: " + string.Join("; ", l.Problems))} |");
                sb.AppendLine();
                sb.AppendLine("## Deferred stats (documented, not dead by accident)");
                foreach (var kv in DeferredStats) sb.AppendLine($"- **{kv.Key}** — {kv.Value}");
                foreach (var p in Problems) sb.AppendLine($"- FAIL {p}");
                return sb.ToString();
            }
        }

        [MenuItem("RuinRail/Production/Validate Stat Consumer Integrity")]
        public static void ValidateMenu() => Debug.Log(WriteReport().ToMarkdown());

        public static Report WriteReport()
        {
            var report = Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ReportPath)) ?? ".");
            File.WriteAllText(ReportPath, report.ToMarkdown());
            return report;
        }

        public static Report Validate() => Validate(LoadAllEquipment(), LoadAllAffixPools(), LoadSources());

        /// <summary>Test seam: the same rules over an injected content set, so a test can prove the validator fails on a real break.</summary>
        public static Report Validate(IReadOnlyList<EquipmentItemDefinition> equipment, IReadOnlyList<AffixPool> pools, IReadOnlyDictionary<string, string> sources)
        {
            var report = new Report();
            if (sources == null || sources.Count == 0) report.Problems.Add("no gameplay sources loaded; the reader scan cannot run");
            if (equipment == null || equipment.Count == 0) report.Problems.Add("no equipment definitions found");

            var consumed = new HashSet<StatId>(((StatId[])Enum.GetValues(typeof(StatId))).Where(s => HasReader(s, sources)));

            // ---- 1. static reader scan ----
            foreach (StatId stat in Enum.GetValues(typeof(StatId)))
            {
                var line = new Line { Subject = stat.ToString(), Kind = "StatId", Detail = consumed.Contains(stat) ? "read by a gameplay system" : "no reader" };
                if (!consumed.Contains(stat) && !DeferredStats.ContainsKey(stat))
                {
                    line.Problems.Add("no runtime consumer and no documented deferral");
                    report.UnconsumedGrantedStats.Add(stat);
                }

                if (consumed.Contains(stat) && DeferredStats.ContainsKey(stat))
                    line.Problems.Add("listed as deferred but a consumer now exists — remove it from DeferredStats");
                report.Lines.Add(line);
            }

            // ---- 2. composition wiring ----
            foreach (var (method, feature) in CompositionSeams)
            {
                // A file that declares the method does not count as its own caller. PlayerInventory.SetAmmoCapacityBonusProvider
                // is a one-line pass-through to the container: counting it would have reported the Ammo Pouch as wired
                // for the entire time it was inert.
                var declaration = new Regex(@"(?:public|internal|protected|private)[\w\s<>,\[\]]*?\b" + Regex.Escape(method) + @"\s*\(");
                var callers = sources
                    .Where(kv => Regex.IsMatch(kv.Value, @"\.\s*" + Regex.Escape(method) + @"\s*\(") && !declaration.IsMatch(kv.Value))
                    .Select(kv => kv.Key).ToList();
                var line = new Line { Subject = method, Kind = "CompositionSeam", Detail = callers.Count == 0 ? "no gameplay caller" : string.Join(", ", callers.Take(3)) };
                if (callers.Count == 0)
                {
                    line.Problems.Add($"only tests call it, so '{feature}' is inert at runtime");
                    report.UnwiredSeams.Add(method);
                }

                report.Lines.Add(line);
            }

            // ---- 2b. provider arguments that must not be a null literal ----
            foreach (var (type, index, feature) in RequiredProviderArguments)
            {
                var constructions = sources
                    .SelectMany(kv => Regex.Matches(kv.Value, @"new\s+(?:[\w.]+\.)?" + Regex.Escape(type) + @"\s*\(").Select(m => (File: kv.Key, Args: ArgumentsAt(kv.Value, m.Index + m.Length))))
                    .Where(c => c.Args != null && c.Args.Count > index)
                    .ToList();
                var line = new Line { Subject = type + " arg " + index, Kind = "ProviderArgument", Detail = constructions.Count == 0 ? "never constructed in gameplay" : string.Join("; ", constructions.Select(c => c.File + ": " + c.Args[index])) };
                foreach (var construction in constructions.Where(c => IsNullLiteral(c.Args[index])))
                    line.Problems.Add($"{construction.File} passes '{construction.Args[index]}', so '{feature}' resolves to nothing at runtime");
                report.Lines.Add(line);
            }

            // ---- 3. stat-math facade callers ----
            foreach (var facade in StatMathFacades)
            {
                if (sources == null || !sources.TryGetValue(facade, out var facadeText))
                {
                    report.Problems.Add($"{facade} is listed as a stat-math facade but was not found under {SourceRoot}");
                    continue;
                }

                var type = Path.GetFileNameWithoutExtension(facade);
                foreach (Match declaration in Regex.Matches(facadeText, @"public\s+static\s+[\w<>\[\].?]+\s+(\w+)\s*\("))
                {
                    var method = declaration.Groups[1].Value;
                    var callers = sources.Where(kv => kv.Key != facade && kv.Value.Contains(type + "." + method + "(")).Select(kv => kv.Key).ToList();
                    var line = new Line
                    {
                        Subject = type + "." + method,
                        Kind = "StatMathFacade",
                        Detail = callers.Count == 0 ? "no gameplay caller" : string.Join(", ", callers)
                    };
                    if (callers.Count == 0)
                    {
                        line.Problems.Add("nothing outside the facade calls it, so the stat it reads reaches no weapon");
                        report.UncalledFacadeMethods.Add(line.Subject);
                    }

                    report.Lines.Add(line);
                }
            }

            // ---- 4. acquisition pools ----
            foreach (var item in (equipment ?? Array.Empty<EquipmentItemDefinition>()).OrderBy(e => e.Id, StringComparer.Ordinal))
            {
                var line = new Line { Subject = item.Id ?? item.name, Kind = "Acquirable" + (item.IsAcquirableInV1 ? string.Empty : " (excluded)") };
                var effects = new List<string>();
                foreach (var modifier in item.BaseModifiers())
                {
                    effects.Add(modifier.Stat.ToString());
                    if (!item.IsAcquirableInV1) continue;
                    if (!consumed.Contains(modifier.Stat)) line.Problems.Add($"intrinsic {modifier.Stat} has no runtime consumer");
                    else if (!HasEffectOn(item, modifier.Stat)) line.Problems.Add($"intrinsic {modifier.Stat} can never take effect on this definition");
                }

                if (item.AffixPool != null)
                {
                    foreach (var affix in item.AffixPool.Affixes.Where(a => a != null))
                    {
                        var stat = StatIds.FromAffix(affix.Stat);
                        effects.Add(affix.Id);
                        if (!item.IsAcquirableInV1) continue;
                        if (!consumed.Contains(stat)) line.Problems.Add($"affix {affix.Id} grants {stat}, which has no runtime consumer");
                        else if (!HasEffectOn(item, stat)) line.Problems.Add($"affix {affix.Id} grants {stat}, which is always 0 on this definition");
                    }

                    if (item.AffixPool.Affixes.Count(a => a != null) < 3)
                        line.Problems.Add($"pool {item.AffixPool.Id} has fewer than 3 affixes, so an Epic/Legendary roll of this item fails and silently downgrades to Common");
                }

                line.Detail = effects.Count == 0 ? "no granted stats" : string.Join(", ", effects);
                report.Lines.Add(line);
            }

            // ---- pools themselves ----
            foreach (var pool in (pools ?? Array.Empty<AffixPool>()).OrderBy(p => p.Id, StringComparer.Ordinal))
            {
                var line = new Line { Subject = pool.Id ?? pool.name, Kind = "AffixPool", Detail = string.Join(", ", pool.Affixes.Where(a => a != null).Select(a => a.Id)) };
                foreach (var affix in pool.Affixes.Where(a => a != null))
                {
                    var stat = StatIds.FromAffix(affix.Stat);
                    if (!consumed.Contains(stat)) line.Problems.Add($"{affix.Id} grants {stat}, which has no runtime consumer");
                }

                report.Lines.Add(line);
            }

            // ---- progression ----
            foreach (var skill in SkillRules.All)
            {
                var affected = SkillCatalog.AffectedStats(skill);
                var line = new Line { Subject = skill.ToString(), Kind = "Progression", Detail = string.Join(", ", affected) };
                if (affected.Count == 0) line.Problems.Add("attribute grants no stat");
                else if (affected.All(s => !consumed.Contains(s))) line.Problems.Add("every stat this attribute grants is unconsumed");
                report.Lines.Add(line);
            }

            return report;
        }

        /// <summary>
        /// Whether a stat can produce a non-zero result on this specific definition. A consumer existing somewhere is
        /// not enough: Knockback is read by every weapon, but multiplies an authored 0 on a pistol, so a Knockback
        /// affix on a pistol is still a no-op. Only stats whose effect is gated by an authored base need a check here.
        /// </summary>
        public static bool HasEffectOn(EquipmentItemDefinition definition, StatId stat)
        {
            var weapon = definition as WeaponDefinition;
            switch (stat)
            {
                // On a weapon these scale that weapon's own authored base, so a 0 base makes them a no-op. On armor or
                // an accessory they scale whatever weapon the player carries, which makes them conditional (like
                // Blaster Cooling Rate without a blaster), not dead.
                case StatId.Knockback: return weapon == null || weapon.Knockback > 0f;
                case StatId.StaggerPower: return weapon == null || weapon.StaggerPower > 0f;
                case StatId.WeaponSpreadReduction: return weapon is not RangedWeaponDefinition ranged || ranged.SpreadDegrees > 0f;
                case StatId.MagazineSize: return weapon is not RangedWeaponDefinition magazine || magazine.MagazineSize > 0;
                default: return true;
            }
        }

        /// <summary>A lambda or literal that always yields null, however it is spelled.</summary>
        private static bool IsNullLiteral(string argument)
        {
            var text = argument.Trim();
            return text == "null" || Regex.IsMatch(text, @"^(\w+|\([^)]*\))\s*=>\s*(\(\w+\))?\s*null$");
        }

        /// <summary>Top-level, comma-separated arguments starting just after an opening parenthesis, or null if unbalanced.</summary>
        private static List<string> ArgumentsAt(string text, int start)
        {
            var arguments = new List<string>();
            var depth = 0;
            var current = new StringBuilder();
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '(' || c == '[' || c == '<' && i > 0 && char.IsLetterOrDigit(text[i - 1])) depth++;
                else if (c == ')' && depth == 0)
                {
                    if (current.ToString().Trim().Length > 0 || arguments.Count > 0) arguments.Add(current.ToString());
                    return arguments;
                }
                else if (c == ')' || c == ']' || c == '>' && depth > 0) depth--;
                else if (c == ',' && depth == 0)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            return null;
        }

        public static bool HasReader(StatId stat, IReadOnlyDictionary<string, string> sources)
        {
            var pattern = @"Get(?:Multiplier|Percent|Flat|ReductionFactor|PercentUncapped)\(\s*StatId\." + stat + @"\s*\)";
            return sources != null && sources.Values.Any(t => Regex.IsMatch(t, pattern));
        }

        public static List<EquipmentItemDefinition> LoadAllEquipment() =>
            AssetDatabase.FindAssets("t:ItemDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .OfType<EquipmentItemDefinition>()
                .Where(d => d.Category != ItemCategory.Consumable && d.Category != ItemCategory.Ammo)
                .ToList();

        public static List<AffixPool> LoadAllAffixPools() =>
            AssetDatabase.FindAssets("t:AffixPool").Select(g => AssetDatabase.LoadAssetAtPath<AffixPool>(AssetDatabase.GUIDToAssetPath(g))).Where(p => p != null).ToList();

        /// <summary>Non-Editor gameplay sources keyed by file name. Editor and test code is excluded: neither ships.</summary>
        public static Dictionary<string, string> LoadSources()
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!Directory.Exists(SourceRoot)) return files;
            foreach (var path in Directory.GetFiles(SourceRoot, "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                if (path.Replace('\\', '/').Contains("/Editor/")) continue;
                files[Path.GetFileName(path)] = File.ReadAllText(path);
            }

            return files;
        }
    }
}
