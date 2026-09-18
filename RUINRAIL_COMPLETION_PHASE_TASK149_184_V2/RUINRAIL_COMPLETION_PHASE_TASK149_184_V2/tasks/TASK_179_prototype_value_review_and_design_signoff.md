# TASK 179 — PROTOTYPE Value Review and Design Sign-off

> **Status:** Approved RUINRAIL completion/release task.
> **Execution mode:** Supervised/review-gated. This task requires design-owner decisions.
> **Game/code language:** English.

## PREREQUISITES

- TASK 178 PASS.
- Final art/audio integrated so feel/readability-dependent values can be judged in context.

## GOAL

Resolve every remaining `PROTOTYPE` marker through explicit evidence-based KEEP, TUNE or SPEC-SOURCE decisions. The purpose is not to change all values; it is to stop shipping undocumented placeholders.

## REQUIREMENTS

1. Re-scan source/assets/docs for `PROTOTYPE`; reconcile against TASK-148 baseline 35 and list every current marker with file/member/value/system.
2. Cover at minimum the categories recorded in `production/135_TASK148_GAP_ANALYSIS_ADDENDUM.md`: camera, feedback/recoil, strike/get-up, heat, ambience, network interpolation/snap, pickup, Downed/revive, depth scaling, coins, dungeon events, merchant/trader weights, stagger, grenade, tutorial, lighting, font metrics, music crossfade and any newly discovered marker.
3. For each marker choose exactly one disposition:
   - `KEEP_FINAL`: current value intentionally becomes final V1 value.
   - `TUNE_FINAL`: change value based on measured/playtest evidence.
   - `SPEC_SOURCE`: an existing approved GDD/spec already defines the value; align to it.
   - `BLOCKED_REVIEW`: cannot be decided yet; task cannot PASS.
4. Human owner must approve all feel/aesthetic/design-dependent dispositions. Claude cannot self-approve them merely because tests pass.
5. When a value changes, log before/after, rationale, source/evidence and affected tests.
6. Remove/replace the `PROTOTYPE` marker only after disposition is approved; retain meaningful tunable comments/config metadata.
7. No new mechanics/content.

## ACCEPTANCE CRITERIA

1. Every current PROTOTYPE marker has an explicit approved disposition.
2. Zero unresolved `PROTOTYPE` markers remain in release-owned runtime/config code unless an explicit non-release tooling exception is documented.
3. Any changed values have evidence and regression coverage.
4. Full suites pass.

## REVIEW GATE

MANDATORY DESIGN GATE: stop as `WAITING_HUMAN_REVIEW` if any disposition requires owner judgment. Resume only after explicit approval.
