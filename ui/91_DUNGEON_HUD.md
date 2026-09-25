# Dungeon HUD

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Bottom Left / Player State
- HP bar and current/max HP number.
- Temporary shield presentation only when an item/effect grants a shield.
- Small status-effect icons/timers.
- Small dash cooldown/readiness indicator, drawn as an icon slot, never as a permanent text line (implementation note 2026-09-18). The cooldown is a continuous vertical wipe over the icon driven by the authoritative `CooldownRemaining / CurrentDashCooldown`: the grey coverage drains downward as the dash recharges and is completely gone the instant it is ready, with a one-shot amber flash on completion (implementation note 2026-09-18, second pass — the previous overlay had no sprite and uGUI therefore drew it as a static grey block).
- Low-health danger vignette: at or below 30 % of *effective* max HP (`CurrentHP / EffectiveMaxHP`, tunable) a red frame appears around the screen edge and pulses at ~1 Hz. The play area stays clear, the opacity band widens as HP approaches 0, it never survives a scene change, death or revive, and it stands down while any overlay window owns the screen (implementation note 2026-09-18).

## Weapons
Display Primary and Secondary together, active weapon emphasized.
- Each slot is a graphical icon slot with its number (1 / 2), the item icon and rarity frame; the active slot carries the amber brackets (implementation note 2026-09-18). Melee shows its icon only — no ammo line.
- Guns: Magazine / Reserve ammo.
- Bow: draw/charge presentation while drawing.
- Blaster: Heat bar and `OVERHEATED` state.
- Legendary active weapon: RMB special name/icon and cooldown overlay.

## Active Consumable
Show active stack and quantity only; backpack consumables are not all permanently shown.
The active stack is an icon slot with an `xN` chip; there is no permanent text line, and an empty slot shows the neutral plate (implementation note 2026-09-18).

## Top Information
- **Top-left:** the room-graph minimap of the current depth, with the biome name directly under it and the depth as a compact chip inside the map frame. There is no permanent objective/depth text block (implementation note 2026-09-18). The map shows visited rooms, the rooms adjacent to a visited room as discovered outlines, the real door connections between them, and the current room highlighted; a special room shows its symbol only once the player has entered it, so the generated dungeon is never revealed up front.
- **Top-centre:** a brief room-title reveal when the player genuinely enters a new room interior - the room name plus, for a special room, its role line. Fade in, hold, fade out in about two seconds; one reveal per entry, never repeated while the player stays in the room. The boss bar sits above it.
- **Top-right:** Carried Coins as the coin token plus the number (no "COINS" word).
- Depth/objective information is contextual, not permanent: the depth objective rides the room-title reveal of the Start room when a depth is entered.
- Co-op teammate names, HP/life state, Downed timer/Dead state, under the biome line.

## Enemies
Normal-enemy HP bars appear after taking damage and fade later. Elites and Bosses get prominent top-screen bars with names.

## Damage Numbers
Small normal damage numbers, no crit styling. Setting allows ON/OFF.

## Enemies Remaining (implementation note 2026-09-20)
- In an active standard combat room (a director encounter or an Elite encounter) the top-right shows, under the coin readout, a compact hostile token plus `xN`: the room encounter's own remaining count (living members plus queued reinforcements; summons once they exist). It updates the moment an encounter enemy dies and disappears when the room clears.
- It is never shown before the encounter activates, in a cleared room, in a Boss arena (the boss bar is the arena's readout), in a Merchant / Treasure / Loot / Weapon Cache / Broken Machine / Transit / Medical / other event room, and never while an event wave (Cursed Chest, Supply Signal) runs in an event room.
- The source is the room runtime's authoritative membership, replicated with the room state; a client shows the host's number and never counts replicas.

## Event Notice (implementation note 2026-09-20)
- One transient line under the tutorial band tells the player what an interaction just did: the reward that dropped (`MACHINE REPAIRED: MEDKIT x1`), a failed gamble (`REPAIR FAILED: THE MACHINE IS DEAD (100 COINS SPENT)`), a started wave, a purchased heal, a boarded transit — and why a press was refused (`BROKEN MACHINE: NOT ENOUGH COINS (100 NEEDED)`). A running Supply Signal holds its countdown on the same line.
