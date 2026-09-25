# RUINRAIL V1 animation asset audit (art/103)

> **SUPERSEDED — historical document (2026-09-25).** Current release status: `production/FINAL_RELEASE_CANDIDATE_AUDIT.md`. This frozen copy predates the art pass; the live audit (22/22 actor sets, 33/33 weapon sprites) is regenerated to TestResults/animation_assets.md. The body below is kept unchanged as the record of its time.


Status: **BLOCKED_EXTERNAL_ASSET** — 0/22 actor animation sets complete; weapon sprites 0/33.

| Actor | Kind | Set | Required clips | Missing | Status |
|---|---|---|---:|---:|---|
| player | Player | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| bomber | Enemy | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| brute | Enemy | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| charger | Enemy | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| grunt | Enemy | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| shield_enemy | Enemy | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| shooter | Enemy | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| sniper_enemy | Enemy | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| summoner | Enemy | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| swarm | Enemy | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| elite_crusher_unit | Elite | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| elite_mutated_brute | Elite | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| elite_prototype_x7 | Elite | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| elite_railguard | Elite | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| elite_scrap_executioner | Elite | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| elite_tunnel_stalker | Elite | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| boss_aegis_core | Boss | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| boss_scrap_king | Boss | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| boss_subject_omega | Boss | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| boss_the_conductor | Boss | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| boss_the_foundry_titan | Boss | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |
| boss_tunnel_maw | Boss | none | 48 | 48 | BLOCKED_EXTERNAL_ASSET |

Runtime behaviour without these assets: SpriteAnimator records the missing clip id and keeps the last frame (placeholder), drivers keep mapping gameplay state; no exceptions.
