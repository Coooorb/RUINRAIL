using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.UI.Multiplayer;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 134 — in-run party status: revive prompt/progress, downed bleedout, dead spectator cycling, disconnect and host-failure feedback, all read from authoritative models.</summary>
    public class PartyStatusUiTests
    {
        private readonly List<Object> _created = new();
        private PlayerBalanceConfig _balance;

        [SetUp]
        public void SetUp()
        {
            _balance = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private (GameObject go, FakePlayerInputReader reader, PlayerLifeStateComponent life, HealthComponent health) Player(string name, PartyLifeRoster roster, Vector2 position)
        {
            var reader = new FakePlayerInputReader();
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = name, IsLocal = true, InputReader = reader, BalanceConfig = _balance, Position = position, LifeRoster = roster, ParticipantId = name });
            _created.Add(go);
            return (go, reader, go.GetComponent<PlayerLifeStateComponent>(), go.GetComponent<HealthComponent>());
        }

        private static void Kill(HealthComponent health) => health.TryApplyDamage(new DamageRequest(999999));

        [Test]
        public void RevivePrompt_Progress_BleedoutAndBeingRevived_FollowTheArbiter()
        {
            var roster = new PartyLifeRoster();
            var (localGo, localReader, local, _) = Player("Local", roster, Vector2.zero);
            var (mateGo, _, mate, mateHealth) = Player("Mate", roster, new Vector2(0.6f, 0f));
            var reviver = localGo.GetComponent<PlayerReviver>();
            var vm = new PartyStatusViewModel(roster, local, reviver, localGo.GetComponent<DeadSpectatorFollow>(), displayName: id => id == "Mate" ? "Rook" : id);
            var mateVm = new PartyStatusViewModel(roster, mate, mateGo.GetComponent<PlayerReviver>(), mateGo.GetComponent<DeadSpectatorFollow>());

            Assert.AreEqual(string.Empty, vm.RevivePrompt, "Nobody is Downed.");
            Assert.AreEqual("Rook: ALIVE", vm.Members[0].Text);
            Kill(mateHealth);
            Assert.AreEqual(PlayerLifeState.Downed, mate.State);
            Assert.AreEqual("Hold Interact to revive Rook", vm.RevivePrompt);
            Assert.AreEqual("Rook: DOWNED 20s", vm.Members[0].Text);
            Assert.AreEqual("DOWNED — 20s", mateVm.LocalStateText);

            localReader.InteractHeld = true;
            for (var i = 0; i < 10; i++) reviver.Step(0.1f);
            Assert.AreEqual(0.25f, vm.ReviveProgress01, 0.01f);
            StringAssert.StartsWith("Reviving Rook… 25%", vm.RevivePrompt);
            Assert.AreEqual("Rook: REVIVING 25%", vm.Members[0].Text);
            Assert.AreEqual(0.25f, mateVm.BeingRevivedProgress01, 0.01f, "The Downed player sees the same authoritative progress.");
            StringAssert.Contains("being revived 25%", mateVm.LocalStateText);

            // The UI cannot force success: only the arbiter's channel completing revives.
            for (var i = 0; i < 31; i++) reviver.Step(0.1f);
            Assert.AreEqual(PlayerLifeState.Alive, mate.State);
            Assert.AreEqual(string.Empty, vm.RevivePrompt);
            Assert.AreEqual("Rook: ALIVE", vm.Members[0].Text);
        }

        [Test]
        public void DeadSpectator_TargetAndCyclePrompt_DisconnectAndHostFailure_AreShown()
        {
            var roster = new PartyLifeRoster();
            var (localGo, localReader, local, localHealth) = Player("Local", roster, Vector2.zero);
            var (_, _, b, _) = Player("B", roster, new Vector2(5f, 0f));
            var (_, _, c, _) = Player("C", roster, new Vector2(10f, 0f));
            var disconnected = new HashSet<string>();
            var vm = new PartyStatusViewModel(roster, local, localGo.GetComponent<PlayerReviver>(), localGo.GetComponent<DeadSpectatorFollow>(), id => disconnected.Contains(id));

            Assert.IsFalse(vm.IsSpectating);
            Kill(localHealth);
            local.Tick(20.01f);
            Assert.AreEqual(PlayerLifeState.Dead, local.State);
            Assert.AreEqual("DEAD — spectating", vm.LocalStateText);
            Assert.IsTrue(vm.IsSpectating);
            Assert.AreEqual("B", vm.SpectatorTargetName);
            Assert.AreEqual("Spectating B — Interact: next teammate", vm.SpectatorPrompt);
            Assert.AreEqual(2, vm.SpectatorCandidates);
            localReader.RaiseInteract();
            Assert.AreEqual("C", vm.SpectatorTargetName, "Interact cycles to the next living teammate.");
            Assert.AreEqual(string.Empty, vm.RevivePrompt, "A dead player revives nobody.");

            disconnected.Add("B");
            Assert.AreEqual("B: DISCONNECTED (reconnecting…)", vm.Members.First(m => m.Name == "B").Text);
            disconnected.Remove("B");
            Assert.AreEqual("B: ALIVE", vm.Members.First(m => m.Name == "B").Text);

            vm.ReportSessionLost(NetworkLifecycleState.Failed, expeditionFailed: true);
            StringAssert.Contains("Connection to the host was lost. The expedition failed", vm.SessionOutcomeText);
            StringAssert.Contains("XP is kept", vm.SessionOutcomeText);
            Assert.IsFalse(vm.SessionOutcomeText.Contains("Exception"));
        }
    }
}
