# RUINRAIL V1 audio event audit (art/105 SFX coverage rule)

> **SUPERSEDED — historical document (2026-09-25).** Current release status: `production/FINAL_RELEASE_CANDIDATE_AUDIT.md`. This frozen copy predates the audio content pass; the live audit (53/53 events with clips) is regenerated to TestResults/audio_assets.md. The body below is kept unchanged as the record of its time.


Contract: **COMPLETE** — 53/53 required events defined. Content: **BLOCKED_EXTERNAL_ASSET** — 0/53 events have clips.

| Event | Bus | Defined | Clips | Status |
|---|---|---|---|---|
| weapon.fire.pistol | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.fire.smg | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.fire.assault_rifle | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.fire.battle_rifle | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.fire.shotgun | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.fire.sniper | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.fire.blaster | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.bow.draw | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.bow.release | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.melee.knife_slash | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.melee.spear_thrust | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.fire.rocket_launch | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.rocket.explosion | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.reload | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.dry_fire | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.blaster.heat_rising | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.blaster.overheat_warning | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.blaster.overheat | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.blaster.vent | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| weapon.legendary_special | Weapons | yes | none | BLOCKED_EXTERNAL_ASSET |
| enemy.telegraph | Enemies | yes | none | BLOCKED_EXTERNAL_ASSET |
| enemy.telegraph.elite | Enemies | yes | none | BLOCKED_EXTERNAL_ASSET |
| enemy.telegraph.boss | Enemies | yes | none | BLOCKED_EXTERNAL_ASSET |
| enemy.hit | Enemies | yes | none | BLOCKED_EXTERNAL_ASSET |
| enemy.stagger | Enemies | yes | none | BLOCKED_EXTERNAL_ASSET |
| enemy.death | Enemies | yes | none | BLOCKED_EXTERNAL_ASSET |
| enemy.elite.spawn | Enemies | yes | none | BLOCKED_EXTERNAL_ASSET |
| enemy.boss.phase | Enemies | yes | none | BLOCKED_EXTERNAL_ASSET |
| player.hit | Player | yes | none | BLOCKED_EXTERNAL_ASSET |
| player.dash | Player | yes | none | BLOCKED_EXTERNAL_ASSET |
| player.downed | Player | yes | none | BLOCKED_EXTERNAL_ASSET |
| player.revive.start | Player | yes | none | BLOCKED_EXTERNAL_ASSET |
| player.revive.complete | Player | yes | none | BLOCKED_EXTERNAL_ASSET |
| player.death | Player | yes | none | BLOCKED_EXTERNAL_ASSET |
| player.consumable.use | Player | yes | none | BLOCKED_EXTERNAL_ASSET |
| player.heal | Player | yes | none | BLOCKED_EXTERNAL_ASSET |
| player.hazard | Player | yes | none | BLOCKED_EXTERNAL_ASSET |
| loot.pickup.item | Loot | yes | none | BLOCKED_EXTERNAL_ASSET |
| loot.pickup.coins | Loot | yes | none | BLOCKED_EXTERNAL_ASSET |
| loot.drop.common | Loot | yes | none | BLOCKED_EXTERNAL_ASSET |
| loot.drop.uncommon | Loot | yes | none | BLOCKED_EXTERNAL_ASSET |
| loot.drop.rare | Loot | yes | none | BLOCKED_EXTERNAL_ASSET |
| loot.drop.epic | Loot | yes | none | BLOCKED_EXTERNAL_ASSET |
| loot.drop.legendary | Loot | yes | none | BLOCKED_EXTERNAL_ASSET |
| loot.chest.open | Loot | yes | none | BLOCKED_EXTERNAL_ASSET |
| world.door.open | Loot | yes | none | BLOCKED_EXTERNAL_ASSET |
| world.transit.depart | Loot | yes | none | BLOCKED_EXTERNAL_ASSET |
| world.merchant.open | Loot | yes | none | BLOCKED_EXTERNAL_ASSET |
| ui.navigate | UI | yes | none | BLOCKED_EXTERNAL_ASSET |
| ui.confirm | UI | yes | none | BLOCKED_EXTERNAL_ASSET |
| ui.cancel | UI | yes | none | BLOCKED_EXTERNAL_ASSET |
| ui.failure | UI | yes | none | BLOCKED_EXTERNAL_ASSET |
| ui.purchase | UI | yes | none | BLOCKED_EXTERNAL_ASSET |

Runtime behaviour without clips: AudioService plays fallback silence and records the event id; hooks stay wired; no exceptions.
