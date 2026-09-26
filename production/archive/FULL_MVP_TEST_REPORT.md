# RUINRAIL V1 — Full MVP test report (TASK 146)

Environment: Unity 6000.3.24f1, batchmode test runner (`scripts/run-unity-tests.sh All`), Windows 11. Run date: 2026-09-15.

## Exact results

| Platform | Discovered | Passed | Failed | Skipped | Not run |
|---|---:|---:|---:|---:|---|
| EditMode | 660 | 659 | 0 | 1 | `MultiplayerTerminalTests.LiveSessionsRelay_IntegrationCheck_OrNotRun` — live Unity Sessions/Relay credentials unavailable in this environment (reported NOT RUN by the test itself, never PASS) |
| PlayMode | 496 | 496 | 0 | 0 | — |
| **Total** | **1156** | **1155** | **0** | **1** | live cloud services: NOT RUN |

Production validators (run inside the EditMode suite, reports under `TestResults/` and `production/`):

| Validator | Result |
|---|---|
| Final production validation (TASK 145, 9 sections) | PASS — 0 errors; 97 external asset items listed separately |
| Content counts (production/126) | PASS — 52/52 exact (33 weapons / 9 armor / 16 accessories / 10 consumables / 9 enemies / 6 Elites / 6 Bosses / 63 rooms, 21 per biome) |
| Presentation (sorting layers, 640×360 @ PPU 32, lighting, feedback) | PASS — 8 checks |
| Room sets (Metro / Rustworks / Labs) | PASS — 63 rooms generator-ready, prefabs bound |
| Weapon catalog / Legendary specials / prices | PASS |
| Animation asset audit | BLOCKED_EXTERNAL_ASSET — 0/22 sets, 0/33 weapon sprites |
| Audio event audit | contract COMPLETE 53/53 — content BLOCKED_EXTERNAL_ASSET 0/53 clips |
| Music/stinger/ambience audit | routing COMPLETE — content BLOCKED_EXTERNAL_ASSET 0/11 tracks, 0/6 stingers, 0/3 ambience |

## Coverage of the MVP loop (requirements 1–3)

- **First profile → Shelter → loadout → expedition → rooms/combat/loot/boss → Return and Descend across all three biomes**: `MvpLoopEndToEndTests` (EditMode; view models + persistence; three seeded depths Metro → Labs → Labs, relaunch exact) and `MetroFullGateTests.CrossBiomeSoloRun…` (PlayMode; real room prefabs, encounters, loot, bosses and transit across RuinedMetro → OvergrownLabs → Rustworks) plus the per-biome full-run gates (TASK 090/118/128: 16-seed seeded solo runs with every room category, both bosses and both Elites per biome).
- **Failure/quit, storage/trader/workshop/progression/save-reload, catalogs, depth scaling, events, room validators**: `MvpLoopEndToEndTests.Expedition_FailureAndQuit…`, `ExpeditionTransactionTests`, `PersistenceHardeningTests`, `SaveSlotTests`, `AutosaveTests`, `StorageTests`, `BaseUiTests`, `OnboardingTests`, weapon/armor/accessory/consumable catalog and definition tests, `DepthScaling*`, `DungeonEvent*`, `RoomValidator*`/room-set tests, `FinalProductionValidatorTests`.
- **Co-op host+client ready/start, scaling, shared loot, downed/revive/dead/spectator, disconnect, vote, host failure**: `MultiplayerGateTests` (TASK 108, 157 checks over the local fake transport), `PartyLobbyTests`, `CoopScaling*`, `NetworkLootAuthorityTests`, `ExploitHardeningTests`, `PlayerLifeStateTests`, `PlayerReviveTests`, `DeadSpectatorTests`, `DisconnectGraceTests`, `TransitVotingTests`, `PartyTransitTests`, `MultiplayerUiTests`, `PartyStatusUiTests`. Live Relay/Sessions: NOT RUN (see above).
- **Presentation/UX contracts**: pause/settings/rebinding, HUD, inventory, base UI, navigation checklist, camera/pixel grid, animation drivers, VFX/damage numbers, audio service, music routing — all green.

## Blockers and open items

1. **External art/audio (BLOCKED_EXTERNAL_ASSET)** — 97 items in `production/PRODUCTION_VALIDATION_REPORT.md`: character/enemy/boss animation sets (22 × 48 clips), 33 weapon sprites, 53 SFX clips, 11 music tracks, 6 stingers, 3 ambience loops, room tile sets, VFX sprites, UI frames/icons, pixel font. Engineering architecture, contracts and fallbacks are complete and tested; content is not.
2. **Boot-flow composition (TASK 147)** — MainMenu/Base/Dungeon scenes have no composition root yet; the view models, services and runtime composers exist and are tested individually, but a built player currently boots to an empty MainMenu scene.
3. **Network player prefab / live services** — no NetworkObject prefab registered (factory receives it at composition); Sessions/Relay live check NOT RUN in this environment.
4. **Build-target profiling** — NOT RUN (editor-frame measurements only; `production/PERFORMANCE_REPORT.md`).

## Verdict

All 1155 executable automated tests pass; the one skipped test is the live-services check that cannot run here. The MVP loop is complete and verified at the systems/view-model level and in playable seeded runs on real room prefabs across all three biomes. The game is not player-complete until the external assets exist and the boot-flow composition (TASK 147) is done.
