# Camera, Sorting, and Lighting

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
## Camera

Orthographic, pixel perfect, fixed normal gameplay zoom. Soft follow without excessive lag. Small aim-direction camera offset may provide slightly more view toward cursor/right-stick aim; keep it subtle.

Every online player has their own local camera. No shared couch-coop framing system.

## Facing
Body animation uses 8 directions (N, NE, E, SE, S, SW, W, NW), while weapon aim remains mathematical 360°.

## Sorting Layers
Recommended layers include Ground, GroundDetails, LowProps, Characters, Weapons, WorldProps, AboveCharacters, Projectiles, WorldVFX, Loot, UIWorld, ScreenUI. Use Y-sorting where needed.

## Walls
Split visible lower blocking geometry and upper/foreground portions so characters can visually move behind tall walls. Only add wall-fade logic if playtesting proves necessary.

## Lighting
Use 2D lighting sparingly for atmosphere, lamps, fire, energy, and boss effects. Do not depend on complex dynamic lighting for basic visibility.
