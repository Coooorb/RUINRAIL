# Ammo System

> **Status:** Approved V1 design specification.
> **Game language:** English.

## Ammo Types

Exactly four normal ammo categories:
- Light Ammo.
- Medium Ammo.
- Heavy Ammo.
- Shells.

Blaster uses Heat, Bow uses charge, Knife/Spear use no ammo resource.

## Backpack Stack Limits

| Ammo | Max units per backpack slot |
|---|---:|
| Light Ammo | **180** |
| Medium Ammo | **120** |
| Heavy Ammo | **60** |
| Shells | **40** |

The Ammo Pouch Accessory modifies these capacities according to its Intrinsic and the global stat rules.

## Magazines

Guns use magazine + reserve ammo presentation, e.g. `24 / 87`. Individual physical magazines are not inventory items.

Reload consumes reserve ammo into the weapon magazine. There is no magazine-management simulation.

## Rocket Launcher

Rocket Launchers use Heavy Ammo rather than a fifth ammo category. A normal Rocket Launcher shot consumes **4 Heavy Ammo** unless a specific weapon definition explicitly replaces that cost as part of an approved future design revision.

## Ammo-Aware Loot

When the game chooses an ammo category for a normal ammo drop:
- **70%**: choose from ammo types currently useful to at least one equipped team weapon.
- **30%**: choose from any of the four normal ammo types.

If the party currently uses no ammo-consuming weapons, use the unrestricted pool.
