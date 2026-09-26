# RUINRAIL — FRESH-RUN COMBAT / ECONOMY / TTK VALIDATION

> Measurement pass executed on 2026-09-23 against the working tree, after the Stat Consumer Integrity pass.
> **No shipping balance value was changed.** The only files added are in `Assets/Game/Tests/`; the only other edit is
> one line in the EditMode test asmdef. No destructive git operation was used.
> Machine: macOS (Apple silicon), Unity 6000.3.24f1, the pinned toolchain in `ENVIRONMENT.md`.

## 1. Executive Summary

Old balance conclusions were re-measured from scratch, because affixes and nine weapon stats only started working in
the previous pass. Five things came out of it, in descending order of how much they change the game:

1. **The Depth 1 boss is the ammo wall, not the depth.** A fresh profile arrives at the boss with ~41 Light rounds
   (normal accuracy) against a boss that needs ~103. The rooms are affordable; the boss is not. Every one of 30
   deterministic runs at normal accuracy finished the boss at **exactly zero reserve**, and 30 of 30 fell back to the
   Field Knife at least once.
2. **The ammo stack caps are too small for the boss for three classes.** From a *completely full* reserve at Depth 1:
   a shotgun needs 145–170 % of its Shells cap, a rocket 120–167 % of its Heavy cap, an assault rifle 102–124 % of its
   Medium cap. Battle rifles (51–68 %) and snipers (43–62 %) are comfortable. This is a cap-versus-boss-HP problem, not
   a weapon-damage problem, and it gets worse with depth for every class.
3. **The starter Field Knife out-damages the starter pistol by 52 %** at zero ammo cost (40.7 vs 26.7 sustained DPS).
   Melee is not the emergency fallback the starter kit frames it as — it is the higher-DPS, cheaper option, and the
   only thing paying for it is contact range.
4. **Blasters are not the economic free lunch the earlier review suspected.** They sit 10–20 % *below* the starter
   pistol and 32–40 % below an assault rifle in sustained DPS, and spend 54–60 % of a firing cycle in overheat
   lockout. If anything they overpay for costing no ammo.
5. **The reward curve flattens as the risk curve steepens.** Expected rarity improvement per extra depth decays from
   +14.2 % (D1→D2) to +1.6 % (D20→D21) to **0 %** past D30, while the value at risk grows from 1 291 to 6 185 and
   next-depth enemy HP goes from ×1.08 to ×2.95. Coins do not scale with depth at all
   (`EconomyConfig._coinRewardPercentPerDepth` is 0).

Two incidental facts worth recording: **no regular weapon in the catalogue is strictly dominated** once
Legendary-only weapons are excluded from the comparison (19 distinct roles, 13 heavy overlaps, 1 needing a feel test),
and the Supply Chest's authored Legendary rate is diluted to about a third because only 2 of its 6 eligible equipment
entries have an authored `LegendaryVariant`.

## 2. Current Balance Snapshot

`TestResults/FreshRunBalance/current_balance_snapshot.csv` — all 33 weapons, 22 columns, plus an `Acquisition` column
that separates the 22 **Regular** weapons from the 11 **Legendary-only** ones (a `WeaponDefinition` with a
`LegendaryMechanicId` never drops as a regular item; `EquipmentRollService.IsRegular` is the authoritative rule). The
non-weapon environment those numbers must be read against is in
`current_balance_snapshot_environment.md`: player stats, starter kit, ammo caps, depth curves, every enemy/elite/boss
stat line, prices, ammo bundles, event costs, the rarity table and the Supply Chest planning rate.

The anchors this report leans on most:

| | |
|---|---|
| Player | 100 base HP (+20 from the starter Scrap Vest = 120), 5 tiles/s, dash 17 tiles/s on a 1.47 s cooldown |
| Starter kit | P9 Ranger, Field Knife, Scrap Vest, 1 Bandage, **60 Light ammo**, no accessory |
| Ammo caps | Light 180, Medium 120, Heavy 60, Shells 40 (×1.25 with the Ammo Pouch) |
| Depth 1 threat budget | 4–6 threat per combat room; a depth is ~9–10 rooms, ~6.7 of them combat |
| Enemy HP / damage at D30 | ×2.9 / ×1.75 of D1 |
| Boss HP | 1 000–1 350 base, ×1 at D1 up to ×2.9 at D30 |
| Coin scaling | **flat** — `CoinRewardMultiplier` is ×1.0 at every depth |
| Rarity table | last authored band is Depth 30; `WeightsAt` holds it flat beyond that |

## 3. Fresh Profile Results

`TestResults/FreshRunBalance/fresh_profile_runs.csv` — 90 runs: 30 fixed seeds × 3 skill profiles, 10 seeds per biome,
all listed in `seed_manifest.txt`. Each run walks a real generated Depth 1 in the order a player meets the rooms, with
the real starter loadout.

| | HIGH (92 % acc.) | NORMAL (75 %) | NEW PLAYER (55 %) |
|---|---:|---:|---:|
| Runs | 30 | 30 | 30 |
| Died | 0 | 0 | **29** (all at the boss) |
| Ran dry before the boss | 0 | 0 | 5 |
| Used melee at least once | **26** | **30** | 30 |
| Melee fallback rooms (avg) | 1.0 | 1.4 | 2.5 |
| Rounds used / found / bought | 135 / 101 / 0 | 137 / 101 / 0 | 137 / 74 / 0 |
| Reserve entering the boss | 55.5 | 41.3 | 21.7 |
| Reserve leaving the boss | 1.7 | **0.0** | 0.0 |
| Lowest HP (of 120) | 98 | 40 | 0 |
| Coins earned | 165 | 165 | 58 |
| Items found | 3.2 | 3.2 | 1.2 |
| Run duration | 120 s | 133 s | 157 s |
| Seeds with a merchant | 4 / 30 | 4 / 30 | 4 / 30 |

Reading it: the **rooms** of Depth 1 are comfortably affordable — ~83 rounds against 60 starting plus ~101 found. The
**boss** is not: it needs ~103 rounds and only ~41–55 are left when the player arrives. So the firearm covers roughly
half the boss and the rest is knife work, for every skill level. The boss is also where every death happens.

The merchant appears on 4 of 30 Depth 1 seeds, so buying your way out of this is not available to most first runs.

## 4. Skill-Profile Results

The three profiles are stated assumptions, not shipped data, and they are the only assumptions in this pass
(`seed_manifest.txt` lists them):

| Profile | Accuracy | Pellet connection | Extra reloads | Dodge efficiency |
|---|---:|---:|---:|---:|
| HIGH_ACCURACY | 0.92 | 0.70 | ×0.00 | 0.90 |
| NORMAL_ACCURACY | 0.75 | 0.55 | ×0.15 | 0.70 |
| NEW_PLAYER | 0.55 | 0.40 | ×0.60 | 0.45 |

Two results are worth separating:

- **Ammo consumption barely changes with accuracy** (135 / 137 / 137 rounds). That is not a modelling artefact: a
  worse player does not fire many more rounds, they simply stop being able to fire and switch to the knife earlier —
  melee attacks go 38 → 70 → 138 across the profiles. The starter loadout absorbs bad aim by converting it into melee
  time, not into ammo.
- **"Wasteful reloading" costs time, never rounds.** `RangedWeapon.CompleteReload` draws only the shortfall
  (`Mathf.Max(0, CurrentMagazineSize - MagazineAmmo)`), so reloading a half-full magazine wastes nothing. This is a
  genuine, deliberate property of the implementation and it means the classic new-player ammo leak does not exist here.

**Survival is the most model-sensitive number in this pass** and must not be read as a fact. Damage taken in one
Aegis Core fight, same build, only the profile changing:

| Build | HP | HIGH | NORMAL | NEW |
|---|---:|---:|---:|---:|
| STARTER | 120 | 14.9 | 56.0 | 161.3 |
| MID_RARE | 138 | 9.0 | 35.4 | 96.2 |
| STRONG_EPIC | 170 | 6.8 | 27.1 | 74.6 |

A ~10× spread. The "29 of 30 new players die at the boss" figure therefore depends on the 0.45 dodge-efficiency
assumption and needs the human playtest to confirm or kill. What does **not** depend on it is the ammo arithmetic, and
that alone says the boss cannot be finished with the starter firearm.

## 5. Ammo Economy

`TestResults/FreshRunBalance/ammo_economy_matrix.csv` — every ammo-using weapon × 3 profiles.

Rounds to kill a Depth 1 boss, from a **full** reserve, at normal accuracy:

| Class | Ammo (cap) | Boss cost | % of cap | Rooms from a full reserve |
|---|---|---:|---:|---:|
| Pistol | Light (180) | 96–122 | 53–68 % | 12–15 |
| SMG | Light (180) | 149–191 | 83–106 % | 7.5–9.5 |
| Assault Rifle | Medium (120) | 122–149 | **102–124 %** | 6.3–8.0 |
| Battle Rifle | Medium (120) | 61–81 | 51–68 % | 12–15 |
| Shotgun | Shells (40) | 58–68 | **145–170 %** | 4.4–5.0 |
| Sniper | Heavy (60) | 26–37 | 43–62 % | 12–15 |
| Rocket | Heavy (60) | 72–100 | **120–167 %** | 5.0 |

A depth has ~6.7 combat rooms plus a boss, so a shotgun or rocket cannot cover even the rooms from one full reserve,
let alone the boss. The shotgun figure moves with the pellet-connection assumption (at 100 % connection a Breacher-12
needs 29 shells, which fits in 40), so treat the shotgun conclusion as LIKELY. The rocket figure depends only on
accuracy — at 92 % accuracy a Twin Tube still needs 82 of a 60 cap — so that one is CONFIRMED.

**Rockets against groups** (`ammo_economy_matrix.csv` splash columns, `depth_ttk_matrix.csv` grouped rows): the splash
reaches at most `1 + explosion radius` targets, so against 3 grouped grunts a rocket's effective throughput roughly
triples and its per-round economy becomes competitive. Fired at single targets it is the worst ammo economy in the
game. The weapon is fine; using it on a boss is what is not.

## 6. Weapon Runtime Metrics

`TestResults/FreshRunBalance/weapon_runtime_metrics.csv` — **all 33 weapons × 3 skill profiles = 99 rows**, with burst
DPS, sustained DPS, damage per ammo unit, ammo per second, effective range, projectile travel time to 5/10/max tiles,
magazine duration, downtime ratio, melee/blaster/bow cadence limits, impact values, role tags, and TTK against a grunt,
a brute, a shielded enemy (frontal), an elite and a boss.

No weapon is labelled best or worst. The role tags that came out, across the 33 regular and Legendary-only weapons:
utility/impact 12, low ammo efficiency 10, no-reserve-ammo 6, zero-ammo-cost (melee) 6, contact range 6, safe range 4,
crowd-control cone 3, AoE 3, heat-limited 3, positional 3, close-risk/high-output 3, high ammo efficiency 2.

Representative sustained DPS at normal accuracy, for scale: Field Knife 40.7, Marauder A2 35.6, Recurve Bow 31.7,
Scrap Spear 35.1, P9 Ranger 26.7, Farline 23.2, Pulse Carbine 22.1, Breacher-12 13.1, Pipe Launcher 12.0. The two
lowest are the shotgun and the rocket — both of which trade DPS for impact and area, which the impact and splash
columns quantify separately.

**Every number above is verified against the running game.** `TestResults/FreshRunBalance/runtime_verification.csv`
(22 checks, all MATCH) fires the real weapon components at real `HealthComponent`s and compares shots-to-kill,
effective magazine, reload time, cadence, blaster heat cycle, melee wind-up/recovery and depth-scaled enemy HP against
the model — including a wall-clock reload measurement (model 1.90 s, measured 1.74 s) and the Epic affix combination
(model and runtime both 34 rounds, 7.085 shots/s, 1.667 s reload).

## 7. Rarity / Affix Impact

`TestResults/FreshRunBalance/rarity_affix_effect.csv` — 9 weapons, one per relevant class, at all five rarities, using
documented affix combinations that deliberately are **not** damage-only.

Now that affixes actually apply, rarity is worth between nothing and +26 % sustained DPS, and the spread is uneven in
a way the 100/135/190/**300**/500 % price curve does not reflect:

| Weapon | Uncommon | Rare | Epic | Legendary |
|---|---:|---:|---:|---:|
| Field Knife | +9.0 % | +9.0 % | +19.9 % | +19.9 % |
| Recurve Bow | +15.0 % | +15.0 % | +15.0 % | +26.5 % |
| P9 Ranger | +6.0 % | +11.3 % | +15.6 % | +22.4 % |
| Marauder A2 | +6.3 % | +10.3 % | +10.3 % | +24.2 % |
| Scrap Spear | **0.0 %** | +9.0 % | +9.0 % | +19.9 % |
| Farline | **0.0 %** | +4.2 % | +4.2 % | +14.7 % |
| Pulse Carbine | +3.9 % | +3.9 % | +3.9 % | +14.3 % |
| Pipe Launcher | +7.4 % | +7.4 % | +7.4 % | +18.1 % |
| Breacher-12 | **0.0 %** | **0.0 %** | +5.5 % | +10.0 % |

The pattern is mechanical, not arbitrary: affixes that touch the damage loop (Fire Rate, Melee Attack Speed, Bow
Charge Speed, Magazine Size, Damage) move DPS; Range, Projectile Speed, Reload, Knockback and Stagger Power move other
axes or nothing measurable. So an impact-class weapon buys **utility** with rarity while a cadence-class weapon buys
**power**, and both pay the same 3× price at Epic.

Answering the question the phase exists for: **yes, a higher-rarity version changes how the weapon performs — but by
5–25 %, and on some weapons the change is invisible in combat throughput.** Range does grow visibly (+15 % on a
single roll), and magazines grow visibly (12→14, 28→34, 32→38, 6→7).

## 8. Melee Economics

`TestResults/FreshRunBalance/melee_economy.csv` — all six melee weapons × 3 profiles.

| Weapon | Sustained DPS | vs starter pistol | Reach | Arc | Knockback | Stagger | Grunt TTK | Boss TTK | Damage taken per boss kill |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Ghostedge ★ | 47.0 | **+76 %** | 1.2 | 80° | 0 | 3 | 0.79 s | 28.8 s | 37.5 |
| Ripper Knife | 43.1 | +61 % | 1.0 | 70° | 0 | 3 | 0.80 s | 31.3 s | 40.9 |
| Field Knife | 40.7 | **+52 %** | 1.2 | 80° | 0 | 3 | 0.86 s | 33.6 s | 43.8 |
| Guard Lance ★ | 36.3 | +36 % | 3.0 | 20° | 8 | 10 | 0.91 s | 37.4 s | 48.8 |
| Railspike ★ | 35.7 | +34 % | 3.0 | 20° | 8 | 10 | 1.18 s | 38.1 s | 49.7 |
| Scrap Spear | 35.1 | +31 % | 2.6 | 20° | 8 | 10 | 1.11 s | 39.0 s | 50.9 |

Answers to the phase's questions:

- **Does melee outperform ammo weapons too broadly?** Against the *starter* firearm, yes, decisively. Against a
  mid-tier AR (35.6 DPS) the Field Knife is level and the knives are ahead. The knife only loses to battle rifles and
  snipers on a damage-per-second basis, and it never loses on economy.
- **How much risk compensates for it?** By the model, 1.2–1.8 HP per grunt kill and 37–51 HP per Depth 1 boss kill.
  The boss figure is real pressure against 120 HP; the grunt figure is not. Note this is the exposure-model number and
  the most model-sensitive thing in the table.
- **Does melee become the rational default when ammo is scarce?** It already is the rational default at Depth 1 for a
  fresh profile, ammo scarcity aside — because it is both stronger and free.
- **Is the starter Field Knife a backup or a DPS upgrade?** Measurably a DPS upgrade over the pistol it ships beside.
- **Does knockback/stagger improve spear safety?** It gives spears something knives do not have: 8 knockback × 0.25 =
  a 2-tile shove, plus stagger power exactly at `StaggerConfig.HighStaggerPower` (10), which staggers an unresisted
  enemy in one hit. The spears pay for it with 20° arcs and lower DPS. Whether the trade feels good is a feel question.

One conservatism worth stating: the harness applies the *same* accuracy to a melee swing as to a ranged shot, which
almost certainly understates melee. The advantage above is therefore a floor, not a ceiling.

## 9. Blaster Economics

`TestResults/FreshRunBalance/blaster_economy.csv`.

| Blaster | Heat-limited DPS | vs pistol | vs AR | Shots per cycle | Time to overheat | Lockout | Downtime | Reserve rounds saved per depth |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Redline ★ | 24.1 | −9.9 % | −32.3 % | 13 | 1.44 s | 2.2 s | 60.4 % | ~700 |
| Pulse Carbine B1 | 22.1 | −17.3 % | −37.9 % | 15 | 1.88 s | 2.2 s | 54.0 % | ~643 |
| Arc Blaster B4 | 21.3 | −20.1 % | −40.0 % | 10 | 1.67 s | 2.2 s | 56.9 % | ~621 |

**"No ammo cost" is currently over-paid for, not under-paid for.** A blaster fires for 1.4–1.9 seconds and then sits
in a 2.2-second lockout, spending the majority of any sustained engagement unable to shoot, and lands 10–20 % below
the *free starter pistol* while doing it. The saving it buys is real — several hundred reserve rounds across a depth —
but the classes it competes with (battle rifle, sniper) were already comfortable on ammo, so the saving is buying
relief from a problem those classes do not have.

This is the one place where the earlier review's suspicion is **contradicted** by measurement.

## 10. Enemy TTK by Depth

`TestResults/FreshRunBalance/depth_ttk_matrix.csv` — D1/D5/D10/D20/D30 × 3 builds × 3 profiles × grunt, shooter,
charger, brute, sniper, summoner, a representative elite and boss, plus the Shield Enemy frontal-vs-flanked pair and
a rocket measured single-target and against three grouped targets.

Normal accuracy, seconds and reserve rounds per kill:

| Target | Build | D1 | D5 | D10 | D20 | D30 |
|---|---|---|---|---|---|---|
| Grunt | STARTER | 1.00 s / 4 rd | 1.25 s / 5 | 1.50 s / 6 | 2.00 s / 8 | 2.25 s / 9 |
| Grunt | STRONG_EPIC | 0.46 s / 4 | 0.57 s / 5 | 0.69 s / 6 | 0.92 s / 8 | 1.15 s / 10 |
| Brute | STARTER | 2.50 s / 10 | 4.63 s / 13 | 5.38 s / 16 | 6.88 s / 22 | 9.51 s / 27 |
| Brute | STRONG_EPIC | 1.15 s / 10 | 1.61 s / 14 | 1.95 s / 17 | 2.75 s / 24 | 3.33 s / 29 |

**The Shield Enemy is the one archetype with a real counterplay axis**: its 80 % frontal shield over a 120° arc turns
55 HP into 275 effective HP from the front, so flanking is a 5× difference. That is a genuine skill expression and it
is working.

1-v-1 survivability at normal accuracy (`SurvivesSolo`): at Depth 1 every build survives every single enemy including
bosses. From Depth 10 the **Scrap King** alone exceeds a starter build's HP pool; by Depth 20 it also exceeds a
mid-Rare build's. A strong Epic build survives everything at every measured depth.

## 11. Boss Duration

`TestResults/FreshRunBalance/boss_duration_matrix.csv` — all six bosses × 5 depths × 3 builds × 3 profiles = 270 rows.

Depth 1, starter build, normal accuracy:

| Boss | Scaled HP | Rounds needed | Practical fight | Time spent dealing damage |
|---|---:|---:|---:|---:|
| Scrap King | 1 000 | 103 | 49.7 s | 51.8 % |
| Aegis Core | 1 050 | 108 | 51.4 s | 52.6 % |
| The Conductor | 1 050 | 108 | 51.4 s | 52.6 % |
| Tunnel Maw | 1 150 | 118 | 56.6 s | 52.1 % |
| Subject Omega | 1 250 | 129 | 62.2 s | 51.9 % |
| The Foundry Titan | 1 350 | 139 | 67.4 s | 51.6 % |

Roughly **half of every boss fight is downtime** — reload plus the 35 % telegraph-dodging the model assumes. Fights
are 50–67 s at Depth 1, which is reasonable.

The attrition flags land at depth. Same boss (Aegis Core), normal accuracy:

| Depth | STARTER | MID_RARE | STRONG_EPIC |
|---|---|---|---|
| D1 | 51 s, 108/180 rd ✔ | 32 s, 116/120 rd ✔ | 25 s, 117/120 rd ✔ |
| D5 | 69 s, 143/180 ✔ *watch* | 42 s, **153/120 ✗** | 33 s, **154/120 ✗** |
| D10 | 90 s, **184/180 ✗** *watch* | 56 s, **197/120 ✗** | 42 s, **199/120 ✗** |
| D20 | **125 s, 254/180 ✗ LONG_ATTRITION** | 75 s, 272/120 ✗ *watch* | 58 s, 275/120 ✗ |
| D30 | **154 s, 313/180 ✗ LONG_ATTRITION** | 92 s, 336/120 ✗ *watch* | 73 s, 339/120 ✗ |

Flagged, not fixed: **from Depth 5 no Medium-ammo build can complete a boss from a full reserve, and from Depth 10 no
build of any ammo type can.** Every deep boss is finished partly with the ammo-free secondary by construction. The
starter build's D20/D30 fights (125 s and 154 s of mostly attrition) are the clearest "sponge" cases in the game.

## 12. Depth Pacing

`TestResults/FreshRunBalance/depth_pacing.csv` and `depth_pacing_summary.csv` (3 seeds per point; runs that ended in a
death are excluded from the timings and counted in a `DeathsSampled` column instead, because a truncated run is not a
depth duration).

Normal accuracy, whole-depth seconds:

| Depth | STARTER | MID_RARE | STRONG_EPIC | Enemy HP | Enemy damage | Deaths sampled |
|---|---|---|---|---:|---:|---|
| D1 | 134 s | 112 s | 102 s | ×1.00 | ×1.00 | 0/3, 0/3, 0/3 |
| D5 | 170 s (×1.27) | 145 s (×1.30) | 140 s (×1.37) | ×1.32 | ×1.15 | 1/3, 0/3, 0/3 |
| D10 | 168 s (×1.26) | 211 s (×1.89) | 193 s (×1.89) | ×1.70 | ×1.30 | 3/3, 2/3, 0/3 |
| D20 | — | — | 214 s (×2.10) | ×2.35 | ×1.55 | 3/3, 3/3, 3/3 |
| D30 | — | — | — | ×2.90 | ×1.75 | 3/3, 3/3, 3/3 |

Two honest limitations: **D20 and D30 depth durations could not be measured for any build** because the modelled
player dies before the depth ends, and that death is the model-sensitive number from §4. What *is* measured is seconds
per enemy, which rises 3.43 → 5.35 for the starter build (D1 → D20) and 2.62 → 5.64 for the strong build.

Against damage scaling of ×1.55 at D20, the strong build's length multiplier of ×2.10 trips the
`LONGER_MORE_THAN_DEADLIER` threshold at both D10 and D20. So the answer to "more dangerous, more complex, or just
longer?" is: **measurably longer, proportionally somewhat more dangerous, and not more complex at all** — the enemy
roster, the threat budget shape and the room mix are the same at D30 as at D1. Only numbers change.

## 13. Coin / Merchant Economy

`TestResults/FreshRunBalance/coin_economy.csv` — D1-only extraction, D1→D2, D1→D5 and D1→D10.

| Depth | Coins earned | of which boss cache | Merchant present | Cumulative carried |
|---|---:|---:|---|---:|
| D1 | 131 | 102 | no | 131 |
| D2 | 125 | 122 | no | 256 |
| D3 | 199 | 126 | no | 455 |
| D5 | 48 | 123 | **yes** | 558 |
| D10 | 104 | 102 | **yes** | 0 (extracted) |

Prices for comparison: cheapest weapon **250**, cheapest armor **300**, accessory **300**, Light ammo bundle **60**,
bandage **60**; events at D1 cost 100 (Broken Machine), 150 (Medical heal), 250 (Locked Vault), 500 (Medical revive).

Answers:

- **When does the merchant become meaningfully usable?** Not on Depth 1. A depth yields ~131 coins and ~78 % of that
  comes from the boss cache — i.e. from *after* every fight that ammo would have helped with. You can afford one ammo
  bundle or one bandage. The first weapon becomes affordable around cumulative Depth 2–3, the first armor around D3.
  And the merchant only appears on 4 of 30 Depth 1 seeds and 3 of the 10 depths in the D1→D10 sequence.
- **Are paid event rooms usable early?** The Broken Machine (100) is; the Locked Vault (250) is not until ~D2–D3
  cumulative; the Medical revive (500) is not until ~D5.
- **Is there a reason to keep coins at risk?** Mechanically yes — carrying them is the only way to reach the prices
  above. But see §15: the amounts are small relative to item value, so the coin gamble is a minor part of the decision.
- **Does depth improve spending power?** **No.** `_coinRewardPercentPerDepth` is 0, so a Depth 30 container pays the
  same as a Depth 1 container. Depth 10 earned 104 coins against Depth 1's 131. Spending power grows only by *not
  spending*, which is the opposite of what a per-run merchant is for.

Kill rewards remain indirect: normal enemies drop nothing. Coins come from containers, the boss cache and events only.

## 14. Loot / Rarity Curve

`TestResults/FreshRunBalance/loot_rarity_curve.csv` — **510 deterministic depth generations** (34 seeds × 5 depths ×
3 biomes), plus a separate high-n check that rolls each quality table 20 000 times, because per-cell samples from the
generation sweep cannot distinguish a 0.9 % Epic rate from a 2 % one. Verdicts use a ~3σ binomial band and explicitly
say when a rate is under-sampled rather than pretending to judge it.

What a player actually ends a depth holding (aggregated across every source and biome):

| Depth | n | Common | Uncommon | Rare | Epic | Legendary |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 261 | 37.9 % | 35.6 % | 16.1 % | 10.3 % | 0 % |
| 5 | 321 | 30.8 % | 26.2 % | 30.8 % | 12.2 % | 0 % |
| 10 | 384 | 18.8 % | 32.8 % | 35.2 % | 12.5 % | 0.8 % |
| 20 | 432 | 17.4 % | 30.6 % | 31.9 % | 18.8 % | 1.4 % |
| 30 | 474 | 9.5 % | 19.6 % | 46.2 % | 21.5 % | 3.2 % |

The high-n table checks: `RollRarity` reproduces the authored permille for every quality at every depth, and the
Treasure Chest and Boss Cache **deliver** it (100 % `LegendaryVariant` coverage). No affix-roll failure occurred
anywhere in the sample.

**One real discrepancy.** The Supply Chest delivers a Legendary rate about a third of its authored one — 0.5 % rather
than 1.5 % at Depth 30 — because only **2 of its 6 eligible equipment entries have an authored `LegendaryVariant`**,
and `LootRoller.CreateInstance` turns a Legendary roll on an entry without one into an Epic. This is the shipped rule
working as written, not a bug; the authored rarity table simply is not the delivered rate for that table.

## 15. Descend Value

`TestResults/FreshRunBalance/descend_value_snapshot.csv`.

| After depth | Secured if returning | At risk if descending | Items at risk | Rarity improvement next depth | Next-depth enemy HP |
|---:|---:|---:|---:|---:|---:|
| D1 | 1 291 | 1 291 | 2 | **+14.2 %** | ×1.08 |
| D5 | 2 393 | 2 393 | 4 | +8.4 % | ×1.40 |
| D10 | 4 832 | 4 832 | 8 | +3.6 % | ×1.77 |
| D20 | 6 185 | 6 185 | 10 | +1.6 % | ×2.41 |
| D30 | 6 185 | 6 185 | 10 | **0.0 %** | ×2.95 |

No correct choice is invented here; the shape is the point. **The two curves move in opposite directions.** Early
descents are cheap and improve loot quality noticeably; late descents risk 5× more for almost no quality gain, against
enemies with three times the health. Past Depth 30 the rarity table has no further band, so `WeightsAt` holds it flat
and additional depth buys **nothing** in rarity — only XP (which does keep growing) and the same flat coins.

The full heal on depth arrival also means descending costs no HP, which removes the most natural brake on the decision.

## 16. Weapon Role Overlap

`TestResults/FreshRunBalance/weapon_role_overlap.csv`. Comparisons are same-behaviour, same-ammo-family **and
same-acquisition-tier**: a Legendary-only weapon is supposed to beat its class's regular weapons, so mixing the tiers
would have flagged most of the catalogue as dominated and told us nothing.

| Classification | Count |
|---|---:|
| distinct role | 19 |
| heavy overlap | 13 |
| requires human feel test | 1 |
| **likely dominated** | **0** |

**No regular weapon is strictly dominated.** The single flagged case is the **Compound Bow vs Recurve Bow**: the
Recurve wins full-draw DPS by 20.9 % (27.5 avg over a 0.65 s draw vs 35.0 over 1.0 s), but the Compound lands a 21.4 %
bigger hit per arrow. Since bows cost no ammo, per-shot damage has no economic value — its only value is fewer, larger
commitments, which is exactly a feel question. Classified as such rather than as dominance.

The 13 heavy overlaps are all intra-class pairs sitting within 12 % DPS and 15 % range of each other on the same ammo
type (the three SMGs, the three ARs, the three battle rifles against the AR family, the two rockets, the two bows).
They are sidegrades; whether that is enough differentiation is a design question, not a defect.

## 17. Human Playtest Checklist

`TestResults/FreshRunBalance/HUMAN_PLAYTEST_CHECKLIST.md` — 25 diagnostic observations, each with the model's
prediction attached so a session can confirm or contradict it. Items 1–9 need a fresh profile; 10–25 use any account.
The ones that matter most for the findings above: #1 (did you run dry before the boss), #2–3 (was melee a choice or a
necessity, and did it feel better), #10 (did a Rare/Epic weapon feel different), #14 (did a blaster feel worse than a
rifle), #18–20 (harder or just longer at D10/D20), #22–25 (why you descended, and whether the carried-coin gamble
mattered).

## 18. Confirmed Balance Problems

Strong runtime evidence; each reproducible from the seed manifest.

1. **The Depth 1 boss is unaffordable with the starter firearm.** Needs ~103 Light rounds; ~41–55 are available on
   arrival across 90 runs. Every normal-accuracy run ended the boss at zero reserve. Evidence:
   `fresh_profile_runs.csv`, `boss_duration_matrix.csv`.
2. **Shells (40) and Heavy (60) caps cannot cover a Depth 1 boss for the classes that use them.** Rocket 120–167 % of
   cap, shotgun 145–170 %. Assault rifles are at 102–124 % of the Medium cap. Evidence: `ammo_economy_matrix.csv`.
   (Rocket: accuracy-only, CONFIRMED. Shotgun: also depends on pellet connection, so LIKELY — see §19.)
3. **From Depth 5 onwards a boss costs more than a full reserve for Medium-ammo builds; from Depth 10, for every ammo
   type.** Evidence: `boss_duration_matrix.csv`.
4. **The starter Field Knife out-damages the starter pistol by 52 % at zero ammo cost.** Evidence:
   `melee_economy.csv`, cross-checked against real components in `runtime_verification.csv`.
5. **Blasters sit 10–20 % below the starter pistol and 32–40 % below an AR in sustained DPS, with 54–60 % downtime.**
   Evidence: `blaster_economy.csv`.
6. **Coins do not scale with depth.** `_coinRewardPercentPerDepth = 0`; D10 earned 104 coins against D1's 131.
   Evidence: `coin_economy.csv`, `current_balance_snapshot_environment.md`.
7. **Rarity improvement per extra depth decays to zero and stops entirely past Depth 30**, while value at risk grows
   ~5×. Evidence: `descend_value_snapshot.csv`.
8. **The Supply Chest delivers ~1/3 of its authored Legendary rate** because 4 of its 6 eligible equipment entries
   have no `LegendaryVariant`. Evidence: `loot_rarity_curve.csv` (20 000-roll check plus the coverage measurement).
9. **Depth adds length faster than danger for stronger builds.** ×2.10 duration at D20 against ×1.55 damage; seconds
   per enemy 2.62 → 5.64. Evidence: `depth_pacing_summary.csv`.
10. **Rarity is worth 0 % on three of nine measured weapons at some steps**, while every step costs the same price
    multiplier. Evidence: `rarity_affix_effect.csv`.

## 19. Suspected / Human-Feel Problems

Multiple indicators; a human session should settle them.

1. **A new player may die at the Depth 1 boss almost every time.** 29 of 30 modelled runs, all at the boss — but the
   figure swings ~10× with the dodge-efficiency assumption (§4). Checklist #1, #5, #9.
2. **Shotgun boss economy** depends on the 55 % pellet-connection assumption; at full connection a Breacher-12 fits
   inside its Shells cap. Checklist #11.
3. **Whether melee feeling better than the starter gun is a problem or a feature.** The measurement is unambiguous;
   whether it undermines the gun-forward fantasy is not. Checklist #2, #3.
4. **Whether the blaster's overheat lockout reads as a cost or as the weapon being broken.** Checklist #14.
5. **Compound Bow vs Recurve Bow** — bigger arrow vs faster draw. Checklist #16.
6. **Whether 13 intra-class sidegrades within 12 % of each other feel like choices.** Checklist #17.
7. **Whether deep bosses read as sponges.** The starter build's 125 s and 154 s D20/D30 fights are flagged
   `LONG_ATTRITION` by the harness. Checklist #20.
8. **Whether the depth-arrival full heal makes descending automatic.** Checklist #25.

## 20. Things That Are Actually Fine

- **The rooms of a fresh Depth 1.** ~83 rounds spent against 161 available. No ammo wall in the rooms at any skill level.
- **No regular weapon is strictly dominated** (§16). Earlier suspicions of strict dominance do not survive a
  tier-aware comparison.
- **The reload implementation does not punish early reloading** — only the shortfall is drawn, so a nervous player
  wastes time, never rounds.
- **The Shield Enemy's frontal shield is a real 5× counterplay axis** and works.
- **Snipers and battle rifles are comfortable on ammo** (43–68 % of cap for a D1 boss) and their lower sustained DPS
  buys reach and per-round efficiency. The review's worry about snipers is not a problem.
- **Rockets are fine as area weapons** — their economy becomes competitive at 3 grouped targets. Only single-target
  use is bad, which is a usage question.
- **`RollRarity` implements the authored rarity tables exactly**, at every depth and quality, over 20 000 rolls each,
  and the Treasure Chest and Boss Cache deliver them faithfully.
- **Depth 1 is survivable 1-v-1 against every enemy including bosses** for all three builds.
- **XP does scale with depth** (920 → 3 700 expected per depth from D1 to D30), so progression pressure exists even
  where coins and rarity flatten.

## 21. Recommended Next Balance Changes

Smallest set with the largest effect. **None of these were implemented.**

### CONFIRMED — 1. Make the Depth 1 boss reachable with the starter firearm

- **Area:** starter ammo, Supply Chest ammo quantities, or Depth 1 boss HP.
- **Evidence:** ~103 rounds needed, ~41–55 available, 30/30 runs at zero reserve (`fresh_profile_runs.csv`).
- **Current measured values:** 60 starting Light, ~101 found per depth, boss 1 000–1 350 HP.
- **Why it matters:** the first boss is where every death happens and where the pistol stops being a weapon. A player
  who is told "here is your gun" and finishes the first boss with a knife has been taught the gun does not work.
- **Direction:** close a gap of roughly **50–60 Light rounds** on Depth 1. The data supports that narrow range; it
  does not say which lever (starting ammo, chest quantities, chest count, or boss HP) should absorb it.
- **Side effects:** more starting ammo weakens the Supply Chest as a reward; lower boss HP shortens an already
  reasonable 50–67 s fight.
- **Regression risk:** low for starting ammo; medium for boss HP (it scales into every depth).
- **Human playtest needed: YES** (checklist #1, #5, #6).

### CONFIRMED — 2. Raise the Shells and Heavy stack caps, or the damage per round of the classes that use them

- **Area:** `AmmoBalanceConfig` stack limits (Shells 40, Heavy 60).
- **Evidence:** rocket needs 120–167 % of the Heavy cap for a D1 boss, shotgun 145–170 % of Shells
  (`ammo_economy_matrix.csv`); rockets remain over cap even at 92 % accuracy.
- **Why it matters:** a full reserve should at minimum cover the content of one depth. These two cannot cover the
  combat rooms alone (4.4–5.0 rooms against ~6.7).
- **Direction:** the measured shortfall is roughly **1.5× on Shells and 1.3–1.7× on Heavy**, so a cap increase in that
  band, or the equivalent in damage per round. Do not raise the Medium cap on this evidence — ARs are at 102–124 %,
  which is borderline rather than broken.
- **Side effects:** larger caps weaken the Ammo Pouch's relative value and make ammo pickups less pressing.
- **Regression risk:** low — caps are a single authored number per type and the tests cover capacity behaviour.
- **Human playtest needed: NO** for the arithmetic; **YES** for whether shotguns then feel too safe (checklist #11).

### CONFIRMED — 3. Give coins a depth curve

- **Area:** `EconomyConfig._coinRewardPercentPerDepth` (currently 0, cap 100 %).
- **Evidence:** identical coin income at D1 and D10; ~78 % of a depth's coins come from the boss cache
  (`coin_economy.csv`).
- **Why it matters:** the merchant is the only in-run way to convert risk into power, and its usefulness never
  improves. Descending buys rarity and XP but not spending power.
- **Direction:** the field and its cap already exist for exactly this; the measurement supports a non-zero
  per-depth percentage. The data does not justify a specific figure, but note the cap is currently 100 %, so the
  curve can at most double income as authored.
- **Side effects:** deep runs become richer, which interacts with the extraction gamble (§15).
- **Regression risk:** low — one authored value with an existing cap.
- **Human playtest needed: YES** (checklist #21).

### CONFIRMED — 4. Extend the rarity table past Depth 30, or accept D30 as the ceiling explicitly

- **Area:** `RarityTable_*` bands (last band is Depth 30).
- **Evidence:** `WeightsAt` holds the final band, so rarity improvement past D30 is exactly 0 %
  (`descend_value_snapshot.csv`).
- **Why it matters:** rarity is the main descend incentive, and it stops. Whatever the intended endgame depth is, the
  table currently says it is 30.
- **Direction:** either add bands, or record D30 as the designed ceiling so the flat tail is intentional. This is a
  design decision, not a number the data can pick.
- **Side effects / risk:** low either way.
- **Human playtest needed: NO** (it is a design intent question).

### LIKELY — 5. Decide what the starter pistol is for, given the knife out-damages it by 52 %

- **Area:** starter kit composition, or P9 Ranger damage/cadence, or knife reach.
- **Evidence:** 40.7 vs 26.7 sustained DPS at zero ammo cost, verified against real components
  (`melee_economy.csv`, `runtime_verification.csv`).
- **Why it matters:** it decides whether the intended first-run fantasy is a gun with a melee backup or the reverse.
- **Direction:** the honest options are to narrow the DPS gap, or to lean in and let the knife be the Depth 1 answer
  while the pistol is the safe-range tool. The data cannot choose; it can say the current gap is 52 % and that the
  only cost is contact range.
- **Side effects:** buffing the pistol interacts with recommendation 1; nerfing the knife hits every melee weapon.
- **Regression risk:** medium — melee DPS is shared across six weapons and two pools.
- **Human playtest needed: YES** (checklist #2, #3).

### LIKELY — 6. Reduce blaster overheat downtime, or raise blaster damage

- **Area:** `BlasterWeaponDefinition` overheat lockout (2.2 s) / heat per shot / cooling rate.
- **Evidence:** 54–60 % downtime, 10–20 % below the free starter pistol, 32–40 % below an AR
  (`blaster_economy.csv`).
- **Why it matters:** "no ammo" is meant to be the trade; right now the trade is losing on both axes against the
  weapons blasters are supposed to be an alternative to.
- **Direction:** the measured gap to the starter pistol is 10–20 %; closing that, via lockout or damage, is the narrow
  range the data supports. Do not close the gap to the AR — costing no ammo should still cost something.
- **Side effects:** blasters become a strong pick for ammo-starved depths, which is arguably the intent.
- **Regression risk:** low — three definitions, and the heat cycle is covered by tests.
- **Human playtest needed: YES** (checklist #14).

### LIKELY — 7. Add `LegendaryVariant` references to the Supply Chest's remaining equipment entries

- **Area:** `LootTable_SupplyChest` — 4 of 6 eligible equipment entries have no variant.
- **Evidence:** delivered Legendary 0.5 % against an authored 1.5 % at D30; Treasure and Boss Cache are at 100 %
  coverage and deliver correctly (`loot_rarity_curve.csv`).
- **Why it matters:** the rarity table is the design document for drop quality, and one table silently disagrees with it.
- **Direction:** either author the missing variants or accept the Supply Chest as a lower-Legendary source on purpose
  and note it. No number needs choosing.
- **Regression risk:** low. **Human playtest needed: NO.**

### HUMAN-FEEL ONLY

- Whether deep bosses read as sponges (starter D20/D30 at 125 s and 154 s) — checklist #20.
- Whether 13 intra-class sidegrades within 12 % DPS feel like choices — checklist #17.
- Compound Bow vs Recurve Bow — checklist #16.
- Whether the depth-arrival full heal makes descending automatic — checklist #25.
- Whether shotgun knockback and spear shove pay for their arcs and DPS — checklist #11, #12.

### NO CHANGE RECOMMENDED

- Depth 1 room ammo economy, the reload rule, the Shield Enemy's shield, sniper and battle-rifle ammo economy, rocket
  splash economics, `RollRarity`, the Treasure Chest and Boss Cache tables, Depth 1 1-v-1 survivability, XP scaling,
  and the regular weapon catalogue's role spread. All measured and all sound.

## 22. Changes Explicitly NOT Recommended

- **Do not broadly rebalance the 33 weapons.** No regular weapon is dominated; the catalogue's role spread holds up.
- **Do not raise the Medium ammo cap** on this evidence — 102–124 % of cap for a D1 boss is borderline, not broken,
  and battle rifles share that cap comfortably at 51–68 %.
- **Do not nerf melee across the board** to fix the starter comparison. The gap is against one specific weapon, the
  P9 Ranger, and melee's exposure cost is the most model-sensitive figure in this pass.
- **Do not lower deep-depth enemy HP as a first move.** The measured problem at depth is *duration and ammo*, not
  enemy health per se, and HP is the lever with the widest blast radius.
- **Do not add coin drops to normal enemies** to fix the coin curve. The existing `_coinRewardPercentPerDepth` field
  does the job with one number and no new drop system.
- **Do not act on the "new players die at the boss" figure alone.** It is the one headline number that is genuinely
  model-dependent; recommendation 1 stands on the ammo arithmetic instead.
- **Do not change the pellet count or spread of shotguns** to fix their boss economy. The measurement that drives it
  is the pellet-connection assumption, which needs a human check first.

## 23. Open Design Questions

1. What is the intended maximum depth? The rarity table says 30; the depth curves are authored to 100.
2. Is the starter kit meant to be gun-forward with a melee backup, or is the knife the intended Depth 1 answer?
3. Should a full ammo reserve be able to complete one depth including its boss? Every recommendation about caps
   depends on the answer.
4. Is "finish the boss with the secondary" an intended pressure valve or an accident? It happens in 100 % of measured
   starter runs from Depth 5 onward and in every build from Depth 10.
5. Should the merchant appear more often than 4 of 30 Depth 1 seeds, given it is the only in-run power conversion?
6. Are the three variants inside each weapon class meant to be a tier ladder or sidegrades? They currently share one
   class price.
7. Should the depth-arrival full heal have a cost, given it removes the natural brake on descending?

## 24. Test / Runtime Evidence

| Gate | Result |
|---|---|
| EditMode | **PASS — 927 passed / 928 discovered** (1 skipped: the pre-existing `MultiplayerTerminalTests.LiveSessionsRelay_IntegrationCheck_OrNotRun`, which needs live UGS) |
| PlayMode | **PASS — 784 passed / 784 discovered** |
| Production validators, content validators, item-description validator, save/load validation, `StatConsumerIntegrityValidator` | **PASS** (they run as EditMode tests) |
| macOS release build | **PASS** — `Builds/MacOS/RUINRAIL.app`, 0 errors, 175.6 MB, `TestResults/build_report_macos.md` |
| Windows x64 build | **NOT RUN — module unavailable** |
| Built-player smoke | **PASS** — `TestResults/FreshRunBalance/smoke/smoke_result.json`, stage `done`, 10 rooms, OvergrownLabs, save/reload verified, 21 stat-consumer checks |

**Determinism.** Every simulation derives from the seeds in `seed_manifest.txt`. Nothing in this pass is clock-seeded,
and `Phase2And3_WriteThirtyFreshProfileRunsAcrossEverySkillProfile` re-runs one seed and asserts identical shots fired,
ammo used and coins earned.

**The model is runtime-verified, not just algebra.** `runtime_verification.csv` — 22 comparisons, all MATCH — fires
real weapon components at real `HealthComponent`s and checks shots-to-kill, effective magazine, reload time (including
a wall-clock measurement), cadence, blaster shots-to-overheat, melee wind-up/recovery, the Epic affix combination, and
depth-scaled enemy HP from a real `EnemyController` at D1/D10/D30. A mismatch fails the test suite.

**Flaky tests, reported not hidden.** `CombatAimCollisionProofTests.LiveRun_DirectHit_…` failed on the first full
PlayMode run of this pass ("no snap, no hit: expected 26 but was 21") and passed on the rerun. It draws its run seed
from the clock and is documented as failing about half of full runs on this machine, including on an untouched
baseline. It was not used as evidence for anything here. The built-player smoke also needed 3 attempts: run 1 failed
`noncombat` (a generated depth with no non-combat room) and run 2 failed `combat` (the same clock-seeded aim check).
Both are pre-existing seed sensitivity, unrelated to this pass, and neither touches the measurements above.

## 25. Files Created / Modified

**Created — measurement harness (test assemblies only; none of it can affect a shipped value)**
- `Assets/Game/Tests/PlayMode/FreshRunBalanceHarness.cs` — `WeaponProfile` / `EnemyProfile` / `SkillProfile` /
  `ExposureModel`, computing through the shipped `WeaponStatMath`, `PlayerStats` and `DepthScaling`
- `Assets/Game/Tests/EditMode/FreshRunBalanceRunHarness.cs` — `FreshRunSimulator`: a whole deterministic depth on the
  real generator, Supply Chest planner, encounter director, elite/boss selectors, loot roller and merchant bundle
- `Assets/Game/Tests/EditMode/FreshRunBalanceWeaponTests.cs` — Phases 1, 4, 5
- `Assets/Game/Tests/EditMode/FreshRunBalanceRunTests.cs` — Phases 2, 3, 9, 17
- `Assets/Game/Tests/EditMode/FreshRunBalanceEconomyTests.cs` — Phases 6, 7, 8, 10
- `Assets/Game/Tests/EditMode/FreshRunBalanceCurveTests.cs` — Phases 11, 12, 13, 14, 15, 16
- `Assets/Game/Tests/PlayMode/FreshRunBalanceRuntimeTests.cs` — the runtime verification of the model

**Modified**
- `Assets/Game/Tests/EditMode/Game.Tests.EditMode.asmdef` — one reference added (`Game.Tests.PlayMode`) so the EditMode
  harness and the PlayMode verification measure through the *same* model rather than two copies of it
- `Assets/Game/Resources/GameContentCatalog.asset` — `VfxSprites` list appended. This is not a balance value and was
  not edited by hand: an existing EditMode test rebuilds the content catalog, which re-collects sprite references.

**No file under `Assets/Game/Scripts/` and no ScriptableObject balance asset was modified by this pass.**

**Artefacts**
- `TestResults/FreshRunBalance/current_balance_snapshot.csv` + `current_balance_snapshot_environment.md`
- `TestResults/FreshRunBalance/fresh_profile_runs.csv` (90 runs)
- `TestResults/FreshRunBalance/ammo_economy_matrix.csv`
- `TestResults/FreshRunBalance/weapon_runtime_metrics.csv` (33 weapons × 3 profiles)
- `TestResults/FreshRunBalance/rarity_affix_effect.csv`
- `TestResults/FreshRunBalance/melee_economy.csv`
- `TestResults/FreshRunBalance/blaster_economy.csv`
- `TestResults/FreshRunBalance/depth_ttk_matrix.csv`
- `TestResults/FreshRunBalance/boss_duration_matrix.csv` (6 bosses × 5 depths × 3 builds × 3 profiles)
- `TestResults/FreshRunBalance/depth_pacing.csv` + `depth_pacing_summary.csv`
- `TestResults/FreshRunBalance/coin_economy.csv`
- `TestResults/FreshRunBalance/loot_rarity_curve.csv` (510 generations + 20 000-roll table checks)
- `TestResults/FreshRunBalance/descend_value_snapshot.csv`
- `TestResults/FreshRunBalance/weapon_role_overlap.csv`
- `TestResults/FreshRunBalance/HUMAN_PLAYTEST_CHECKLIST.md`
- `TestResults/FreshRunBalance/seed_manifest.txt`
- `TestResults/FreshRunBalance/runtime_verification.csv`
- `TestResults/FreshRunBalance/smoke/` (3 logs, `smoke_result.json`, `dungeon.png`)
- `TestResults/build_report_macos.md`

## 26. Final Status

All fifteen success conditions are met:

1. Post-stat-fix weapon and economy data re-measured from scratch — ✔ (§2, §5, §6)
2. 30 deterministic fresh-profile Depth 1 runs — ✔ 30 seeds × 3 profiles = 90 runs (§3)
3. Normal and lower-skill ammo scenarios included — ✔ three profiles throughout (§4)
4. All 33 weapons have runtime metric coverage — ✔ 99 rows (§6)
5. Rarity/affix value measured after the affix fix — ✔ 9 weapons × 5 rarities (§7)
6. Melee and blaster economics explicitly evaluated — ✔ (§8, §9)
7. Enemy TTK for D1/D5/D10/D20/D30 — ✔ (§10)
8. All six bosses across the depth ladder — ✔ 270 rows (§11)
9. Coin/merchant/event affordability measured — ✔ (§13)
10. Loot rarity curve from a large deterministic sample — ✔ 510 generations + 20 000-roll checks (§14)
11. Descend risk/reward quantified without changing it — ✔ (§15)
12. Role overlap re-evaluated with current mechanics — ✔ tier-aware; 0 dominated (§16)
13. Focused human playtest checklist — ✔ 25 diagnostics (§17)
14. No shipping balance value changed — ✔ (§25)
15. Confirmed facts separated from design judgement — ✔ (§18 vs §19, §21 grouped CONFIRMED / LIKELY / HUMAN-FEEL /
    NO CHANGE)

`FRESH_RUN_COMBAT_ECONOMY_BALANCE_VALIDATION_COMPLETE`
