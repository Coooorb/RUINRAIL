using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Core.Rendering;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Grid;
using RuinRail.Presentation.Animation;
using RuinRail.UI.Base;
using RuinRail.UI.Hud;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RuinRail.Tests
{
    /// <summary>
    /// Live-run proof of the three post-polish regression fixes, measured off real expedition frames rather than
    /// asserted about: the player body is drawn where the player stands, every doorway of every placed room either
    /// leads into its connected neighbour or is walled, and the lower-left HUD draws its three rows in separate bands
    /// with the pixel face. Captures land in <c>TestResults/RegressionProof</c>.
    /// </summary>
    public sealed class DungeonRegressionProofTests
    {
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_proof_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var screen in Object.FindObjectsByType<MainMenuScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            foreach (var screen in Object.FindObjectsByType<BaseHubScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            // The dungeon scene stays loaded after this fixture: its room geometry, player, camera and canvases would
            // otherwise leak into every later physics test in the run. Clear everything but the test runner itself.
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || IsTestRunner(root)) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private static bool IsTestRunner(GameObject root)
        {
            if (root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var component in root.GetComponents<Component>())
            {
                if (component != null && (component.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools")) return true;
            }

            return false;
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time (last: '{_app.ComposedScene}').");
                yield return null;
            }
        }

        private IEnumerator EnterDungeon()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Proof Runner");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator LiveDungeon_PlayerIsDrawn_ExitsAreClosed_HudRowsAreSeparate()
        {
            yield return EnterDungeon();
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            Assert.IsNotNull(run);
            var ppu = run.Camera.Config.PixelsPerUnit;
            var player = run.Rig.Player;

            // ---- Priority 1: the player is composed with a body that is actually drawn ----
            Assert.IsNotNull(player, "the player spawned");
            Assert.IsTrue(player.activeInHierarchy);
            var startRoom = run.Rooms[run.Generation.Graph.StartId];
            Assert.IsTrue(startRoom.State.EntryCount > 0 || Vector2.Distance(player.transform.position, startRoom.transform.position) < 40f, "the player stands in the start room");
            var renderer = CharacterVisual.RendererOf(player);
            Assert.IsNotNull(renderer, "the player visual exists");
            Assert.IsTrue(renderer.enabled && renderer.gameObject.activeInHierarchy, "the sprite renderer is enabled");
            Assert.IsNotNull(renderer.sprite, "a sprite is assigned");
            Assert.AreEqual(1f, renderer.color.a, 1e-4f, "alpha is opaque");
            Assert.AreEqual(Color.white, renderer.color, "no tint");
            Assert.AreEqual(SortingLayers.Characters, renderer.sortingLayerName, "characters draw above the floor layers");
            Assert.Less(System.Array.IndexOf(SortingLayers.Ordered.ToArray(), SortingLayers.GroundDetails), System.Array.IndexOf(SortingLayers.Ordered.ToArray(), renderer.sortingLayerName));
            var driver = player.GetComponent<PlayerAnimationDriver>();
            Assert.IsNotNull(driver, "the animation driver is bound");
            Assert.AreSame(renderer, driver.Animator.Renderer);
            Assert.IsFalse(driver.Animator.IsPlaceholder, $"the driver found its clip ({driver.State}/{driver.Facing}); missing: {string.Join(",", driver.Animator.MissingClips)}");

            var shot = LiveDungeonCapture.Capture("live_start_room", run.Camera.Camera, ppu);
            renderer.enabled = false;
            var without = LiveDungeonCapture.Capture("live_start_room_player_hidden", run.Camera.Camera, ppu);
            renderer.enabled = true;

            var feet = shot.WorldToPixel(player.transform.position);
            var bodyRect = new RectInt(feet.x - 20, feet.y - 12, 40, 56);
            var differing = 0;
            for (var y = bodyRect.yMin; y < bodyRect.yMax; y++)
            for (var x = bodyRect.xMin; x < bodyRect.xMax; x++)
            {
                var a = shot.At(x, y);
                var b = without.At(x, y);
                if (a.r != b.r || a.g != b.g || a.b != b.b) differing++;
            }

            Assert.Greater(differing, 150, $"the player body changes the frame where the player stands ({differing} px differ in {bodyRect})");

            // ---- Priority 2: every doorway leads somewhere, or is wall ----
            Assert.IsEmpty(run.ExitProblems, string.Join("\n", run.ExitProblems));
            var problems = DungeonExitValidator.Validate(run.Generation.Layout, run.Rooms.ToDictionary(kv => kv.Key, kv => kv.Value.Root));
            Assert.IsEmpty(problems, string.Join("\n", problems));

            var bounds = run.Generation.Layout.TotalBounds();
            var centre = new Vector2((bounds.xMin + bounds.xMax) * 0.5f, (bounds.yMin + bounds.yMax) * 0.5f) * GridConstants.TileWorldSize;
            var aspect = LiveDungeonCapture.Width / (float)LiveDungeonCapture.Height;
            var size = Mathf.Max(bounds.height * 0.5f + 1f, (bounds.width * 0.5f + 1f) / aspect) * GridConstants.TileWorldSize;
            var overview = LiveDungeonCapture.Capture("live_dungeon_overview", run.Camera.Camera, centre, size, ppu, includeUi: false);

            var sealedSockets = 0;
            var closeUps = 0;
            foreach (var placement in run.Generation.Layout.Placements)
            {
                var room = run.Rooms[placement.NodeId].Root;
                foreach (var socket in room.GetSockets())
                {
                    if (run.Generation.Layout.IsSocketUsed(placement.NodeId, socket.Direction)) continue;
                    sealedSockets++;
                    Assert.IsTrue(RoomExitSealer.IsSealed(room, socket), $"room {placement.NodeId} {socket.Direction} is walled");
                    foreach (var cell in socket.Cells())
                    {
                        var world = (Vector2)room.transform.TransformPoint(GridCoordinates.CellToWorldCenter(cell));
                        var px = overview.WorldToPixel(world);
                        Assert.Greater(overview.CountLit(new RectInt(px.x - 1, px.y - 1, 3, 3)), 0, $"sealed cell {cell} of room {placement.NodeId} ({socket.Direction}) is drawn as wall, not void, in the overview");
                    }

                    if (closeUps < 2)
                    {
                        var world = (Vector2)room.transform.TransformPoint(socket.transform.localPosition);
                        var close = LiveDungeonCapture.Capture($"live_sealed_exit_{closeUps + 1}", run.Camera.Camera, world, LiveDungeonCapture.Height / (2f * ppu), ppu, includeUi: false);
                        // The door run lies along the edge: horizontal on North/South sockets, vertical on East/West.
                        var horizontal = RuinRail.Dungeon.Rooms.DoorDirections.IsHorizontalEdge(socket.Direction);
                        var doorRect = horizontal
                            ? new RectInt(LiveDungeonCapture.Width / 2 - socket.Width * ppu / 2, LiveDungeonCapture.Height / 2 - ppu / 2, socket.Width * ppu, ppu)
                            : new RectInt(LiveDungeonCapture.Width / 2 - ppu / 2, LiveDungeonCapture.Height / 2 - socket.Width * ppu / 2, ppu, socket.Width * ppu);
                        Assert.Less(close.BlackShare(doorRect), 0.2f, $"the sealed doorway ({placement.NodeId}.{socket.Direction}) is drawn as wall in the close-up");
                        closeUps++;
                    }
                }
            }

            Debug.Log($"Proof: {sealedSockets} spare sockets sealed across {run.Rooms.Count} rooms; {run.Generation.Layout.Connections.Count} reciprocal connections.");

            // ---- Priority 3: lower-left HUD rows are readable and separate ----
            var hud = Object.FindObjectsByType<DungeonHudView>(FindObjectsSortMode.None);
            Assert.AreEqual(1, hud.Length, "exactly one HUD, so no duplicate text layers");
            var view = hud[0];
            var texts = view.HpPanel.GetComponentsInChildren<Text>(true);
            Assert.AreEqual(1, texts.Length, "HP text once; the dash indicator is an icon slot beside it");
            foreach (var t in texts)
            {
                Assert.AreSame(UiFont.Font(), t.font, $"{t.name} uses the project pixel face");
                Assert.AreEqual(UiText.GlyphHeight, t.fontSize);
            }

            var hpRect = ReferenceRect((RectTransform)texts.First(t => t.name == "HpText").transform, view);
            var dashRect = ReferenceRect(view.DashPanel, view);
            Assert.IsFalse(hpRect.Overlaps(dashRect), $"HP {hpRect} and the dash slot {dashRect} do not share pixels");
            StringAssert.IsMatch(@"^\d+ / \d+", view.HpText);
            Assert.IsEmpty(view.DashPanel.GetComponentsInChildren<Text>(true), "no DASH text: the dash is an icon slot");
            Assert.AreEqual(HudDashState.Ready, view.DashIcon.State, "the dash icon reads READY at the start of the run");

            // Off the frame: each band holds drawn text and the bar is drawn above them.
            var hpBand = PixelRect(hpRect);
            var dashBand = PixelRect(dashRect);
            Assert.Greater(shot.CountLit(hpBand, 96), 40, "HP text pixels present in its band");
            Assert.Greater(shot.CountLit(dashBand, 60), 40, "dash icon pixels present in its slot");
            var barRect = PixelRect(ReferenceRect((RectTransform)view.HpPanel.Find("HpBarBack"), view));
            var red = 0;
            for (var y = barRect.yMin; y < barRect.yMax; y++)
            for (var x = barRect.xMin; x < barRect.xMax; x++)
            {
                var p = shot.At(x, y);
                if (p.r > 150 && p.g < 90 && p.b < 90) red++;
            }

            Assert.Greater(red, barRect.width * barRect.height / 2, "the HP bar fill is drawn");
            Assert.IsFalse(barRect.Overlaps(hpBand) || barRect.Overlaps(dashBand), "the bar has its own band");
        }

        /// <summary>Reference-pixel rect (origin bottom-left) of a HUD element anchored at a corner of a 640x360 canvas.</summary>
        private static Rect ReferenceRect(RectTransform rect, DungeonHudView view)
        {
            var reference = new Vector2(DungeonHudView.ReferenceWidth, DungeonHudView.ReferenceHeight);
            var origin = Vector2.zero;
            var current = rect;
            var size = rect.sizeDelta;
            while (current != null && current != (RectTransform)view.transform)
            {
                var parent = current.parent as RectTransform;
                var parentSize = parent == (RectTransform)view.transform || parent == null ? reference : parent.sizeDelta;
                origin += Vector2.Scale(current.anchorMin, parentSize) + current.anchoredPosition - Vector2.Scale(current.pivot, current.sizeDelta);
                current = parent;
            }

            return new Rect(origin, size);
        }

        private static RectInt PixelRect(Rect r) => new(Mathf.RoundToInt(r.xMin), Mathf.RoundToInt(r.yMin), Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height));
    }
}
