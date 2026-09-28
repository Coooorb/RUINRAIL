# Free Starter Kit

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Full carried-loot loss must never softlock a player who has no gear left.

A free basic starter kit is always available when needed:
- **P9 Ranger** (Primary), forced Common with no Affixes and zero resale value.
- **Field Knife** (Secondary), forced Common with no Affixes and zero resale value — the guaranteed ammo-free fallback: it uses the existing Knife melee rules, consumes no ammo resource, and keeps a run playable when every firearm reserve is empty. Its damage is the catalog value; it is never buffed for being the fallback.
- **Scrap Vest**, forced Common with no Affixes and zero resale value.
- **Bandage ×1**.
- **Light Ammo ×60**.
- No guaranteed Accessory.

> **Design update (2026-09-17, explicit owner instruction, LOOT/AMMO/AUDIO runtime fix pass):** the kit now guarantees the ammo-free Secondary. Previously "No guaranteed Secondary Weapon".

> **Design update (2026-09-19, ECONOMY/CONTAINMENT/DEATH/PROJECTILES runtime fix pass, explicit owner instruction):** a player who reaches READY / START EXPEDITION with **no weapon equipped** (nothing equipped, everything stored, or weapon slots emptied by the save validator's quarantine) is not refused: the free Starter Loadout above is equipped through the same kit service before launch (`STARTER LOADOUT EQUIPPED` notice, non-blocking). A loadout that already has a weapon equipped is never touched; the fallback is idempotent, never writes Storage, never runs on scene load and never during a run. The existing first-profile grant and the no-weapon-anywhere rescue grant are unchanged.

> **Design update (2026-09-27, explicit owner instruction):** the kit's **P9 Ranger, Field Knife and Scrap Vest are run-only**. The Return (extraction) transaction leaves them behind — worn or in the backpack — so they never reach the Shelter loadout or Storage and claiming the free kit run after run cannot pile them up; they are listed as left behind in the run summary, not as secured or lost. Only the kit's own pieces are affected (the kit marks them unsellable at grant; looted items of the same definitions carry no mark and extract normally). The kit's **Light Ammo and Bandage are exempt** and return like any carried item. Failure/death/wipe already lose everything carried and are unchanged. Kit pieces already stored in an older save are not deleted; one that is carried on a later run follows this rule when that run returns. Solo and every co-op peer run the same Return transaction.

Starter items are deliberately weak and cannot be exploited for profit. Give them zero resale value or otherwise prevent repeated free-kit selling.
