# Animation Rules

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Sprite animation may run around 8–12 FPS while gameplay simulation remains smooth/high-frame-rate.

## Player Baseline Animations
- Idle.
- Walk.
- Dash.
- Downed.
- Revive/Get Up.
- Death.

Weapons are separate sprites on a WeaponPivot and can provide much of firearm animation through rotation, recoil, muzzle flash, and effects. Do not require unique full-body reload animations for every gun.

Melee requires visually accurate attack motion. The visible attack arc/reach must closely match the actual hitbox.

Enemy telegraph animations are more important than realism. A dangerous attack must clearly show preparation before damage.
