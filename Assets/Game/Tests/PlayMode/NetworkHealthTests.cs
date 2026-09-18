using System.Collections.Generic;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 096: host-only health commits, versioned once-only replication, duplicate hit guards, zero-HP hook.</summary>
    public class NetworkHealthTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private HealthComponent Health(int max = 100)
        {
            var go = new GameObject("Health");
            _created.Add(go);
            var health = go.AddComponent<HealthComponent>();
            health.SetMaxHealth(max);
            return health;
        }

        private sealed class DashLike : IInvulnerabilityState
        {
            public bool IsInvulnerable { get; set; }
        }

        [Test]
        public void ClientProcess_CannotForgeHealth_NeitherDamageNorHeal()
        {
            var health = Health();
            health.TryApplyDamage(new DamageRequest(30));
            DamageAuthority.LocalIsAuthoritative = false;
            Assert.IsFalse(health.TryApplyDamage(new DamageRequest(30)));
            Assert.IsFalse(health.Heal(30));
            Assert.AreEqual(70, health.CurrentHealth, "Neither damage nor heal is applied on a client.");
            Assert.IsNull(typeof(HealthComponent).GetProperty("CurrentHealth").GetSetMethod(), "No public setter for current health.");
        }

        [Test]
        public void HostReplicator_PublishesExactIntegers_Once_WithMonotonicVersions()
        {
            var host = Health(100);
            var dashLike = new DashLike();
            host.SetInvulnerabilityState(dashLike);
            var replicator = new HealthReplicator(host);
            var published = new List<HealthNetState>();
            replicator.StateChanged += published.Add;

            Assert.IsTrue(host.TryApplyDamage(new DamageRequest(23)));
            dashLike.IsInvulnerable = true;
            Assert.IsFalse(host.TryApplyDamage(new DamageRequest(50)), "Dash iFrames are evaluated on the host: no hit, no publish.");
            dashLike.IsInvulnerable = false;
            Assert.IsTrue(host.Heal(10));
            Assert.IsTrue(host.TryApplyDamage(new DamageRequest(87)));

            CollectionAssert.AreEqual(new[] { 77, 87, 0 }, published.ConvertAll(s => s.Current));
            CollectionAssert.AreEqual(new uint[] { 1, 2, 3 }, published.ConvertAll(s => s.Version));
            Assert.AreEqual(23, published[0].LastDamage);
            Assert.AreEqual(10, published[1].LastHeal);
            Assert.IsFalse(published[2].IsAlive);
            Assert.AreEqual(3, replicator.Publishes);
            replicator.Dispose();
        }

        [Test]
        public void ClientApplier_AppliesEachVersionOnce_IgnoresDuplicatesAndLateMessages()
        {
            var host = Health(100);
            var replicator = new HealthReplicator(host);
            var states = new List<HealthNetState>();
            replicator.StateChanged += states.Add;
            host.TryApplyDamage(new DamageRequest(40));
            host.Heal(15);
            host.TryApplyDamage(new DamageRequest(5));

            var client = Health(100);
            DamageAuthority.LocalIsAuthoritative = false;
            var applier = new HealthReplicaApplier(client);
            var feedback = new List<int>();
            client.Damaged += feedback.Add;

            Assert.IsTrue(applier.Apply(states[0]));
            Assert.IsTrue(applier.Apply(states[1]));
            Assert.IsFalse(applier.Apply(states[1]), "Duplicate message ignored.");
            Assert.IsFalse(applier.Apply(states[0]), "Late (older) message ignored.");
            Assert.IsTrue(applier.Apply(states[2]));
            Assert.AreEqual(host.CurrentHealth, client.CurrentHealth, "Exact integer result converges.");
            Assert.AreEqual(70, client.CurrentHealth);
            CollectionAssert.AreEqual(new[] { 40, 5 }, feedback, "Damage feedback fires per applied state, never for duplicates.");
            Assert.AreEqual(3, applier.Applied);
            Assert.AreEqual(2, applier.Ignored);
            replicator.Dispose();
        }

        [Test]
        public void HitGate_AppliesEachHitIdOnce()
        {
            var host = Health(100);
            var gate = new AuthoritativeHitGate();
            Assert.IsTrue(gate.TryApply(host, 1001, new DamageRequest(10)));
            Assert.IsFalse(gate.TryApply(host, 1001, new DamageRequest(10)), "Same hit delivered twice (trigger re-entry / late callback).");
            Assert.IsTrue(gate.TryApply(host, 1002, new DamageRequest(10)));
            Assert.AreEqual(80, host.CurrentHealth);
            Assert.AreEqual(2, gate.Applied);
            Assert.AreEqual(1, gate.Duplicates);
        }

        [Test]
        public void ZeroHealthHook_FiresOnce_AndRearmsAfterARevivingHeal()
        {
            var host = Health(50);
            var replicator = new HealthReplicator(host);
            var zero = 0;
            replicator.ZeroHealthReached += _ => zero++;
            host.TryApplyDamage(new DamageRequest(50));
            host.TryApplyDamage(new DamageRequest(50));
            Assert.AreEqual(1, zero, "Once per life.");
            Assert.IsFalse(host.IsAlive);

            // A revive (later tasks) restores health through the replicated path; the hook re-arms.
            host.ApplyReplicatedHealth(15, 50);
            Assert.IsTrue(host.IsAlive);
            host.Heal(5);
            host.TryApplyDamage(new DamageRequest(99));
            Assert.AreEqual(2, zero);
            replicator.Dispose();
        }

        [Test]
        public void DamageReductionAndInvulnerability_AreEvaluatedOnTheHostBeforeReplication()
        {
            var host = Health(100);
            host.SetIncomingDamageModifier(new HalvingModifier());
            var replicator = new HealthReplicator(host);
            HealthNetState last = default;
            replicator.StateChanged += s => last = s;
            host.TryApplyDamage(new DamageRequest(40));
            Assert.AreEqual(80, host.CurrentHealth, "Temporary DR applied on the host.");
            Assert.AreEqual(80, last.Current);
            Assert.AreEqual(20, last.LastDamage, "Clients receive the final integer, not the raw hit.");
            replicator.Dispose();
        }

        private sealed class HalvingModifier : IIncomingDamageModifier
        {
            public int ModifyIncomingDamage(DamageRequest request) => request.Amount / 2;
        }
    }
}
