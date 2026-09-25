---
name: mac-no-windows-build-module
description: "This Mac's Unity 6000.3.24f1 has only MacStandaloneSupport — Windows x64 builds are impossible here; use ReleaseBuildTool.BuildMacBatch + the .app smoke as the verification substitute"
metadata: 
  node_type: memory
  type: project
  originSessionId: f8bae119-a189-43a4-8e98-510e38136f64
  modified: 2026-09-19T15:48:39.003Z
---

`/Applications/Unity/Hub/Editor/6000.3.24f1/PlaybackEngines` contains only `MacStandaloneSupport` (checked 2026-09-19),
so `ReleaseBuildTool.BuildBatch` (StandaloneWindows64) cannot run on this machine.

**Why:** prompts in this repo require a "Windows x64 non-development build + headless/windowed smoke"; on the Mac that
gate is NOT RUN and forces an INCOMPLETE terminal status no matter how green everything else is.

**How to apply:** build the substitute with
`Unity -batchmode -quit -nographics -projectPath . -executeMethod RuinRail.EditorTools.Production.ReleaseBuildTool.BuildMacBatch`
(output `Builds/MacOS/RUINRAIL.app`, report `TestResults/build_report_macos.md`), then run the smoke with
`./Builds/MacOS/RUINRAIL.app/Contents/MacOS/RUINRAIL -batchmode -nographics -smoke -seed 31 -savedir <dir> -proofdir <dir>`
(headless) and without `-batchmode -nographics` plus `-screen-width 1280 -screen-height 720 -hudproofdir … -inventoryproofdir …`
(windowed; opens a window, works in this session). Seed 11 has a merchant room on depth 1, seed 31 does not.
Report the Windows build as NOT RUN and say the macOS substitute passed. See also [[final-audit-report-regenerated-on-mac]].
