# V1 Baseline and Tuning Policy

> **Status:** Project control document.
> **Game language:** English.

The documentation now contains approved **V1 baseline values** for the major balance systems. They are the values Claude should implement first.

## Important Distinction

**Fixed design rule** means the structure should not be changed without an explicit design revision.
Examples:
- Exactly four normal ammo categories.
- Backpack has 8 slots.
- Legendary weapon specials cost only cooldown.
- No crits, weak spots, stamina, durability, classes, or crafting-material economy.

**Approved V1 balance baseline** means the number is the current official starting value, but it must remain data-driven so deliberate playtesting can tune it later.
Examples:
- Medium Ammo stack limit = 120.
- Base Max HP = 100.
- Dash cooldown = 1.4706 seconds (1.25 / 0.85; design update 2026-09-17). Dash distance = 17 u/s × 0.18 s = 3.06 tiles (was 3.6; × 0.85).
- A specific Legendary special cooldown = 12 seconds.

Claude must not silently change approved V1 baselines because it believes another value would be better.

## Configuration Rule

Never scatter balance values across behavior scripts. Put values in the relevant ScriptableObject definition or central balance configuration.

When a playtest-driven tuning task explicitly changes a value, update both the data and the owning design document.
