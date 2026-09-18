using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Networking;
using RuinRail.UI.Multiplayer;
using UnityEditor;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 134 — terminal (host / code / join / leave / roster / ready) and transit-vote view models over the authoritative models.</summary>
    public sealed class MultiplayerUiTests
    {
        private ItemDefinitionRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            FakeMultiplayerServices.ResetRegistry();
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        private static T Wait<T>(Task<T> task)
        {
            task.Wait();
            return task.Result;
        }

        private static InventorySnapshot Loadout() => new()
        {
            Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } },
            Backpack = Array.Empty<InventorySnapshot.Entry>()
        };

        private (TerminalViewModel vm, MultiplayerTerminalService terminal, PartyLobby lobby, FakeMultiplayerServices services, FakeNetworkDriver driver) Terminal()
        {
            var services = new FakeMultiplayerServices();
            var driver = new FakeNetworkDriver();
            var terminal = new MultiplayerTerminalService(new NetworkSessionController(services, driver));
            var lobby = new PartyLobby(0, Resolve);
            lobby.Join(0, "host");
            return (new TerminalViewModel(terminal, lobby, 0), terminal, lobby, services, driver);
        }

        // ---- Acceptance 1: host / code / join / leave / roster / ready mirror authoritative state ----

        [Test]
        public void Terminal_HostShowsCode_CopyJoinLeave_AndRosterReadyStates_MirrorTheServices()
        {
            var (host, _, lobby, _, _) = Terminal();
            CollectionAssert.AreEqual(new[] { "SOLO", "HOST CO-OP", "JOIN BY CODE", "LEAVE SESSION", "READY", "COPY JOIN CODE" }, TerminalViewModel.Actions.Select(TerminalViewModel.Label));
            Assert.AreEqual(3, host.MaxPartySize);
            Assert.AreEqual("Choose Solo, Host or Join.", host.StatusText);
            Assert.IsFalse(host.IsEnabled(TerminalAction.Leave));
            Assert.IsFalse(host.IsEnabled(TerminalAction.CopyCode));

            // Keyboard/controller: move the selection to HOST and activate.
            host.MoveSelection(+1);
            Assert.AreEqual(TerminalAction.Host, host.SelectedAction);
            Assert.IsTrue(Wait(host.ActivateAsync()).IsNone);
            Assert.IsTrue(host.IsInSession);
            Assert.AreEqual(6, host.JoinCodeToShare.Length);
            StringAssert.Contains(host.JoinCodeToShare, host.StatusText);
            Assert.IsTrue(host.IsEnabled(TerminalAction.CopyCode));
            Assert.IsTrue(Wait(host.ActivateAsync(TerminalAction.CopyCode)).IsNone);
            Assert.AreEqual(host.JoinCodeToShare, host.LastCopiedCode);
            Assert.AreEqual(1, host.CopyRequests);
            Assert.IsFalse(host.IsEnabled(TerminalAction.Host), "Already in a session.");

            // A client joins by code (its own terminal, same fake registry), the lobby roster shows both with ready states.
            var (client, _, _, _, _) = Terminal();
            client.SetJoinCodeInput(" " + host.JoinCodeToShare.ToLowerInvariant() + " ");
            Assert.IsTrue(client.JoinCodeInputIsWellFormed);
            Assert.IsTrue(Wait(client.ActivateAsync(TerminalAction.Join)).IsNone, client.ErrorText);
            Assert.IsTrue(client.IsInSession);
            Assert.AreEqual("In party.", client.StatusText);

            lobby.Join(1, "mate");
            lobby.SetLoadout(0, Loadout());
            Assert.AreEqual(2, host.Roster.Count);
            Assert.AreEqual("LOADOUT INVALID", host.Roster[1].StatusText, "No loadout yet.");
            Assert.IsTrue(host.Roster[0].IsHost && host.Roster[0].IsLocal);
            Assert.AreEqual("NOT READY", host.Roster[0].StatusText);
            Assert.IsTrue(Wait(host.ActivateAsync(TerminalAction.ToggleReady)).IsNone);
            Assert.AreEqual("READY", host.Roster[0].StatusText, "Ready mirrors the lobby (authoritative).");
            lobby.SetLoadout(1, Loadout());
            lobby.SetReady(1, true);
            Assert.IsTrue(host.Roster.All(r => r.IsReady));
            lobby.Join(2, "third");
            Assert.Throws<InvalidOperationException>(() => lobby.Join(3, "fourth"), "Max 3.");
            Assert.AreEqual(3, host.Roster.Count);

            Assert.IsTrue(Wait(host.ActivateAsync(TerminalAction.Leave)).IsNone);
            Assert.IsFalse(host.IsInSession);
            Assert.IsNull(host.JoinCodeToShare);
            Assert.IsFalse(host.IsEnabled(TerminalAction.CopyCode));
        }

        // ---- Acceptance 4 / Req 2: deterministic, non-technical error feedback ----

        [Test]
        public void Terminal_SurfacesErrorsAsApprovedText_NeverExceptions()
        {
            var (vm, terminal, _, services, driver) = Terminal();
            vm.SetJoinCodeInput("ab");
            Assert.IsFalse(vm.JoinCodeInputIsWellFormed);
            var malformed = Wait(vm.ActivateAsync(TerminalAction.Join));
            Assert.AreEqual(ServiceErrorKind.InvalidCode, malformed.Kind);
            Assert.AreEqual("That join code is invalid or has expired.", malformed.Message);
            Assert.AreEqual(0, services.InitializeCalls, "Malformed codes never reach the services.");

            vm.SetJoinCodeInput("ABC234");
            var missing = Wait(vm.ActivateAsync(TerminalAction.Join));
            Assert.AreEqual(ServiceErrorKind.InvalidCode, missing.Kind);
            Assert.AreEqual(TerminalState.Error, vm.State);
            Assert.AreEqual(missing.Message, vm.StatusText);
            Assert.IsTrue(vm.ErrorIsRetryable);

            services.IsAvailable = false;
            var offline = Wait(vm.ActivateAsync(TerminalAction.Host));
            Assert.AreEqual(ServiceErrorKind.Unavailable, offline.Kind);
            StringAssert.Contains("still play solo", vm.ErrorText);
            Assert.IsTrue(Wait(vm.ActivateAsync(TerminalAction.Solo)).IsNone, "Solo always works.");
            Assert.AreEqual("Solo expedition.", vm.StatusText);

            services.IsAvailable = true;
            Assert.IsTrue(Wait(vm.ActivateAsync(TerminalAction.Host)).IsNone);
            driver.Drop();
            Assert.AreEqual(NetworkLifecycleState.Failed, terminal.Controller.State);
            Assert.IsFalse(vm.IsInSession);
            foreach (var text in new[] { vm.StatusText, vm.ErrorText, SessionError.For(ServiceErrorKind.Timeout).Message, SessionError.For(ServiceErrorKind.Unknown).Message })
            {
                Assert.IsFalse(text.Contains("Exception") || text.Contains("   at ") || text.Contains("StackTrace"), "No raw exception text as final UX.");
            }

            var full = SessionError.For(ServiceErrorKind.SessionFull);
            Assert.AreEqual("That party is already full (3 players).", full.Message);
            Assert.IsFalse(full.IsRetryable);
        }

        // ---- Acceptance 2 + 3 (vote side): living-only controls, dead-return warning, no local forcing ----

        [Test]
        public void TransitVote_LivingOnlyControls_DeadReturnWarningBeforeReturn_AndNoLocalResolution()
        {
            var decision = new TransitDecision(new PartyTransitPolicy(), new[] { "a", "b" }, new[] { "c" });
            decision.Open();
            using var a = new TransitVoteViewModel(decision, "a", id => id.ToUpperInvariant());
            using var c = new TransitVoteViewModel(decision, "c", id => id.ToUpperInvariant());

            Assert.IsTrue(a.HasVoteControls);
            Assert.IsFalse(c.HasVoteControls, "86: Dead players have no vote controls.");
            Assert.AreEqual("Dead players have no vote.", c.NoVoteText);
            Assert.IsFalse(c.Vote(TransitChoice.ReturnToShelter), "A dead player's vote cannot be forced from the UI.");
            Assert.AreEqual(TransitDecisionState.Open, decision.State);
            CollectionAssert.AreEqual(new[] { "A", "B" }, a.PendingVoterNames);
            StringAssert.Contains("Waiting for: A, B", a.StatusText);

            // Return while C is Dead: the warning must be acknowledged first; nothing is submitted until confirmed.
            Assert.IsFalse(a.Vote(TransitChoice.ReturnToShelter));
            Assert.IsTrue(a.AwaitingReturnConfirmation);
            StringAssert.Contains("C is Dead", a.ReturnWarningText);
            StringAssert.Contains("loses their carried gear", a.ReturnWarningText);
            Assert.IsNull(a.LocalVote);
            a.CancelReturn();
            Assert.IsFalse(a.AwaitingReturnConfirmation);
            Assert.IsNull(a.LocalVote, "Cancelled: no vote cast.");

            // Descend needs no warning and needs everyone living.
            Assert.IsTrue(a.Vote(TransitChoice.DescendDeeper));
            Assert.AreEqual(TransitChoice.DescendDeeper, a.LocalVote);
            Assert.IsFalse(a.IsResolved, "B has not voted; the UI cannot resolve on its own.");
            CollectionAssert.AreEqual(new[] { "B" }, a.PendingVoterNames);

            // A changes to Return: warning, confirm, resolved for the whole party.
            Assert.IsFalse(a.Vote(TransitChoice.ReturnToShelter));
            Assert.IsTrue(a.ConfirmReturn());
            Assert.IsTrue(a.IsResolved);
            Assert.AreEqual(TransitChoice.ReturnToShelter, a.Result);
            Assert.AreEqual("The party returns to the Shelter.", c.StatusText);

            // No warning when nobody is Dead.
            var clean = new TransitDecision(new PartyTransitPolicy(), new[] { "a", "b" });
            clean.Open();
            using var cleanVm = new TransitVoteViewModel(clean, "a");
            Assert.AreEqual(string.Empty, cleanVm.ReturnWarningText);
            Assert.IsTrue(cleanVm.Vote(TransitChoice.ReturnToShelter), "Return without a dead teammate needs no confirmation.");
            Assert.IsTrue(cleanVm.IsResolved);
        }
    }
}
