# V1 Weapon Catalog

> **Status:** Approved V1 balance specification.
> **Game language:** English.

The V1 catalog contains **33 authored weapon definitions**: two regular weapons plus one Legendary-only weapon for each of the 11 approved weapon classes.

Regular weapons can roll Common, Uncommon, Rare, or Epic. The named Legendary weapon in each class is a distinct Legendary-only definition with 3 normal Affixes plus its fixed RMB Special.

`Range` is expressed in tiles. Damage values are whole-number random ranges. Fire Rate is attacks/shots per second.

## Firearms

| Class | Weapon | Damage | Fire Rate | Magazine | Reload | Range | Resource |
|---|---|---:|---:|---:|---:|---:|---|
| Pistol | **P9 Ranger** | 12–14 | 4.0 | 12 | 1.2s | 10 | Light Ammo |
| Pistol | **Kestrel-12** | 10–12 | 5.2 | 15 | 1.4s | 9 | Light Ammo |
| Pistol | **Quickfang** ★ | 13–15 | 4.4 | 12 | 1.2s | 10 | Light Ammo |
| SMG | **Rattler-9** | 6–8 | 10.0 | 32 | 1.6s | 8 | Light Ammo |
| SMG | **Wasp-45** | 8–10 | 8.0 | 26 | 1.7s | 9 | Light Ammo |
| SMG | **Buzzsaw** ★ | 7–9 | 11.0 | 36 | 1.8s | 8 | Light Ammo |
| Assault Rifle | **AR-17** | 8–10 | 7.5 | 30 | 1.8s | 12 | Medium Ammo |
| Assault Rifle | **Marauder A2** | 10–12 | 6.5 | 28 | 1.9s | 13 | Medium Ammo |
| Assault Rifle | **Vanguard** ★ | 9–11 | 8.0 | 30 | 1.8s | 12 | Medium Ammo |
| Battle Rifle | **Sentinel BR** | 18–22 | 3.0 | 12 | 2.0s | 15 | Medium Ammo |
| Battle Rifle | **Hound BR** | 15–18 | 4.0 | 16 | 2.1s | 14 | Medium Ammo |
| Battle Rifle | **Judicator** ★ | 20–24 | 2.8 | 10 | 2.0s | 16 | Medium Ammo |
| Sniper | **Longshot S1** | 45–52 | 1.0 | 5 | 2.4s | 20 | Heavy Ammo |
| Sniper | **Needle M7** | 34–40 | 1.5 | 7 | 2.2s | 18 | Heavy Ammo |
| Sniper | **Farline** ★ | 48–55 | 0.9 | 5 | 2.4s | 21 | Heavy Ammo |
| Rocket Launcher | **Pipe Launcher** | 65–80 | 0.50 | 1 | 2.2s | 14 | 4 Heavy Ammo/shot |
| Rocket Launcher | **Twin-Tube** | 50–60 | 0.65 | 2 | 2.8s | 13 | 4 Heavy Ammo/shot |
| Rocket Launcher | **Sunbreaker** ★ | 70–85 | 0.50 | 1 | 2.3s | 14 | 4 Heavy Ammo/shot |

## Shotguns

Damage is **per pellet**.

| Weapon | Pellets | Pellet Damage | Fire Rate | Magazine | Reload | Range |
|---|---:|---:|---:|---:|---:|---:|
| **Breacher-12** | 6 | 5–7 | 1.4 | 6 | 2.2s | 6 |
| **Scatter-8** | 8 | 4–5 | 1.7 | 8 | 2.4s | 5 |
| **Crowdbreaker** ★ | 7 | 5–7 | 1.5 | 6 | 2.2s | 6 |

All Shotguns use Shells.

## Projectile Speed Baselines

| Class | Projectile speed |
|---|---:|
| Pistol | 20 tiles/s |
| SMG | 18 tiles/s |
| Assault Rifle | 22 tiles/s |
| Battle Rifle | 26 tiles/s |
| Shotgun pellets | 16 tiles/s |
| Sniper | 34 tiles/s |
| Rocket Launcher | 10 tiles/s |
| Blaster | 22 tiles/s |

## Bow

Bows require no ammo. Holding Fire charges the shot; full draw uses the listed Full Draw Damage.

| Weapon | Quick Damage | Full Draw Damage | Full Charge Time |
|---|---:|---:|---:|
| **Recurve Bow** | 10–12 | 25–30 | 0.65s |
| **Compound Bow** | 12–15 | 32–38 | 1.0s |
| **Stormstring** ★ | 12–15 | 30–36 | 0.8s |

## Blaster

Blasters require no ammo. Max Heat = **100**. Cooling Delay = **0.6s**. Overheat Lockout = **2.2s**.

| Weapon | Damage | Fire Rate | Heat/Shot | Cooling Rate |
|---|---:|---:|---:|---:|
| **Pulse Carbine B1** | 7–9 | 8.0 | 7 | 35/s |
| **Arc Blaster B4** | 10–12 | 6.0 | 10 | 35/s |
| **Redline** ★ | 8–10 | 9.0 | 8 | 40/s |

A holstered Blaster continues cooling.

## Melee

| Class | Weapon | Damage | Attack Rate | Range | Attack Arc |
|---|---|---:|---:|---:|---:|
| Knife | **Field Knife** | 14–17 | 3.5 | 1.2 | 80° |
| Knife | **Ripper Knife** | 10–13 | 5.0 | 1.0 | 70° |
| Knife | **Ghostedge** ★ | 15–18 | 3.8 | 1.2 | 80° |
| Spear | **Scrap Spear** | 24–28 | 1.8 | 2.6 | 20° |
| Spear | **Guard Lance** | 20–24 | 2.2 | 3.0 | 20° |
| Spear | **Railspike** ★ | 26–30 | 1.7 | 3.0 | 20° |

Melee consumes no Stamina and no ammo.
