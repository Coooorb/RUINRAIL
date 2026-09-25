---
name: built-player-smoke-seed-flaky-on-mac
description: "The built-player smoke was clock-seeded and passed ~2-3 in 10; since the final release audit (2026-09-25) the official gate is scripts/run-release-smoke.sh with pinned seeds and it is deterministic (40/40) — never go back to \"rerun until green\""
metadata:
  node_type: memory
  type: project
  originSessionId: 3c983042-2863-4932-8556-406d3ad0e678
  modified: 2026-09-24T22:54:53.319Z
---

**History.** With no `-seed` the smoke's run seed comes from the clock, so every run built a different dungeon and
the seed-dependent checks were dice rolls: 3/10 and 3/16 clean passes (2026-09-23/24), 2/10 again in the final audit's
"before" loop, failing in five different stages (direct-hit dummy, knife dummy, knockback target, containment overshoot
0.065–0.307, "a non-combat room exists").

**Now (2026-09-25):** the release gate is `./scripts/run-release-smoke.sh <out> 10` — independent scenarios, each on a
fixed seed with a fresh save dir, no retries: `fresh` (seed 11), `returning` (relaunch on fresh run i's save), `death`
(seed 11), `cache` (seed 31), plus `longrun` on request. It passed 10/10 per scenario. Root causes fixed in the harness:
seed 31's Weapon Cache ordered-stage coupling (the non-combat stage now asserts the spent cache's used state), the
stat-consumer knockback target (was "first `FindObjectsByType` result", could be the frozen dummy), the Field Knife dummy
(the aim EASES toward the pointer after the camera, so place/hold the dummy on the aim through the wind-up — the knife
arc is ±40°), the direct-hit lane (hold the pack, enemy-free lane), and "a non-combat room exists" (dungeon/55 ranges
start at 0 — only rooms present must be driven). Clock seeds went 2/10 → 6/10; the residue is layout-dependent. Co-op equivalent:
`./scripts/run-coop-release-smoke.sh <out> 2 5 46` / `3 2 46`.

**Never widen tolerances to get a green run**, and a new failure on a pinned seed is a real signal — reproduce it with
`scripts/run-smoke-loop.sh <out> 10 <seed|clock>` before touching anything.

**How to apply:** fresh `-savedir` per run (a reused one carries Skill Points forward and fails progression at once);
absolute `-logFile`; the result JSON lands in the save dir. The fresh smoke performs the real Shelter onboarding (name
"Smoke Runner") and ends by saving screen-shake 0.5 — the returning scenario checks both, so don't remove them. Don't
read deferred stats (e.g. `StatId.WeaponSwitchSpeed`) in App code: StatConsumerIntegrity's source scan counts any
reader and fails. See [[combat-aim-proof-flaky-on-mac]], [[release-smoke-and-dead-seams-2026-09-25]].
