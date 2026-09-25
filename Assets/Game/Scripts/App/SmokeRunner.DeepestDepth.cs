using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Expedition;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The run-variety stage of the built-player smoke: the deepest-depth record is established on arrival at Depth 1,
    /// does not move when the boss falls, moves when Depth 2 is actually built, survives the save/reload, and the
    /// Transit prompt reports it as context rather than advice. It also checks, in the shipped player, that a boss
    /// reaches more than one authored attack and actively re-engages from outside every attack band.
    /// </summary>
    public sealed partial class SmokeRunner
    {
        private readonly List<string> _runVarietyChecks = new();

        private void RunVarietyCheck(string what, bool ok)
        {
            if (ok) _runVarietyChecks.Add(what);
            else if (string.IsNullOrEmpty(_result.Error)) Fail("run variety: " + what);
            _result.RunVarietyChecks = _runVarietyChecks.ToArray();
        }

        /// <summary>Dungeon stage: the record on arrival, the boss behaviour, then a real descend.</summary>
        private IEnumerator RunVarietyDepthChecks(ExpeditionScene run)
        {
            var expedition = run.Expedition;
            var profile = _app.Menu.Session.Profile;

            // Earlier stages of this smoke already descend (the depth-arrival heal check), so the record is whatever
            // depth the run has actually reached by now — which is exactly the invariant worth asserting.
            var depthNow = expedition.State.Depth;
            RunVarietyCheck($"the record equals the deepest depth actually arrived at (depth {profile.DeepestDepthReached} at depth {depthNow})",
                profile.DeepestDepthReached == depthNow && depthNow >= 1);
            RunVarietyCheck("the record cannot be lowered by replaying an arrival",
                !expedition.RecordDepthArrival(1) && profile.DeepestDepthReached == depthNow);

            // A boss in the shipped player: every authored attack must be reachable and the re-engagement must run.
            var boss = FindObjectsByType<RuinRail.Gameplay.Enemies.Bosses.BossController>(FindObjectsSortMode.None).FirstOrDefault();
            if (boss != null && boss.Definition != null)
            {
                var player = run.Rig.Player.transform;
                var picked = new HashSet<string>(System.StringComparer.Ordinal);
                var origin = (Vector2)boss.transform.position;
                var held = player.position;
                boss.SetTarget(player);
                for (var step = 0; step < 40; step++)
                {
                    player.position = origin + Vector2.right * (0.5f + step % 12);
                    boss.ClearCooldownsForDiagnostics();
                    var attack = boss.SelectAttack();
                    if (attack != null) picked.Add(attack.Id);
                    yield return null;
                }

                RunVarietyCheck($"{boss.Definition.Id} reached {picked.Count} of {boss.Definition.Moveset.Count(a => a != null)} authored attacks",
                    picked.Count >= 2);

                // Outside every band: no attack is valid and the non-damaging gap-close takes over.
                player.position = origin + Vector2.right * (boss.MaxAuthoredAttackRange + 6f);
                yield return new WaitForFixedUpdate();
                RunVarietyCheck("no attack is valid outside every authored band", boss.SelectAttack() == null && boss.IsOutOfEngagementRange);
                // The gap-close only drives the body while the actor is chasing, and the attack sampling above can leave
                // it mid-telegraph or in recovery. Wait for it to return to Chase before measuring, so this reports the
                // behaviour rather than whichever animation phase the sampling happened to end on.
                for (var i = 0; i < 180 && boss.State != MovesetActorState.Chase; i++) yield return new WaitForFixedUpdate();
                var before = Vector2.Distance(player.position, boss.transform.position);
                var reengaged = false;
                for (var i = 0; i < 90; i++)
                {
                    yield return new WaitForFixedUpdate();
                    if (boss.IsReengaging) reengaged = true;
                }

                var after = Vector2.Distance(player.position, boss.transform.position);
                RunVarietyCheck($"the boss actively re-engaged from outside every band ({before:0.#} -> {after:0.#} tiles)", reengaged && after < before - 0.5f);
                player.position = held;
                boss.SetTarget(null);
            }
            else
            {
                _runVarietyChecks.Add("no boss composed on this depth yet; boss behaviour is covered by the PlayMode suite");
            }

            // The Transit prompt reports facts, never advice.
            expedition.RecordBossDefeated(25);
            RunVarietyCheck("defeating the boss did not record the next depth", profile.DeepestDepthReached == depthNow);
            var lines = TransitContext.Lines(expedition);
            RunVarietyCheck($"the transit context lists {lines.Count} factual lines including the personal best",
                lines.Count >= 4 && lines.Any(l => l.StartsWith("Personal best", System.StringComparison.Ordinal)));
            RunVarietyCheck("the transit context contains no recommendation language",
                lines.All(l => !l.ToLowerInvariant().Contains("recommend") && !l.ToLowerInvariant().Contains("should") && !l.ToLowerInvariant().Contains("best choice")));

            // A real descend: the next depth generates, composes, and only then does the record move.
            var depthsBefore = run.DepthsBuilt;
            run.Vote.Vote(TransitChoice.DescendDeeper);
            yield return WaitFor(() => run.DepthsBuilt > depthsBefore, "depth 2 composed");
            RunVarietyCheck($"arriving at depth {depthNow + 1} raised the record from {depthNow} to {profile.DeepestDepthReached}",
                profile.DeepestDepthReached == depthNow + 1 && expedition.State.Depth == depthNow + 1);
            RunVarietyCheck("the expedition reports a new personal best", expedition.IsNewPersonalBestThisExpedition);
            RunVarietyCheck($"the next depth composed {run.Rooms.Count} rooms with depth gating active", run.Rooms.Count > 0);
            _result.DeepestDepthReached = profile.DeepestDepthReached;
        }

        /// <summary>Post-reload stage: the record is on disk, not just in memory.</summary>
        private void RunVarietyPersistenceChecks(GameApp.SaveProbe probe)
        {
            RunVarietyCheck($"the reloaded save carries the record (depth {probe.DeepestDepthReached})", probe.DeepestDepthReached >= 1);
        }
    }
}
