# Biomes

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
The MVP uses exactly **3 biomes** to control scope.

## Ruined Metro
Abandoned subway infrastructure, tunnels, platforms, damaged trains, concrete, tiles, cables, emergency lights. Hazards may include electricity/rail machinery and cramped movement layouts.

## Rustworks
Factory/scrapyard/industrial facility with rusted metal, pipes, presses, furnaces, machinery, explosive environmental props, heavy mechanical atmosphere.

## Overgrown Labs
Abandoned research complex with damaged clean-tech interiors, glass, terminals, bio tanks, overgrowth, roots/vines, and failed experiments. Hazards may include acid/organic danger zones without requiring a large resistance system.

## Selection
Every new depth randomly selects a biome. Direct repeats are allowed but weighted down. Initial example after Rustworks: Metro 40%, Labs 40%, Rustworks 20%. This weighting is tunable.

All three use the same technical grid/room system. Biomes are content/visual sets, not separate game architectures.
