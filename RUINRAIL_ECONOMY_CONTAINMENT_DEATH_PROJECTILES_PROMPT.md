# RUINRAIL — ECONOMY / ROOM CONTAINMENT / STARTER FALLBACK / DEATH SCREEN / BACKPACK REORDER / PROJECTILE VISUALS PASS
## Execute from the CURRENT repository state after ROOM_HUD_AUDIO_QOL_PASS

This is a focused gameplay/QoL pass based entirely on issues observed during real play.

Do NOT ask for intermediate approval.
Do NOT create a new task series.
Do NOT refactor unrelated systems.
Do NOT change unrelated weapon damage, enemy damage/HP, progression, save semantics, room topology, network authority, or inventory capacity.

Fix the actual runtime behavior, add regression coverage, run the required validation/build gates, capture built-player proof, and return one consolidated report only at the end.

Write the final report to:

`production/ECONOMY_CONTAINMENT_DEATH_PROJECTILES_QOL_REPORT.md`

# 0. READ FIRST

Read the latest/current versions of:

- CLAUDE.md
- CLAUDE_START_HERE.md
- technical/117_CODING_RULES_FOR_CLAUDE.md
- technical/118_TESTING_STRATEGY.md
- production/START_HP_DASH_AND_ICON_HUD_REPORT.md
- production/GRAPHICAL_INVENTORY_REWORK_REPORT.md
- production/COMBAT_AIM_COLLISION_AND_FINAL_VISUAL_FIX_REPORT.md
- production/LOOT_AMMO_AUDIO_RUNTIME_FIX_REPORT.md
- production/ROOM_HUD_AUDIO_QOL_REPORT.md if present
- current Merchant / economy / buy/sell pricing code
- current ammo item definitions and stack/value rules
- current room runtime / encounter ownership / door-lock / AI movement code
- current Boss movement/pathing code
- current starter loadout / loadout resolution / expedition-start code
- current death / downed / wipe / fail / return-to-shelter code
- current InventoryView / InventoryViewModel / ItemTransferService / backpack container code
- current projectile simulation / projectile pooling / bullet VFX / enemy projectile code
- current weapon definitions and all ranged weapon categories

Repository truth wins over older reports.

# 1. HARD SCOPE

Fix these exact issues:

1. Ammo can currently be sold to the Merchant for far too many coins.
2. Enemies — especially Bosses — can leave the room they belong to. They must remain contained in their encounter room.
3. If the player has no active/equipped loadout, automatically equip the existing free Starter Loadout before the run begins.
4. Add a proper Death / Run Failed screen.
5. Backpack items must be movable/reorderable between backpack slots using left-click select/move and mouse drag-and-drop.
6. Player and enemy bullets/projectiles must be visibly rendered in flight, with projectile visuals appropriate to the weapon/attack that fired them.

Do NOT expand scope into unrelated systems.

# 2. MERCHANT AMMO SELL PRICE — ECONOMY EXPLOIT FIX

Observed in real play:

Ammo can be sold to the Merchant for an absurdly high amount of coins.

Treat this as an economy correctness bug.

Trace:
1. ammo definition;
2. stack quantity;
3. per-unit value;
4. stack value;
5. merchant sell multiplier;
6. rounding;
7. buy vs sell direction;
8. UI price;
9. authoritative transaction result;
10. idempotency/retry behavior.

Explicitly investigate:
- stack price treated as per-round value;
- stack count multiplied twice;
- buy price reused as sell payout;
- rarity/value formulas applied to ammo;
- bundle-size mismatch;
- UI and transaction paying different amounts.

If no stronger authoritative ammo resale rule exists, use:

**Ammo sell payout = 15% of the equivalent current Merchant purchase value for the exact quantity sold, rounded down.**

Rules:
- no double stack multiplication;
- no rarity multiplier unless explicitly authoritative;
- payout may be 0 for tiny quantities;
- do not enforce minimum 1 coin if that enables micro-stack exploits;
- UI and actual payout must match exactly.

Tests:
- Light / Medium / Heavy / Shells;
- partial/full stacks;
- repeated sell request;
- cannot oversell;
- no overflow/negative values;
- buy -> immediate sell always loses meaningful value.

Document before/after examples.

# 3. ENEMIES MUST NOT LEAVE THEIR ENCOUNTER ROOM

Observed:
Enemies can leave the room they belong to, especially Bosses.

Core rule:
Once an enemy is assigned to an encounter room, its collider must remain within that room's legal encounter bounds until death/despawn/end-of-encounter or an explicitly authored in-room relocation.

Bosses must remain inside the Boss arena.

Investigate:
- room ownership;
- encounter membership;
- AI target pursuit;
- steering/pathfinding;
- open/closed doors;
- combat-lock timing;
- boss dash/charge/leap;
- knockback;
- direct transform movement;
- Rigidbody2D movement;
- nav cells;
- room bounds/door thresholds.

Preferred architecture:
- every encounter enemy has authoritative OwnerRoom/room-bound context;
- movement/path destinations constrained to valid room bounds;
- encounter exits are non-traversable for encounter AI;
- closed combat doors remain physical blockers;
- AI cannot leave just because a door briefly opens.

Do NOT use visible teleport-back as the primary solution.

Boss rules:
- never leave arena;
- charge/dash/leap endpoints constrained;
- knockback cannot eject boss;
- pursuit stops at legal edge;
- player outside arena does not cause boss to chase out.

Normal/Elite:
- cannot leak into previous/adjacent rooms;
- cannot cross doorway corners;
- can use full legal room interior.

Tests:
- melee pursuit at doorway;
- ranged enemy at doorway;
- Charger/dash;
- Elite;
- Boss pursuit;
- Boss charge/dash;
- knockback;
- N/E/S/W exits;
- all 3 biomes;
- open-door timing;
- network-authoritative position remains legal.

Invariant:
**An encounter enemy's collider may not cross the legal encounter-room boundary into a different room.**

# 4. AUTOMATIC FREE STARTER LOADOUT FALLBACK

If the player has NO active/equipped loadout when starting an expedition, automatically equip the existing free Starter Loadout.

Before expedition launch:
1. resolve selected/active loadout;
2. validate it;
3. if valid, preserve it;
4. if absent/completely empty/invalid because nothing is equipped, resolve and equip the existing authoritative free Starter Loadout.

Use the existing Starter Loadout service/data.
Do NOT hardcode a second starter pack.

Expected current contents must come from repo truth, likely including:
- P9 Ranger;
- Field Knife;
- Scrap Vest;
- Bandage;
- any other current authoritative starter items.

Requirements:
- no storage duplication;
- no repeated free sellable copies;
- starter restrictions preserved;
- intentional valid loadout never overwritten;
- idempotent;
- no repeated grant on scene load.

Small non-blocking `STARTER LOADOUT EQUIPPED` feedback is acceptable.

Tests:
- valid loadout preserved;
- no loadout -> starter;
- empty equipment -> starter;
- invalid missing refs -> safe starter;
- repeated preparation no duplicates;
- wipe/recovery correct.

# 5. DEATH / RUN FAILED SCREEN

Add a proper Death / Run Failed screen in established RUINRAIL style.

Use:
- charcoal/steel;
- restrained danger red;
- pixel font;
- clean hierarchy.

Trigger semantics:
- Solo: show only on conclusive run failure.
- Co-op: do NOT show final screen while merely downed/revivable.
- Preserve existing spectate/wait behavior if teammates are alive.
- Final Run Failed screen only on authoritative final failure/wipe.

Use existing `Fail()` / expedition failure transaction.
Do not create a second loss implementation.

At minimum show:
- `RUN LOST` or equivalent;
- Depth reached;
- Biome;
- rooms cleared if already tracked;
- enemies defeated if already tracked;
- Boss defeated yes/no if available;
- carried coins lost / run-risk result if available;
- XP/persistent progression result if available.

Do not invent stats that are not tracked.

Buttons:
1. `RETURN TO SHELTER`
2. `MAIN MENU`

Do NOT add Retry unless already safely supported.

Support mouse/keyboard/controller.
No gameplay input leakage.

Tests:
- solo death -> screen;
- downed/revivable -> no final screen;
- co-op wipe -> final screen;
- Fail() once;
- Return to Shelter;
- Main Menu;
- no duplicate screen;
- save/persistence semantics correct.

# 6. BACKPACK SLOT REORDER / MOVE FIX

Observed in real play:
The graphical Backpack exists, but items cannot reliably be moved from one Backpack slot to another.

Treat previous test claims as insufficient.

Audit:
- InventorySlotView pointer handlers;
- InventoryView selection;
- InventoryViewModel;
- ItemTransferService;
- same-container moves;
- slot indices;
- swap behavior;
- auto-compaction.

Explicitly verify whether same-container `backpack slot A -> backpack slot B` is rejected.

Required behavior:

Left click:
- click occupied A -> select/pick up;
- click empty B -> move to exact B;
- click occupied B -> swap A/B;
- clicking original source may cancel.

Drag/drop:
- A -> empty B moves;
- A -> occupied B swaps;
- invalid drop does nothing.

Exact slot order must remain visible.
Do NOT auto-compact/re-sort after manual reorder unless explicitly authoritative.

Preserve ordering after:
- closing/reopening Inventory;
- another item move;
- normal runtime refresh.

Keyboard/controller select->move uses same operation.

No duplication/deletion/stack corruption.

Tests:
- 0 -> empty 5;
- 5 -> occupied 2 swap;
- drag 0 -> 7;
- keyboard/controller move;
- close/reopen preserves order;
- no auto-compaction;
- stack merge/remainder correct;
- no duplicate/loss.

Built-player proof must show before/after positions.

# 7. VISIBLE PLAYER + ENEMY PROJECTILES

Observed:
Gameplay projectiles/shots are not visually readable in flight for player or enemies.

Add visible in-flight projectile presentation.

Do NOT change:
- damage;
- speed;
- collision;
- hit registration;
- aim assist;
- range;
- authority.

Visuals must follow the real authoritative projectile.

# 8. PROJECTILE VISUAL ARCHITECTURE

Use one reusable projectile-visual system integrated with the current projectile/pool architecture.

The visual must:
- spawn at actual projectile/muzzle position;
- move with real projectile;
- orient to velocity;
- despawn on hit/expiry;
- reset cleanly when pooled.

Do NOT spawn independent fake bullets that can diverge from physics.

# 9. PLAYER WEAPON PROJECTILE VISUALS

Do not use one identical sprite for all ranged weapons.

Create/bind a valid projectile visual profile for every ranged weapon, using family defaults and per-weapon overrides.

At minimum distinguish:

- Pistols: small conventional bullet/tracer.
- SMGs: small fast-looking tracer.
- Assault Rifles: medium rifle tracer.
- Battle Rifles: heavier/brighter rifle projectile.
- Shotguns: multiple readable small pellets.
- Snipers: thin high-contrast fast tracer.
- Bows: visible arrow/bolt aligned to travel.
- Blasters/energy weapons: distinct energy bolt.
- Rocket Launcher: visibly larger rocket + appropriate trail.
- Legendary/special weapons: weapon-specific identity where appropriate.

Melee needs no bullet visual.

Every ranged weapon definition must resolve a valid profile.

Significant/Legendary weapons should differ meaningfully where appropriate by:
- sprite;
- size;
- tracer length;
- accent;
- trail;
- animation.

Keep palette restrained and RUINRAIL-consistent.

# 10. ENEMY PROJECTILE VISUALS

Every enemy attack that uses a travelling projectile must show one.

Requirements:
- clearly hostile/readable;
- readable in all 3 biomes;
- fits attack archetype;
- not confused with loot/UI.

Examples:
- ballistic hostile round -> muted red/orange tracer;
- toxic/lab projectile -> restrained sickly green;
- industrial energy -> amber/red bolt;
- Boss projectile -> larger/clearer attack-specific profile.

Preserve established telegraph color language.

# 11. PROJECTILE READABILITY

At 640×360:
- bullets readable but small;
- no giant debug rectangles;
- crisp nearest-neighbor pixels;
- fast bullets may use short tracer/trail;
- avoid excessive bloom;
- do not obscure targets.

Use existing ArtGen/VFX pipeline and manifest/provenance rules.

If assets are missing:
- generate original RUINRAIL pixel projectile sprites through existing pipeline;
- bind through release catalog;
- no placeholder colored quads.

For fast bullets:
- short pooled tracer aligned with velocity.

For slower projectiles:
- sprite/animation may be sufficient.

Do not alter projectile speed for visibility.

# 12. PROJECTILE SORTING / COLLISION / PERFORMANCE

Visual projectile:
- above floor;
- readable near props;
- correct relation to AbovePlayer walls;
- matches real physics position;
- stops at wall/enemy hit;
- never continues visually beyond a registered hit.

Use pooling.
No per-shot unbounded allocation for automatic weapons/shotguns.
No stale trail/sprite state.

# 13. PROJECTILE TESTS

Add tests for:
- P9 Ranger;
- SMG;
- AR;
- battle rifle;
- shotgun;
- sniper;
- bow;
- blaster;
- rocket;
- representative Legendary;
- enemy ranged bullet;
- enemy energy attack;
- Boss projectile.

Verify:
- spawn position;
- follow;
- rotation;
- pooled reset;
- despawn on hit;
- wall impact;
- speed/damage/range unchanged;
- every ranged weapon resolves profile;
- every travelling enemy projectile resolves profile.

# 14. BUILT-PLAYER PROOF

Save under:

`TestResults/EconomyContainmentDeathProjectileProof/`

Capture at minimum:

1. corrected ammo sale payout;
2. exact coin change after sell;
3. normal enemy contained near room exit;
4. Boss contained at arena boundary;
5. no-loadout -> Starter Loadout applied;
6. Death / Run Lost screen;
7. Return to Shelter from death screen;
8. Backpack item before move;
9. same item moved to another slot by click;
10. occupied slots swapped;
11. drag/drop reorder;
12. P9 projectile visible;
13. SMG/AR projectile visible;
14. shotgun pellets visible;
15. sniper tracer visible;
16. bow/blaster/rocket representative projectile;
17. enemy projectile visible;
18. Boss projectile visible.

For very fast bullets, use controlled frame capture and position evidence.
Do not fake projectile graphics into screenshots.

# 15. FULL VALIDATION GATES

After the final source change run:

- strict EditMode;
- strict PlayMode;
- ContentCountValidator;
- FinalProductionValidator;
- PresentationValidator;
- AssetPipelineValidator;
- ReleasePathScan;
- ArtProductionContract;
- projectile/weapon tests;
- room/AI/collision tests;
- merchant/economy tests;
- inventory tests;
- save/exploit hardening;
- network authority tests.

Requirements:
- failed = 0;
- no stale XML;
- no missing scripts/references;
- no placeholder projectile art on release path.

Do not disable Smart App Control.

# 16. RELEASE BUILD + SMOKE

Produce a clean Windows x64 NON-DEVELOPMENT build.

Run headless + windowed smoke.

Require:
- ammo resale sane and non-exploitable;
- encounter enemies stay in their room;
- Boss stays in Boss arena;
- Starter Loadout auto-equips only if no valid active loadout exists;
- Death Screen works;
- Backpack slots manually reorder/swap;
- manual order preserved;
- player projectiles visible;
- enemy projectiles visible;
- projectile visuals fit weapon/attack families;
- visuals end on actual hits;
- no exceptions/missing refs.

# 17. FINAL REPORT

Write:

`production/ECONOMY_CONTAINMENT_DEATH_PROJECTILES_QOL_REPORT.md`

Include:
- ammo sell-price root cause;
- before/after pricing examples;
- enemy/Boss containment root cause and fix;
- Starter fallback behavior;
- Death Screen flow;
- Backpack reorder root cause and exact behavior;
- projectile visual architecture;
- per-family/per-weapon mappings;
- enemy projectile mappings;
- tests added;
- final EditMode/PlayMode counts;
- build/smoke results;
- proof paths;
- remaining genuine limitations.

Terminal status:

`ECONOMY_CONTAINMENT_DEATH_PROJECTILES_COMPLETE`

only if every repository-local requirement is fixed and verified.

Use:

`ECONOMY_CONTAINMENT_DEATH_PROJECTILES_INCOMPLETE`

if anything remains unfinished.

Do not ask for approval during execution.

BEGIN NOW.
