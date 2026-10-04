# Biomes

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
The game ships exactly **4 biomes**: the three MVP biomes plus Cryo Vaults (added 2026-09-29 by explicit design update).

## Ruined Metro
Abandoned subway infrastructure, tunnels, platforms, damaged trains, concrete, tiles, cables, emergency lights. Hazards may include electricity/rail machinery and cramped movement layouts.

## Rustworks
Factory/scrapyard/industrial facility with rusted metal, pipes, presses, furnaces, machinery, explosive environmental props, heavy mechanical atmosphere.

## Overgrown Labs
Abandoned research complex with damaged clean-tech interiors, glass, terminals, bio tanks, overgrowth, roots/vines, and failed experiments. Hazards may include acid/organic danger zones without requiring a large resistance system.

## Cryo Vaults
An abandoned underground industrial preservation and cold-storage facility — not a snow biome. Dark steel, ice blue,
pale cyan and white frost, with sparse amber/red emergency lighting: refrigeration machinery, frozen pipes, cryo
containers, storage racks, condensation, fractured ice and automated systems still partly running. Rooms are storage
grids — container aisles, pod rows, rack blocks — with coolant runs along the service channels. Hazard: **Cryo Coolant
Leak** (floor hazard on the shared hazard framework: 5–7 damage every 0.9 s after a 0.2 s grace, players and enemies,
no stagger; values chosen inside the existing hazards' damage band). Its tile always shows coolant across the real
damage footprint and cycles a frost/pressure build-up into a release surge on top; it has no Freeze, Slow or
temperature mechanic. Elites: Cryo Enforcer, Vault Stalker (45). Bosses: The Warden, Subject Zero (46). Normal enemies
are the shared roster in the biome palette, weighted toward shields and ranged guards (shield 20, sniper 15, shooter
15, swarm 5, others 10).

## Selection
Every new depth randomly selects a biome. Direct repeats are allowed but weighted down: each other biome weighs 40 and
the previous biome 20 (the approved example after Rustworks with three biomes was Metro 40%, Labs 40%, Rustworks 20%).
With four biomes that is 40/40/40/20 of 140 — each other biome 28.6%, a repeat 14.3%. Depth 1 is uniform (25% each).
This weighting is tunable.

All four use the same technical grid/room system. Biomes are content/visual sets, not separate game architectures.

## Room-entry atmosphere (presentation, 2026-09-28)
A player's first entry into a room on a depth (never the depth's start room or a boss arena, never again on re-entry) plays a brief (≤ 2.2 s) pixel flourish in the biome's voice, one of three variants per biome: Metro — signal fault (blinking signal lamps, electrical spits, a cold flicker) / passing train (a headlight streak behind a side wall, ceiling dust) / emergency alarm (red beacons, a chase of floor strip lights); Rustworks — steam vents (floor steam, a spark shower) / furnace flare (a warm pulse, embers rising along a wall) / pressure release (a steam jet from a wall pipe, sparks, gauge lamps); Overgrown Labs, in violet and pale teal — creeping veins (bioluminescent growth crawling in from the walls) / specimen stir (bubbles rising through glowing tanks, a stuttering specimen lamp) / quarantine scan (a scan line sweeping the room, detection brackets flaring where it passes); Cryo Vaults — pressure purge (vents in both side walls blink amber, then purge cold vapour across the floor with glinting ice crystals) / emergency thaw cycle (amber heating lamps and floor heater strips warm up while frost patches melt into drips and wisps of steam) / cryo wake sequence (dormant status lights on the walls and containers switch on one after another, red to amber to cyan). The variant per room comes from the depth seed and the room graph (neighbouring rooms differ wherever the graph allows), so every peer sees the same one. Presentation only (no collider, no light, overlays under 8 % opacity); each peer plays it for its own player.

## Room ambient details (presentation, 2026-09-28)
While a player stands in a room (the Start room from arrival; never a boss arena), three small details chosen from the biome's set by the room seed act now and then at spots near the walls or on the floor: Metro — electrical sparks, a weak blinking signal lamp, a flickering cable run, dust sifting down; Rustworks — a small steam release, embers lifting off a wall, a machinery pulse lamp, a twitching gauge; Labs — tank bubbles over a faint tank glow, a violet growth vein pulsing, drips, drifting motes, a stuttering specimen lamp; Cryo Vaults — condensation puffs off the floor, frost crystals glinting as they settle, a temperature/status readout, a cold condensation drip, a machine indicator shuttling along a wall rail. A few pixels each on the ground-details layer (under hazard footprints, characters, loot, telegraphs and effects), no collider, light or overlay; they slow to a third while the room's fight runs. Only the current room's details exist (replaced on entering another room, removed with the depth).
