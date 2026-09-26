# RUINRAIL — Current State

> Owner of "what is true now". Keep it short; no history, bug lists or test transcripts. Last reviewed 2026-09-25.

## Concept
Top-down 2D pixel-art PvE extraction roguelite, Solo/Duo/Trio. Shelter (safe base) → expedition through endless depths
of three biomes → boss → transit: **Return** (secure carried loot) or **Descend** (everything stays at risk). Failed
extraction loses carried loot; XP, level, skill points, banked coins and Storage persist. Build comes from equipment,
not classes. Full loop: `design/03_CORE_GAME_LOOP.md`.

## Implementation / release state
- **Feature-complete V1 release candidate** on branch `release-candidate-2026-09-25`. No repository-local release blocker.
- Release gates, test baselines and platform status: `../production/CURRENT_RELEASE_STATUS.md` (the only release status).
- Unity `6000.3.24f1`, URP 2D, 640×360 pixel reference; pinned packages in `ENVIRONMENT.md`.

## Shipping systems (all composed in the shipping runtime)
- Scenes: Bootstrap → MainMenu → Base (Shelter) → Dungeon.
- Player: 8-direction presentation, dash, leveling to 61, six attributes, global stat caps, downed/revive/death.
- Items: one-slot items, 8-slot backpack, 2 weapon slots + armor + accessory + active consumable; 11 weapon classes,
  rarity/affixes, Legendary weapon specials and accessory passives (event producers wired), 4 ammo types, consumables.
- Combat: enemy framework, normal enemies, Elites, two-phase biome bosses, stagger/knockback (player knockback live).
- Dungeon: seeded graph generator over hand-authored grid rooms; biomes Ruined Metro, Rustworks, Overgrown Labs;
  events, chests, merchant, weapon cache; depth scaling; transit vote/extraction.
- Shelter: Storage, Trader, Character station, Workshop, starter kit, expedition summary, onboarding.
- Co-op: NGO over UnityTransport, host-authoritative, 1–3 players, revive, transit vote, reconnect, spectator.
- Persistence: versioned save (v2) with migration chain, autosave at safe points, deepest-depth record.
- Presentation: final integrated art (340 roles), pixel UI font/skin, audio (53 events, 11 tracks), settings, rebinding,
  controller navigation.

## Frozen / accepted
Balance values in `../production/FINAL_RELEASE_FROZEN_BASELINE.csv` are frozen; the release validator compares against
them. Decisions and deferrals: `DECISIONS.md`.

## Known non-blocking issues
- Pack pile-up: a pressed body can sit ≤~0.4 tiles inside a wall/door collider for one physics step before correction.
- Mono heap drift ≈0.09 MB per depth in long runs (no object growth).
- Smoke direct-hit reference placement can miss on some clock seeds (official smoke uses pinned seeds).

## Environment limitations (this Mac)
- `Windows x64: NOT RUN — module unavailable` — macOS non-development build is the local substitute.
- `Live UGS Sessions/Relay: NOT RUN — project/service configuration unavailable` — co-op proven with real peer processes by direct address.
- EditMode regenerates `TestResults/*` reports; `TestResults/` is gitignored and disposable except for the latest full-suite XML
  (`EditMode-results.xml`, `PlayMode-results.xml`), which `FinalMvpAudit` reads.

## Where things live
Design `design/` · technical contracts `technical/` · release status `../production/CURRENT_RELEASE_STATUS.md` ·
history `../production/archive/` and git (not startup context).
