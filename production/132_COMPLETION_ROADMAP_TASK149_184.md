# RUINRAIL Completion Roadmap — TASK 149 through TASK 184 (Revision 2)

> **Purpose:** Convert the proven TASK-148 engineering MVP into a production-complete, live-verified, human-reviewed Windows V1 release candidate without expanding gameplay feature scope.
> **Revision reason:** TASK-148 gap analysis identified distinct release work that should not be hidden inside one final playtest task: deprecated Physics2D API migration, loading/error presentation, 35 PROTOTYPE-value sign-off, live fake→real multiplayer composition and real player-build profiling.

## Phase Rules

- Gameplay feature design is frozen. No new weapons/classes/enemies/bosses/currencies/crafting/PvP/stamina/durability/Gear Score/crits/weak spots/ammo categories.
- Numerical tuning is allowed only through TASK 179 disposition or TASK 182 human-playtest evidence and must be logged.
- Final external art/audio must be original RUINRAIL work and comply with `art/106_RUINRAIL_FINAL_ART_BIBLE.md`.
- Reference games inform principles only; do not rip/trace/recolor their assets.
- Reuse TASK-138–141 presentation pipelines; do not rebuild animation/VFX/audio architecture.
- Review gates intentionally stop the runner. Claude cannot self-approve aesthetics, audio, live-service evidence or fun.

## Task Index

| Task | Name | Phase |
|---:|---|---|
| 149 | Post-TASK-148 Completion Gap Audit | Audit |
| 150 | Final Art Bible and Reference Boundaries | Art direction |
| 151 | Production Asset Manifest, Naming and Import Rules | Asset pipeline |
| 152 | Visual Vertical Slice Integration and Approval Gate | Visual approval |
| 153 | Final Player Sprite Set | Character art |
| 154 | Final Normal Enemy Sprite Sets | Enemy art |
| 155 | Final Elite Sprite Sets | Elite art |
| 156 | Final Boss Sprite Sets | Boss art |
| 157 | Final Weapon Sprite Catalog | Weapon art |
| 158 | Equipment, Consumable, Loot and Pickup Art | Item art |
| 159 | Ruined Metro Final Tileset and Props | Environment art |
| 160 | Rustworks Final Tileset and Props | Environment art |
| 161 | Overgrown Labs Final Tileset and Props | Environment art |
| 162 | Safehouse, Transit and Base Environment Art | Environment art |
| 163 | Final UI Skin, Icons, Glyphs and Pixel Font | UI art |
| 164 | 63-Room Art Dressing and Environment Integration | Environment integration |
| 165 | Full Visual Content Gate | Visual gate |
| 166 | Final Player Animation Production and Integration | Animation |
| 167 | Final Normal Enemy Animation Production and Integration | Animation |
| 168 | Final Elite and Boss Animation Production and Integration | Animation |
| 169 | Weapon and Interaction Animation Finalization | Animation |
| 170 | Animation Production Gate | Animation gate |
| 171 | Final Combat VFX Asset Integration | VFX |
| 172 | Environment, Loot, Status and UI VFX Integration | VFX |
| 173 | VFX and Combat Readability Gate | VFX gate |
| 174 | Final Gameplay SFX Content and Mix | Audio |
| 175 | Final Music, Stingers and Ambience Content and Mix | Audio |
| 176 | Audio and Presentation Approval Gate | Presentation gate |
| 177 | Physics2D API Migration and Release-Warning Cleanup | Engineering polish |
| 178 | Loading, Transition and Recoverable Error Presentation | UX/release polish |
| 179 | PROTOTYPE Value Review and Design Sign-off | Design sign-off |
| 180 | Release Network Player Prefab and Live UGS Setup | Live multiplayer setup |
| 181 | Live Sessions/Relay Two-Client Co-op Gate | Live multiplayer proof |
| 182 | Human Playtest and Evidence-Based Balance Pass | Human quality gate |
| 183 | Player-Build Profiling and Release Candidate | Hardware/release candidate |
| 184 | Final Release Completion Audit | Terminal gate |

## Mandatory Human / External Gates

1. **After TASK 152:** approve representative visual slice before bulk visual production.
2. **After TASK 165:** approve full visual content in an actual build.
3. **After TASK 176:** approve sound/music/integrated presentation.
4. **TASK 179:** human design owner resolves every feel/design-dependent PROTOTYPE disposition.
5. **TASK 180–181:** real UGS linkage and real two-client Relay evidence; mocks do not count.
6. **TASK 182:** human owner plays and signs off feel/readability/balance.
7. **TASK 183:** intended Windows target hardware profiling must actually run.

## End Condition

TASK 184 may return `RELEASE_COMPLETE` only if all mandatory final asset roles are integrated and approved, no unresolved prototype-value or project-owned deprecated-API blocker remains, final loading/error presentation exists, live Sessions/Relay was proven with two clients, target-hardware profiling ran, human playtest was approved, full regression is green, and a fresh Windows x64 non-development build passes built-player smoke/log audit.
