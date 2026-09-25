using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RuinRail.App;
using RuinRail.Audio;
using RuinRail.Core;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Player;
using RuinRail.Presentation.World;
using RuinRail.UI.Codex;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// The contract gate for the presentation / audio / UX-QoL pass.
    ///
    /// Every rule here guards something this pass could quietly lose later: an audio mix that drifts back to all-1.0
    /// gain, a substrate that stops being darker than the floor it sits under, a base pickup radius that creeps up
    /// into a room vacuum, a Magnetic Coil that stops being worth wearing, a Codex that outlives the rules it
    /// describes, or a setting whose row exists while nothing reads it — the dead-seam defect this project keeps
    /// finding. Counting content would pass all of those.
    ///
    /// <see cref="Validate"/> takes its inputs, so a deliberately broken fixture can be fed in and the gate proven to
    /// fail rather than assumed to work.
    /// </summary>
    public static class PresentationAudioUxQolValidator
    {
        public const string ReportPath = "TestResults/presentation_audio_ux_qol.md";

        /// <summary>A substrate must be at most this fraction of its biome floor's luminance to read as underlay.</summary>
        public const float SubstrateMaxFloorLuminanceFraction = 0.5f;

        /// <summary>Authored floor base colours per biome (TileFactory), the thing the substrate must sit under.</summary>
        public static readonly IReadOnlyDictionary<Biome, string> FloorBaseHex = new Dictionary<Biome, string>
        {
            { Biome.RuinedMetro, "#4E5153" }, { Biome.Rustworks, "#393E41" }, { Biome.OvergrownLabs, "#7C837C" }
        };

        /// <summary>No release event may sit at full gain: an authored mix is the point of the pass.</summary>
        public const float MaxEventGain = 0.99f;

        /// <summary>Nor may the mix collapse to near-silence, which would be "authored" in name only.</summary>
        public const float MinEventGain = 0.15f;

        /// <summary>Events fired many times a second in normal play; these carry the repetition policy that matters.</summary>
        public static readonly IReadOnlyList<string> HighFrequencyEvents = new[]
        {
            AudioEventIds.FirePistol, AudioEventIds.FireSmg, AudioEventIds.FireAssaultRifle, AudioEventIds.FireBattleRifle,
            AudioEventIds.FireShotgun, AudioEventIds.FireBlaster, AudioEventIds.KnifeSlash, AudioEventIds.SpearThrust,
            AudioEventIds.DryFire, AudioEventIds.EnemyHit, AudioEventIds.EnemyStagger, AudioEventIds.EnemyDeath,
            AudioEventIds.EnemyTelegraph, AudioEventIds.PickupCoins, AudioEventIds.PickupItem,
            AudioEventIds.DropCommon, AudioEventIds.DropUncommon, AudioEventIds.DropRare,
            AudioEventIds.UiNavigate, AudioEventIds.PlayerHit, AudioEventIds.HazardDamage, AudioEventIds.PlayerDash
        };

        /// <summary>The Codex sections the manual must keep. Losing one is losing a rule the player needs.</summary>
        public static readonly IReadOnlyList<string> RequiredCodexSections = new[]
        {
            CodexContent.CoreRunRulesId, CodexContent.LoadoutId, CodexContent.AmmoId, CodexContent.RarityId,
            CodexContent.CombatId, CodexContent.CoopId, CodexContent.ControlsId, CodexContent.SettingsId
        };

        /// <summary>Gameplay actions the input map must keep, including the one this pass added.</summary>
        public static readonly IReadOnlyList<string> RequiredInputActions = new[]
        {
            "Move", "Aim", "Fire", "Special", "Dash", "Reload", "Interact",
            "Weapon1", "Weapon2", "WeaponSwap", "Consumable", "QuickGrenade", "Inventory", "Pause"
        };

        public sealed class Line
        {
            public string Rule = string.Empty;
            public string Subject = string.Empty;
            public string Detail = string.Empty;
            public readonly List<string> Problems = new();
            public bool Pass => Problems.Count == 0;
        }

        public sealed class Report
        {
            public readonly List<Line> Lines = new();
            public int Failed => Lines.Count(l => !l.Pass);
            public bool Pass => Lines.Count > 0 && Lines.All(l => l.Pass);

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine("# RUINRAIL presentation / audio / UX-QoL contract");
                sb.AppendLine();
                sb.AppendLine($"Result: **{(Pass ? "PASS" : "FAIL")}** — {Lines.Count} rules checked, {Lines.Count - Failed} clean, {Failed} with problems.");
                sb.AppendLine();
                sb.AppendLine("| Rule | Subject | Detail | Result |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var l in Lines.OrderBy(l => l.Rule, StringComparer.Ordinal).ThenBy(l => l.Subject, StringComparer.Ordinal))
                    sb.AppendLine($"| {l.Rule} | {l.Subject} | {l.Detail} | {(l.Pass ? "PASS" : "FAIL: " + string.Join("; ", l.Problems))} |");
                return sb.ToString();
            }
        }

        [MenuItem("RuinRail/Production/Validate Presentation / Audio / UX-QoL")]
        public static void ValidateMenu() => Debug.Log(WriteReport().ToMarkdown());

        public static Report WriteReport()
        {
            var report = Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ReportPath)) ?? ".");
            File.WriteAllText(ReportPath, report.ToMarkdown());
            return report;
        }

        public static Report Validate()
        {
            var catalog = GameContentCatalog.Load();
            var events = catalog?.AudioEvents != null ? catalog.AudioEvents.Events.Where(e => e != null).ToList() : new List<AudioEventDefinition>();
            var items = catalog != null ? catalog.Items.Where(i => i != null).ToList() : new List<ItemDefinition>();
            return Validate(events, items, PickupAttractor.DefaultBaseRadiusTiles);
        }

        /// <summary>Test seam: the same rules over injected content, so a broken fixture can be proven to fail.</summary>
        public static Report Validate(IReadOnlyList<AudioEventDefinition> events, IReadOnlyList<ItemDefinition> items, float basePickupRadiusTiles)
        {
            var report = new Report();
            events ??= Array.Empty<AudioEventDefinition>();
            items ??= Array.Empty<ItemDefinition>();

            // ---- 1. world substrate: exists per biome, quieter than that biome's floor, non-traversable ----
            foreach (Biome biome in Enum.GetValues(typeof(Biome)))
            {
                var (baseColor, dark, light) = WorldSubstrate.Palette(biome);
                var floor = WorldSubstrate.Luminance(Hex(FloorBaseHex[biome]));
                var substrate = WorldSubstrate.Luminance(baseColor);
                var line = new Line
                {
                    Rule = "substrate",
                    Subject = biome.ToString(),
                    Detail = $"L {substrate:F4} vs floor {floor:F4} ({(floor > 0f ? substrate / floor * 100f : 0f):F0}% of floor)"
                };
                if (floor <= 0f || substrate > floor * SubstrateMaxFloorLuminanceFraction)
                    line.Problems.Add($"substrate is not clearly quieter than the floor (max {SubstrateMaxFloorLuminanceFraction:P0} of it)");
                if (WorldSubstrate.Luminance(light) <= WorldSubstrate.Luminance(dark))
                    line.Problems.Add("the substrate ramp has no texture: its light note is not brighter than its dark one");
                if (WorldSubstrate.ClearColorFor(biome) == Color.black)
                    line.Problems.Add("the camera clear colour is still raw black");
                report.Lines.Add(line);
            }

            // A substrate is presentation: the component may never carry a collider, and it must render below the
            // floor tilemaps (which sit at order 0 on the Ground layer).
            var sorting = new Line
            {
                Rule = "substrate",
                Subject = "non-traversable",
                Detail = $"Ground layer, order {WorldSubstrate.SortingOrder}, no collider, margin {WorldSubstrate.MarginTiles} tiles"
            };
            if (WorldSubstrate.SortingOrder >= 0) sorting.Problems.Add("the substrate does not render below the floor");
            if (typeof(WorldSubstrate).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Any(f => typeof(Collider2D).IsAssignableFrom(f.FieldType)))
                sorting.Problems.Add("the substrate holds a collider field: it would become traversable geometry");
            if (WorldSubstrate.MarginTiles < 12f) sorting.Problems.Add("the substrate margin is smaller than the camera's reach past a layout edge");
            report.Lines.Add(sorting);

            // ---- 2. audio: an authored mix, and a repetition policy where it matters ----
            var mix = new Line { Rule = "audio mix", Subject = "authored gain", Detail = events.Count + " events" };
            if (events.Count == 0) mix.Problems.Add("no audio events were found at all");
            var atFullGain = events.Where(e => e.Volume > MaxEventGain).Select(e => e.Id).ToList();
            if (atFullGain.Count > 0) mix.Problems.Add($"{atFullGain.Count} event(s) still at full gain: {string.Join(", ", atFullGain.Take(6))}");
            var tooQuiet = events.Where(e => e.Volume < MinEventGain).Select(e => e.Id).ToList();
            if (tooQuiet.Count > 0) mix.Problems.Add($"{tooQuiet.Count} event(s) below the mix floor: {string.Join(", ", tooQuiet.Take(6))}");
            if (events.Count > 0)
            {
                var loudest = events.Max(e => e.Volume);
                var quietest = events.Min(e => e.Volume);
                mix.Detail += $", gain {quietest:F2}..{loudest:F2}";
                // A mix is a mix, not a ladder of extremes: more than a 12x spread is not restraint.
                if (quietest > 0f && loudest / quietest > 12f) mix.Problems.Add($"extreme gain spread ({loudest / quietest:F1}x)");
            }

            report.Lines.Add(mix);

            foreach (var id in HighFrequencyEvents)
            {
                var definition = events.FirstOrDefault(e => e.Id == id);
                var line = new Line { Rule = "repetition policy", Subject = id };
                if (definition == null)
                {
                    line.Problems.Add("event not found");
                    report.Lines.Add(line);
                    continue;
                }

                line.Detail = $"pitch {definition.PitchMin:F2}..{definition.PitchMax:F2}, interval {definition.MinIntervalMs} ms, max {definition.MaxInstances}, {definition.Clips?.Length ?? 0} clip(s)";
                // Variation OR a throttle is not enough on its own for a sound fired this often: it needs both.
                if (definition.PitchMax - definition.PitchMin < 0.01f && (definition.Clips?.Length ?? 0) < 2)
                    line.Problems.Add("no variation: one clip and no pitch range");
                if (definition.MinIntervalMs <= 0) line.Problems.Add("no minimum interval");
                if (definition.MaxInstances > 6) line.Problems.Add($"instance cap {definition.MaxInstances} is too loose for a high-frequency event");
                report.Lines.Add(line);
            }

            // Loops must never be pitch-varied: a randomised loop drifts audibly against itself.
            foreach (var definition in events.Where(e => e.Loop))
            {
                var line = new Line { Rule = "loop policy", Subject = definition.Id, Detail = $"pitch {definition.PitchMin:F2}..{definition.PitchMax:F2}, max {definition.MaxInstances}" };
                if (definition.PitchMax - definition.PitchMin > 0.001f) line.Problems.Add("a looping event must not be pitch-randomised");
                if (definition.MaxInstances != 1) line.Problems.Add("a looping event must be capped at one instance");
                report.Lines.Add(line);
            }

            // ---- 3. pickup attraction: small base, Coil strictly stronger ----
            var coil = items.FirstOrDefault(i => i != null && i.Id == "accessory_magnetic_coil");
            var coilBonus = CoilRadiusBonus(coil);
            var attraction = new Line
            {
                Rule = "pickup attraction",
                Subject = "base vs Magnetic Coil",
                Detail = $"base {basePickupRadiusTiles:F2} tiles, Coil +{coilBonus:F2} tiles"
            };
            if (basePickupRadiusTiles <= 0f) attraction.Problems.Add("base attraction is off: the QoL change is not wired");
            if (basePickupRadiusTiles > PickupAttractor.MaxBaseRadiusTiles)
                attraction.Problems.Add($"base attraction {basePickupRadiusTiles:F2} exceeds the declared bound of {PickupAttractor.MaxBaseRadiusTiles:F2} tiles");
            if (coil == null) attraction.Problems.Add("the Magnetic Coil accessory was not found");
            else if (coilBonus <= 0f) attraction.Problems.Add("the Magnetic Coil grants no pickup attraction radius");
            else if (coilBonus <= basePickupRadiusTiles)
                attraction.Problems.Add($"the Magnetic Coil (+{coilBonus:F2}) no longer adds more than the base radius ({basePickupRadiusTiles:F2})");
            report.Lines.Add(attraction);

            // ---- 4. status display: every runtime timed effect can be shown ----
            var timed = items.OfType<ConsumableDefinition>().Where(c => c.EffectKind == ConsumableEffectKind.TimedBuff).ToList();
            foreach (var definition in timed)
            {
                var line = new Line
                {
                    Rule = "status display",
                    Subject = definition.Id,
                    Detail = $"{definition.BuffDurationSeconds:F0}s, {definition.BuffStat} {definition.BuffPercent:+0;-0}%"
                };
                if (definition.BuffDurationSeconds <= 0f) line.Problems.Add("a timed buff with no duration cannot show a timer");
                if (definition.Icon == null) line.Problems.Add("no icon: the HUD chip would have nothing to draw");
                if (string.IsNullOrWhiteSpace(definition.DisplayName)) line.Problems.Add("no display name for the status detail line");
                report.Lines.Add(line);
            }

            var statusCount = new Line { Rule = "status display", Subject = "HUD capacity", Detail = $"{timed.Count} timed effect(s), {RuinRail.UI.Hud.HudSnapshot.MaxStatusEffects} chips" };
            if (timed.Count == 0) statusCount.Problems.Add("no runtime timed effect exists, so the status strip would be dead UI");
            if (timed.Count > RuinRail.UI.Hud.HudSnapshot.MaxStatusEffects)
                statusCount.Problems.Add($"more timed effects ({timed.Count}) than chips ({RuinRail.UI.Hud.HudSnapshot.MaxStatusEffects})");
            report.Lines.Add(statusCount);

            // ---- 5. grenade quick-use: the action exists, and a grenade can actually be thrown ----
            var actions = InputActionNames();
            foreach (var name in RequiredInputActions)
            {
                var line = new Line { Rule = "input action", Subject = name, Detail = actions.Count + " actions in the Player map" };
                if (!actions.Contains(name)) line.Problems.Add("action missing from the input map");
                report.Lines.Add(line);
            }

            var grenades = items.OfType<ConsumableDefinition>().Where(c => c.EffectKind == ConsumableEffectKind.Grenade).ToList();
            var quick = new Line { Rule = "grenade quick-use", Subject = "authoritative path", Detail = grenades.Count + " grenade consumable(s)" };
            if (grenades.Count == 0) quick.Problems.Add("no grenade consumable exists, so quick-use would be dead UI");
            // The seam that was dead before this pass: without a launcher on the player, every grenade is refused.
            if (!SourceContains("Assets/Game/Scripts/App/PlayerRigComposer.cs", "AddComponent<GrenadeLauncher>"))
                quick.Problems.Add("the player rig composes no GrenadeLauncher: grenades cannot be thrown at all");
            if (!SourceContains("Assets/Game/Scripts/Player/PlayerConsumableUser.cs", "TryQuickGrenade"))
                quick.Problems.Add("no quick-grenade entry point on the consumable user");
            if (!SourceContains("Assets/Game/Scripts/Core/Input/PlayerInputReader.cs", "if (GameplayAllowed) QuickGrenadeUsed"))
                quick.Problems.Add("the quick-grenade press is not gated by GameplayInputGate");
            report.Lines.Add(quick);

            // ---- 6. aim assist: the setting reaches the real shot path ----
            var assist = new Line { Rule = "aim assist", Subject = "setting maps to runtime", Detail = "SettingsData.Accessibility.AimAssist -> AssistPreferences -> ShotSolver" };
            if (!SourceContains("Assets/Game/Scripts/Combat/Weapons/ShotSolver.cs", "AssistPreferences.AimAssist"))
                assist.Problems.Add("ShotSolver does not read the aim-assist setting: the row would change nothing");
            if (!SourceContains("Assets/Game/Scripts/UI/Settings/SettingsViewModel.cs", "AssistPreferences.Set"))
                assist.Problems.Add("the settings layer never publishes the aim-assist setting");
            if (!new RuinRail.Persistence.SettingsData().Accessibility.AimAssist)
                assist.Problems.Add("aim assist does not default to ON");
            if (!RuinRail.Core.Rendering.AssistPreferences.AimAssist && !SourceContains("Assets/Game/Scripts/Core/Rendering/AssistPreferences.cs", "AimAssist { get; private set; } = true"))
                assist.Problems.Add("the runtime default is not ON");
            report.Lines.Add(assist);

            // ---- 7. teaching prompt ----
            var prompt = new Line { Rule = "low-ammo prompt", Subject = "contextual and once-only", Detail = "TutorialPromptId.LowAmmo" };
            if (!Enum.IsDefined(typeof(RuinRail.UI.Onboarding.TutorialPromptId), "LowAmmo")) prompt.Problems.Add("no low-ammo prompt exists");
            else
            {
                var text = RuinRail.UI.Onboarding.TutorialPromptText.Build(RuinRail.UI.Onboarding.TutorialPromptId.LowAmmo, null);
                if (string.IsNullOrWhiteSpace(text)) prompt.Problems.Add("the prompt has no copy");
                // It must teach resource management, not order the player into melee.
                if (text.IndexOf("melee", StringComparison.OrdinalIgnoreCase) >= 0) prompt.Problems.Add("the copy tells the player to use melee");
            }

            report.Lines.Add(prompt);

            // ---- 8. Codex: the required sections, and no empty page ----
            var sections = CodexContent.Sections();
            foreach (var id in RequiredCodexSections)
            {
                var section = sections.FirstOrDefault(s => s.Id == id);
                var line = new Line { Rule = "codex section", Subject = id };
                if (section == null) { line.Problems.Add("section missing"); report.Lines.Add(line); continue; }
                line.Detail = $"\"{section.Title}\", {section.Lines.Count} line(s)";
                if (section.Lines.Count == 0) line.Problems.Add("section has no content");
                if (string.IsNullOrWhiteSpace(section.Title)) line.Problems.Add("section has no title");
                report.Lines.Add(line);
            }

            // Facts the Codex states that the code owns: it must not drift from them.
            var facts = new Line { Rule = "codex facts", Subject = "numbers match the code", Detail = $"{PlayerInventory.BackpackCapacity} backpack slots" };
            var loadoutSection = sections.FirstOrDefault(s => s.Id == CodexContent.LoadoutId);
            if (loadoutSection != null && !loadoutSection.Lines.Any(l => l.Contains(PlayerInventory.BackpackCapacity.ToString())))
                facts.Problems.Add("the loadout page does not state the real backpack capacity");
            var combatSection = sections.FirstOrDefault(s => s.Id == CodexContent.CombatId);
            if (combatSection != null && !combatSection.Lines.Any(l => l.IndexOf("aim assist", StringComparison.OrdinalIgnoreCase) >= 0))
                facts.Problems.Add("the combat page does not mention the aim-assist setting");
            report.Lines.Add(facts);

            // ---- 9. glyphs: every control the Codex and the prompts name resolves to text ----
            var glyphs = new RuinRail.UI.Onboarding.SchemeGlyphs(RuinRail.UI.Onboarding.InputScheme.KeyboardMouse);
            foreach (var action in RequiredInputActions)
            {
                var line = new Line { Rule = "glyph", Subject = action, Detail = glyphs.For(action) };
                // An unmapped action falls back to its own name, which is the omission this catches.
                if (string.IsNullOrWhiteSpace(line.Detail) || line.Detail == action) line.Problems.Add("no glyph text for this action");
                report.Lines.Add(line);
            }

            // ---- 10. fonts: the UI face is the authored pixel font, not a Unity built-in placeholder ----
            var font = new Line { Rule = "font", Subject = "UI face" };
            var uiFont = RuinRail.UI.Theme.UiFont.Font();
            font.Detail = uiFont != null ? uiFont.name : "(none)";
            if (uiFont == null) font.Problems.Add("no UI font resolved");
            else if (uiFont.name.IndexOf("Arial", StringComparison.OrdinalIgnoreCase) >= 0 || uiFont.name.IndexOf("LegacyRuntime", StringComparison.OrdinalIgnoreCase) >= 0)
                font.Problems.Add($"the UI still uses the Unity built-in placeholder face ({uiFont.name})");
            report.Lines.Add(font);

            // ---- 11. one listener ----
            var listener = new Line { Rule = "audio listener", Subject = "single rig", Detail = "AudioListenerRig owns the process listener" };
            // Runtime code only: an editor tool (this validator included) naming the call is not composing a listener.
            var listenerSites = SourceMatches("Assets/Game/Scripts", "AddComponent<AudioListener>", excludeDirectory: "Assets/Game/Scripts/Editor");
            listener.Detail += $" ({listenerSites} runtime site(s))";
            if (listenerSites != 1) listener.Problems.Add($"{listenerSites} runtime places add an AudioListener; exactly one (AudioListenerRig) may");
            if (SourceContains("Assets/Game/Scripts/App/ExpeditionScene.cs", "camGo.AddComponent<AudioListener>"))
                listener.Problems.Add("the dungeon camera adds a second listener");
            report.Lines.Add(listener);

            // ---- 12. Shelter Trader presentation parity ----
            var trader = new Line { Rule = "shelter trader", Subject = "presentation parity", Detail = "MerchantRowView rows + details pane + comparison" };
            foreach (var (needle, problem) in new[]
                     {
                         ("MerchantRowView.Create", "the counter does not use the merchant's row primitive (no icon, rarity or price on the row)"),
                         ("ItemDetailLayout.Compose", "the counter has no details pane"),
                         ("ShelterTraderPresentation.CompareFor", "the counter does not compare an offer against the equipped item")
                     })
            {
                if (!SourceContains("Assets/Game/Scripts/App/BaseHubScreen.cs", needle)) trader.Problems.Add(problem);
            }

            report.Lines.Add(trader);

            // ---- 13. prop dressing stays presentation ----
            var dressing = new Line
            {
                Rule = "prop dressing",
                Subject = "presentation only",
                Detail = $"FloorDetail only, {RuinRail.Dungeon.Runtime.RoomPropDressing.DoorClearanceTiles}-tile door clearance, <= {RuinRail.Dungeon.Runtime.RoomPropDressing.MaxAddedFraction:P0} of floor added"
            };
            if (SourceContains("Assets/Game/Scripts/Dungeon/Runtime/RoomPropDressing.cs", "RoomTilemapLayer.Walls, ") ||
                SourceContains("Assets/Game/Scripts/Dungeon/Runtime/RoomPropDressing.cs", "SetTile(cell, null)"))
                dressing.Problems.Add("the dressing pass writes outside the FloorDetail layer");
            if (RuinRail.Dungeon.Runtime.RoomPropDressing.DoorClearanceTiles < 1) dressing.Problems.Add("no door clearance: dressing could block a doorway");
            if (RuinRail.Dungeon.Runtime.RoomPropDressing.MaxAddedFraction > 0.1f) dressing.Problems.Add("the density cap is high enough to hide a telegraph");
            report.Lines.Add(dressing);

            return report;
        }

        /// <summary>The flat PickupAttractionRadius an accessory grants, or 0.</summary>
        public static float CoilRadiusBonus(ItemDefinition definition)
        {
            if (definition is not AccessoryDefinition accessory) return 0f;
            var total = 0f;
            foreach (var modifier in accessory.BaseModifiers())
            {
                if (modifier.Stat == RuinRail.Gameplay.Stats.StatId.PickupAttractionRadius && modifier.Kind == RuinRail.Gameplay.Stats.StatModifierKind.Flat)
                    total += modifier.Value;
            }

            return total;
        }

        private static Color Hex(string hex)
        {
            var h = hex.TrimStart('#');
            return new Color(
                Convert.ToInt32(h.Substring(0, 2), 16) / 255f,
                Convert.ToInt32(h.Substring(2, 2), 16) / 255f,
                Convert.ToInt32(h.Substring(4, 2), 16) / 255f);
        }

        private static HashSet<string> InputActionNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            const string path = "Assets/Game/Settings/Input/RuinRailInputActions.inputactions";
            if (!File.Exists(path)) return names;
            var json = File.ReadAllText(path);
            // The action names are the "name" keys inside the map's "actions" array; the bindings that follow name
            // their action through "action", so reading the actions block alone cannot count a binding twice.
            var start = json.IndexOf("\"actions\"", StringComparison.Ordinal);
            var end = json.IndexOf("\"bindings\"", StringComparison.Ordinal);
            if (start < 0 || end < 0 || end <= start) return names;
            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(json.Substring(start, end - start), "\"name\"\\s*:\\s*\"([^\"]+)\""))
                names.Add(match.Groups[1].Value);
            return names;
        }

        private static bool SourceContains(string path, string needle) => File.Exists(path) && File.ReadAllText(path).Contains(needle, StringComparison.Ordinal);

        private static int SourceMatches(string directory, string needle, string excludeDirectory = null)
        {
            if (!Directory.Exists(directory)) return 0;
            return Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(f => excludeDirectory == null || !f.Replace('\\', '/').StartsWith(excludeDirectory, StringComparison.Ordinal))
                .Count(f => File.ReadAllText(f).Contains(needle, StringComparison.Ordinal));
        }
    }
}
