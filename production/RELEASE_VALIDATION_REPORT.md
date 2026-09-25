# RUINRAIL V1 release validation report (TASK 147)

> **SUPERSEDED — historical document (2026-09-25).** Current release status: `production/FINAL_RELEASE_CANDIDATE_AUDIT.md`. Its offline fake-transport multiplayer path is superseded by real host/client built-player proofs. The body below is kept unchanged as the record of its time.


## Build
- Unity 6000.3.24f1, target StandaloneWindows64 (the development-environment platform; the GDD commits to no other platform), BuildOptions.None (non-development release), scenes Bootstrap → MainMenu → Base → Dungeon.
- Command: `Unity.exe -batchmode -nographics -executeMethod RuinRail.EditorTools.Production.ReleaseBuildTool.BuildBatch`; output Builds/Windows64/RUINRAIL.exe.
- Result: **Succeeded** — 0 errors, 14 warnings, 103.4 MB, 51 s (TestResults/build_report.md).
- Fix applied to build: Assets/Settings/UniversalRenderPipelineGlobalSettings.asset carried m_AssetVersion 11 (written by a newer URP template) while the pinned URP 17.3.0 declares last version 10, which fails the player preprocess with "not at last version"; the asset version was aligned to 10 (no data fields changed). No package or Unity version was changed.

## Warning triage (14)
- 12 × CS0618 obsolete Physics2D NonAlloc overloads (OverlapCircleNonAlloc / RaycastNonAlloc / CircleCastNonAlloc / OverlapBoxNonAlloc in AreaDamageResolver, SpecialPrimitives, Projectile, AttackResolver, ImpactReceiver, PickupAttractor, PlayerInteractor): deprecated-but-supported APIs in Unity 6; functionally verified by the full suite; migration to the non-NonAlloc overloads is a mechanical follow-up, not a release blocker.
- 1 × CS4014 (ScreenNavigation terminal action fire-and-forget): fixed in this task (explicit discard).
- 1 × ServicesCore "link your Unity project to a project ID": no Unity dashboard project is linked in this environment — live Sessions/Relay stay NOT RUN.

## Built-player smoke (startup → menu → profile/base → solo expedition → save/load)
- Command: `RUINRAIL.exe -batchmode -nographics -smoke -savedir <tmp> -logFile`; exit code 0.
- Result (TestResults/smoke_result.json): { "Success": true, "Stage": "done", "Error": "", "ScenesComposed": "MainMenu;Base;Dungeon;Base;", "RoomsComposed": 10, "Biome": "Rustworks", "BankedCoinsAfterReturn": 25, "TotalXpAfterReturn": 0, "Pistol": "ae400d2b2ada4df6b5f77e89207d775a", "SaveReloaded": true, "UnityVersion": "6000.3.24f1"}
- Stages logged: menu → base → dungeon (10 rooms, Rustworks, player + starter pistol mounted, camera/HUD composed) → return → done; save written and reloaded with the secured pistol instance id; no exceptions in the player log.

## Boot flow composed in this task
- GameApp (created by AppRoot's Booting event via the App assembly; DontDestroyOnLoad) composes MainMenu, Base and Dungeon scenes from code over the tested view models; GameContentCatalog (Resources) ships every definition/config (63 rooms, 72 items, 9/6/6 actors, 11 specials, 3 lighting profiles, audio/music catalogs); PlayerRig mounts weapons/stats/passives/consumables/Legendary specials from the at-risk inventory.
- PlayMode BootFlowTests: MainMenu → Base (new profile on disk, onboarding) → TRANSIT start → Dungeon (rooms, player, camera, HUD, start-room entry) → Return → Base with the file save reloaded.

## Production flow hygiene
- Test-only content excluded: the _Test room fixture is not in the content catalog (63 rooms), the PlayerMovementTestRoom scene is not in the build; no development cheats; log spam: none observed in the smoke log.
- Editor-only code: the assembly-hygiene validator (TASK 145) passes; the App assembly references no Editor assembly.

## NOT RUN
- Host/join-code smoke on built clients: live Unity Sessions/Relay require a linked project ID and credentials (unavailable); the offline fake transport path is exercised by the multiplayer gate and the smoke's terminal composition. NOT RUN.
- Build-target device profiling beyond the smoke: NOT RUN (see production/PERFORMANCE_REPORT.md).
