using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 103: hold-to-revive (4 s, interruptions, 30% Max HP, ~1.5 s protection), host-arbitrated, no double completion.</summary>
    public class PlayerReviveTests
    {
        private readonly List<Object> _created = new();
        private PlayerBalanceConfig _balance;

        [SetUp]
        public void SetUp()
        {
            _balance = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            Assert.IsNotNull(_balance);
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private sealed class Actor
        {
            public GameObject Go;
            public FakePlayerInputReader Reader;
            public PlayerLifeStateComponent Life;
            public HealthComponent Health;
            public PlayerReviver Reviver;
            public ReviveProtection Protection;
            public PlayerDash Dash;
        }

        private Actor Player(string name, PartyLifeRoster roster, Vector2 position)
        {
            var reader = new FakePlayerInputReader();
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = name, IsLocal = true, InputReader = reader, BalanceConfig = _balance, Position = position, LifeRoster = roster });
            _created.Add(go);
            return new Actor
            {
                Go = go, Reader = reader, Life = go.GetComponent<PlayerLifeStateComponent>(), Health = go.GetComponent<HealthComponent>(),
                Reviver = go.GetComponent<PlayerReviver>(), Protection = go.GetComponent<ReviveProtection>(), Dash = go.GetComponent<PlayerDash>()
            };
        }

        private static void Kill(HealthComponent health) => health.TryApplyDamage(new DamageRequest(999999));

        private (PartyLifeRoster roster, Actor downed, Actor reviver) DownedPair(float distance = 0.5f)
        {
            var roster = new PartyLifeRoster();
            var downed = Player("Downed", roster, Vector2.zero);
            var reviver = Player("Reviver", roster, new Vector2(distance, 0f));
            Kill(downed.Health);
            Assert.AreEqual(PlayerLifeState.Downed, downed.Life.State);
            return (roster, downed, reviver);
        }

        private static void Hold(Actor reviver, float seconds, float step = 0.1f)
        {
            reviver.Reader.InteractHeld = true;
            var remaining = seconds;
            while (remaining > 0f)
            {
                var dt = Mathf.Min(step, remaining);
                reviver.Reviver.Step(dt);
                remaining -= dt;
            }
        }

        // ---- Acceptance 1: exact values ----

        [Test]
        public void ApprovedValues_NoConsumableNoClassNoSkillInvolved()
        {
            Assert.AreEqual(4f, _balance.ReviveChannelSeconds, 0.0001f);
            Assert.AreEqual(30, _balance.ReviveHealthPercent);
            Assert.AreEqual(1.5f, _balance.ReviveProtectionSeconds, 0.0001f);
            foreach (var type in new[] { typeof(PlayerReviver), typeof(ReviveArbiter), typeof(ReviveChannel) })
            {
                foreach (var member in type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    var n = member.Name.ToLowerInvariant();
                    Assert.IsFalse(n.Contains("consumable") || n.Contains("class") || n.Contains("skill") || n.Contains("defib"), $"{type.Name}.{member.Name}: standard revive needs no consumable/skill/class.");
                }
            }
        }

        [Test]
        public void HoldingInteract_ForFourSeconds_RevivesAtThirtyPercent_WithProtection_AndStopsBleedout()
        {
            var (roster, downed, reviver) = DownedPair();
            var completed = 0;
            roster.Revives.ChannelCompleted += _ => completed++;
            downed.Life.Tick(5f);
            Assert.AreEqual(15f, downed.Life.BleedoutRemaining, 0.0001f);

            Hold(reviver, 3.9f);
            Assert.IsTrue(reviver.Reviver.IsReviving);
            Assert.AreEqual(PlayerLifeState.Downed, downed.Life.State);
            Assert.AreEqual(3.9f, reviver.Reviver.Channel.Elapsed, 0.001f);

            Hold(reviver, 0.11f);
            Assert.AreEqual(PlayerLifeState.Alive, downed.Life.State);
            Assert.AreEqual(Mathf.RoundToInt(downed.Health.MaxHealth * 0.3f), downed.Health.CurrentHealth, "30% Max HP.");
            Assert.AreEqual(1.5f, downed.Protection.Remaining, 0.0001f);
            Assert.IsTrue(downed.Health.IsInvulnerable);
            Assert.AreEqual(0f, downed.Life.BleedoutRemaining);
            downed.Life.Tick(30f);
            Assert.AreEqual(PlayerLifeState.Alive, downed.Life.State, "Bleedout stopped.");
            Assert.AreEqual(1, completed);
            Assert.IsFalse(reviver.Reviver.IsReviving);
            Assert.AreEqual(0, roster.Revives.Channels.Count);
        }

        // ---- Acceptance 4: protection expires independently; damage works afterwards ----

        [Test]
        public void Protection_BlocksDamage_ThenExpires_AndDamageApplies()
        {
            var (_, downed, reviver) = DownedPair();
            Hold(reviver, 4.05f);
            Assert.AreEqual(PlayerLifeState.Alive, downed.Life.State);
            var hp = downed.Health.CurrentHealth;
            Assert.IsFalse(downed.Health.TryApplyDamage(new DamageRequest(5)), "Protected.");
            Assert.AreEqual(hp, downed.Health.CurrentHealth);

            downed.Protection.Tick(1.49f);
            Assert.IsTrue(downed.Health.IsInvulnerable);
            downed.Protection.Tick(0.02f);
            Assert.IsFalse(downed.Health.IsInvulnerable);
            Assert.IsTrue(downed.Health.TryApplyDamage(new DamageRequest(5)));
            Assert.AreEqual(hp - 5, downed.Health.CurrentHealth);
        }

        // ---- Acceptance 1: interruptions ----

        [Test]
        public void Interruptions_Release_MoveAway_Attack_Dash_Damage_AndInvalidReviver()
        {
            var (roster, downed, reviver) = DownedPair();
            var cancels = new List<ReviveCancelReason>();
            roster.Revives.ChannelCancelled += (_, r) => cancels.Add(r);

            Hold(reviver, 1f);
            reviver.Reader.InteractHeld = false;
            reviver.Reviver.Step(0.1f);
            Assert.IsFalse(reviver.Reviver.IsReviving);

            Hold(reviver, 1f);
            reviver.Go.transform.position = new Vector2(_balance.ReviveRangeTiles + 0.5f, 0f);
            reviver.Reviver.Step(0.1f);
            Assert.IsFalse(reviver.Reviver.IsReviving);
            reviver.Go.transform.position = new Vector2(0.5f, 0f);

            Hold(reviver, 1f);
            reviver.Reader.FireHeld = true;
            reviver.Reviver.Step(0.1f);
            Assert.IsFalse(reviver.Reviver.IsReviving);
            reviver.Reader.FireHeld = false;

            Hold(reviver, 1f);
            Assert.IsTrue(reviver.Health.TryApplyDamage(new DamageRequest(1)));
            reviver.Reviver.Step(0.1f);
            Assert.IsFalse(reviver.Reviver.IsReviving);

            Hold(reviver, 1f);
            Kill(reviver.Health);
            reviver.Reviver.Step(0.1f);
            Assert.IsFalse(reviver.Reviver.IsReviving);
            Assert.AreEqual(PlayerLifeState.Dead, reviver.Life.State, "Last Alive player at 0 HP dies (84).");
            Assert.IsFalse(reviver.Reviver.CanRevive);
            Hold(reviver, 1f);
            Assert.IsFalse(reviver.Reviver.IsReviving, "A Dead reviver cannot start a channel.");

            CollectionAssert.AreEqual(new[] { ReviveCancelReason.Released, ReviveCancelReason.MovedAway, ReviveCancelReason.Attacked, ReviveCancelReason.TookDamage, ReviveCancelReason.TookDamage }, cancels, "The killing blow is damage taken first; the Dead reviver then cannot start again.");
            Assert.AreEqual(PlayerLifeState.Downed, downed.Life.State, "Progress never completes a revive by accident.");
            Assert.AreEqual(0, roster.Revives.Completed);
        }

        [UnityTest]
        public IEnumerator Dashing_InterruptsTheChannel()
        {
            var (roster, downed, reviver) = DownedPair();
            var cancels = new List<ReviveCancelReason>();
            roster.Revives.ChannelCancelled += (_, r) => cancels.Add(r);
            reviver.Reader.InteractHeld = true;
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(reviver.Reviver.IsReviving);
            Assert.IsTrue(reviver.Dash.TryStartDash(Vector2.up));
            yield return new WaitForSeconds(0.3f);
            CollectionAssert.Contains(cancels, ReviveCancelReason.Dashed);
            Assert.AreEqual(PlayerLifeState.Downed, downed.Life.State);
            reviver.Reader.InteractHeld = false;
        }

        [Test]
        public void ProgressRestartsFromZero_AfterAnInterruption()
        {
            var (_, downed, reviver) = DownedPair();
            Hold(reviver, 3f);
            reviver.Reader.InteractHeld = false;
            reviver.Reviver.Step(0.1f);
            Hold(reviver, 3f);
            Assert.AreEqual(PlayerLifeState.Downed, downed.Life.State, "3 + 3 seconds is not a 4 s channel.");
            Assert.AreEqual(3f, reviver.Reviver.Channel.Elapsed, 0.001f);
        }

        // ---- Acceptance 2: two revivers cannot double-complete ----

        [Test]
        public void TwoRevivers_OnOneTarget_CompleteExactlyOnce_AndHealOnce()
        {
            var roster = new PartyLifeRoster();
            var downed = Player("Downed", roster, Vector2.zero);
            var a = Player("A", roster, new Vector2(0.5f, 0f));
            var b = Player("B", roster, new Vector2(-0.5f, 0f));
            Kill(downed.Health);
            var completed = 0;
            var heals = 0;
            roster.Revives.ChannelCompleted += _ => completed++;
            downed.Health.Healed += _ => heals++;

            a.Reader.InteractHeld = true;
            b.Reader.InteractHeld = true;
            for (var i = 0; i < 45; i++)
            {
                a.Reviver.Step(0.1f);
                b.Reviver.Step(0.1f);
            }

            Assert.AreEqual(PlayerLifeState.Alive, downed.Life.State);
            Assert.AreEqual(1, completed, "Exactly one channel completes.");
            Assert.AreEqual(1, heals, "Health restored once.");
            Assert.AreEqual(1, downed.Protection.Activations, "Protection applied once.");
            Assert.AreEqual(Mathf.RoundToInt(downed.Health.MaxHealth * 0.3f), downed.Health.CurrentHealth);
            Assert.AreEqual(0, roster.Revives.Channels.Count);
            Assert.IsTrue(a.Reviver.IsReviving == false && b.Reviver.IsReviving == false);
        }

        [Test]
        public void OneReviver_RevivesOnlyOneTargetAtATime_AndClientsNeverArbitrate()
        {
            var roster = new PartyLifeRoster();
            var d1 = Player("D1", roster, Vector2.zero);
            var d2 = Player("D2", roster, new Vector2(0.6f, 0f));
            var reviver = Player("R", roster, new Vector2(0.3f, 0f));
            Kill(d1.Health);
            Kill(d2.Health);
            Hold(reviver, 1f);
            Assert.IsTrue(reviver.Reviver.IsReviving);
            Assert.AreEqual(1, roster.Revives.Channels.Count, "One channel per reviver.");

            DamageAuthority.LocalIsAuthoritative = false;
            Hold(reviver, 5f);
            Assert.AreEqual(1f, reviver.Reviver.Channel.Elapsed, 0.001f, "A client never advances a channel.");
            Assert.AreEqual(PlayerLifeState.Downed, d1.Life.State);
            DamageAuthority.LocalIsAuthoritative = true;
            Assert.IsNull(roster.Revives.TryBegin(reviver.Reviver, d2.Life, 4f), "Busy reviver cannot open a second channel.");
        }

        [UnityTest]
        public IEnumerator ReviverUpdate_DrivesTheChannel_FromHeldInteract()
        {
            var (_, downed, reviver) = DownedPair();
            reviver.Reader.InteractHeld = true;
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(reviver.Reviver.IsReviving);
            Assert.Greater(reviver.Reviver.Channel.Progress, 0f);
            Assert.Less(reviver.Reviver.Channel.Progress, 0.5f);
            Assert.AreEqual(PlayerLifeState.Downed, downed.Life.State);
        }
    }
}
