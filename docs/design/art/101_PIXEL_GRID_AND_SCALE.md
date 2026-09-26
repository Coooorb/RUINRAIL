# Pixel Grid and Scale

> **Status:** Approved design specification unless explicitly marked as tunable.
> **Game language:** English. Planning discussions may be in German, but all player-facing text, code naming, comments, and Claude implementation specs should be English.
Initial target art setup:
- Reference resolution: 640×360.
- Aspect ratio: 16:9.
- Tile: 32×32 px.
- Pixels Per Unit: 32.
- Orthographic camera.
- Pixel Perfect Camera enabled.

1920×1080 is a clean 3× reference scale.

## Sprite Scale
Do not arbitrarily rescale pixel sprites to fit. Normal gameplay sprites should use transform scale 1,1,1.

Initial sprite-canvas targets:
- Player humanoid: ~32×48 px.
- Similar humanoids: comparable scale.
- Swarm: ~16–24 px.
- Brute: ~48×64 px.
- Elites: ~48–64+ px.
- Bosses: ~64–128 px depending on design.

These are art-production targets, not arbitrary world-scale transforms.
