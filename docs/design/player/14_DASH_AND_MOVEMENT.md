# Dash and Movement

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Dash

Initial tunable values:
- Dash duration: ~0.18 s.
- Dash cooldown: ~1.47 s (design update 2026-09-17: the original ~1.25 s recharged ~15 % too fast; rate × 0.85).
- One dash, no charge system.
- No stamina cost.
- A very short invulnerability window occurs during the actual dash.
- The player can immediately resume movement and combat after the dash.

Dash is a universal core movement mechanic, not a class ability.

## Visual and Input Feel

The dash must feel responsive and must not introduce long recovery. Its exact world distance is tuned in the prototype alongside camera scale and player movement speed.

## Equipment Interaction

Accessories/affixes may improve dash cooldown or dash distance where specified. Permanent Mobility does not directly reduce dash cooldown.
