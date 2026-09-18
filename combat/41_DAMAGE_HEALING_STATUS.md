# Damage, Healing, and Status Effects

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Damage

Damage requests flow through common damage/health handling rather than direct HP mutation from every attack script. Armor and other reductions are applied in a central predictable pipeline.

Initial reference enemy damage bands at early depth:
- Weak attacks: ~5–8.
- Normal attacks: ~8–15.
- Heavy attacks: ~15–25.
- Strong telegraphed attacks: ~25–40.

These are tunable baselines.

## Healing

Healing may be used in combat but should carry use-time risk. Bandage = smaller/shorter; Medkit = larger/longer. While using a healing item, the player should not freely fire; movement may be slowed depending on final feel.

## Status Effects

Keep the initial set small:
- Burn: damage over time.
- Shock: supports stagger/interruption pressure.
- Slow: reduces movement speed.
- Bleed: simple damage over time where used.

Do not create a large elemental resistance matrix.
