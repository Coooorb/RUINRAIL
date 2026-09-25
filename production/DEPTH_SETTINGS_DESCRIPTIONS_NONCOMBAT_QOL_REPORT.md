# RUINRAIL — Depth Heal / Item Descriptions / Settings UI / Enemy Count / Non-Combat Room Fix Pass

> **Scope:** `RUINRAIL_DEPTH_SETTINGS_DESCRIPTIONS_NONCOMBAT_PROMPT.md`, executed from the repository state after the
> `ECONOMY_CONTAINMENT_DEATH_PROJECTILES` pass. The five listed issues only. No weapon/enemy balance, progression,
> save semantics, inventory capacity, room topology or network-authority change; no pinned Editor or package version
> changed (one test-assembly reference to `Unity.InputSystem`, already a pinned dependency, was added so the PlayMode
> suite can compose a real rebinder); nothing was installed.
> **Terminal status:** `DEPTH_SETTINGS_DESCRIPTIONS_NONCOMBAT_INCOMPLETE`
>
> Every repository-local requirement (§2–§9 and §11 of the prompt) is fixed, tested and verified in the Editor
> **and** in a built player. The built player is a **macOS** non-development build, because this machine
> (`/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/PlaybackEngines` holds only
> `MacStandaloneSupport`) has no Windows Build Support module. The **Windows x64 release build and its smokes (§10)
> are therefore NOT RUN** and are the only reason the status is not COMPLETE; §12 keeps the two things apart.

| Gate (final sequence after the last source change: build → smokes → PlayMode → EditMode) | Result |
|---|---|
| `./scripts/run-unity-tests.sh PlayMode` (strict, Unity `6000.3.24f1`, macOS) | **PASS — 747 passed / 747 discovered, 0 failed, 0 skipped** |
| `./scripts/run-unity-tests.sh EditMode` (strict) | **PASS — 872 passed / 873 discovered, 0 failed** (1 skipped: the pre-existing `RUINRAIL_LIVE_SERVICES=1` live-Sessions check) |
| `ContentCountValidator` | PASS — 52/52 counts exact, 0 problems |
| `FinalProductionValidator` | PASS — **10/10** sections, 0 errors, 0 outstanding external assets (section 10 is the new item-description gate) |
| `ItemDescriptionValidator` (`TestResults/item_descriptions.md`) | **PASS — 72 items audited, 72 complete, 0 problems** |
| `PresentationValidator` / `AssetPipelineValidator` / `ReleasePathVisualScan` / `ArtProductionContract` | PASS 8 · PASS 6 · CLEAN (0 placeholder, 0 missing references) · PASS 10 |
| `CompletionAssetManifest` | **340 roles — 340 INTEGRATED, 0 PLACEHOLDER, 0 MISSING** (was 339; `ui.enemy_icon` added, provenance recorded) |
| Non-combat room matrix (`noncombat_room_matrix.csv`) | **54/54 PASS** — every non-combat room prefab of the 63-room pool, the 6 Event rooms under all 5 seeded kinds |
| Generated-depth sweep (`event_seed_scan.txt`) | seeds 1–80 × depths 1–2 through the real pipeline: all 3 biomes, every event kind reachable |
| Settings persistence, encounter/HUD, depth-heal, save/exploit, network-authority tests | inside the two suites above (§9) |
| Release build, **Windows x64** non-development (`ReleaseBuildTool.BuildBatch`) | **NOT RUN — no Windows Build Support module on this macOS machine; nothing installed** |
| Release build, **macOS** non-development (`ReleaseBuildTool.BuildMacBatch`, same scenes, `BuildOptions.None`) | **Succeeded** — 0 errors, 2 pre-existing environment warnings (UGS project id, Unity Cloud symbols), 175.6 MB |
| Built-player smoke, headless (`RUINRAIL.app/Contents/MacOS/RUINRAIL -batchmode -nographics -smoke -seed 79 …`) | **PASS** — 15 playability + 12 combat + 25 loot + 24 audio + 35 inventory + 24 HUD + 1 merchant + 1 weapon-cache + 31 room/HUD + 46 economy/containment + **65 depth/descriptions/settings/enemy-count/non-combat** + 7 starter-fallback + 9 death-screen checks, `ReturnToMenuOk: true`, 0 exceptions / errors (any logged exception fails the smoke) |
| Built-player smoke, windowed (`-smoke -seed 79 -screen-width 1280 -screen-height 720 -proofdir … -hudproofdir … -inventoryproofdir …`) | **PASS** — the same checks, 80 captures written by the shipped executable, music carried signal in 58/60 sampled frames |

Baseline before this pass: EditMode 850 discovered, PlayMode 721 → **+23 EditMode, +26 PlayMode tests**.

---

## 1. Full HP on a new depth — hook, order, anti-exploit

**Hook (exact).** `ExpeditionScene.OnDepthEntered` is the scene's subscriber to `ExpeditionService.DepthEntered`. The
service raises that event from `Start` (depth 1 — the scene is composed afterwards and never sees it) and from
`Descend()` (depth N → N+1, only after the transit decision resolved to DESCEND). The scene closes the old depth's
windows, calls `BuildDepth()` (the new layout is generated, instantiated, its rooms composed, the player placed on
the Start spawn), and **then** — guarded by `IsExpeditionActive && state.Depth > 1` — calls
`DepthArrivalHeal.ApplyToParty(_roster)` exactly once and counts it (`DepthArrivalHeals`,
`LastDepthArrivalHeals`).

**Rule (`Player/DepthArrivalHeal.cs`).** For every member of the party roster: the effective maximum is
`PlayerStatsBinder.Stats.MaxHealth` — the stat pipeline's `round((base + Σ flat) × multiplier)` over every
registered armor / accessory / affix source, the same figure the HUD denominator and the run-start fill use; never a
hard-coded base. The component's ceiling is resized to it if the pipeline moved (`ResizeMaxHealth`, clamp only), then
the missing amount goes through the ordinary `HealthComponent.Heal` — so the fill is host-authoritative
(`DamageAuthority.LocalIsAuthoritative`, a client applies nothing), raises one `Healed` event for HUD/audio, and
cannot exceed the maximum. 100 effective → 100/100; 120 (Scrap Vest) → 120/120; the heaviest catalog armor → its own
maximum (asserted).

**Exactly once / no exploit.** The only caller is the DepthEntered path above. Room entry and revisits
(`RoomRuntime.PlayerEntered` → title/minimap only), the Transit UI (`OnTransitOpened` pushes the vote list, nothing
else), voting before resolution, `BuildDepth` inside a depth, `EquippedChanged` (→ `ResizeMaxHealth`, clamp only) and
the pause/inventory/merchant/cache windows never reach it. Returning to the Shelter goes through `Return()` →
`ExpeditionEnded`, not `DepthEntered`. Reopening a transit on the next depth (second boss defeat) opens a new
decision and heals nothing (asserted live). **No resurrection:** a member whose life state is not `Alive` (Downed or
Dead) is skipped with `Eligible = false`; `Heal()` itself also refuses a 0-HP body; life states are untouched. Co-op:
`ApplyToParty` walks the host-side `PartyLifeRoster`, each member with its own `PlayerStatsBinder` maximum.

**Proof.** Live run seed 53: end of depth 1 at 55/120 with the transit boarded → pause opened/closed, armor
unequipped/re-equipped, two rooms revisited: 55 unchanged, 0 heals → DESCEND → depth 2 (Rustworks) at **120/120**,
`DepthArrivalHeals = 1`, one `Healed(65)` event → damaged to 91 → room entries, revisits and the reopened depth-2
transit: 91, still 1 heal (`live_01_damaged_end_of_depth.png`, `live_02_next_depth_full_hp.png`, evidence file).
Shipped player (seed 79): 62/120 → 120/120, exactly once, revisits never heal (`dsnc_01`, `dsnc_02`).

## 2. Item descriptions — 72 items audited

**What was wrong.** `ItemDefinition` had no description at all. The tooltip (`ItemTooltip.Build`) produced stat lines
for weapons/armor/accessories only; **consumables and ammo had no lines whatsoever** — a Bandage's details panel showed
its name, `COMMON · CONSUMABLE · x3` and nothing else. Stat labels were raw enum names (`MovementSpeed`), the Legendary
line was `LEGENDARY SPECIAL: Snapfire (Burst, 10s cooldown)` with no effect, and the three details panels each kept
their own copy of the rendering and cut the row list at their row count.

**Fix — descriptions are data, not prose.** `Items/ItemDescriptions.cs` builds every item's description from the
definition itself, so the text cannot drift from the mechanic:

| Category (count) | Derived from | Example (actual repo values) |
|---|---|---|
| Consumable — Heal (2) | `HealAmount`, `UseTimeSeconds` | Bandage: *Restore 25 HP when the 1.5 s use completes (the unit is spent on completion). Healing Received bonuses apply.* — Medkit: 60 HP / 3 s |
| Consumable — Timed buff (3) | `BuffStat`, `BuffPercent`, `BuffDurationSeconds`, `UseTimeSeconds`, `ActivatesArmorInjectorCap`; the runner's refresh-never-stack policy | Combat Stim: *Gain +20% Movement Speed for 8 s (0.5 s use). Using another refreshes the timer; it never stacks.* — Damage Stim: +20% Weapon Damage / 10 s — Armor Injector: +20% Damage Reduction / 10 s, *the Damage Reduction cap rises to 50%* |
| Consumable — Grenade (4) | `GrenadeData` (kind, radius, damage, stagger, burn, smoke, throw range) and `ThrownGrenade`/`BurnZone`/`SmokeZone` behaviour | Frag: *Throw up to 6 tiles: explodes for 35–45 damage in a 2.5-tile radius with stagger 4.* — Shock: 15–20, heavy stagger (20) — Incendiary: 12–16 blast, then burning ground 5 s at 6/s — Smoke: 4-tile cloud 6 s, normal enemies lose line of sight, Elites/Bosses ignore it, no damage |
| Consumable — Revive (1) | `ReviveHealthPercent`, `DropEligibility` | Defibrillator: *Co-op only: revive a fully Dead teammate at 30% of their Max HP (instant). Has no use in Solo and never drops there.* |
| Ammo (4) | `AmmoType`, `MaxStack`, and the shipped weapon catalog (which classes consume the type, rocket cost) | Heavy Ammo: *Reserve rounds for Rocket Launcher and Sniper Rifle weapons. … A backpack stack holds up to 60. Rocket Launchers spend 4 per shot.* |
| Ranged weapon (21) | class, damage, fire rate, pellets/spread, explosion radius, ammo type & cost per shot, magazine, reload, range | Scatter-8: *Shotgun: each shot fires 8 pellets of 4–5 damage in a 30-degree spread. Uses Shells; 8-round magazine, 2.4 s reload, 5-tile range.* — Pipe Launcher: *explodes on impact in a 2-tile radius … Each shot spends 4 Heavy Ammo* |
| Blaster (3) | heat per shot, max heat, cooling rate/delay, lockout | Arc Blaster B4: *No ammo: each shot adds 10 Heat (max 100); Heat cools 35 per second after 0.6 s without firing, and overheating locks the weapon for 2.2 s.* |
| Bow (3) | quick/full damage, charge time, ranges | Recurve Bow: *hold to draw. A quick shot deals 10–12 … a full draw (0.65 s) deals 25–30 and flies faster and farther (12 tiles).* |
| Melee (6) | arc, reach, damage, rate | Field Knife: *an 80-degree swing of 1.2 tiles for 14–17 damage, 3.5 attacks per second* — Guard Lance: *a narrow 20-degree thrust of 3 tiles* |
| Armor (9) | `BaseModifiers()` (Max HP, DR, extra property) | Heavy Plate: *+35 Max HP, +9% Damage Reduction and -4% Movement Speed while worn* |
| Accessory (16) | `BaseModifiers()` (intrinsic) | Magnetic Coil: *+3 tiles Pickup Attraction Radius while worn* |
| Legendary weapon special (11 families) | the `LegendarySpecialDefinition` (kind, shots, damage, arc, range, radius, distance, cooldown) | Quickfang: *LEGENDARY (RMB / LT): Snapfire — fire 8 rapid shots of 10–12 damage each. 10 s cooldown; uses no ammo or Heat.* |
| Legendary armor/accessory passive (25) | each passive class now exposes `Description` written beside its constants | Scrap Vest: *LEGENDARY PASSIVE: After clearing a Combat Room, restore 6% of Max HP.* — Heat Sink: *… emit a 2.5-tile energy pulse for 30–40 damage (8 s cooldown)* |

Nothing was invented: the heal text originally claimed an interrupted use costs nothing — the runtime has no
interruption in V1, so the text says what the code does. No balance value was changed to match text. Stat names now
come from one `StatLabels` table (`Movement Speed`, `Damage Reduction`, …) shared by tooltips and descriptions.

**Presentation.** `ItemTooltip.Description` leads the details; consumables/ammo get structured effect lines (`Heal
+25 HP`, `Use time 1.5 s`, `Duration 8 s`, `Blast 35–45`, `Radius 2.5 tiles`, `Stack up to 5`); the Legendary line is
the description's. The three panels (Inventory, Merchant, Weapon Cache) render through one `ItemDetailLayout`
(description → Legendary → stats → affixes → comparison, word-wrapped by `UiText.Wrap` to whole glyph cells) and one
`DetailPager`: when the rows overflow the panel (10 rows in the inventory, 12 in the others) the last row becomes
`MORE (1/2) WHEEL/PGDN` and the mouse wheel, PageDown/PageUp and the controller's right stick page — the tail is
never cut and the description is never the part that gets lost. Every description wraps into ≤ 5 lines of the
narrowest panel with nothing dropped (asserted for all 72), and uses only characters the pixel face draws (the
validator and the tests check every character against `PixelFontFactory.Charset` — which is also why the resolution
text is `1280 x 720`, the face has no `×`).

**Validation.** `Editor/Production/ItemDescriptionValidator` (menu + report `TestResults/item_descriptions.md`, and
section 10 of `FinalProductionValidator`) fails on: empty/short text, placeholder markers, the item's own id or any
snake_case id in the text, a consumable whose effect lines cannot be generated, a Legendary mechanic the family owns
that is not described, a numeric claim that does not match the definition (heal amount, buff percent/duration,
grenade radius/damage, weapon damage/magazine/ammo/pellets/explosion, armor HP/DR, accessory intrinsic, ammo stack),
and any character outside the pixel charset. `ItemDescriptionTests` (10) compare structured tooltip values to the
definitions for Bandage, Combat Stim, Frag, Quickfang (Rare vs Legendary instance), and assert the representative
texts above; two fixture items prove the validator fails a heal without an amount and an unknown special.

## 3. Settings — real category pages

**What was wrong.** One flat `FocusList` mixed tab labels, values and actions (`MASTER VOLUME` cycled 0→1 in 0.1 steps
on Enter, `AUDIO`/`VIDEO` rows did nothing visible), and the shipped app constructed its `SettingsViewModel` with a
**null rebinder**, so CONTROLS listed nothing in the built player.

**Architecture.** `SettingsViewModel` gained a page state (`Page`, `OpenPage`, `BackFromPage`, `ResetToCategories`,
`RequestClose`) and `RowsFor(tab)` — every row a `SettingsRow` with its kind (Toggle / Selector / Slider / Action),
live value text, fill, an `Adjust(±1)` and an `Activate`. `FocusItem` gained an optional `Adjust`; `MenuInput`
routes left/right to the focused control's `Adjust` first (keyboard arrows, D-pad, stick) and only then to the
screen's tab bar. `ScreenNavigation.Settings` is the category root, `ScreenNavigation.SettingsPage(tab)` a page.
`App/SettingsPanel` (one builder for Main Menu and Pause) draws the root as four category buttons with a hint line
each plus RESET TO DEFAULTS and BACK, and a page as rows — the shared `UiControl` plate (hover, press, focus
brackets) with the value on the right and, for sliders, a fill bar the pointer can press/drag (`SettingsRowView`);
long pages (CONTROLS) scroll by focus through the existing `FocusWindow`. `GameApp` now composes a real
`InputRebinder` over its own `RuinRailInputActions` instance carrying the persisted overrides.

| Page | Adjustable options actually implemented | Apply / persist |
|---|---|---|
| VIDEO | Display mode (Fullscreen / Windowed) · Resolution (Native + the display's offered options) · VSync · Frame-rate limit (Unlimited / 30 / 60 / 120 / 144 / 240; `Application.targetFrameRate`, new `VideoPreferences.FrameRateLimit`) · APPLY DISPLAY SETTINGS · REVERT DISPLAY CHANGE | VSync and the limit persist on Back. A display-mode / resolution change never reaches the engine until APPLY, is then applied **provisionally** with a 12 s KEEP timer (`Tick` on unscaled time); KEEP persists it, REVERT / Back / timeout restore the previous values to engine and draft; leaving the page with an unapplied display edit drops it. Pixel-perfect/scaling: no such setting exists in the project, none was invented. |
| AUDIO | Master · Music · SFX · **Ambience** (new `AudioPreferences.AmbienceVolume`, `AudioLevels.Ambience`, the ambience bus = master × sfx × ambience still under the art/105 ceiling) · MUTE ALL | 5 % steps, pointer drag/press along the bar, 0–100 % text; every edit is **previewed at once** (`AudioLevels`) and persisted on Back; Discard restores the persisted mix; explicit 0 persists as a mute; fresh defaults 100 %. |
| CONTROLS | Bindings-for selector (Keyboard & Mouse / Controller) · every rebindable action row (Enter/click listens through the one existing `InputRebinder`; Pause fixed; `*` marks an override) · RESET BINDINGS | Back applies: overrides persist to the settings document and publish through `ActiveBindingOverrides` to live and future readers. No sensitivity/aim-assist setting exists in the project; none was invented. |
| GAMEPLAY | Screen shake · Shake intensity (slider, inert while shake is off) · Damage numbers · Hit flash · Tutorial prompts · Reset tutorials (only with a profile) | Persist on Back; published through `FeedbackPreferences`. Only pre-existing preferences. |

**Navigation.** Main Menu → SETTINGS → categories → page → BACK (row, Esc or B) → categories → BACK → Main Menu
(`MainMenuScreen.OnBack` / `CloseSettings`). Pause → SETTINGS → … → Back → Pause root → Resume
(`PauseMenuViewModel.Back` / `HandlePauseInput` step a page first; the root BACK row asks the owner through
`CloseRequested`). One focus list on the stack at a time (the page replaces the categories, no stacked duplicates);
`GameplayInputGate` stays held under the pause layer; the pointer cursor is the overlay's; every label is on the
pixel face and no label runs into its value (asserted). Two shipped-player defects found on the way and fixed: the
tutorial prompt line drew through the pause/settings panel (it now hides while any menu layer is open), and
descending through the transit vote panel raised a `NullReferenceException` in the scene's stale vote handler (the
vote resolves and rebuilds the depth before its own `Changed` fires; the handler now ignores a stale vote).

## 4. Enemies remaining HUD

**Source.** `RoomRuntime` (the room's authoritative encounter membership): `EnemiesRemaining` =
`EncounterRuntime.LivingCount + PendingCount` (living members incl. summons registered into the encounter, plus
queued reinforcements) for a standard encounter, 1 while an Elite engagement's Elite lives, 0 otherwise. It is
recomputed on spawn, death, tick and completion and written to `RoomRuntimeState.EnemiesRemaining`, so it
**replicates with the room state** (`RestoreState`) — a client's room reads the host's number and never counts
replicas (asserted host → clone → client). `ShowsEnemyCount` = `RoomType.Combat && Lifecycle == Active && engagement
is not BossEngagement`.

**Eligibility, enforced by that rule:** shown only while the local player's current room (the same inset entry
trigger that reveals the room title) is an active standard combat encounter with enemies left; hidden before
activation, when cleared, in Boss arenas (even with summons on the floor), and in Start / Loot / Treasure /
Merchant / Event (Weapon Cache, Broken Machine, Cursed Chest, Supply Signal, Locked Vault) / Medical / transit rooms —
also while an event wave runs in an event room. Adjacent rooms' enemies and dead/pending-despawn actors are never
counted (scene sweeps are not used; asserted with seven enemies alive across two rooms).

**Display.** `HudEnemyCountView`: the new generated hostile token (`UiFactory.EnemyIcon`, 12×12 emergency-red threat
mask, `ui.enemy_icon`, bound into `UiSkin.EnemyIcon`) plus `xN`, on the coin readout's plate style, top-right directly
under the coins (`DungeonHudView.EnemiesRect`, inside the frame, overlapping nothing — the layout regression test
covers it). `DungeonHudViewModel.BindEnemyCount(delegate)` polls it in `Tick`; the HUD never references room types
(the source-scan test still forbids `RoomRuntime` under `UI/Hud`). 6 → 5 on the first death (`live_11/12`,
`dsnc_11/12`), gone on clear, absent in the Boss arena (`live_13`, `dsnc_13`) and in an event room (`live_14`,
`dsnc_14`). Room clear is not delayed.

## 5. Non-combat rooms — authoritative list and audit

**Derived from the 63-room pool (RoomDefinition metadata, not names):** 6 Start · 33 Combat · 3 Loot (2 chests each)
· 3 Treasure (1 chest) · 3 Merchant · 6 Event (no `event:` tag pinned, so each is one of the five seeded kinds
**Cursed Chest, Locked Vault, Broken Machine, Supply Signal, Weapon Cache**) · 3 Medical/Recovery (**Medical
Station**) · 6 Boss (the **Boss Cache** and the **Transit Car** are the arena's non-combat interactions). Ordinary
combat rooms additionally get a planned **Supply Chest** (already covered by `SupplyChestRuntimeTests`). That is the
complete non-combat/special surface: 30 rooms, 12 mechanics.

### Broken Machine — intended mechanic, root cause, fix

**Design (57.3 + 77):** pay Carried Coins (100 + 10 × (depth − 1), cap 400) to attempt a repair; the seeded attempt
yields one item / ammo / consumable from `LootTable_BrokenMachine` (50 % in `DungeonEventConfig`) or simply fails;
the coins are spent either way; one attempt per depth; no punishment, no option menu (none is designed, none was
invented). `BrokenMachineEvent` implemented exactly that and its table was bound.

**Root cause of "does not work" (verified on the real interaction path):** `DungeonEventInteractable.CanInteract`
returned `event.CanActivate(actor)`, and `PlayerInteractor.FindNearestInteractable` only surfaces targets whose
`CanInteract` is true — so a player who could not afford the repair (a run starts with **0** Carried Coins; the
machine costs 100+) saw **no prompt at all** and the press did nothing, with no message. When the player *could* pay,
a failed repair spent the coins and produced nothing visible beyond a tint change; a successful one dropped the
reward silently. The same seam hid the Locked Vault (250+ coins) and the Medical Station (at full HP or after its
use) in exactly the same way. Every other link of the chain was sound (object at the anchor, final art, collider,
transaction, once-only phase, state restore).

**Fix (shared seam, no per-event hacks).** `DungeonEventInteractable.CanInteract` = "the event is still open and the
actor exists"; the prompt (`IInteractionPrompt`) now carries the cost and — when the press would be refused — the
reason from `EventPromptBuilder.RefusalReason`: `REPAIR BROKEN MACHINE (100 COINS) — NEED 100 MORE COINS`,
`USE MEDICAL STATION (150 COINS) — HP FULL`, `— USED`. A refused press raises the new `Refused` seam and changes
nothing; `EventOutcomeText` turns every result into the player-facing line the new HUD notice shows: `MACHINE
REPAIRED: HEAVY AMMO X12`, `REPAIR FAILED: THE MACHINE IS DEAD (100 COINS SPENT)`, `VAULT UNLOCKED: …`,
`HEALED +40 HP (150 COINS)`, `CURSED CHEST OPENED: DEFEAT ITS GUARDIANS` / `… CLEARED: RIOT ARMOR, SMOKE GRENADE X1,
77 COINS`, `SUPPLY SIGNAL SENT: SURVIVE 30 S` (held as a countdown while it runs) / `SUPPLY DROP DELIVERED: …`,
`WEAPON TAKEN`, `BROKEN MACHINE: NOT ENOUGH COINS (100 NEEDED)`. `ExpeditionScene.AttachEventNotices` wires it per
room from the existing `Activated`/`Completed` events; the interact prompt line was widened (400 px) to hold a prompt
with its reason. The Weapon Cache's `ChoiceRequired` answer is checked before the refusal branch (the first draft
broke it; `WeaponCacheUiTests` caught it).

### Every other non-combat mechanic

| Mechanic | Finding | Fix / verification |
|---|---|---|
| Locked Vault | same hidden-prompt seam when unaffordable; silent payout | prompt with shortfall, refusal notice, `VAULT UNLOCKED: …` notice; pays exactly the cost once, dims, restores on revisit (matrix, smoke `dsnc_17_locked_vault_unlocked`) |
| Medical Station | no prompt at full HP / after use / unaffordable | `— HP FULL` / `— USED` / `— NEED n MORE COINS` prompts; heal-to-full for exactly the cost once per participant, second press refused (matrix, smoke `dsnc_17_medical_station_healed`) |
| Cursed Chest | sound; no outcome feedback | opened/cleared notices; doors lock, wave in room, loot on clear, doors open, no chip during the wave (matrix, live `live_17_cursed_chest_wave/_cleared_loot`) |
| Supply Signal | sound; no visible timer or outcome | held `SUPPLY SIGNAL: SURVIVE n S` countdown, waves, drop delivered notice (matrix, `SupplySignalEvent.Tick` driven) |
| Weapon Cache (fixed last pass) | regression only | prompt → selection → one weapon taken → consumed; `WEAPON TAKEN` notice; reopen impossible (matrix + `WeaponCacheUiTests` unchanged) |
| Merchant (fixed two passes ago) | regression only | prompt → trade window → close → reopen (matrix + smoke) |
| Loot / Treasure chests | sound | `OPEN CHEST` → loot, opened art, spent, state recorded (matrix, live + smoke captures) |
| Boss Cache + Transit Car | cache `LOCKED` until the boss falls; **the transit car was a collider without any prompt** | `TransitCar` implements `IInteractionPrompt` (`BOARD TRANSIT` while the decision is open, once); boarding restates the open choice on the notice; cache opens once after defeat (matrix, live) |
| Start rooms | sound | PlayerSpawn inside the room, no enemies, cleared on entry (matrix) |

### Matrix — `TestResults/DepthSettingsDescriptionsNonCombatProof/noncombat_room_matrix.csv`

`NonCombatRoomMatrixTests` instantiates every non-combat room **prefab** of the shipped pool, composes it with the real
composer and the shipped services (loot catalog, event config, prices, merchant config, a fake boss body), and drives
the end-to-end contract per row: composed without skips · object present with its final world art · prompt text ·
refused press for a poor player (with reason) · the press reaching the event · outcome / reward / cost exactly once ·
used state (no prompt, no second outcome, dimmed, resolved) · revisit restores it · room cleared and doors open.
**54 rows (30 rooms; the 6 Event rooms under all 5 kinds, seeds found per kind), 54 PASS, 0 NOT IMPLEMENTED.**
`EventSeedScanTests` sweeps seeds 1–80 × depths 1–2 through the real generation pipeline across all three biomes
and lists every special room and seeded event kind (`event_seed_scan.txt`); it picked the proof seed 53 (Metro: Loot,
Broken Machine, Cursed Chest) and the smoke seed 79 (Labs: Medical, Loot, Locked Vault, Broken Machine).

## 6. Built-player proof — `TestResults/DepthSettingsDescriptionsNonCombatProof/`

| # (prompt §8) | Shipped player, windowed, seed 79 (`smoke_windowed/`) | Live run in the Editor, seed 53 (`live_*`, 640×360 + `_x2`) |
|---|---|---|
| 1 damaged at the end of a depth | `dsnc_01_damaged_end_of_depth.png` (62/120) | `live_01_damaged_end_of_depth.png` (55/120) |
| 2 next depth at full effective HP | `dsnc_02_next_depth_full_hp.png` (120/120) | `live_02_next_depth_full_hp.png` (120/120) |
| 3 consumable description | `dsnc_03_consumable_description.png` | `live_03_consumable_description_bandage.png` |
| 4 weapon description (+ Legendary paged) | `dsnc_04_weapon_description.png`, `dsnc_04b_legendary_weapon_description_paged.png` | `live_04_weapon_description_p9.png` |
| 5 armor description | `dsnc_05_armor_description.png` | `live_05_armor_description_scrap_vest.png` |
| 6 accessory description | `dsnc_06_accessory_description.png` | `live_06_accessory_description_field_scope.png` |
| 7 Settings categories | `dsnc_07_settings_categories.png` (Pause) | `live_07_settings_categories_main_menu.png` (Main Menu) |
| 8 Video page | `dsnc_08_settings_video.png` | `live_08_settings_video_page.png` |
| 9 Audio page (4 sliders + mute) | `dsnc_09_settings_audio.png` | `live_09_settings_audio_page.png` |
| 10 Controls page (+ Gameplay) | `dsnc_10_settings_controls.png`, `dsnc_10b_settings_gameplay.png` | `live_10_settings_controls_page.png`, `live_10b_settings_gameplay_page.png` |
| 11 combat room enemy count | `dsnc_11_combat_room_enemies_remaining.png` | `live_11_combat_room_enemies_remaining.png` (x6) |
| 12 decremented | `dsnc_12_combat_room_enemies_decremented.png` | `live_12_combat_room_enemies_decremented.png` (x5) |
| 13 Boss room, no chip | `dsnc_13_boss_room_no_enemy_hud.png` | `live_13_boss_room_no_enemy_hud.png` |
| 14 non-combat room, no chip | `dsnc_14_noncombat_room_no_enemy_hud.png` | `live_14_noncombat_room_no_enemy_hud.png` |
| 15 Broken Machine prompt | `dsnc_15_broken_machine_prompt_unaffordable.png`, `dsnc_15_broken_machine_prompt.png` | `live_15a_…_unaffordable.png`, `live_15_broken_machine_prompt.png` |
| 16 Broken Machine outcome | `dsnc_16_broken_machine_outcome.png` (`MACHINE REPAIRED: HEAVY AMMO X12`) | `live_16_broken_machine_outcome.png` (`MACHINE REPAIRED: BREACHER-12`) |
| 17+ other mechanics | `dsnc_17_medical_station_healed.png`, `dsnc_17_locked_vault_unlocked.png`, `dsnc_17_loot_chest_opened.png` | `live_17_cursed_chest_wave.png`, `live_17_cursed_chest_cleared_loot.png`, `live_17_loot_chest_opened.png`; Boss Cache + `BOARD TRANSIT` in the evidence file |

Also there: `live_depth_settings_descriptions_noncombat_evidence.txt` (every HP figure, heal count, prompt, coin
change, notice and description line), `noncombat_room_matrix.csv`, `event_seed_scan.txt`, and in `TestResults/`
`smoke_mac_headless/smoke_result.json`, `smoke_mac_windowed/smoke_result.json`, `item_descriptions.md`,
`build_report_macos.md`, `build_macos.log`. Weapon Cache selection and Merchant trade captures exist from the two
previous passes (`RoomHudAudioQolProof/qol_*`, `EconomyContainmentDeathProjectileProof/ecdp_01/02`) and were not
re-shot; the Supply Signal wave is exercised by the matrix and the smoke stage but did not generate on either proof
seed's play path, so it has no capture (limitation §12).

## 7. Smoke

`SmokeRunner.DepthSettingsNonCombat.cs` (65 checks, listed in `smoke_result.json` → `DepthSettingsNonCombatChecks`):
every catalog item resolves a complete description; the inventory shows the description first for a consumable,
weapon, armor and accessory and pages a Legendary weapon's rows; Settings from the pause menu — categories, each
page, a slider stepped and previewed, persisted on Back, Back → categories → pause root, gameplay input held/released;
the enemy chip in a real combat room (x6 → x5 → gone), absent in an event room and the Boss arena; **every non-combat
room the depth generated** driven through prompt → refused-when-poor → press → outcome → used state (seed 79:
Medical Station, Loot chest, Locked Vault, Broken Machine); boss defeat → transit → DESCEND → depth 2 at 120/120
exactly once, revisits never healing; the run then Returns, and the starter-fallback / death / leave stages of the
previous passes run unchanged. Any logged exception or error fails the smoke; both runs report none.

## 8. Tests

**New — EditMode (23).** `ItemDescriptionTests` (10), `SettingsPagesTests` (12: root/pages, four sliders independent
+ preview + persist + explicit 0, discard restores the mix, VSync/frame-rate persist, display apply/KEEP/timeout/
REVERT/Back, unapplied display edit dropped, CONTROLS scheme/rebind/reset, GAMEPLAY toggles, left/right routing,
pause back-stack, document round-trip incl. old documents), `EventSeedScanTests` (1).

**New — PlayMode (26).** `DepthArrivalHealTests` (10: 100 → 100/100, 120 → 120/120, heaviest armor, already full,
twice, one Healed event, co-op each own max, Downed/Dead never, client applies nothing, equipment change never heals),
`EnemyRemainingHudTests` (8: planned count / decrement / hide, reinforcements counted, adjacent room excluded, Elite,
Boss arena hidden, non-combat + event waves hidden, replicated client count, view model + chip), `SettingsPanelTests`
(6: pause → categories → pages → back-stack, audio pointer + keyboard, video selectors + apply/revert, controls
scroll/scheme/listen, pixel font + no overlap, Esc never leaks into gameplay), `NonCombatRoomMatrixTests` (1, the
54-row matrix), `DepthSettingsDescriptionsNonCombatProofTests` (1, the live run of §6).

**Updated.** `UiNavigationTests` (category/page ids), `PauseSettingsRebindingTests` (`1280 x 720`),
`AudioRuntimeHardeningTests` (message), `MerchantTradeUiTests`, `GraphicalInventoryTests`,
`GraphicalInventoryProofTests` (description leads, comparison reached by paging), `FinalProductionValidatorTests`
(10 sections), `SmokeRunner.Inventory` (paged comparison).

## 9. Files

**Runtime — new:** `Player/DepthArrivalHeal.cs`, `Items/ItemDescriptions.cs`, `Stats/StatLabels.cs`,
`Events/EventOutcomeText.cs`, `UI/Inventory/ItemDetailLayout.cs`, `App/SmokeRunner.DepthSettingsNonCombat.cs`.
**Runtime — changed:** `App/ExpeditionScene.cs`, `App/GameApp.cs`, `App/SettingsPanel.cs` (rewritten),
`App/MainMenuScreen.cs`, `App/PauseMenuScreen.cs`, `App/UiKit.cs`, `App/SmokeRunner.cs`, `App/SmokeRunner.Inventory.cs`,
`UI/Settings/SettingsViewModel.cs`, `UI/Pause/PauseMenuViewModel.cs`, `UI/Navigation/ScreenNavigation.cs`,
`UI/Navigation/FocusNavigation.cs`, `UI/Inventory/ItemTooltip.cs`, `UI/Inventory/InventoryView.cs`,
`UI/Merchant/MerchantView.cs`, `UI/WeaponCache/WeaponCacheView.cs`, `UI/Theme/ScreenLayout.cs`, `UI/Theme/UiSkin.cs`,
`UI/Hud/DungeonHudView.cs`, `UI/Hud/DungeonHudViewModel.cs`, `UI/Hud/HudInfoViews.cs`, `Dungeon/Runtime/RoomRuntime.cs`,
`Dungeon/Runtime/RoomRuntimeState.cs`, `Events/DungeonEventInteractable.cs`, `Events/EventPrompt.cs`,
`Expedition/TransitCar.cs`, `Items/Passives/EquipmentPassive.cs`, `Items/Armor/ArmorPassives.cs`,
`Items/Accessories/AccessoryPassives.cs`, `Persistence/SettingsData.cs`, `Core/Rendering/AudioLevels.cs`,
`Audio/AudioService.cs`.
**Editor:** `Editor/Production/ItemDescriptionValidator.cs` (new), `Editor/Production/FinalProductionValidator.cs`,
`Editor/Production/CompletionAssetManifest.cs`, `Editor/ArtGen/UiFactory.cs`, `Editor/ArtGen/ArtIntegration.cs`.
**Content:** `Assets/Game/Art/UI/ui_enemy_icon.png` (generated through the pipeline), `Resources/UiSkin.asset`
(enemy icon bound), `production/COMPLETION_ASSET_MANIFEST.md`, `production/asset_provenance.json`.
**Tests:** the eight new files of §8 and the updated ones; `Tests/PlayMode/Game.Tests.PlayMode.asmdef` (+
`Unity.InputSystem`).
**Docs:** `ui/90_UI_UX_OVERVIEW.md`, `ui/91_DUNGEON_HUD.md`, `ui/93_LOOT_TOOLTIPS.md`, `dungeon/57_EVENTS.md`,
`dungeon/60_EXTRACTION_TRANSIT.md`, `art/105_AUDIO_MUSIC.md` (implementation notes dated 2026-09-20).

## 10. Deliberately not changed

Weapon/enemy/economy balance and every price; the run-start fill and the mid-run "resize never heals" invariant; the
Broken Machine's design (paid seeded gamble, no menu); the Weapon Cache and Merchant flows; save/wipe semantics;
inventory capacity; room topology; network authority (the count replicates through the existing room-state path;
the heal goes through the existing authoritative heal); the pinned Editor and package versions; the platform modules.

## 11. Build and smoke

- **Windows x64: NOT RUN.** No Windows Build Support module on this machine; nothing was installed.
  `ReleaseBuildTool.BuildBatch` is unchanged and remains the release entry.
- **macOS verification build** (`ReleaseBuildTool.BuildMacBatch`, same `SceneOrder`, `BuildOptions.None`,
  `Builds/MacOS/RUINRAIL.app`): Succeeded, 0 errors, 2 pre-existing environment warnings.
- **Headless smoke** (seed 79) and **windowed smoke** (seed 79, 1280×720): both `Success: true`, all stages through
  `ReturnToMenuOk`; every §10 requirement checked inside the shipped player — next depth starts at full HP, item
  descriptions display, the Settings category pages work, the enemy count appears only in eligible combat rooms, the
  Broken Machine works, every generated non-combat room of the depth had a functional outcome, 0 exceptions / missing
  scripts / missing references.

## 12. Remaining limitations

1. **Windows x64 build + smokes: NOT RUN** (no Windows Build Support module; a platform gate, not repository-local
   work). Everything above was verified on the macOS player built from the identical scenes and options.
2. The Supply Signal and the Cursed Chest did not both fall on the same proof seed's play path: the Supply Signal is
   proven by the matrix (all six event rooms) and drives correctly in the smoke stage when generated, but neither
   proof run's depths generated one, so it has no screenshot; the Weapon Cache and Merchant captures are those of the
   previous passes.
3. The previous pass's containment smoke check is seed-sensitive: on seed 23 it reported a 0.119-tile contact push at
   an open doorway (tolerance 0.06) before this pass's stage ran; seeds 79/31/11 pass. Not touched (out of scope).
4. Co-op depth heal and the replicated enemy count are verified with host-side rosters/rooms and client-side restored
   state (the same seams the live networking uses); they were not run in a multi-process Multiplayer Play Mode
   session here.
5. The details pager's controller input is the right stick (edge-triggered) and PageDown/PageUp on the keyboard; the
   MORE row names the current device's key.
