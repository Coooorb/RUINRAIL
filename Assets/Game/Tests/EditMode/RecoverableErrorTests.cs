using System;
using System.Linq;
using NUnit.Framework;
using RuinRail.UI.Transitions;

namespace RuinRail.Tests.EditMode
{
    /// <summary>
    /// TASK 178 — an error screen has to do three things: say what happened in plain language, always offer a way
    /// out, and keep the underlying detail for diagnostics instead of swallowing it.
    /// </summary>
    public sealed class RecoverableErrorTests
    {
        private static readonly RecoverableErrorKind[] AllKinds =
            (RecoverableErrorKind[])Enum.GetValues(typeof(RecoverableErrorKind));

        [Test]
        public void EveryErrorKind_IsHumanReadableAndOffersAWayOut()
        {
            foreach (var kind in AllKinds)
            {
                var vm = new RecoverableErrorViewModel();
                vm.Show(kind, "System.IO.IOException: raw detail the player must never be shown");

                Assert.IsTrue(vm.IsShowing);
                Assert.IsNotEmpty(vm.Title, $"{kind} needs a title.");
                Assert.IsNotEmpty(vm.Message, $"{kind} needs a player-facing message.");
                Assert.IsNotEmpty(vm.Actions, $"{kind} must not be a dead end.");

                StringAssert.DoesNotContain("Exception", vm.Message, $"{kind} must not show a raw exception to the player.");
                StringAssert.DoesNotContain("System.", vm.Message);

                foreach (var action in vm.Actions)
                    Assert.IsNotEmpty(RecoverableErrorViewModel.Label(action), $"{action} needs a button label.");
            }
        }

        [Test]
        public void TechnicalDetail_IsPreservedForDiagnostics_NotSwallowed()
        {
            const string detail = "save.write: Save could not be committed: disk full.";
            var vm = new RecoverableErrorViewModel();
            vm.Show(RecoverableErrorKind.SaveFailed, detail);

            Assert.AreEqual(detail, vm.Technical, "Existing save/network diagnostics still need the original failure.");
            Assert.AreNotEqual(detail, vm.Message, "…but it is not what the player reads.");
        }

        [Test]
        public void SaveFailure_TellsThePlayerThePreviousSaveIsIntact()
        {
            var vm = new RecoverableErrorViewModel();
            vm.Show(RecoverableErrorKind.SaveFailed);

            StringAssert.Contains("intact", vm.Message,
                "Save semantics are atomic: the player must be told their previous save survived, or they will assume the worst.");
            CollectionAssert.Contains(vm.Actions.ToList(), ErrorAction.Retry);
        }

        [Test]
        public void ConnectionFailure_LetsThePlayerKeepPlayingSolo()
        {
            var vm = new RecoverableErrorViewModel();
            vm.Show(RecoverableErrorKind.ServiceConnection);

            CollectionAssert.Contains(vm.Actions.ToList(), ErrorAction.ContinueOffline,
                "A service outage must not lock a solo player out of their own game.");
            CollectionAssert.Contains(vm.Actions.ToList(), ErrorAction.Retry);
        }

        [Test]
        public void DefaultActionIsAlwaysSafe()
        {
            foreach (var kind in AllKinds)
            {
                var vm = new RecoverableErrorViewModel();
                vm.Show(kind);

                CollectionAssert.Contains(vm.Actions.ToList(), vm.DefaultAction);
                Assert.AreEqual(vm.Actions[0], vm.DefaultAction, "The first offered action is the focused one.");
            }
        }

        [Test]
        public void ChoosingAnAction_RaisesItOnceAndClosesTheScreen()
        {
            var vm = new RecoverableErrorViewModel();
            var chosen = 0;
            ErrorAction? last = null;
            vm.ActionChosen += a => { chosen++; last = a; };

            vm.Show(RecoverableErrorKind.SessionJoin);
            vm.Choose(ErrorAction.Retry);

            Assert.AreEqual(1, chosen);
            Assert.AreEqual(ErrorAction.Retry, last);
            Assert.IsFalse(vm.IsShowing);

            vm.Choose(ErrorAction.Retry);
            Assert.AreEqual(1, chosen, "A closed error screen must not fire again — that is the double-submit guard.");
        }

        [Test]
        public void ChoosingAnActionThatWasNotOffered_IsRejected()
        {
            var vm = new RecoverableErrorViewModel();
            vm.Show(RecoverableErrorKind.SceneLoadFailed);

            Assert.Throws<InvalidOperationException>(() => vm.Choose(ErrorAction.ContinueOffline),
                "A scene failure offers no offline path; taking one would leave the player somewhere undefined.");
        }
    }
}
