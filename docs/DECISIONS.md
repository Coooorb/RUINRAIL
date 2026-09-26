# RUINRAIL — Durable Decisions

> Accepted decisions only; not a changelog. The linked spec owns the detail. Changing any of these needs an explicit
> user design update. Core V1 design has **no open decisions**.

## Scope
- **1–3 players, PvE only.** No PvP, dedicated servers, host migration, public matchmaking. (`design/04_SCOPE_AND_NON_GOALS.md`)
- **No classes/abilities.** Build comes from equipment. Also excluded: crits, weak spots, stamina, durability, attachments,
  crafting materials, multiple currencies (Coins only), survival needs, pets, set bonuses/sockets, base building, quests,
  monetization. (`design/04_SCOPE_AND_NON_GOALS.md`)
- **Extraction stakes:** failed extraction loses all carried loot; XP, level, skill points, banked coins, Storage persist. (`design/01_PROJECT_VISION.md`)
- **Original IP only;** reference games inform feel, never assets/names/layouts. (`design/08_REFERENCE_BOUNDARIES.md`)

## Co-op
- **Co-op ships in V1:** host-authoritative NGO over UnityTransport; clients never own shared gameplay state. (`design/multiplayer/82_NETWORK_AUTHORITY.md`)
- Local/built proof joins by direct address; live UGS Sessions/Relay needs a linked Unity project (external configuration).
- Co-op scaling per `design/multiplayer/83_COOP_SCALING.md`; remote members' reactive accessory passives run on the host copy.
- Display names are fixed for the life of a session: the Shelter's CHANGE NAME (Character station) is refused while
  hosting or joined; the next host/join carries the new name. (`design/player/10_PLAYER_PROFILE.md`)

## Items / inventory
- One item = one slot, no grid sizes, **8 backpack slots**, equipped: Primary, Secondary (any classes), Armor, Accessory,
  Active Consumable. No trade UI — drop items for teammates. (`design/items/20_INVENTORY_SYSTEM.md`)
- Exactly 11 V1 weapon classes; exactly four normal ammo categories; Legendary weapon specials cost only cooldown. (`design/items/24`, `26`, `25`)
- Banked Coins may be taken into an expedition: chosen at Transit, moved once by the start transaction into Carried Coins, then subject to the normal carried rules (banked on return, lost on failure). (`design/base/77_ECONOMY.md`)
- Max-HP changes (equipment, attributes, affixes, passives) never heal damage: a full-health player stays full against
  the new maximum, a damaged player keeps their current HP, a lower maximum clamps (never below 1 while alive). One
  equipment change is one change — the old item's modifiers leaving first never costs or grants HP. (`HealthComponent.ResizeMaxHealth`)

## Balance
- Approved V1 baselines stay data-driven and are **frozen** in `../production/FINAL_RELEASE_FROZEN_BASELINE.csv`
  (incl. D1 ammo/Blaster tuning and Field Knife). Never rebalance during unrelated work. (`design/07_TUNABLE_VALUES.md`)
- **Depth scaling D1–D30 accepted:** reward multiplier exactly ×1.00 through D30; bounded continuation after D30; rarity
  capped by the D30+ band; persistent personal deepest-depth record. (`design/dungeon/59_DEPTH_SCALING.md`)
- Seeded boss attack selection, boss anti-kite repositioning, biome encounter weighting (weighting, never exclusion). (`design/combat/46_BOSSES.md`)

## Presentation
- 640×360 reference resolution, pixel-perfect grid rules (`design/art/101_PIXEL_GRID_AND_SCALE.md`); final art per
  `design/art/FINAL_ART_PRODUCTION_SPEC.md`; no builtin LegacyRuntime font on the release path (spec 29.12).
- Rebinding: discrete bindings rebindable; Pause, Aim (pointer / right stick) and gamepad Move are fixed. (`technical/116_INPUT_SYSTEM.md`)

## Toolchain
- Unity `6000.3.24f1` and the seven package pins in `ENVIRONMENT.md`; never changed silently.

## Deliberately deferred (post-V1, not requirements)
More weapons/Legendaries, rooms, bosses/events; cosmetics; achievements; deepest-depth leaderboards; more Shelter NPC
flavour; host migration; public matchmaking; Windows device profiling once hardware is available. Do not build
architecture for these at the MVP's expense.
