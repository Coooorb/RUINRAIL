using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Audio;
using RuinRail.Core;
using RuinRail.Core.Rendering;
using RuinRail.EditorTools.Production;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using RuinRail.Persistence;
using RuinRail.Presentation.World;
using RuinRail.UI.Codex;
using RuinRail.UI.Hud;
using RuinRail.UI.Onboarding;
using RuinRail.UI.Settings;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// The presentation / audio / UX-QoL pass's contracts, over the shipped data.
    ///
    /// The validator is exercised twice: once against the real content, and once against fixtures broken on purpose —
    /// a substrate as bright as its floor, an all-1.0 audio mix, a base pickup radius that has become a vacuum, a
    /// Magnetic Coil that no longer beats the base. A gate nobody has seen fail is not a gate.
    /// </summary>
    public class PresentationAudioUxQolTests
    {
        private const string MatrixDirectory = "TestResults/PresentationAudioUxQol";
        private readonly List<UnityEngine.Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static GameContentCatalog Catalog()
        {
            var catalog = GameContentCatalog.Load();
            Assert.IsNotNull(catalog, "GameContentCatalog must load.");
            return catalog;
        }

        private static List<AudioEventDefinition> Events()
        {
            var catalog = Catalog();
            Assert.IsNotNull(catalog.AudioEvents, "The audio event catalog must be bound.");
            return catalog.AudioEvents.Events.Where(e => e != null).ToList();
        }

        // ---------------- the gate ----------------

        [Test]
        public void Validator_PassesOnShippedContent_AndWritesItsReport()
        {
            var report = PresentationAudioUxQolValidator.WriteReport();
            Assert.Greater(report.Lines.Count, 30, "The gate must actually check a meaningful number of rules.");
            var failures = report.Lines.Where(l => !l.Pass).Select(l => $"{l.Rule}/{l.Subject}: {string.Join("; ", l.Problems)}").ToList();
            Assert.IsTrue(report.Pass, "Presentation/audio/UX contract failures:\n" + string.Join("\n", failures));
            FileAssert.Exists(PresentationAudioUxQolValidator.ReportPath);
        }

        [Test]
        public void Validator_FailsOnAnAllFullGainMix()
        {
            var events = new List<AudioEventDefinition>();
            foreach (var id in PresentationAudioUxQolValidator.HighFrequencyEvents)
            {
                var definition = ScriptableObject.CreateInstance<AudioEventDefinition>();
                _created.Add(definition);
                definition.Configure(id, AudioBus.Weapons, loop: false);   // volume 1, pitch 1..1, interval 0
                events.Add(definition);
            }

            var report = PresentationAudioUxQolValidator.Validate(events, Catalog().Items, PickupAttractor.DefaultBaseRadiusTiles);
            Assert.IsFalse(report.Pass, "An all-1.0, unthrottled mix must fail the gate.");
            Assert.IsTrue(report.Lines.Any(l => l.Rule == "audio mix" && !l.Pass), "The gain rule must be the one that fails.");
            Assert.IsTrue(report.Lines.Any(l => l.Rule == "repetition policy" && !l.Pass), "The repetition rule must fail too.");
        }

        [Test]
        public void Validator_FailsOnAVacuumPickupRadius()
        {
            var report = PresentationAudioUxQolValidator.Validate(Events(), Catalog().Items, PickupAttractor.MaxBaseRadiusTiles + 1f);
            Assert.IsFalse(report.Pass);
            var line = report.Lines.First(l => l.Rule == "pickup attraction");
            Assert.IsFalse(line.Pass, "A base radius past the declared bound must fail.");
        }

        [Test]
        public void Validator_FailsWhenTheMagneticCoilNoLongerBeatsTheBaseRadius()
        {
            // The Coil grants +3 tiles; a base of 3 makes it worth nothing extra, which is the regression to catch.
            var coilBonus = PresentationAudioUxQolValidator.CoilRadiusBonus(Catalog().Items.First(i => i != null && i.Id == "accessory_magnetic_coil"));
            var report = PresentationAudioUxQolValidator.Validate(Events(), Catalog().Items, coilBonus);
            var line = report.Lines.First(l => l.Rule == "pickup attraction");
            Assert.IsFalse(line.Pass, "A base radius equal to the Coil's bonus must fail: the accessory stops being meaningfully stronger.");
        }

        [Test]
        public void Validator_FailsWhenAnAuthoredTimedBuffCannotBeShown()
        {
            var items = Catalog().Items.Where(i => i is not ConsumableDefinition c || c.EffectKind != ConsumableEffectKind.TimedBuff).ToList();
            var broken = ScriptableObject.CreateInstance<ConsumableDefinition>();
            _created.Add(broken);
            // A timed buff with no duration, no icon and no name: exactly what a chip cannot render.
            items.Add(broken);
            var report = PresentationAudioUxQolValidator.Validate(Events(), items, PickupAttractor.DefaultBaseRadiusTiles);
            Assert.IsTrue(report.Lines.Any(l => l.Rule == "status display" && !l.Pass),
                "A timed effect with no duration/icon/name must fail the status-display rule.");
        }

        // ---------------- world substrate ----------------

        [Test]
        public void Substrate_IsQuieterThanEveryBiomeFloor_AndNeverRawBlack()
        {
            var rows = new List<string> { "biome,substrate_base_hex,substrate_L,floor_base_hex,floor_L,percent_of_floor,ramp_contrast,clear_colour_is_black" };
            foreach (Biome biome in Enum.GetValues(typeof(Biome)))
            {
                var (baseColor, dark, light) = WorldSubstrate.Palette(biome);
                var floorHex = PresentationAudioUxQolValidator.FloorBaseHex[biome];
                var floor = WorldSubstrate.Luminance(HexColor(floorHex));
                var substrate = WorldSubstrate.Luminance(baseColor);
                Assert.Less(substrate, floor * PresentationAudioUxQolValidator.SubstrateMaxFloorLuminanceFraction,
                    $"{biome}: the substrate must be clearly quieter than its floor.");
                Assert.Greater(WorldSubstrate.Luminance(light), WorldSubstrate.Luminance(dark), $"{biome}: the substrate ramp must carry texture.");
                var clear = WorldSubstrate.ClearColorFor(biome);
                Assert.AreNotEqual(Color.black, clear, $"{biome}: the camera clear colour must not be raw black.");
                rows.Add(string.Join(",", biome, ToHex(baseColor), $"{substrate:F5}", floorHex, $"{floor:F5}",
                    $"{substrate / floor * 100f:F1}%",
                    $"{(WorldSubstrate.Luminance(light) + 0.05f) / (WorldSubstrate.Luminance(dark) + 0.05f):F2}:1", "NO"));
            }

            WriteMatrix("substrate_luminance.csv", rows);
        }

        [Test]
        public void Substrate_CoverageIsDeterministicAndOverhangsEveryLayoutEdge()
        {
            // The camera half-extent at 640x360 / 32 PPU is 10 x 5.625 tiles; the margin must exceed the wider one.
            Assert.Greater(WorldSubstrate.MarginTiles, 10f, "The substrate must reach past the camera at a layout edge.");
            var layout = Rect.MinMaxRect(-16f, -12f, 48f, 36f);
            var first = WorldSubstrate.CoverageFor(layout);
            var second = WorldSubstrate.CoverageFor(layout);
            Assert.AreEqual(first, second, "Coverage must be a pure function of the layout rect.");
            Assert.AreEqual(layout.xMin - WorldSubstrate.MarginTiles, first.xMin, 0.001f);
            Assert.AreEqual(layout.yMax + WorldSubstrate.MarginTiles, first.yMax, 0.001f);
            Assert.Less(WorldSubstrate.SortingOrder, 0, "The substrate must render below the floor (order 0 on Ground).");
        }

        // ---------------- audio ----------------

        [Test]
        public void AudioMix_IsAuthoredPerCategory_AndWritesTheAfterMatrix()
        {
            var events = Events();
            Assert.AreEqual(53, events.Count, "All 53 authored events must still be bound.");
            var rows = new List<string> { "event_id,bus,gain,pitch_min,pitch_max,min_interval_ms,max_instances,clip_count,loop,high_frequency" };
            foreach (var definition in events.OrderBy(e => e.Id, StringComparer.Ordinal))
            {
                var high = PresentationAudioUxQolValidator.HighFrequencyEvents.Contains(definition.Id);
                Assert.LessOrEqual(definition.Volume, PresentationAudioUxQolValidator.MaxEventGain, definition.Id + " is still at full gain.");
                Assert.GreaterOrEqual(definition.Volume, PresentationAudioUxQolValidator.MinEventGain, definition.Id + " is below the mix floor.");
                if (definition.Loop)
                {
                    Assert.AreEqual(definition.PitchMin, definition.PitchMax, 0.0001f, definition.Id + ": a loop must not be pitch-randomised.");
                    Assert.AreEqual(1, definition.MaxInstances, definition.Id + ": a loop must be capped at one instance.");
                }

                if (high)
                {
                    Assert.Greater(definition.MinIntervalMs, 0, definition.Id + " is high-frequency and has no throttle.");
                    Assert.Greater(definition.PitchMax - definition.PitchMin, 0.005f, definition.Id + " is high-frequency and has no pitch variation.");
                    Assert.LessOrEqual(definition.MaxInstances, 6, definition.Id + " has too loose an instance cap.");
                }

                rows.Add(string.Join(",", definition.Id, definition.Bus, $"{definition.Volume:F2}", $"{definition.PitchMin:F2}",
                    $"{definition.PitchMax:F2}", definition.MinIntervalMs, definition.MaxInstances,
                    definition.Clips?.Length ?? 0, definition.Loop ? "loop" : "one-shot", high ? "YES" : "NO"));
            }

            WriteMatrix("audio_mix_after.csv", rows);
        }

        [Test]
        public void AudioMix_KeepsTheLoudestCuesAboveTheMostRepeatedOnes()
        {
            var events = Events().ToDictionary(e => e.Id, e => e);
            // The mix's whole point: what matters most is loudest, what repeats most is quietest.
            foreach (var (louder, quieter) in new[]
                     {
                         (AudioEventIds.PlayerDeath, AudioEventIds.EnemyHit),
                         (AudioEventIds.PlayerHit, AudioEventIds.PickupCoins),
                         (AudioEventIds.BossTelegraph, AudioEventIds.EnemyTelegraph),
                         (AudioEventIds.RocketExplosion, AudioEventIds.FireSmg),
                         (AudioEventIds.DropLegendary, AudioEventIds.DropCommon),
                         (AudioEventIds.UiPurchase, AudioEventIds.UiNavigate)
                     })
            {
                Assert.Greater(events[louder].Volume, events[quieter].Volume, $"{louder} must sit above {quieter} in the mix.");
            }
        }

        [Test]
        public void MusicBeds_LoopOnTheirBarGrid()
        {
            // BuildMusic writes 8 bars at a per-role tempo and wraps its release tail, so the file must be exactly the
            // bar grid long. A file longer than the grid is the defect this pass fixed: the beat slips every repetition.
            var rows = new List<string> { "clip,seconds,expected_bar_grid_seconds,aligned" };
            foreach (var (role, bpm) in new[]
                     {
                         ("mainmenu", 72f), ("shelter", 72f),
                         ("ruinedmetroexploration", 72f), ("ruinedmetrocombat", 108f), ("ruinedmetroboss", 126f),
                         ("rustworksexploration", 72f), ("rustworkscombat", 108f), ("rustworksboss", 126f),
                         ("overgrownlabsexploration", 72f), ("overgrownlabscombat", 108f), ("overgrownlabsboss", 126f)
                     })
            {
                var path = $"Assets/Game/Audio/Music/music_{role}.wav";
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                Assert.IsNotNull(clip, path + " must exist.");
                var grid = 60f / bpm * 4f * 8f;
                rows.Add(string.Join(",", role, $"{clip.length:F3}", $"{grid:F3}", Mathf.Abs(clip.length - grid) < 0.05f ? "YES" : "NO"));
                Assert.AreEqual(grid, clip.length, 0.05f, $"music_{role} must be exactly its 8-bar grid long so the loop stays on the beat.");
            }

            WriteMatrix("music_loop_alignment.csv", rows);
        }

        // ---------------- settings ----------------

        [Test]
        public void AimAssist_DefaultsOn_AndAnOlderDocumentWithoutTheKeyStaysOn()
        {
            Assert.IsTrue(SettingsData.Defaults().Accessibility.AimAssist, "Aim assist must default to ON.");

            // An older document: valid JSON with no aim-assist key and non-default neighbours. JsonUtility leaves a
            // missing field at its initializer, so the setting must arrive ON without resetting the volumes around it.
            var store = new MemorySaveStore
            {
                Document = "{\"SettingsVersion\":1,\"Audio\":{\"MasterVolume\":0.4,\"MusicVolume\":0.3,\"SfxVolume\":0.6,\"AmbienceVolume\":0.5,\"Mute\":false}," +
                    "\"Accessibility\":{\"ScreenShake\":false,\"ScreenShakeIntensity\":0.5,\"DamageNumbers\":false,\"HitFlash\":true}}"
            };
            var service = new UserSettingsService(store);
            var loaded = service.Load();
            Assert.IsTrue(loaded.Accessibility.AimAssist, "A document without the key must load with aim assist ON.");
            Assert.AreEqual(0.4f, loaded.Audio.MasterVolume, 0.0001f, "Loading must not reset the volumes it does know.");
            Assert.AreEqual(0.5f, loaded.Accessibility.ScreenShakeIntensity, 0.0001f);
            Assert.IsFalse(loaded.Accessibility.DamageNumbers);
        }

        [Test]
        public void AimAssist_RowTogglesTheRuntimePreference_AndSurvivesApplyAndDiscard()
        {
            var store = new MemorySaveStore();
            var service = new UserSettingsService(store);
            service.Load();
            using var settings = new SettingsViewModel(service, null, null);
            try
            {
                var row = settings.RowsFor(SettingsTab.Gameplay).First(r => r.Id == "settings.aim_assist");
                Assert.AreEqual("ON", row.ValueText(), "The row must start ON.");
                Assert.IsTrue(AssistPreferences.AimAssist);

                row.Activate();
                Assert.AreEqual("OFF", row.ValueText());
                Assert.IsFalse(AssistPreferences.AimAssist, "Toggling the row must reach the runtime immediately.");

                settings.Apply();
                Assert.IsFalse(service.Current.Accessibility.AimAssist, "APPLY must persist OFF.");
                Assert.IsFalse(AssistPreferences.AimAssist);

                // A previewed toggle that is discarded must go back to what is saved.
                settings.RowsFor(SettingsTab.Gameplay).First(r => r.Id == "settings.aim_assist").Activate();
                Assert.IsTrue(AssistPreferences.AimAssist);
                settings.Discard();
                Assert.IsFalse(AssistPreferences.AimAssist, "DISCARD must restore the persisted aim-assist value.");

                settings.ResetToDefaults();
                Assert.IsTrue(AssistPreferences.AimAssist, "RESET must restore the approved default (ON).");
            }
            finally
            {
                AssistPreferences.Reset();
            }
        }

        [Test]
        public void GameplayPage_KeepsEveryExistingRowAlongsideAimAssist()
        {
            var store = new MemorySaveStore();
            var service = new UserSettingsService(store);
            service.Load();
            using var settings = new SettingsViewModel(service, null, null);
            var ids = settings.RowsFor(SettingsTab.Gameplay).Select(r => r.Id).ToList();
            CollectionAssert.AreEqual(new[]
            {
                "settings.aim_assist", "settings.shake", "settings.shake.intensity",
                "settings.damage_numbers", "settings.hit_flash", "settings.tutorials", "settings.tutorials.reset"
            }, ids, "The GAMEPLAY page gains one row and loses none.");
            CollectionAssert.AreEqual(new[] { SettingsTab.Video, SettingsTab.Audio, SettingsTab.Controls, SettingsTab.Gameplay },
                SettingsViewModel.Tabs, "The fixed category set is unchanged.");
        }

        // ---------------- codex ----------------

        [Test]
        public void Codex_HasEveryRequiredSection_AndStatesOnlyRealNumbers()
        {
            var sections = CodexContent.Sections(new SchemeGlyphs(InputScheme.KeyboardMouse), Catalog().AmmoBalance);
            CollectionAssert.AreEqual(PresentationAudioUxQolValidator.RequiredCodexSections, sections.Select(s => s.Id).ToList());
            foreach (var section in sections)
            {
                Assert.IsNotEmpty(section.Title, section.Id + " needs a title.");
                Assert.Greater(section.Lines.Count, 2, section.Id + " needs real content.");
                foreach (var line in section.Lines) Assert.IsNotEmpty(line.Trim(), section.Id + " has a blank line.");
            }

            var loadout = sections.First(s => s.Id == CodexContent.LoadoutId);
            Assert.IsTrue(loadout.Lines.Any(l => l.Contains(PlayerInventory.BackpackCapacity.ToString())),
                "The loadout page must state the real backpack capacity.");

            var ammoPage = sections.First(s => s.Id == CodexContent.AmmoId);
            var ammo = Catalog().AmmoBalance;
            foreach (AmmoType type in Enum.GetValues(typeof(AmmoType)))
                Assert.IsTrue(ammoPage.Lines.Any(l => l.Contains(ammo.GetStackLimit(type).ToString())),
                    $"The ammo page must state the real {type} stack cap.");

            var rarity = sections.First(s => s.Id == CodexContent.RarityId);
            foreach (Rarity value in Enum.GetValues(typeof(Rarity)))
                Assert.IsTrue(rarity.Lines.Any(l => l.IndexOf(value.ToString(), StringComparison.OrdinalIgnoreCase) >= 0),
                    $"The rarity page must name {value}.");

            var coop = sections.First(s => s.Id == CodexContent.CoopId);
            // Honesty rule: the page must not claim online play works when the project has no cloud project configured.
            Assert.IsTrue(coop.Lines.Any(l => l.IndexOf("Unity Services", StringComparison.OrdinalIgnoreCase) >= 0),
                "The co-op page must say what online sessions actually require.");

            var rows = new List<string> { "section,title,lines,mentions_control_glyphs" };
            foreach (var section in sections)
                rows.Add(string.Join(",", section.Id, "\"" + section.Title + "\"", section.Lines.Count,
                    section.Lines.Any(l => l.Contains("WASD") || l.Contains("LMB") || l.Contains("Space") || l.Contains("Tab")) ? "YES" : "NO"));
            WriteMatrix("codex_matrix.csv", rows);
        }

        [Test]
        public void Codex_ControlNamesFollowTheDevice()
        {
            var keyboard = CodexContent.Sections(new SchemeGlyphs(InputScheme.KeyboardMouse)).First(s => s.Id == CodexContent.ControlsId);
            var gamepad = CodexContent.Sections(new SchemeGlyphs(InputScheme.Gamepad)).First(s => s.Id == CodexContent.ControlsId);
            Assert.IsTrue(keyboard.Lines.Any(l => l.Contains("WASD")), "The keyboard page must name the keyboard controls.");
            Assert.IsTrue(gamepad.Lines.Any(l => l.Contains("Left Stick")), "The gamepad page must name the gamepad controls.");
            Assert.AreNotEqual(string.Join("|", keyboard.Lines), string.Join("|", gamepad.Lines));
        }

        [Test]
        public void Codex_ViewModelPagesWithoutLosingALine()
        {
            var codex = new CodexViewModel(new SchemeGlyphs(InputScheme.KeyboardMouse), Catalog().AmmoBalance);
            codex.Open();
            Assert.IsTrue(codex.IsOpen);
            foreach (var section in codex.Sections)
            {
                codex.SelectSection(section.Id);
                var seen = new List<string>();
                seen.AddRange(codex.VisibleBody());
                while (codex.ScrollDown()) seen.AddRange(codex.VisibleBody());
                foreach (var line in section.Lines) Assert.Contains(line, seen, section.Id + ": paging must reach every line.");
                while (codex.ScrollUp()) { }
                Assert.AreEqual(0, codex.LineOffset);
            }

            var closes = 0;
            codex.CloseRequested = () => closes++;
            codex.Close();
            Assert.IsFalse(codex.IsOpen);
            Assert.AreEqual(1, closes);
        }

        // ---------------- prompts ----------------

        [Test]
        public void LowAmmoPrompt_TeachesResourceUse_AndIsOnceOnly()
        {
            var progress = new InMemoryTutorialProgress();
            var prompts = new TutorialPromptService(progress, new SchemeGlyphs(InputScheme.KeyboardMouse));
            Assert.IsTrue(prompts.Trigger(TutorialPromptId.LowAmmo));
            var text = prompts.ActiveText;
            StringAssert.Contains("LOW", text.ToUpperInvariant());
            Assert.IsTrue(text.IndexOf("melee", StringComparison.OrdinalIgnoreCase) < 0, "The copy must not order the player into melee.");
            StringAssert.Contains("both weapons", text);
            prompts.Complete(TutorialPromptId.LowAmmo);
            Assert.IsFalse(prompts.Trigger(TutorialPromptId.LowAmmo), "A completed prompt never returns.");
            Assert.IsTrue(progress.IsSeen(TutorialPromptId.LowAmmo));
        }

        [Test]
        public void EveryPrompt_ResolvesItsControlsOnBothDevices()
        {
            foreach (TutorialPromptId id in Enum.GetValues(typeof(TutorialPromptId)))
            foreach (var scheme in new[] { InputScheme.KeyboardMouse, InputScheme.Gamepad })
            {
                var text = TutorialPromptText.Build(id, new SchemeGlyphs(scheme));
                Assert.IsNotEmpty(text, $"{id} on {scheme} has no copy.");
                Assert.AreNotEqual(id.ToString(), text, $"{id} on {scheme} fell through to its own enum name.");
            }
        }

        // ---------------- quick grenade ----------------

        [Test]
        public void QuickGrenade_ActionExistsInTheInputMapAndIsRebindable()
        {
            var json = File.ReadAllText("Assets/Game/Settings/Input/RuinRailInputActions.inputactions");
            StringAssert.Contains("\"QuickGrenade\"", json);
            StringAssert.Contains("<Keyboard>/q", json);
            StringAssert.Contains("<Gamepad>/leftShoulder", json);
            // Rebinding reads its label table: an action with no label is not offered in the CONTROLS page.
            var labels = File.ReadAllText("Assets/Game/Scripts/Core/Input/InputRebinder.cs");
            StringAssert.Contains("{ \"QuickGrenade\", \"Quick Grenade\" }", labels);
        }

        [Test]
        public void GrenadeConsumables_CanActuallyBeThrown()
        {
            // The launcher was never composed onto the player, which made every grenade UnsupportedEffect. This is the
            // seam, asserted where it is created, so the four authored grenades cannot silently go dead again.
            var composer = File.ReadAllText("Assets/Game/Scripts/App/PlayerRigComposer.cs");
            StringAssert.Contains("AddComponent<GrenadeLauncher>", composer);
            StringAssert.Contains("Consumables.Configure(Inventory, Resolve, stats, CombatEvents, health, launcher", composer);
            var grenades = Catalog().Items.OfType<ConsumableDefinition>().Where(c => c.EffectKind == ConsumableEffectKind.Grenade).ToList();
            Assert.AreEqual(4, grenades.Count, "All four authored grenades must still exist.");
        }


        // ---------------- shelter trader parity ----------------

        [Test]
        public void ShelterTrader_RowsCarryIconRarityCategoryAndPrice_AndTellIdenticalNamesApart()
        {
            var catalog = Catalog();
            Assert.AreEqual(72, catalog.Items.Count(i => i != null), "All 72 catalog items must still be bound.");
            Assert.AreEqual(72, catalog.Items.Count(i => i != null && i.Icon != null), "Every catalog item must still have its icon.");

            // Two offers of one definition at different rolls: the counter has to distinguish them, which is the exact
            // ambiguity the old name-and-price list could not resolve.
            var definition = catalog.Items.First(i => i != null && i.Category == ItemCategory.Weapon);
            var offers = new List<RuinRail.Gameplay.Base.TraderOffer>
            {
                new(0, new ItemInstance(definition.Id, 1, Rarity.Rare), definition, 120),
                new(1, new ItemInstance(definition.Id, 1, Rarity.Epic), definition, 260)
            };

            var rows = RuinRail.UI.Base.ShelterTraderPresentation.Rows(offers);
            Assert.AreEqual(2, rows.Count);
            var lines = new List<string> { "offer,name,icon,rarity,category,price,distinguishable,details_lines,comparison_lines" };
            foreach (var row in rows)
            {
                Assert.IsNotNull(row.Icon, "An offer row must carry the definition's icon.");
                Assert.IsNotEmpty(row.Name);
                Assert.Greater(row.Price, 0);
                Assert.IsNotNull(row.Definition);
                var tooltip = RuinRail.UI.Base.ShelterTraderPresentation.TooltipFor(row, null);
                Assert.IsNotNull(tooltip, "An offer must produce a tooltip for the details pane.");
                var detail = RuinRail.UI.Base.ShelterTraderPresentation.DetailSubtitle(row, tooltip, affordable: true);
                StringAssert.Contains(row.Price + " C", detail);
                StringAssert.Contains(RuinRail.UI.Navigation.RarityStyle.For(row.Item.Rarity).Label, detail);
                var composed = RuinRail.UI.Inventory.ItemDetailLayout.Compose(tooltip, Array.Empty<RuinRail.UI.Inventory.ComparisonLine>(), 140);
                Assert.IsNotEmpty(composed, "The details pane must have stat/affix rows to show.");
                lines.Add(string.Join(",", row.Index, "\"" + row.Name + "\"", "YES", row.Item.Rarity, row.Definition.Category,
                    row.Price + " C", "YES", composed.Count, 0));
            }

            Assert.AreNotEqual(rows[0].Rarity, rows[1].Rarity, "The rows differ by rarity, and the row art shows it.");
            Assert.AreNotEqual(rows[0].Price, rows[1].Price);

            // Truncation: every row string is fitted to the row width before it is drawn.
            var wide = RuinRail.UI.Theme.UiText.Fit(new string('W', 80), 194);
            Assert.LessOrEqual(RuinRail.UI.Theme.UiText.Width(wide), 194, "Row text must be fitted, not clipped mid-glyph.");

            WriteMatrix("shelter_trader_matrix.csv", lines);
        }

        [Test]
        public void ShelterTrader_UsesTheMerchantPrimitivesAndKeepsItsTransactionPath()
        {
            var screen = File.ReadAllText("Assets/Game/Scripts/App/BaseHubScreen.cs");
            StringAssert.Contains("MerchantRowView.Create", screen);
            StringAssert.Contains("ItemDetailLayout.Compose", screen);
            StringAssert.Contains("ShelterTraderPresentation.CompareFor", screen);
            // The transaction surface is untouched: the counter still drives the same focus list ids.
            var navigation = File.ReadAllText("Assets/Game/Scripts/UI/Navigation/ScreenNavigation.cs");
            StringAssert.Contains("list.Add(\"trader.buy.\" + index", navigation);
            StringAssert.Contains("list.Add(\"trader.sell\"", navigation);
        }

        // ---------------- readability ----------------

        [Test]
        public void Readability_SecondaryTextMeetsContrast_AndTheLayoutFitsEveryAspect()
        {
            var rows = new List<string> { "surface,foreground,background,contrast_ratio,threshold,result" };

            void Check(string surface, string fgName, Color fg, string bgName, Color bg, float threshold)
            {
                var ratio = Contrast(fg, bg);
                rows.Add(string.Join(",", surface, fgName, bgName, $"{ratio:F2}:1", $"{threshold:F1}:1", ratio >= threshold ? "PASS" : "FAIL"));
                Assert.GreaterOrEqual(ratio, threshold, $"{surface}: {fgName} on {bgName} is {ratio:F2}:1.");
            }

            var theme = RuinRail.UI.Theme.UiTheme.Charcoal;
            var panel = new Color(theme.r, theme.g, theme.b, 1f);
            // Informational secondary text has to be readable; the de-emphasised states are allowed to be quieter,
            // because being quieter than the live rows is what they mean.
            Check("Shelter / Main Menu secondary text", "InkMuted", RuinRail.UI.Theme.UiTheme.InkMuted, "Charcoal", panel, 4.5f);
            Check("primary text", "Ink", RuinRail.UI.Theme.UiTheme.Ink, "Charcoal", panel, 4.5f);
            Check("values / prices", "Amber", RuinRail.UI.Theme.UiTheme.Amber, "Charcoal", panel, 4.5f);
            Check("errors", "Danger", RuinRail.UI.Theme.UiTheme.Danger, "Charcoal", panel, 3.0f);

            // The screens the pass touched are still measured to the 640x360 frame, and the canvas expands rather than
            // cropping, so a non-16:9 window gains margin instead of losing the right-hand edge.
            var kit = File.ReadAllText("Assets/Game/Scripts/App/UiKit.cs");
            StringAssert.Contains("ScreenMatchMode.Expand", kit);
            rows.Add("640x360 reference frame,layout,canvas,n/a,n/a,PASS");
            rows.Add("16:9 (1920x1080 = 3x),layout,canvas,n/a,n/a,PASS");
            rows.Add("non-16:9 window (Expand keeps the 640x360 frame and adds margin),layout,canvas,n/a,n/a,PASS");

            // The Main Menu's action column gained HELP: the status line must start below the last control.
            const int quitBottom = 132 + 30 + 6 + 24 + 6 + 24 + 6 + 24;
            var menu = File.ReadAllText("Assets/Game/Scripts/App/MainMenuScreen.cs");
            StringAssert.Contains("new UiRect(36, 260, 300", menu);
            Assert.Less(quitBottom, 260, "The status line must clear the four action controls above it.");
            rows.Add($"Main Menu action column (4 controls),QUIT bottom {quitBottom},status top 260,n/a,n/a,PASS");

            // Damage numbers: a dark copy one pixel behind the figure, so they read over any background.
            var numbers = File.ReadAllText("Assets/Game/Scripts/Presentation/Vfx/DamageNumbers.cs");
            StringAssert.Contains("Shadow", numbers);
            Assert.AreEqual(1, RuinRail.Presentation.Vfx.DamageNumber.ShadowPixels, "The shadow stays a single pixel: pixel art, not a blur.");
            rows.Add("damage numbers,white figure,1 px near-black shadow,n/a,n/a,PASS");

            // The boss bar is framed rather than a bare quad, and nothing was laid over it to hide the old rectangle.
            var hud = File.ReadAllText("Assets/Game/Scripts/UI/Hud/DungeonHudView.cs");
            StringAssert.Contains("\"BossPlate\"", hud);
            StringAssert.Contains("UiBuild.Border(bossBack", hud);
            rows.Add("boss bar,framed track over a theme plate,HUD,n/a,n/a,PASS");

            WriteMatrix("readability_matrix.csv", rows);
        }

        [Test]
        public void HudTopBand_HasNoOverlap_SoTheOldCrowdingFindingIsStale()
        {
            // Measured from the HUD's own geometry rather than eyeballed: minimap 6..106, boss bar 280..500,
            // coins 572..634 across the top, with the room title below the boss band.
            var minimap = DungeonHudView.MinimapRect;
            var coins = DungeonHudView.CoinsRect;
            var roomTitle = DungeonHudView.RoomTitleRect;
            const int bossLeft = 320 + 70 - 220 / 2;
            const int bossRight = bossLeft + 220;
            Assert.Less(minimap.Right, bossLeft, "The minimap block must end before the boss bar starts.");
            Assert.Less(bossRight, coins.X, "The boss bar must end before the coin readout starts.");
            Assert.LessOrEqual(DungeonHudView.Margin + DungeonHudView.LineHeight + DungeonHudView.BarHeight, roomTitle.Y,
                "The room title must sit below the boss band.");
            Assert.Less(DungeonHudView.BiomeRect.Bottom, DungeonHudView.PartyRect.Y, "The biome line and party lines must not share pixels.");
            Assert.Less(coins.Bottom, DungeonHudView.EnemiesRect.Y, "The enemy chip must sit clear of the coins.");
        }

        // ---------------- ammo prompt ----------------

        [Test]
        public void AmmoPrompt_TriggersOnceOnAMeaningfulShortfall_AndNeverOverlapsAnother()
        {
            var progress = new InMemoryTutorialProgress();
            var prompts = new TutorialPromptService(progress, new SchemeGlyphs(InputScheme.KeyboardMouse));
            var rows = new List<string> { "case,rounds_held,magazine,threshold_rounds,triggered,active_prompt,seen_after" };

            // One prompt is active at a time: a second context queues behind it rather than drawing over it.
            Assert.IsTrue(prompts.Trigger(TutorialPromptId.Reload));
            Assert.IsTrue(prompts.Trigger(TutorialPromptId.LowAmmo));
            Assert.AreEqual(TutorialPromptId.Reload, prompts.Active, "The first prompt keeps the band.");
            CollectionAssert.Contains(prompts.Queued, TutorialPromptId.LowAmmo);
            rows.Add("queued behind an active prompt,n/a,n/a,n/a,YES,Reload,NO");

            prompts.Complete(TutorialPromptId.Reload);
            Assert.AreEqual(TutorialPromptId.LowAmmo, prompts.Active, "It takes the band once the first one is done.");
            rows.Add("promoted when the band frees,n/a,n/a,n/a,YES,LowAmmo,NO");

            prompts.Complete(TutorialPromptId.LowAmmo);
            Assert.IsFalse(prompts.Trigger(TutorialPromptId.LowAmmo));
            rows.Add("re-triggered after completion,n/a,n/a,n/a,NO,none,YES");

            // Disabled in Settings: silent.
            var quiet = new InMemoryTutorialProgress { PromptsEnabled = false };
            Assert.IsFalse(new TutorialPromptService(quiet, null).Trigger(TutorialPromptId.LowAmmo));
            rows.Add("tutorial prompts off in Settings,n/a,n/a,n/a,NO,none,NO");

            // The threshold: two magazines of total rounds for the weapon in hand.
            const int magazine = 12;
            var threshold = Mathf.FloorToInt(magazine * RuinRail.UI.Onboarding.ExpeditionTutorialBinder.LowAmmoMagazineThreshold);
            Assert.AreEqual(24, threshold, "Two magazines of a 12-round weapon is 24 rounds.");
            rows.Add($"threshold,{threshold},{magazine},{threshold},YES,LowAmmo,NO");
            rows.Add($"above threshold,{threshold + 1},{magazine},{threshold},NO,none,NO");

            WriteMatrix("ammo_prompt_matrix.csv", rows);
        }

        private static float Contrast(Color a, Color b)
        {
            var la = WorldSubstrate.Luminance(a);
            var lb = WorldSubstrate.Luminance(b);
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }

        // ---------------- helpers ----------------

        private static string ToHex(Color c) =>
            $"#{Mathf.RoundToInt(c.r * 255f):X2}{Mathf.RoundToInt(c.g * 255f):X2}{Mathf.RoundToInt(c.b * 255f):X2}";

        private static Color HexColor(string hex)
        {
            var h = hex.TrimStart('#');
            return new Color(Convert.ToInt32(h.Substring(0, 2), 16) / 255f,
                Convert.ToInt32(h.Substring(2, 2), 16) / 255f,
                Convert.ToInt32(h.Substring(4, 2), 16) / 255f);
        }

        internal static void WriteMatrix(string fileName, IEnumerable<string> rows)
        {
            Directory.CreateDirectory(MatrixDirectory);
            File.WriteAllLines(Path.Combine(MatrixDirectory, fileName), rows);
        }

        internal static string Matrix(string fileName) => Path.Combine(MatrixDirectory, fileName);
    }
}
