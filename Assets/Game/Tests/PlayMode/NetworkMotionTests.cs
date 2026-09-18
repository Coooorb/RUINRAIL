using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// TASK 094: host simulates remote owners from intents through the unchanged components, validates dashes once,
    /// replicas interpolate, owners reconcile, aim is a world direction — and solo behaviour is untouched.
    /// </summary>
    public class NetworkMotionTests
    {
        private readonly List<Object> _created = new();
        private PlayerBalanceConfig _balance;

        [SetUp]
        public void SetUp()
        {
            _balance = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            Assert.IsNotNull(_balance);
            Assert.AreEqual(0.18f, _balance.DashDuration, 0.0001f);
            Assert.AreEqual(1.4706f, _balance.DashCooldown, 0.0001f);
            Assert.AreEqual(0.10f, _balance.DashIFrameDuration, 0.0001f);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private (GameObject go, RemoteIntentInputReader reader) HostEntityForRemoteOwner(Vector2 position)
        {
            var reader = new RemoteIntentInputReader();
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "HostSim", IsLocal = true, InputReader = reader, BalanceConfig = _balance, Position = position });
            _created.Add(go);
            return (go, reader);
        }

        private (GameObject go, FakePlayerInputReader reader) OwnerEntity(Vector2 position)
        {
            var reader = new FakePlayerInputReader();
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "OwnerPrediction", IsLocal = true, InputReader = reader, BalanceConfig = _balance, Position = position });
            _created.Add(go);
            return (go, reader);
        }

        // ---- Acceptance 1 + 4: host simulation from intents converges with the owner's prediction ----

        [UnityTest]
        public IEnumerator HostSimulatesRemoteOwnerFromIntents_AndConvergesWithOwnerPrediction()
        {
            var (host, remote) = HostEntityForRemoteOwner(new Vector2(0f, 0f));
            var (owner, ownerInput) = OwnerEntity(new Vector2(0f, 20f));
            uint sequence = 0;
            for (var step = 0; step < 30; step++)
            {
                var move = new Vector2(1f, 0.5f);
                ownerInput.Move = move;
                Assert.IsTrue(remote.Apply(MovementIntent.Create(++sequence, move, Vector2.up)));
                yield return new WaitForFixedUpdate();
            }

            var hostTravel = (Vector2)host.transform.position - Vector2.zero;
            var ownerTravel = (Vector2)owner.transform.position - new Vector2(0f, 20f);
            Assert.Greater(hostTravel.magnitude, 1f, "The host moved the remote owner's entity from intents alone.");
            Assert.AreEqual(ownerTravel.x, hostTravel.x, 0.05f, "Same components + same intents = same result (deterministic prediction).");
            Assert.AreEqual(ownerTravel.y, hostTravel.y, 0.05f);
            Assert.IsFalse(OwnerReconciliation.NeedsCorrection(owner.transform.position - new Vector3(0f, 20f, 0f), host.transform.position), "No correction needed when in sync.");
            Assert.AreEqual(30, remote.AppliedIntents);

            // Out-of-order / duplicate intents are ignored; a malformed one too.
            Assert.IsFalse(remote.Apply(MovementIntent.Create(3, Vector2.left, Vector2.zero)));
            Assert.IsFalse(remote.Apply(new MovementIntent { Sequence = 99, Move = new Vector2(5f, 5f), Aim = Vector2.up }), "Unclamped move is invalid.");
            Assert.AreEqual(new Vector2(1f, 0.5f).normalized * Vector2.ClampMagnitude(new Vector2(1f, 0.5f), 1f).magnitude, remote.Move);
        }

        // ---- Acceptance 2: dash validation ----

        [UnityTest]
        public IEnumerator DashRequests_AreValidatedOnce_WithAuthoritativeTiming_AndSpamIsRejected()
        {
            var (host, remote) = HostEntityForRemoteOwner(Vector2.zero);
            var dash = host.GetComponent<PlayerDash>();
            var validator = new HostDashValidator(dash);
            remote.Apply(MovementIntent.Create(1, Vector2.right, Vector2.right));
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(DashVerdict.Accepted, validator.Validate(new DashRequest { Sequence = 1, Direction = Vector2.right }));
            Assert.IsTrue(dash.IsDashing);
            Assert.IsTrue(dash.IsInvulnerable);
            Assert.AreEqual(1, dash.DashesStarted);
            Assert.AreEqual(DashVerdict.RejectedDuplicate, validator.Validate(new DashRequest { Sequence = 1, Direction = Vector2.right }), "Re-sent request.");
            Assert.AreEqual(DashVerdict.RejectedAlreadyDashing, validator.Validate(new DashRequest { Sequence = 2, Direction = Vector2.up }), "Spam during the dash.");
            Assert.AreEqual(DashVerdict.RejectedStale, validator.Validate(new DashRequest { Sequence = 1, Direction = Vector2.up }));
            Assert.AreEqual(1, dash.DashesStarted, "Exactly one authoritative dash.");

            // 0.10 s iFrames end before the 0.18 s movement.
            var fixedStep = Time.fixedDeltaTime;
            var iFrameSteps = 0;
            var dashSteps = 0;
            for (var i = 0; i < 40 && dash.IsDashing; i++)
            {
                if (dash.IsInvulnerable) iFrameSteps++;
                dashSteps++;
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(Mathf.CeilToInt(0.10f / fixedStep), iFrameSteps, 1, "iFrames ~0.10 s (fixed steps).");
            Assert.AreEqual(Mathf.CeilToInt(0.18f / fixedStep), dashSteps, 1, "Dash movement ~0.18 s (fixed steps).");
            Assert.IsFalse(dash.IsDashing);
            Assert.IsFalse(dash.IsInvulnerable);

            Assert.AreEqual(DashVerdict.RejectedCooldown, validator.Validate(new DashRequest { Sequence = 3, Direction = Vector2.right }), "1.4706 s cooldown enforced by the host.");
            Assert.AreEqual(DashVerdict.RejectedDirection, validator.Validate(new DashRequest { Sequence = 4, Direction = Vector2.zero }));
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(DashVerdict.Accepted, validator.Validate(new DashRequest { Sequence = 5, Direction = Vector2.up }));
            Assert.AreEqual(2, dash.DashesStarted);
            Assert.AreEqual(2, validator.Accepted);
            Assert.AreEqual(5, validator.Rejected, "duplicate, already dashing, stale, cooldown, zero direction");
        }

        // ---- Acceptance 3: aim is a world direction, facing derived ----

        [UnityTest]
        public IEnumerator RemoteAim_IsAWorldDirection_NeverPointerCoordinates()
        {
            var (host, remote) = HostEntityForRemoteOwner(Vector2.zero);
            var aiming = host.GetComponent<PlayerAiming>();
            remote.Apply(MovementIntent.Create(1, Vector2.zero, new Vector2(-3f, 3f)));
            yield return null;
            yield return null;
            Assert.AreEqual(new Vector2(-1f, 1f).normalized.x, aiming.AimDirection.x, 0.001f);
            Assert.AreEqual(new Vector2(-1f, 1f).normalized.y, aiming.AimDirection.y, 0.001f);
            Assert.AreEqual(BodyFacing8.NW, aiming.BodyFacing);
            Assert.IsFalse(remote.IsAimFromPointer);

            var fields = typeof(MovementIntent).GetFields().Select(f => f.Name.ToLowerInvariant()).ToList();
            Assert.IsFalse(fields.Any(f => f.Contains("pointer") || f.Contains("screen") || f.Contains("mouse") || f.Contains("position")), "Intent carries no pointer/screen/position data.");
            Assert.AreEqual(1f, MovementIntent.Create(2, Vector2.zero, new Vector2(400f, 300f)).Aim.magnitude, 0.001f, "Aim is normalized (full precision direction, not quantized).");
        }

        // ---- Replica interpolation and owner reconciliation ----

        [Test]
        public void ReplicaInterpolator_RendersBehindTheNewestSample_AndSnapsOnLargeJumps()
        {
            var interpolator = new ReplicaInterpolator(0.1);
            interpolator.Push(new PlayerNetState { Position = new Vector2(0f, 0f), Time = 0.0 });
            interpolator.Push(new PlayerNetState { Position = new Vector2(1f, 0f), Time = 0.1 });
            interpolator.Push(new PlayerNetState { Position = new Vector2(2f, 0f), Time = 0.2, IsDashing = true });
            interpolator.Push(new PlayerNetState { Position = new Vector2(1f, 0f), Time = 0.05 });
            Assert.AreEqual(3, interpolator.BufferedSamples, "Out-of-order samples are dropped.");

            var mid = interpolator.Sample(0.25);
            Assert.AreEqual(1.5f, mid.Position.x, 0.001f, "Render time 0.15 lies halfway between the 0.1 and 0.2 samples.");
            Assert.IsTrue(mid.IsDashing, "Flags come from the newer sample.");
            Assert.AreEqual(0f, interpolator.Sample(0.0).Position.x, 0.001f, "Before the first sample: the first sample.");
            Assert.AreEqual(2f, interpolator.Sample(9.0).Position.x, 0.001f, "Past the buffer: the latest sample.");

            interpolator.Push(new PlayerNetState { Position = new Vector2(20f, 0f), Time = 0.3 });
            Assert.AreEqual(20f, interpolator.Sample(0.35).Position.x, 0.001f, "A jump above the snap distance is not smoothed.");
        }

        [Test]
        public void OwnerReconciliation_IgnoresSmallDrift_AndSnapsBeyondTolerance()
        {
            Assert.AreEqual(new Vector2(0.3f, 0f), OwnerReconciliation.Reconcile(new Vector2(0.3f, 0f), Vector2.zero));
            Assert.AreEqual(Vector2.zero, OwnerReconciliation.Reconcile(new Vector2(2f, 0f), Vector2.zero));
            Assert.IsTrue(OwnerReconciliation.NeedsCorrection(new Vector2(0f, 0.8f), Vector2.zero));
            Assert.IsFalse(OwnerReconciliation.NeedsCorrection(new Vector2(0f, 0.7f), Vector2.zero));
        }

        // ---- Acceptance 4: solo unchanged ----

        [UnityTest]
        public IEnumerator SoloDash_FromLocalInput_StillFollowsTheApprovedTimings()
        {
            var (solo, input) = OwnerEntity(Vector2.zero);
            var dash = solo.GetComponent<PlayerDash>();
            input.Move = Vector2.right;
            yield return new WaitForFixedUpdate();
            input.RaiseDash();
            Assert.IsTrue(dash.IsDashing);
            Assert.AreEqual(1, dash.DashesStarted);
            input.RaiseDash();
            Assert.AreEqual(1, dash.DashesStarted, "A second press during the dash is ignored, exactly as before.");
            yield return new WaitForSeconds(0.25f);
            Assert.IsFalse(dash.IsDashing);
            Assert.Greater(solo.transform.position.x, 1f, "The dash moved the solo player.");
            Assert.AreEqual(1.4706f, dash.CurrentDashCooldown, 0.001f);
        }
    }
}
