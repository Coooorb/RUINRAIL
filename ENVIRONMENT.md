# RUINRAIL — Environment and Toolchain

> **Status:** Operational project configuration, not game design.
> **Proposed pinned baseline:** each listed version was confirmed in Unity's official Unity 6000.0 package documentation on 2026-09-14 as a released/available package version. **Project-level Package Manager resolution is NOT YET VERIFIED** and must be verified in TASK 001.
> **Change policy:** Versions may be intentionally upgraded later, but a coding task must never silently change them. If a required package/API conflicts with these pins, report the conflict before changing versions.

## Unity Editor

- **Unity:** `6000.3.24f1` — Unity 6.3 LTS.
- Use the **Universal Render Pipeline (URP)** with the 2D Renderer; URP is an Editor-matched core package in Unity 6.
- Reference resolution and pixel rules remain defined in `art/101_PIXEL_GRID_AND_SCALE.md`.

## Direct Package Pins

The project should explicitly pin these direct dependencies in `Packages/manifest.json` once the Unity project exists:

| Package | Version | Purpose |
|---|---:|---|
| `com.unity.inputsystem` | `1.17.0` | Keyboard/mouse/controller input and rebinding |
| `com.unity.netcode.gameobjects` | `2.7.0` | GameObject/MonoBehaviour networking |
| `com.unity.services.multiplayer` | `1.2.0` | Sessions / Multiplayer Services |
| `com.unity.multiplayer.playmode` | `1.6.2` | Local multi-player editor testing |
| `com.unity.transport` | `2.6.0` | Unity transport layer used by networking |
| `com.unity.2d.pixel-perfect` | `5.1.1` | Pixel Perfect Camera |
| `com.unity.2d.tilemap.extras` | `4.1.0` | Rule Tiles and Tilemap utilities |

## Editor-Matched Core Packages

Do **not** invent manual versions for Unity core packages that are fixed to the Editor version. In particular:

- `com.unity.test-framework`
- `com.unity.2d.tilemap`
- `com.unity.render-pipelines.universal`

Use the versions supplied/matched by Unity `6000.3.24f1`.

## Package Change Rule

If Unity Package Manager cannot resolve an approved pin:

1. Do not silently choose a newer or older API.
2. Record the exact resolver/compiler error.
3. Report which pin is incompatible.
4. Propose the smallest version change separately.
5. Update this file and `Packages/manifest.json` together only after approval.

## Unity Test Harness

Repo scripts are the canonical commands Claude Code may run:

### PowerShell / Windows

```powershell
./scripts/run-unity-tests.ps1 -TestPlatform EditMode
./scripts/run-unity-tests.ps1 -TestPlatform PlayMode
./scripts/run-unity-tests.ps1 -TestPlatform All
```

### Bash / macOS / Linux / WSL

```bash
./scripts/run-unity-tests.sh EditMode
./scripts/run-unity-tests.sh PlayMode
./scripts/run-unity-tests.sh All
```

Set `UNITY_PATH` if Unity is not installed in a standard Unity Hub location. Test XML and logs are written to `TestResults/`.

A model may only report a test as passed when the command actually ran and the harness reports PASS. The harness treats **zero discovered tests as a failure**, even if Unity exits with code 0. It also rejects suites with zero passed tests or reported failures. If Unity is unavailable, report **NOT RUN — Unity executable unavailable**, not PASS.

When `All` is requested, the harness attempts both EditMode and PlayMode and returns failure if either suite fails. EditMode runs with `-nographics`; PlayMode intentionally does not.

## Verification Status

- **Documentation check:** Each of the seven version numbers above was confirmed in Unity's official Unity 6000.0 package documentation on 2026-09-14 as released/available. This documentation check does not prove that the exact combination resolves together under Unity `6000.3.24f1`.
- **Actual project resolution:** **VERIFIED (2026-09-25).** `Packages/manifest.json` carries exactly the seven pins above; the project resolves, compiles, runs the full EditMode/PlayMode suites and builds a non-development player under Unity `6000.3.24f1` (see `production/FINAL_RELEASE_CANDIDATE_AUDIT.md`). No version was changed.
- **TASK 001 requirement:** Unity Package Manager must resolve the exact approved pins in the real project without silent substitution. If resolution fails, follow the Package Change Rule above and report the exact conflict.

Re-check official Unity documentation before an intentional toolchain upgrade.
