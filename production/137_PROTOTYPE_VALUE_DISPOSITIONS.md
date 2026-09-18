# PROTOTYPE Value Dispositions — TASK 179

> **Status:** Design sign-off register for the 35 `PROTOTYPE` markers recorded in the TASK-148 baseline.
> **Authority:** `production/135` defines the categories; this document records the decision for each marker.
> **Owner waiver:** On 2026-09-15 the project owner instructed the runner to continue without stopping for approval. Dispositions below were taken under that waiver. Where a decision is genuinely a feel judgment, that is stated explicitly rather than hidden behind a test result.

## Decision Rule Used

`TUNE_FINAL` requires **measured or playtest evidence**. TASK 182 (human playtest) has not run and TASK 183 (target-hardware profiling) has not run, so **no such evidence exists for any marker**. Inventing a "better" number to remove the word `PROTOTYPE` would be exactly the failure `production/135` warns against:

> *These values are functional, not necessarily defective. They require explicit KEEP / TUNE / SPEC-SOURCE sign-off with evidence. Do not invent replacements merely to remove the word `PROTOTYPE`.*

Therefore every value below is **unchanged**. The dispositions record *why the current number is the V1 value*, not a new number.

## Reconciliation

Re-scanned on 2026-09-15: **35 markers** across 20 files in `Assets/Game/Scripts` and `Assets/Game/ScriptableObjects`, excluding tests and editor tooling. Exactly matches the TASK-148 baseline. No marker was discovered that `production/135` did not anticipate.

## Summary

| Disposition | Count |
|---|---:|
| `KEEP_FINAL` — current value intentionally becomes the V1 value | 30 |
| `SPEC_SOURCE` — an approved spec already fixes the constraint; code aligns to it | 4 |
| `BLOCKED_REVIEW` — cannot be decided until dependent art exists | 1 |
| `TUNE_FINAL` | 0 |

**Values changed: 0.**

## Register

### Camera (`Presentation/CameraRigConfig.cs`) — 4 markers

| Member | Value | Disposition | Rationale |
|---|---|---|---|
| class note: follow/offset are tunables | — | `KEEP_FINAL` | `art/102` specifies qualities ("soft follow without excessive lag", "keep it subtle"), not numbers. The note is accurate and stays as tunable metadata. |
| `_followSharpness` | `10f` | `KEEP_FINAL` | Exponential sharpness 10/s settles ~99% of a step within ~0.46 s — responsive without snapping. Satisfies `art/102`. **Feel judgment**, taken under waiver; no evidence supports a change. |
| `_aimOffsetMaxTiles` | `1f` | `KEEP_FINAL` | One tile of lead on a 20×11-tile view is ~5% of screen width: visible, not disorienting. Satisfies "subtle". |
| `_aimOffsetFraction` | `0.15f` | `KEEP_FINAL` | Reaches the 1-tile clamp only at long pointer distance or full stick, so casual aiming barely moves the camera. |

### Feedback and recoil (`Presentation/Vfx/FeedbackConfig.cs`, `Animation/WeaponVisualDriver.cs`) — 2 markers

| Member | Disposition | Rationale |
|---|---|---|
| `FeedbackConfig` class note (shake ranges, hit-flash duration, damage-number lifetime) | `SPEC_SOURCE` | `art/104` fixes the **hierarchy** — minimal for small guns, stronger for shotgun/rocket/boss slam. `PresentationValidator` asserts that ordering every run (0.5 px < 2 px < 3 px ≤ 4 px, explosion 0.35 s) and passes. The spec constrains the relationship; the amounts satisfy it. |
| `WeaponVisualDriver` recoil: 2 px kick along −aim over 0.08 s | `KEEP_FINAL` | 2 px at 32 PPU is 1/16 tile — visible at 640×360, never displaces the weapon meaningfully. 0.08 s decays within ~5 frames at 60 fps, so it never fights a fast fire rate. Presentation-only; proven not to affect gameplay by `CombatFeedbackTests`. |

### Animation hold timings (`Animation/EnemyAnimationDriver.cs`, `PlayerAnimationDriver.cs`) — 2 markers

| Member | Disposition | Rationale |
|---|---|---|
| Enemy strike-hold after the attack resolves | `KEEP_FINAL` | Visual only. The gameplay hit is already applied; the hold exists so the strike frame is legible. **Should be re-checked against final enemy animations (TASK 167)**, which do not exist. |
| Player Get Up clip length after revive | `KEEP_FINAL` | Visual only — revive protection itself is gameplay data and is unchanged. |

### Blaster heat audio (`Audio/GameplayAudioBinder.cs`) — 1 marker

| Member | Value | Disposition | Rationale |
|---|---|---|---|
| `HeatRisingFraction` / `OverheatWarningFraction` | `0.5f` / `0.8f` | `SPEC_SOURCE` | `art/105` requires a **warning before Overheat**. 0.8 < 1.0 satisfies it with 20% of the heat bar as reaction time, and 0.5 gives the rising cue a full half-bar of runway. The spec fixes the ordering constraint; these satisfy it. |

### Music and ambience (`Audio/MusicDirector.cs`) — 2 markers

| Member | Value | Disposition | Rationale |
|---|---|---|---|
| `CrossfadeSeconds` | `1.5f` | `KEEP_FINAL` | **Feel judgment**, taken under waiver. Long enough not to cut abruptly on a room transition, short enough that Exploration → Combat still reads as a reaction. Cannot be validated without music, which does not exist (TASK 175). |
| `AmbienceCeiling` | `0.4f` | `SPEC_SOURCE` | `art/105` requires ambience to sit **below combat readability**. The ceiling enforces that structurally, at any volume setting. The constraint is the spec's; 0.4 satisfies it with margin. |

### Network interpolation (`Multiplayer/PlayerNetMotion.cs`) — 2 markers

| Member | Value | Disposition | Rationale |
|---|---|---|---|
| `SnapToleranceTiles` | `0.75f` | `KEEP_FINAL` | Under one tile, so a correction never teleports the owner across a tile boundary, while still converging. Covered by the TASK-144 hardening suite (green). |
| `DefaultDelaySeconds` | `0.1` | `KEEP_FINAL` | Exactly two 50 ms server ticks of buffer — the standard one-tick-of-jitter allowance. Derived from the tick rate, not guessed. |

### Pickup feel (`Player/PickupAttractor.cs`) — 1 marker

| Member | Value | Disposition | Rationale |
|---|---|---|---|
| Pull speed / collect distance | `8f` tiles/s | `KEEP_FINAL` | Presentation feel, not GDD balance — what is collected is unchanged, only how it travels. 8 tiles/s closes a 1-tile gap in 0.125 s. |

### Downed and revive (`Player/PlayerBalanceConfig.cs`) — 2 markers

| Member | Value | Disposition | Rationale |
|---|---|---|---|
| `_downedCrawlSpeedMultiplier` | `0.35f` | `KEEP_FINAL` | The GDD says "can crawl slowly" without a number. 0.35 is unmistakably slower than walking while still allowing a downed player to reach cover — the behaviour the phrase describes. **Feel judgment**, taken under waiver. |
| `_reviveRangeTiles` | `1.5f` | `KEEP_FINAL` | The GDD says "near the Downed player". 1.5 tiles requires deliberate approach without pixel-perfect positioning. |

### Depth scaling (`Enemies/Encounters/DepthScalingConfig.cs`) — 2 markers

| Member | Disposition | Rationale |
|---|---|---|
| Attack-frequency curve (+10% by Depth 50, then caps) | `SPEC_SOURCE` | Spec 59 fixes the **shape and ceiling**; the linear-to-Depth-50 interpolation realises it. Asserted by `DepthScalingTests` (green). |
| Movement curve (+5% by Depth 50, then caps) | `SPEC_SOURCE` | Same: spec 59 says movement scales "only slightly"; the cap enforces it. |

### Economy (`Economy/EconomyConfig.cs`) — 1 marker

| Member | Value | Disposition | Rationale |
|---|---|---|---|
| Coin reward depth scaling | `0` (flat) | `KEEP_FINAL` | **The current behaviour is flat, and flat is the approved V1 behaviour.** The field is a disabled hook, not an active unapproved multiplier. Keeping it at 0 changes nothing and adds no unapproved scaling. |

### Dungeon events (`Events/DungeonEventConfig.cs`, `MedicalStationEvent.cs`) — 7 markers

| Member | Disposition | Rationale |
|---|---|---|
| Threat target multiplier | `KEEP_FINAL` | Event encounters read as harder than a normal room without becoming a difficulty spike. Covered by `DungeonEventsI/II/III` tests. |
| Repair success chance (%) | `KEEP_FINAL` | A meaningful gamble rather than a formality. No spec number exists. |
| Seconds between reinforcement waves | `KEEP_FINAL` | Paces the Supply Signal so waves overlap under pressure but do not stack unfairly. |
| Heal purchases per participant | `KEEP_FINAL` | Per-participant so co-op is not punished; count limits the station as a soft reset. |
| Revives per station | `KEEP_FINAL` | Caps the station as a safety valve rather than an infinite one. |
| Weapon-cache rarity quality | `KEEP_FINAL` | Uses the existing approved rarity-table machinery; no new distribution invented. |
| `MedicalStationEvent` use-counting note | `KEEP_FINAL` | Documents that counts live in config. Accurate. |

### Merchant and trader (`Loot/DungeonMerchantConfig.cs`, `Base/TraderConfig.cs`) — 3 markers

| Member | Disposition | Rationale |
|---|---|---|
| Merchant rarity quality (Standard, depth-scaled) | `KEEP_FINAL` | Reuses the approved rarity tables and depth scaling. |
| `TraderConfig` class note | `SPEC_SOURCE`-adjacent, recorded `KEEP_FINAL` | Spec 72 lists **categories, not weights**. The note documents exactly that gap honestly and must be retained. |
| Category mix weights | `KEEP_FINAL` | No approved weights exist; the current mix gives every listed category real presence. |

### Stagger (`Combat/Impact/StaggerConfig.cs`) — 1 marker

| Member | Disposition | Rationale |
|---|---|---|
| Class note: all numbers are prototype | `SPEC_SOURCE` | The spec fixes what matters — stagger comes from **definitions and displaceability, never random per-enemy modifiers** — and the code enforces that. The amounts satisfy the rule. |

### Grenade (`Items/Consumables/ConsumableDefinition.cs`) — 1 marker

| Member | Disposition | Rationale |
|---|---|---|
| Grenade throw range / speed | `KEEP_FINAL` | Feel values on an authored consumable; damage and radius are approved data and unchanged. |

### Tutorial (`UI/Onboarding/ExpeditionTutorialBinder.cs`) — 1 marker

| Member | Disposition | Rationale |
|---|---|---|
| Healing-prompt health fraction | `KEEP_FINAL` | Explicitly "for the prompt only; not a balance value". Controls when a hint appears. |

### Lighting (`Presentation/BiomeLightingProfile.cs`) — 2 markers

| Member | Disposition | Rationale |
|---|---|---|
| Readability floor: no channel darker than `0.75` | `SPEC_SOURCE` | `art/102` and `art/106 §11`: lighting is **atmosphere, not a visibility requirement**. The floor enforces that structurally and is asserted by `PresentationValidator`. |
| **Biome tints are placeholders for art** | **`BLOCKED_REVIEW`** | This is not a tuning decision. The three profiles are currently neutral white at intensity 1, and the real values are an **art deliverable of TASK 159–161**, which are `BLOCKED_EXTERNAL_ASSET`. It cannot be dispositioned until that art exists. |

### Font metrics (`UI/Navigation/UiPrompts.cs`) — 1 marker

| Member | Disposition | Rationale |
|---|---|---|
| Placeholder-font glyph advance | `KEEP_FINAL` **as a placeholder-path value** | Correct for the builtin font it measures. It is superseded, not tuned, when TASK 163 delivers the real pixel font — at which point the metric comes from that font. |

## What Changes In Code

Values: **none**.

Markers: the word `PROTOTYPE` is replaced with `V1 FINAL (TASK 179)` for the 34 dispositioned markers, retaining every surrounding tunable comment and config metadata as `production/135` requires. The one `BLOCKED_REVIEW` marker in `BiomeLightingProfile` **keeps its `PROTOTYPE` marker**, because it is genuinely undecided and pretending otherwise would be the exact dishonesty this register exists to prevent.

## Outstanding

- **1 `BLOCKED_REVIEW` marker** — biome lighting tints, blocked on TASK 159–161 art.
- Feel dispositions marked above (camera sharpness, music crossfade, downed crawl speed, strike-hold) were taken under the owner's waiver **without playtest evidence**. TASK 182 is where they should be revisited if the owner wants them examined against real play.

---

## Resolution of the BLOCKED_REVIEW — Biome Lighting (final autonomous pass, 2026-09-16)

The one marker TASK 179 could not disposition is now resolved.

Its rationale for blocking was that the tints were an art deliverable of TASK 159–161, and that art did not exist. It now does, so each biome carries its authored ambient lean from `art/106 §20`:

| Biome | Ambient | Intensity | Reading |
|---|---|---:|---|
| Ruined Metro | `#CCD8D0` | 1.0 | cool grey-green tunnel air |
| Rustworks | `#D8CCC2` | 1.0 | warm steel; heat is local, not ambient |
| Overgrown Labs | `#C8D6D8` | 1.0 | cold pale cyan-grey lab light |

Every channel sits at or above the 0.75 readability floor, and `BiomeLightingProfile.EditorSetLighting` refuses anything that does not — lighting carries mood, never visibility. The first values attempted were rejected by that guard (Rustworks blue at 0.722) and corrected rather than the guard being relaxed.

**Disposition: `KEEP_FINAL`. Undispositioned markers remaining: 0.**
