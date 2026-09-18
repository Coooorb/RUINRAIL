using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.UI.Hud;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RuinRail.Tests
{
    /// <summary>
    /// The reworked HUD bands: the minimap and biome identity top-left instead of the old text block, the graphical
    /// coin readout top-right, the room-title reveal top-centre, the low-HP vignette under everything, and the dash
    /// cooldown as a moving vertical wipe rather than a block of grey.
    ///
    /// The dash wipe is the regression that matters here: the overlay had no sprite, and uGUI silently ignores an
    /// <see cref="Image.type"/> of Filled when an image has none — so the fill amount was discarded and the icon sat
    /// under a flat grey rectangle for the whole cooldown.
    /// </summary>
    public sealed class RoomHudQolTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private DungeonHudView View(out DungeonHudViewModel vm)
        {
            vm = new DungeonHudViewModel();
            var view = DungeonHudView.Create(vm);
            _created.Add(view.gameObject);
            return view;
        }

        // ---- Top-left: minimap + biome, no text block ----

        [Test]
        public void TopLeft_IsTheMinimapAndTheBiome_NotAnObjectiveTextBlock()
        {
            var view = View(out var vm);
            Assert.IsNotNull(view.Minimap, "the minimap is the top-left element");
            Assert.AreSame(view.Minimap.Rect, view.TopLeftPanel);
            Assert.AreEqual(HudMinimapView.Width, Mathf.RoundToInt(view.TopLeftPanel.sizeDelta.x));
            Assert.AreEqual(HudMinimapView.Height, Mathf.RoundToInt(view.TopLeftPanel.sizeDelta.y));

            // The minimap panel and its biome line together own only the top-left corner.
            Assert.LessOrEqual(DungeonHudView.MinimapRect.Right, 160, "the map never dominates the 640x360 frame");
            Assert.LessOrEqual(DungeonHudView.BiomeRect.Bottom, 120);

            // The old permanent objective/depth block is gone.
            var texts = view.GetComponentsInChildren<Text>(true).Select(t => t.text).ToList();
            CollectionAssert.DoesNotContain(texts, "DEPTH 1 — RUINED METRO");
            Assert.IsFalse(texts.Any(t => t.Contains("find and defeat")), "no permanent objective sentence");
            Assert.IsFalse(texts.Any(t => t.StartsWith("DEPTH ")), "depth is a compact chip, not a sentence");
            Assert.AreEqual(HudSnapshot.BiomeName(vm.Snapshot.Biome), view.BiomeText);
        }

        [Test]
        public void TheMinimapDrawsTheModel_CurrentRoomDiscoveryConnectionsAndMarkers()
        {
            var view = View(out _);
            var model = new MinimapModel();
            model.BeginDepth(3, "RUSTWORKS");
            model.AddRoom(0, new Vector2(0f, 0f), MinimapRoomKind.Start);
            model.AddRoom(1, new Vector2(24f, 0f), MinimapRoomKind.Merchant);
            model.AddRoom(2, new Vector2(48f, 0f), MinimapRoomKind.Boss);
            model.AddLink(0, 1);
            model.AddLink(1, 2);
            view.BindMinimap(model);

            CollectionAssert.IsEmpty(view.Minimap.DrawnNodeIds, "nothing is drawn before the player has been anywhere");
            model.MarkEntered(0);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, view.Minimap.DrawnNodeIds, "the room and its neighbour outline");
            CollectionAssert.AreEquivalent(new[] { 0 }, view.Minimap.DrawnMarkerNodeIds, "only the entered room shows its symbol");
            Assert.Greater(view.Minimap.DrawnLinkParts, 0, "the door between them is drawn");
            Assert.AreEqual("D3", view.DepthText, "the depth is a chip inside the map frame");

            model.MarkEntered(1);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, view.Minimap.DrawnNodeIds);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, view.Minimap.DrawnMarkerNodeIds, "the merchant appears once entered");
            Assert.IsFalse(view.Minimap.DrawnMarkerNodeIds.Contains(2), "the boss room is not spoiled before it is entered");
        }

        [Test]
        public void TheMinimapNeverConsumesGameplayMouseInput()
        {
            var view = View(out _);
            foreach (var graphic in view.Minimap.GetComponentsInChildren<Graphic>(true))
                Assert.IsFalse(graphic.raycastTarget, $"{graphic.name}: the HUD is informational");
            foreach (var graphic in view.GetComponentsInChildren<Graphic>(true))
                Assert.IsFalse(graphic.raycastTarget, $"{graphic.name}: nothing in the HUD eats a click");
            Assert.IsNull(view.GetComponent<GraphicRaycaster>(), "the HUD canvas has no raycaster at all");
        }

        // ---- Top-right: graphical coins ----

        [Test]
        public void Coins_AreAnIconPlusANumber_ThatFollowsTheWalletImmediately()
        {
            var view = View(out var vm);
            Assert.IsNotNull(view.CoinView);
            Assert.AreSame(view.CoinView.Rect, view.TopRightPanel);
            Assert.IsTrue(view.CoinView.HasIconSprite, "the generated coin token is bound through the UI skin");
            Assert.IsTrue(view.CoinView.IconVisible);
            Assert.AreEqual("0", view.CoinsText);
            Assert.IsFalse(view.GetComponentsInChildren<Text>(true).Any(t => t.text.Contains("COINS")), "the token replaces the word");

            // The wallet change reaches the readout through the snapshot the view renders.
            vm.Snapshot.Coins = 1234;
            vm.SetOverlayOpen(true);  // any publication re-renders; the coin number is never cached
            Assert.AreEqual("1234", view.CoinsText);
            vm.SetOverlayOpen(false);
            Assert.AreEqual("1234", view.CoinsText);
            view.CoinView.Show(7);
            Assert.AreEqual("7", view.CoinsText, "no stale value survives a render");
            Assert.LessOrEqual(UiText.Width("99999"), HudCoinView.Width - HudCoinView.IconSize - 10, "a five-digit purse still fits the plate");
        }

        // ---- Top-centre: the room-title reveal ----

        [UnityTest]
        public IEnumerator TheRoomTitle_FadesInHoldsAndFadesOut_AndNeverRepeatsOnItsOwn()
        {
            var view = View(out _);
            var title = view.RoomTitle;
            Assert.IsFalse(title.IsShowing, "nothing is announced before a room is entered");
            Assert.AreEqual(0f, title.Alpha);

            title.Reveal("Collapsed Platform", "");
            Assert.AreEqual(1, title.Reveals);
            Assert.AreEqual("COLLAPSED PLATFORM", title.NameText);
            Assert.IsTrue(title.IsShowing);
            yield return null;
            yield return null;
            Assert.Greater(title.Alpha, 0f, "it fades in rather than popping");

            var deadline = Time.realtimeSinceStartup + 6f;
            while (title.IsShowing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(title.IsShowing, "and clears itself");
            Assert.AreEqual(0f, title.Alpha);
            Assert.AreEqual(1, title.Reveals, "one entry, one reveal");

            title.Reveal("Sealed Strongroom", "TREASURE");
            Assert.AreEqual(2, title.Reveals);
            Assert.AreEqual("SEALED STRONGROOM", title.NameText);
            Assert.AreEqual("TREASURE", title.RoleText, "a special room reads as what it is");
            title.Clear();
            Assert.IsFalse(title.IsShowing, "a depth change clears it immediately");
            Assert.AreEqual(0f, title.Alpha);
        }

        [Test]
        public void TheRoomTitleBand_SitsClearOfTheBossBarAndTheMapAndNeverLeavesTheFrame()
        {
            var view = View(out _);
            var title = DungeonHudView.RoomTitleRect;
            Assert.GreaterOrEqual(title.X, 0);
            Assert.LessOrEqual(title.Right, DungeonHudView.ReferenceWidth);
            Assert.LessOrEqual(title.Bottom, DungeonHudView.ReferenceHeight);
            Assert.IsFalse(title.Overlaps(DungeonHudView.MinimapRect), "the map never covers the reveal");
            Assert.IsFalse(title.Overlaps(DungeonHudView.CoinsRect));
            Assert.IsFalse(title.Overlaps(DungeonHudView.BiomeRect));
            Assert.IsFalse(title.Overlaps(DungeonHudView.PartyRect));
            Assert.Less(Mathf.Abs(title.X + title.Width / 2 - DungeonHudView.ReferenceWidth / 2), 2, "top-centre");
            Assert.IsNotNull(view.RoomTitlePanel);
            // The run's contextual tutorial prompt lives on another canvas above the HUD; it must not be drawn over
            // the room name (it was, before this pass moved it down).
            Assert.IsFalse(title.Overlaps(RuinRail.App.ExpeditionScene.TutorialPromptRect),
                $"the tutorial prompt band {RuinRail.App.ExpeditionScene.TutorialPromptRect} must clear the room-title band {title}");
        }

        // ---- Low-HP vignette ----

        [UnityTest]
        public IEnumerator TheVignette_AppearsAtTheThreshold_PulsesHarderNearDeath_AndClearsWhenHealed()
        {
            var view = View(out var vm);
            var vignette = view.Vignette;
            Assert.IsTrue(vignette.HasSprite, "the generated vignette frame is bound through the UI skin");
            Assert.AreEqual(0.30f, HudLowHealthVignetteView.DefaultThreshold, 0.0001f, "tunable default: 30 %");

            // Effective max HP, never a base value: 120 with armor, so the threshold is 36, not 30.
            vignette.Show(50, 120);
            Assert.IsFalse(vignette.IsActive, "50 / 120 is above the threshold");
            vignette.Show(36, 120);
            Assert.IsTrue(vignette.IsActive, "exactly at the threshold it is on");
            Assert.AreEqual(0f, vignette.Intensity, 0.001f, "at the threshold it is at its gentlest");
            vignette.Show(30, 100);
            Assert.IsTrue(vignette.IsActive, "the same 30 % of a base-HP player");
            vignette.Show(6, 120);
            Assert.Greater(vignette.Intensity, 0.7f, "closer to death, stronger");

            // The view re-renders from the snapshot every time the view model publishes, so the snapshot has to
            // agree with the state under test for the frame-driven part below.
            vm.Snapshot.Hp = 6;
            vm.Snapshot.MaxHp = 120;
            yield return null;
            yield return null;
            Assert.IsTrue(vignette.IsActive, "the render agrees with the snapshot");
            Assert.IsTrue(vignette.IsVisible);
            var a = vignette.Alpha;
            var changed = false;
            for (var i = 0; i < 40 && !changed; i++) { yield return null; changed = Mathf.Abs(vignette.Alpha - a) > 0.001f; }
            Assert.IsTrue(changed, "it breathes rather than sitting at one opacity");
            Assert.LessOrEqual(vignette.Alpha, HudLowHealthVignetteView.DefaultCriticalMaxAlpha + 0.01f, "the centre stays readable: never a full red screen");

            vm.Snapshot.Hp = 120;
            vignette.Show(120, 120);
            Assert.IsFalse(vignette.IsActive);
            var clearDeadline = Time.realtimeSinceStartup + 3f;
            while (vignette.IsVisible && Time.realtimeSinceStartup < clearDeadline) yield return null;
            Assert.IsFalse(vignette.IsVisible, "healing above the threshold fades it away");

            // An overlay window standing over the run suppresses it; death/scene change resets it outright.
            vignette.Show(6, 120);
            Assert.IsTrue(vignette.IsActive);
            vignette.Show(6, 120, suppressed: true);
            Assert.IsFalse(vignette.IsActive, "it never tints a menu");
            vignette.Show(6, 120);
            vignette.Reset();
            Assert.IsFalse(vignette.IsActive);
            Assert.AreEqual(0f, vignette.Alpha, "no stale state survives a scene change or a death");
            vignette.Show(0, 120);
            Assert.IsFalse(vignette.IsActive, "a dead player gets no danger frame");
            vignette.Show(10, 0);
            Assert.IsFalse(vignette.IsActive, "and neither does an unbound health component");
            Assert.IsFalse(vm.Snapshot.VignetteSuppressed);
        }

        [Test]
        public void TheVignetteSitsUnderTheHud_AndTheOverlayWindowsDrawAboveIt()
        {
            var view = View(out var vm);
            Assert.AreEqual(0, view.Vignette.transform.GetSiblingIndex(), "first child: every HUD element draws over it");
            Assert.AreEqual(0, view.Canvas.sortingOrder, "the HUD canvas stays under the overlay canvases (order 30)");
            vm.SetOverlayOpen(true);
            Assert.IsTrue(vm.Snapshot.VignetteSuppressed);
            vm.SetOverlayOpen(false);
            Assert.IsFalse(vm.Snapshot.VignetteSuppressed);
        }

        // ---- Dash cooldown wipe ----

        [Test]
        public void TheDashCooldown_IsAMovingVerticalWipe_NotAStaticGreyBlock()
        {
            var view = View(out _);
            var icon = view.DashIcon;
            Assert.IsTrue(icon.CoverIsVertical, "a vertical fill, so the grey recedes across the icon");

            // 0 % elapsed: the icon is fully covered.
            icon.Show(false, 1f, false);
            Assert.AreEqual(HudDashState.Cooldown, icon.State);
            Assert.AreEqual(1f, icon.Cooldown01, 0.001f);
            var full = icon.CoveredPixels;
            Assert.Greater(full, 0f);

            // 25 / 50 / 75 % elapsed: strictly less coverage each time, proportional to the remaining cooldown.
            icon.Show(false, 0.75f, false);
            var threeQuarters = icon.CoveredPixels;
            icon.Show(false, 0.5f, false);
            var half = icon.CoveredPixels;
            icon.Show(false, 0.25f, false);
            var quarter = icon.CoveredPixels;
            Assert.Less(threeQuarters, full);
            Assert.Less(half, threeQuarters);
            Assert.Less(quarter, half);
            Assert.AreEqual(full * 0.5f, half, 1.01f, "half the cooldown, half the cover");
            Assert.IsTrue(icon.IconVisible, "the icon stays identifiable under the overlay");

            // Ready: no overlay at all, and a one-shot flash rather than a repeating pulse.
            icon.Show(true, 0f, false);
            Assert.AreEqual(HudDashState.Ready, icon.State);
            Assert.AreEqual(0f, icon.Cooldown01);
            Assert.AreEqual(0f, icon.CoveredPixels);
            Assert.IsTrue(icon.ReadyFlashActive, "completing the cooldown flashes once");
            icon.Show(true, 0f, false);
            icon.Show(true, 0f, false);
            Assert.IsTrue(icon.ReadyFlashActive, "the same flash, not a new one per frame");

            // A second dash refills the cover from scratch.
            icon.Show(false, 1f, false);
            Assert.AreEqual(full, icon.CoveredPixels, 0.001f);
            Assert.IsFalse(icon.ReadyFlashActive);

            // Disabled (Downed/Dead) is its own state and never leaves a stale wipe behind.
            icon.Show(false, 0.4f, true);
            Assert.AreEqual(HudDashState.Disabled, icon.State);
            Assert.AreEqual(0f, icon.CoveredPixels);
        }

        [UnityTest]
        public IEnumerator TheDashWipe_TracksTheAuthoritativeCooldown_ThroughTheViewModel()
        {
            var go = new GameObject("DashPlayer");
            _created.Add(go);
            go.AddComponent<Rigidbody2D>();
            var health = go.AddComponent<RuinRail.Gameplay.Combat.HealthComponent>();
            health.SetMaxHealth(100);
            var dash = go.AddComponent<RuinRail.Gameplay.Player.PlayerDash>();
            var balance = UnityEditor.AssetDatabase.LoadAssetAtPath<RuinRail.Gameplay.Player.PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            dash.SetBalanceConfig(balance);

            var vm = new DungeonHudViewModel();
            vm.BindPlayer(health, dash);
            var view = DungeonHudView.Create(vm);
            _created.Add(view.gameObject);
            yield return null;
            Assert.AreEqual(HudDashState.Ready, view.DashIcon.State);

            Assert.IsTrue(dash.TryStartDash(Vector2.right));
            yield return null;
            var samples = 0;
            var deadline = Time.realtimeSinceStartup + 5f;
            while (!dash.CanDash && Time.realtimeSinceStartup < deadline)
            {
                var expected = dash.CooldownRemaining / dash.CurrentDashCooldown;
                Assert.AreEqual(expected, view.DashIcon.Cooldown01, 0.06f, "the wipe is the authoritative remaining fraction, never a UI timer");
                samples++;
                yield return null;
            }

            Assert.Greater(samples, 10, "the wipe moved over many frames rather than snapping");
            Assert.IsTrue(dash.CanDash);
            yield return null;
            Assert.AreEqual(HudDashState.Ready, view.DashIcon.State);
            Assert.AreEqual(0f, view.DashIcon.CoveredPixels, "the overlay is gone the moment the dash is ready");
        }

        [Test]
        public void TheHudSourceHasNoSpritelessFilledImage_WhichUguiSilentlyDrawsAsAFullBlock()
        {
            // The dash wipe's original bug: Image.type is ignored when an image has no sprite. Every filled overlay in
            // the HUD must therefore come from the one builder that hands it a sprite.
            foreach (var path in System.IO.Directory.GetFiles("Assets/Game/Scripts/UI/Hud", "*.cs", System.IO.SearchOption.AllDirectories))
            {
                var source = System.IO.File.ReadAllText(path);
                var declaresFilled = source.Contains("Image.Type.Filled");
                if (!declaresFilled) continue;
                Assert.IsTrue(source.Contains("UiBuild.Fillable") || source.Contains(".sprite ="),
                    $"{path}: a Filled image must be given a sprite or it draws as a solid rectangle");
            }

            Assert.IsNotNull(UiBuild.Solid(), "the shared fill sprite exists");
            Assert.AreSame(UiBuild.Solid(), UiBuild.Solid(), "and is created once per process");
        }
    }
}
