# Approved Weapon Classes

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.

## Non-Negotiable Rules

- Do not add Revolver, LMG, Axe, Hammer, Sword, Grenade Launcher, or other classes without a design change.
There are exactly **11 approved base weapon classes** for the planned MVP.

| Class | Resource | Identity |
|---|---|---|
| Pistol | Light Ammo | reliable, accurate general-purpose sidearm |
| SMG | Light Ammo | very high fire rate, lower per-shot damage, closer-range spread |
| Assault Rifle | Medium Ammo | balanced all-round automatic weapon |
| Battle Rifle | Medium Ammo | slower, harder-hitting, accurate rifle |
| Shotgun | Shells | close-range multi-pellet burst, high knockback/stagger |
| Sniper | Heavy Ammo | very high single-shot damage, very fast projectile, slow fire/reload |
| Bow | none | hold-to-draw charge weapon; charge improves damage/speed/range |
| Rocket Launcher | Heavy Ammo | explosive AoE; may consume multiple Heavy Ammo units per shot |
| Blaster | heat | assault-rifle-like energy weapon with no ammo, controlled by heat/overheat |
| Knife | none | very fast short-range melee, mobile, low stagger |
| Spear | none | longer narrow melee thrust, higher reach/damage, moderate speed |

## Bow

No arrow ammo type. Quick shots deal less damage; full draw deals significantly more. Exact curve is tunable.

A release short of full draw is followed by a recovery before the next draw can begin, proportional to the draw time skipped and the damage the shot still delivered. No draw length — including rapid quick-shot tapping — can exceed the full-draw damage rate. Full draws receive no additional recovery. Bow Charge Speed shortens draw time and this recovery in the same proportion.

## Blaster

No ammo. Shooting builds Heat. Below max heat, cooling begins after a short delay. At 100% the weapon enters Overheated state and cannot fire until the overheat lockout/cooling rule completes. A holstered blaster continues cooling in the background, allowing weapon-switch gameplay.

## Rocket Launcher

No separate rocket-ammo inventory category. It uses Heavy Ammo and may consume more than 1 unit per shot to balance its power.

## Concrete V1 Weapons

Exact named weapons and approved V1 base values are defined in `items/33_WEAPON_CATALOG.md`.
