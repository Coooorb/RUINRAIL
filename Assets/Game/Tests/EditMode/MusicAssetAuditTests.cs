using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.EditorTools.ArtGen;
using RuinRail.EditorTools.Production;
using UnityEngine;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 141 — routing/data complete with exact slot counts; every track, stinger and ambience loop is a truthful external blocker.</summary>
    public sealed class MusicAssetAuditTests
    {
        [Test]
        public void Audit_RoutingComplete_ContentBlocked_ExactCounts()
        {
            var report = MusicAssetAudit.WriteReport();
            Assert.IsTrue(report.CatalogExists);
            Assert.IsTrue(report.ExactSlots);
            Assert.IsTrue(report.RoutingComplete);
            Assert.IsTrue(report.ContentComplete, "All 11 tracks, 7 stingers and 3 ambience loops are bound.");
            Assert.AreEqual(0, report.MissingTracks.Length);
            Assert.AreEqual(0, report.MissingStingers.Length);
            Assert.AreEqual(0, report.MissingAmbience.Length);
            var md = report.ToMarkdown();
            StringAssert.Contains("Ruined Metro — Boss", md);
            StringAssert.Contains("LegendaryDrop", md);
            StringAssert.Contains("tracks 11/11, stingers 7/7, ambience 3/3", md);
            Assert.IsTrue(File.Exists(MusicAssetAudit.ReportPath));
            Assert.AreEqual(md, MusicAssetAudit.Audit().ToMarkdown());
        }

        /// <summary>
        /// Each biome has its own theme, not the same bed in another key: its own tempo (the loop of eight bars differs
        /// per biome, and every biome's three tracks sit on their tempo's bar grid), and Ruined Metro's exploration bed is
        /// no longer the Main Menu bed. Brightness and pulse density per track are written as evidence.
        /// </summary>
        [Test]
        public void BiomeThemes_AreDistinct_OnTheirOwnBarGrid_AndNotTheMenuBed()
        {
            var tempi = new System.Collections.Generic.Dictionary<string, float[]>
            {
                ["RuinedMetro"] = new[] { 76f, 112f, 128f },
                ["Rustworks"] = new[] { 66f, 100f, 116f },
                ["OvergrownLabs"] = new[] { 84f, 116f, 132f },
            };
            var lines = new System.Collections.Generic.List<string>();
            var exploreLengths = new System.Collections.Generic.List<int>();
            foreach (var (biome, bpm) in tempi.Select(p => (p.Key, p.Value)))
            {
                var roles = new[] { biome + "Exploration", biome + "Combat", biome + "Boss" };
                for (var i = 0; i < 3; i++)
                {
                    var clip = GameAudioFactory.BuildMusic(roles[i]);
                    var expected = Mathf.RoundToInt(8 * 4 * 60f / bpm[i] * AudioSynth.SampleRate);
                    Assert.AreEqual(expected, clip.Length, 2, roles[i] + " loops on its bar grid");
                    if (i == 0) exploreLengths.Add(clip.Length);
                    double energy = 0, diff = 0;
                    for (var n = 1; n < clip.Length; n++) { energy += clip.L[n] * clip.L[n]; var d = clip.L[n] - clip.L[n - 1]; diff += d * d; }
                    lines.Add($"{roles[i]}: {bpm[i]} BPM, {clip.Length / (float)AudioSynth.SampleRate:0.00}s, brightness {diff / System.Math.Max(1e-9, energy):0.0000}");
                }
            }

            CollectionAssert.AllItemsAreUnique(exploreLengths, "each biome explores at its own tempo");
            var menu = GameAudioFactory.BuildMusic("MainMenu");
            var metro = GameAudioFactory.BuildMusic("RuinedMetroExploration");
            Assert.IsFalse(menu.Length == metro.Length && menu.L.SequenceEqual(metro.L), "Ruined Metro no longer plays the Main Menu bed");
            TestContext.WriteLine(string.Join("\n", lines));
        }

        /// <summary>
        /// The Main Menu theme is its own calm material (it had shared the Shelter's buzzy generic bed): on a 60 BPM
        /// eight-bar grid, far darker than that bed (little energy in the upper spectrum), with softer transients, and no
        /// longer the same bed as the Shelter or any biome track.
        /// </summary>
        [Test]
        public void MainMenuTheme_IsCalm_Dark_AndSoftAgainstTheSharedBedItReplaced()
        {
            static (double bright, double transient) Measure(AudioSynth.Clip clip)
            {
                double energy = 0, diff = 0;
                for (var n = 1; n < clip.Length; n++) { energy += clip.L[n] * clip.L[n]; var d = clip.L[n] - clip.L[n - 1]; diff += d * d; }
                const int w = 221; // 5 ms windows: the largest jump of a window over the 50 ms before it
                var windows = new System.Collections.Generic.List<double>();
                for (var k = 0; k + w < clip.Length; k += w)
                {
                    double sum = 0;
                    for (var i = k; i < k + w; i++) sum += clip.L[i] * clip.L[i];
                    windows.Add(System.Math.Sqrt(sum / w) + 1e-6);
                }

                var jump = 0.0;
                for (var k = 10; k < windows.Count; k++) jump = System.Math.Max(jump, windows[k] / windows.Skip(k - 10).Take(10).Average());
                return (diff / System.Math.Max(1e-9, energy), jump);
            }

            var menu = GameAudioFactory.BuildMusic("MainMenu");
            var shelter = GameAudioFactory.BuildMusic("Shelter");
            Assert.AreEqual(Mathf.RoundToInt(32f * AudioSynth.SampleRate), menu.Length, 2, "eight bars at 60 BPM");
            var (menuBright, menuTransient) = Measure(menu);
            var (bedBright, bedTransient) = Measure(shelter);
            TestContext.WriteLine($"menu brightness {menuBright:0.00000} transient {menuTransient:0.00}; shared bed brightness {bedBright:0.00000} transient {bedTransient:0.00}");
            Assert.Less(menuBright, bedBright * 0.3, "far darker than the buzzy bed it replaced: nothing piercing");
            Assert.Less(menuTransient, bedTransient, "softer transients than that bed");
            foreach (var role in new[] { "Shelter", "RuinedMetroExploration", "RustworksExploration", "OvergrownLabsExploration" })
            {
                var other = GameAudioFactory.BuildMusic(role);
                Assert.IsFalse(other.Length == menu.Length && other.L.SequenceEqual(menu.L), "the menu shares no bed with " + role);
            }
        }

        /// <summary>
        /// The biome ambience is dark room tone, not a noise floor: almost no energy in the upper band (the hiss that
        /// read as a constant rush), a steady level with no hard transients dominating, and still three distinct beds.
        /// </summary>
        [Test]
        public void BiomeAmbience_IsDarkRoomTone_NotBroadbandHiss()
        {
            var fingerprints = new System.Collections.Generic.List<double>();
            foreach (var biome in new[] { "RuinedMetro", "Rustworks", "OvergrownLabs" })
            {
                var clip = GameAudioFactory.BuildAmbience(biome);
                double energy = 0, diff = 0;
                for (var n = 1; n < clip.Length; n++) { energy += clip.L[n] * clip.L[n]; var d = clip.L[n] - clip.L[n - 1]; diff += d * d; }
                var brightness = diff / System.Math.Max(1e-9, energy);
                TestContext.WriteLine($"{biome} ambience brightness {brightness:0.00000} (the white-noise beds measured 0.11-0.22)");
                Assert.Less(brightness, 0.005, biome + ": no broadband hiss in the bed");
                fingerprints.Add(System.Math.Round(brightness, 6));
            }

            CollectionAssert.AllItemsAreUnique(fingerprints, "three distinct biome beds");
        }
    }
}