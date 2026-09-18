# RUINRAIL — FINAL PLAYABILITY / UX REGRESSION PASS
## Execute from the CURRENT repository state after POST_POLISH_REGRESSION_FIX_REPORT

This is a focused final playability pass.

Do NOT ask for intermediate approval.
Do NOT create a new task series.
Do NOT add unrelated gameplay features.

Fix every issue below, add regression coverage, run the full relevant test/build gates, and return one consolidated report only at the end.

---

# 0. READ FIRST

Read the latest/current versions of:

- CLAUDE.md
- CLAUDE_START_HERE.md
- technical/117_CODING_RULES_FOR_CLAUDE.md
- technical/118_TESTING_STRATEGY.md
- FINAL_ART_PRODUCTION_SPEC.md
- production/FINAL_SHIPPABLE_V1_REPORT.md
- production/UI_AND_BIOME_POLISH_REPORT.md
- production/POST_POLISH_REGRESSION_FIX_REPORT.md
- production/FINAL_AUTONOMOUS_COMPLETION_LOG.md
- current UI, player visual, weapon visual, NGO presence, room runtime, door lock, HUD, inventory and pause-menu code

Repository truth wins over older documents.

Create/update:

`production/FINAL_PLAYABILITY_REGRESSION_REPORT.md`

---

# 1. HARD SCOPE

Primary issues to fix:

1. Held weapon sprites are not bound/rendered in live gameplay.
2. Remote co-op player replicas need the same final character visual composition as the local player.
3. InventoryView still uses LegacyRuntime.ttf.
4. Enemies need readable health bars.
5. Pause menu needs actual usable buttons, including Return to Main Menu.
6. Room entry/combat locking currently triggers too early and blocks the player before they have entered the new room.
7. Custom pixel-art mouse cursors are missing.

Do NOT redesign:
- player character art
- enemy art
- weapon art
- current VFX
- biome art
- audio

Do NOT change:
- approved weapon/enemy stats
- save semantics
- networking authority rules
- stable IDs

Do NOT create new gameplay features.

---

# 2. HELD WEAPON RUNTIME VISUALS

Known current limitation:
Character bodies now render, but the held weapon sprite is not bound at runtime.

Fix the real runtime path.

Requirements:

- local player displays the currently equipped active weapon;
- weapon is attached to the existing WeaponPivot / 360-degree aim system;
- sprite orientation uses the authored +X convention;
- correct weapon sprite changes immediately on:
  - Weapon1
  - Weapon2
  - WeaponSwap
  - loadout changes when applicable;
- hidden/inactive weapon must not remain duplicated on the body;
- melee, bow, blaster, firearm and launcher families all use their current visual contracts;
- current bow/blaster/reload visual states remain compatible;
- muzzle position remains aligned with projectile origin;
- weapon sprite rotates correctly across full 360° aim;
- no visual changes to weapon gameplay stats or hitboxes.

Sorting:
- held weapon must appear in the correct relationship to the body according to aim direction;
- it must not disappear behind Floor/Wall tilemaps;
- it must not render permanently above AbovePlayer layers;
- preserve current sorting convention.

Add regression tests proving:
- every 33 weapon definition has a runtime visual binding;
- local player active slot changes visual;
- inactive slot is not simultaneously rendered;
- 360° orientation remains valid.

Capture proof screenshot(s) in:
`TestResults/FinalPlayabilityProof/`

---

# 3. REMOTE CO-OP CHARACTER + WEAPON VISUALS

Known limitation:
The local ExpeditionScene path now uses CharacterVisual.Attach, but remote NgoPlayerPresence replicas may not.

Fix the remote replica composition.

Requirements:

- every spawned network player replica receives:
  - CharacterVisual
  - SpriteRenderer / animation path
  - correct CharacterAnimationSet
  - correct sorting/y-sort behavior
  - active held weapon visual
- local and remote players must not instantiate duplicate visual stacks;
- owner/non-owner logic must remain correct;
- remote animation state follows synchronized gameplay state as supported by the current architecture;
- remote facing/body direction and weapon aim are visually updated;
- remote players remain visible after reconnect/re-spawn paths;
- host-authority model must remain unchanged.

Do not create a second independent character-visual implementation.
Reuse the same composition seam where possible.

Add tests for:
- host sees client body;
- client sees host body;
- remote active weapon visible;
- no duplicate SpriteRenderer stack;
- reconnect rebinds visuals;
- despawn cleans visuals.

If live UGS is unavailable, use the existing network harness/fake transport for deterministic regression coverage, but do NOT claim live Relay verification.

---

# 4. INVENTORY FINAL FONT CLEANUP

Known limitation:
InventoryView.cs still loads LegacyRuntime.ttf.

Remove this remaining release dependency.

Requirements:

- InventoryView uses the same final RUINRAIL pixel font source as the rest of the UI;
- do not duplicate font-loading logic;
- use the existing UiFont / UiKit seam;
- preserve readable inventory metrics;
- fix any resulting clipping/spacing instead of reverting font;
- all inventory text must fit at 640×360;
- no direct LegacyRuntime.ttf load remains in release UI paths.

Search the entire release UI codebase for remaining direct LegacyRuntime.ttf references.
If any additional accidental release references exist, replace them through the same shared font seam unless they are editor/test-only.

Add regression coverage:
- InventoryView resolves final pixel font;
- no release UI direct LegacyRuntime.ttf dependency;
- representative long item names/tooltips do not overlap critical controls.

---

# 5. ENEMY HEALTH BARS

Add readable health bars for enemies.

Use the existing UI/presentation architecture rather than embedding gameplay logic into UI.

## Normal enemies

World-space or screen-projected bar above the enemy.

Target presentation:
- compact pixel bar;
- approximately 20–28 px visual width at reference resolution;
- 3–4 px total height including border;
- dark background;
- high-contrast health fill;
- 1 px pixel border;
- follows enemy position;
- does not rotate with enemy;
- remains readable against all three biomes.

Visibility:
- hidden while enemy is at full HP;
- becomes visible immediately after first damage;
- remains visible while damaged;
- disappears when the enemy dies/despawns.

Do not add an arbitrary timer unless the current UI architecture already defines one.

## Elites

Use a slightly stronger world health bar:
- approximately 28–36 px width;
- visually distinguish Elite status using the existing Elite visual accent;
- still unobtrusive.

## Bosses

Inspect the current game first.

If a dedicated boss-health UI already exists:
- preserve it;
- do not add a redundant small world bar.

If bosses currently have no health UI:
- add a dedicated boss bar at a stable screen location;
- it must show boss display name + HP fraction/bar;
- it must not overlap depth/objective HUD;
- boss bar appears only during active boss encounter.

Networking:
- bars reflect authoritative synchronized health;
- remote clients see correct health state.

Pooling/performance:
- do not instantiate/destroy UI objects every damage tick;
- use existing pooling/presentation patterns where appropriate.

Add tests:
- full-health normal enemy -> bar hidden;
- damaged enemy -> visible;
- HP fraction maps correctly;
- death/despawn -> hidden/removed;
- Elite style distinct;
- Boss behavior matches chosen path;
- client receives synchronized health display.

---

# 6. PAUSE MENU — REAL BUTTONS / NAVIGATION

Known issue:
Pause menu currently lacks practical button controls.

Implement a real pause screen using existing RUINRAIL UI style.

Required buttons:

1. RESUME
2. SETTINGS
3. RETURN TO MAIN MENU
4. QUIT GAME

Do NOT add Restart Run.

## RESUME
- closes pause menu;
- returns control to gameplay;
- restores correct input map/state;
- no duplicate pause layers.

## SETTINGS
- opens existing settings/rebinding screen;
- Back returns to Pause, not directly to gameplay unless the existing navigation architecture requires otherwise.

## RETURN TO MAIN MENU
This is destructive during an active expedition and must follow existing expedition-loss semantics.

Requirements:
- show a confirmation dialog;
- clearly state that leaving an active expedition counts according to the existing quit/fail/abandoned-expedition rules and at-risk run state is not secured;
- reuse existing expedition failure/abandon transaction path;
- do NOT invent a second loss implementation;
- must not duplicate/lose persistent safe data;
- after confirmation:
  - resolve expedition correctly;
  - flush required save transaction;
  - leave network/session cleanly where applicable;
  - load Main Menu.

If in Shelter/non-expedition state:
- returning to Main Menu should not fabricate an expedition failure.

## QUIT GAME
- show confirmation where appropriate;
- preserve existing quit semantics;
- active expedition uses approved no-mid-run-resume / fail-or-abandon semantics;
- flush safe persistent state correctly.

## Input
All pause buttons support:
- mouse hover/click;
- keyboard;
- controller;
- visible focus;
- active pressed state.

Escape/Menu behavior:
- pressing Pause while paused should Resume unless a nested confirmation/settings panel owns Back;
- no stacked duplicate pause screens.

Multiplayer:
- pausing must respect current multiplayer design;
- do not globally freeze authoritative simulation on host if current co-op design does not allow that;
- the menu may pause local input/UI without violating network simulation semantics;
- Return to Main Menu must cleanly disconnect the local user/session according to existing V1 host/client rules.

Add tests for:
- Resume;
- Settings navigation;
- Return to Main Menu from Shelter;
- Return to Main Menu from active solo expedition;
- host/client leave behavior using current network harness;
- save/transaction semantics;
- no item duplication;
- Quit path;
- mouse + controller navigation.

---

# 7. ROOM ENTRY / COMBAT LOCK TIMING BUG

Known observed bug:
When attempting to enter a new combat room, the player is blocked before actually entering it. The encounter therefore effectively happens from the previous room/corridor instead of inside the target room.

This is wrong.

Fix the root cause in room activation / entry trigger / door-lock timing.

## Core rule

A combat room must not lock the entry before the entering player has crossed into the actual interior of that room.

The encounter should take place inside the room being entered.

## Required spatial semantics

The following ordering must hold:

1. doorway/open connection is traversable;
2. player crosses through doorway;
3. player reaches the destination room's interior activation region;
4. room encounter activates;
5. door/entry lock engages behind the player according to existing room rules;
6. enemies engage/spawn/activate inside the destination room.

The door collider must NOT block the player at or before the activation threshold in a way that leaves them in the previous room.

## Do not solve by:
- shrinking player collider arbitrarily;
- moving the whole room;
- delaying enemy AI with an unrelated timer;
- disabling collision globally;
- allowing combat to leak backwards into previous rooms as normal behavior.

## Trigger geometry

Inspect:
- RoomRuntime trigger volumes
- DoorSocket geometry
- RoomDoorLock colliders
- PlayerRoomEventsRelay
- DungeonRoomRuntimeComposer
- RoomContentBinding / category composer
- instantiated room transform offsets

Fix the actual world-space boundary relationship.

Use explicit interior margins rather than a trigger sitting directly on the socket seam.

The activation volume must remain within valid room bounds and must not overlap the previous room.

## Connected-room / exit behavior

Preserve the newly fixed rule:
- every open exit reciprocally connects to another room;
- unused sockets remain sealed;
- no void openings.

Do not regress RoomExitSealer / DungeonExitValidator.

## Co-op behavior

Do not strand living teammates outside a locked combat room.

First inspect the existing approved party room-entry semantics.

Preserve that design.

At minimum:
- a room must never lock such that one living player is physically trapped on the wrong side solely because the trigger fired before they could cross a valid doorway;
- remote player movement/door state must be synchronized;
- encounter activates exactly once.

If current architecture already has a party-entry policy, fix trigger placement/timing to satisfy it rather than inventing a new policy.

## Tests

Add tests covering:
- player can fully cross doorway before lock collision becomes blocking;
- activation occurs inside destination room;
- fight/enemies belong to destination room;
- previous room remains behind the closed entry;
- room activates exactly once;
- sealed unused socket remains sealed;
- reciprocal exits still validate;
- multiple room sizes/orientations;
- N/E/S/W sockets;
- representative seeds across all 3 biomes;
- co-op harness does not strand a living teammate due to premature lock.

Capture proof screenshot showing:
- player visibly inside combat room;
- entry door locked behind them;
- enemies/encounter inside that room.

---

# 8. CUSTOM PIXEL MOUSE CURSORS

Add original RUINRAIL custom mouse cursors.

Follow current pixel-art style.

Required cursor states:

## UI Default
- compact pixel pointer;
- high contrast;
- readable on dark and light UI.

## UI Hover / Select
- visibly distinct hover/select state;
- may use amber/terminal accent.

## Gameplay Aim
- pixel crosshair / reticle;
- centered hotspot;
- readable over all 3 biomes;
- does not obscure small targets;
- no operating-system arrow visible over gameplay while aim cursor is active.

## Optional Interact state
Only add if the current interaction architecture already exposes a reliable interactable-hover signal.

Technical rules:
- nearest/point-filter source;
- correct hotspot;
- cursor scale appropriate for desktop;
- no anti-aliased fringe;
- hide/restore OS cursor correctly;
- menus use pointer;
- gameplay uses aim cursor;
- pause/menu overlays switch back to UI cursor;
- alt-tab/focus regain restores the correct state;
- gamepad-only interaction should not require mouse movement.

Add tests where feasible for state switching and cursor-mode ownership.

---

# 9. FINAL UX / VISUAL CHECK

Capture at minimum:

1. local player in dungeon with visible held P9 Ranger;
2. a second/remote network player through the network harness with body + held weapon;
3. normal damaged enemy with health bar;
4. Elite with health bar;
5. Boss UI during boss encounter;
6. pause menu main screen;
7. pause -> Settings;
8. pause Return-to-Main confirmation;
9. combat-room entry with player fully inside and locked door behind;
10. custom UI cursor;
11. gameplay aim cursor;
12. Inventory using final pixel font.

Save under:

`TestResults/FinalPlayabilityProof/`

If automated screenshot capture cannot show a hardware cursor, document that limitation and validate the cursor texture/hotspot/state through tests plus a manual/windowed run. Do not fake cursor pixels into a screenshot.

---

# 10. FULL TEST / VALIDATION GATES

After the final source change:

Run strict EditMode:
- exit code 0;
- XML exists;
- total > 0;
- passed > 0;
- failed = 0.

Run strict PlayMode with same requirements.

Also run:
- ContentCountValidator
- FinalProductionValidator
- PresentationValidator
- relevant art/animation validators
- save/exploit hardening
- dungeon layout/exit validation
- network regression tests
- UI/navigation tests

Do not accept stale XML.

Be aware of the known local Smart App Control / Bee.TundraBackend behavior from the previous report.
Do not disable Windows Smart App Control automatically.
If the first Unity batch invocation fails only because of the documented environment issue and a clean second invocation works, document it accurately.

---

# 11. RELEASE BUILD + BUILT-PLAYER SMOKE

Produce a clean Windows x64 NON-DEVELOPMENT build.

Run built-player smoke.

Require:
- player body visible;
- held weapon visible;
- enemy visuals visible;
- enemy health display works;
- no open void exit;
- room entry occurs before combat lock;
- HUD readable;
- Inventory font correct;
- Pause menu usable;
- Return-to-Main path works without save corruption;
- custom cursors load and switch correctly;
- no exceptions;
- no missing scripts/references.

Do not claim live co-op Relay success unless actually run over UGS.

---

# 12. TERMINAL REPORT

Write:

`production/FINAL_PLAYABILITY_REGRESSION_REPORT.md`

Include:
- root cause for each issue;
- files/systems changed;
- exact tests added;
- final test counts;
- build result;
- smoke result;
- proof screenshot paths;
- known remaining external blockers.

Allowed terminal status:

`FINAL_PLAYABILITY_PASS_COMPLETE`
only if all repository-local issues above are fixed and verified.

`FINAL_PLAYABILITY_PASS_INCOMPLETE`
if any of these repository-local issues remain.

A missing UGS account/project link is NOT a reason to leave these repository-local fixes incomplete. Finish them first and list UGS separately as an external follow-up.

Do not ask for approval during execution.

BEGIN NOW.
