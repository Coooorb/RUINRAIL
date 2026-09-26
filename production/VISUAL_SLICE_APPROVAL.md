# Visual Vertical Slice Approval — Review Gate A (TASK 152)

> **This record is written by a human.** No validator, tool or model may set the verdict.
> TASK 153 may not start until the verdict below reads `APPROVED`.

## Verdict

Verdict: NOT_APPROVED

Reviewer:
Date:

## Current State

**The slice has not been captured, because the art it requires does not exist.**

`TestResults/visual_slice.md` reports `BLOCKED_EXTERNAL_ASSET` — 0 of 23 slice roles carry final art. Acceptance criterion 1 requires no placeholder fallback for the selected roles, so the existing placeholder tiles, white-square VFX and grey UI rectangles may not stand in for this review.

There is nothing to approve yet. This is not a rejection of the visual direction.

## What Is Needed To Open This Gate

Deliver final art for the 23 slice roles listed in `TestResults/visual_slice.md`, following `docs/design/art/106_RUINRAIL_FINAL_ART_BIBLE.md` and the pipeline in `docs/technical/136_ASSET_REPLACEMENT_WORKFLOW.md`. In summary:

| Subject | What is needed |
|---|---|
| Player | 8-direction source sheet (~32×48 px) and its animation set |
| P9 Ranger | World/equipped sprite drawn pointing +X, and its inventory icon |
| Grunt | 8-direction source sheet and its animation set, with a telegraph pose that reads at a glance |
| Ruined Metro combat room kit | Five 32×32 tiles (floor, floor detail, wall, obstacle, hazard), a props package, a doors package, and a tuned biome lighting profile |
| Loot and pickup presentation | Item pickup and coin pickup sprites, plus the loot glow effect |
| HUD and inventory panel treatment | Panel frame, inventory slot, HP bar, and the pixel font with a recorded licence |
| Muzzle and impact feedback | Muzzle flash and impact effect sprites |

Each asset needs a `production/asset_provenance.json` entry before it can claim final status.

## Review Procedure Once Assets Exist

1. Integrate the assets per `docs/technical/136_ASSET_REPLACEMENT_WORKFLOW.md` and remove the placeholders for those roles.
2. Run `RuinRail/Production/Validate Asset Pipeline` — zero problems required.
3. Run `RuinRail/Production/Check Visual Slice Gate` — it must report every slice role as final.
4. Capture the five standardized beats at 640×360 from the composed Dungeon scene, into `TestResults/VisualSlice/`.
5. Review the captures **in the running game**, not as isolated PNGs (`art/106 §13.8`).

## What The Reviewer Is Judging

- **Silhouette and readability at 640×360.** Does the player read against the floor? Is the Grunt's telegraph unmistakable? Do projectiles, loot and hazards survive the room dressing?
- **Facing.** Does the 8-direction body with independent 360° weapon aim look right in motion, not just correct on paper?
- **Biome identity.** Does Ruined Metro feel like abandoned transit infrastructure per `art/106 §7`?
- **Originality.** Does anything read as recognizably lifted from Soul Knight, Zero Sievert, Fallout 4 or ARC Raiders? References inform qualities only (`art/106 §12`).
- **Direction, not polish.** This gate decides whether bulk production may proceed on this visual language. Individual assets can still be revised later.

## Recording The Decision

Set `Verdict:` to `APPROVED` or `REJECTED`, add reviewer and date, and note anything that must change. On `REJECTED`, say which subjects failed and why, so the next slice attempt has something concrete to answer.
