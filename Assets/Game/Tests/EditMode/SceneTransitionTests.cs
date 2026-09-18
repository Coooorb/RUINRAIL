using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.UI.Transitions;

namespace RuinRail.Tests.EditMode
{
    /// <summary>
    /// TASK 178 — the transition overlay exists to stop two concrete bugs: a raw frame appearing mid-load, and a
    /// player double-submitting an action while the screen is changing. Both are pinned here.
    /// </summary>
    public sealed class SceneTransitionTests
    {
        private static SceneTransitionViewModel Covered(out List<string> loads)
        {
            var vm = new SceneTransitionViewModel();
            var requested = new List<string>();
            vm.LoadRequested += requested.Add;
            loads = requested;
            return vm;
        }

        [Test]
        public void Load_HappensOnlyAfterTheScreenIsFullyCovered()
        {
            var vm = Covered(out var loads);
            vm.Begin(TransitionKind.ShelterToExpedition, SceneNames.Dungeon);

            Assert.AreEqual(TransitionPhase.CoveringIn, vm.Phase);
            CollectionAssert.IsEmpty(loads, "Loading before the cover is complete is exactly the raw-frame bug.");

            vm.Tick(SceneTransitionViewModel.CoverSeconds * 0.5f);
            CollectionAssert.IsEmpty(loads);
            Assert.Less(vm.CoverAlpha, 1f);

            vm.Tick(SceneTransitionViewModel.CoverSeconds);
            Assert.AreEqual(TransitionPhase.Loading, vm.Phase);
            CollectionAssert.AreEqual(new[] { SceneNames.Dungeon }, loads);
            Assert.AreEqual(1f, vm.CoverAlpha, "The screen stays fully covered across the load.");
        }

        [Test]
        public void CoverStaysOpaque_UntilTheDestinationHasComposed()
        {
            var vm = Covered(out _);
            vm.Begin(TransitionKind.MainMenuToShelter, SceneNames.Base);
            vm.Tick(SceneTransitionViewModel.CoverSeconds);

            // A slow compose must not let the cover lift early.
            for (var i = 0; i < 20; i++) vm.Tick(0.1f);
            Assert.AreEqual(1f, vm.CoverAlpha, "No half-built scene may be shown.");
            Assert.AreEqual(TransitionPhase.Loading, vm.Phase);

            vm.DestinationComposed();
            Assert.AreEqual(TransitionPhase.Revealing, vm.Phase);

            vm.Tick(SceneTransitionViewModel.RevealSeconds);
            Assert.AreEqual(TransitionPhase.Idle, vm.Phase);
            Assert.AreEqual(0f, vm.CoverAlpha);
            Assert.IsFalse(vm.IsTransitioning);
        }

        [Test]
        public void SecondRequestWhileCoveringOrLoading_IsIgnored_NotQueued()
        {
            var vm = Covered(out var loads);

            Assert.IsTrue(vm.Begin(TransitionKind.ShelterToExpedition, SceneNames.Dungeon));
            Assert.IsFalse(vm.Begin(TransitionKind.ShelterToExpedition, SceneNames.Dungeon),
                "Pressing START twice must not launch two expeditions.");
            Assert.IsFalse(vm.Begin(TransitionKind.ShelterToMainMenu, SceneNames.MainMenu));

            vm.Tick(SceneTransitionViewModel.CoverSeconds);
            Assert.AreEqual(TransitionPhase.Loading, vm.Phase);
            Assert.IsFalse(vm.Begin(TransitionKind.ShelterToMainMenu, SceneNames.MainMenu),
                "Still blocked while the load is in flight.");

            Assert.AreEqual(1, loads.Count, "Exactly one load, no matter how many times the player pressed.");
        }

        [Test]
        public void ANewNavigationDuringTheReveal_IsHonoured_NotDropped()
        {
            var vm = Covered(out var loads);
            vm.Begin(TransitionKind.MainMenuToShelter, SceneNames.Base);
            vm.Tick(SceneTransitionViewModel.CoverSeconds);
            vm.DestinationComposed();

            Assert.AreEqual(TransitionPhase.Revealing, vm.Phase);
            Assert.IsTrue(vm.Begin(TransitionKind.ShelterToExpedition, SceneNames.Dungeon),
                "The destination is already live, so this is a real navigation, not a stray second press. Dropping it would deadlock the flow.");

            vm.Tick(SceneTransitionViewModel.CoverSeconds);
            CollectionAssert.AreEqual(new[] { SceneNames.Base, SceneNames.Dungeon }, loads);
        }

        [Test]
        public void InputIsGated_FromCoverStartUntilTheDestinationHasComposed()
        {
            var vm = new SceneTransitionViewModel();
            Assert.IsFalse(vm.BlocksInput);

            vm.Begin(TransitionKind.DepthTransit, SceneNames.Dungeon);
            Assert.IsTrue(vm.BlocksInput, "Covering: the player must not act on a screen that is going away.");

            vm.Tick(SceneTransitionViewModel.CoverSeconds);
            Assert.IsTrue(vm.BlocksInput, "Loading: this is the window where a double-submit does damage.");

            vm.DestinationComposed();
            Assert.IsFalse(vm.BlocksInput, "Revealing: the scene is live and interactive; only the fade remains.");
            Assert.IsTrue(vm.IsTransitioning, "…but the overlay is still drawn.");

            vm.Tick(SceneTransitionViewModel.RevealSeconds);
            Assert.IsFalse(vm.IsTransitioning);
        }

        [Test]
        public void NoFakeProgressPercentageIsEverPresented()
        {
            var vm = new SceneTransitionViewModel();
            vm.Begin(TransitionKind.ShelterToExpedition, SceneNames.Dungeon);
            vm.Tick(SceneTransitionViewModel.CoverSeconds);

            Assert.IsTrue(vm.ShowsIndeterminateProgress,
                "SceneManager.LoadScene exposes no real progress metric, so the overlay must be indeterminate rather than invent a bar.");

            var members = typeof(SceneTransitionViewModel).GetProperties().Select(p => p.Name).ToList();
            CollectionAssert.DoesNotContain(members, "Progress");
            CollectionAssert.DoesNotContain(members, "Percent");
        }

        [Test]
        public void EveryTransitionPath_IsClassifiedAndLabelled()
        {
            var paths = new (string From, string To, TransitionKind Expected)[]
            {
                ("", SceneNames.MainMenu, TransitionKind.BootToMainMenu),
                (SceneNames.MainMenu, SceneNames.Base, TransitionKind.MainMenuToShelter),
                (SceneNames.Base, SceneNames.Dungeon, TransitionKind.ShelterToExpedition),
                (SceneNames.Dungeon, SceneNames.Dungeon, TransitionKind.DepthTransit),
                (SceneNames.Dungeon, SceneNames.Base, TransitionKind.ExpeditionToShelter),
                (SceneNames.Base, SceneNames.MainMenu, TransitionKind.ShelterToMainMenu)
            };

            foreach (var (from, to, expected) in paths)
            {
                Assert.AreEqual(expected, SceneTransitionViewModel.KindFor(from, to), $"{from} -> {to}");

                var vm = new SceneTransitionViewModel();
                vm.Begin(expected, to);
                Assert.IsNotEmpty(vm.Label, $"{expected} must tell the player what is happening.");
            }
        }

        [Test]
        public void Abort_DropsTheOverlayImmediately_SoAnErrorCanTakeTheScreen()
        {
            var vm = new SceneTransitionViewModel();
            vm.Begin(TransitionKind.ShelterToExpedition, SceneNames.Dungeon);

            vm.Abort();

            Assert.IsFalse(vm.IsTransitioning);
            Assert.AreEqual(0f, vm.CoverAlpha);
            Assert.AreEqual(TransitionKind.None, vm.Kind);
        }
    }
}
