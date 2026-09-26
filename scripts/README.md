# Scripts

| Script | Use | Mode |
|---|---|---|
| `run-unity-tests.sh` / `.ps1` | EditMode/PlayMode, full or targeted | all modes (targeted first) |
| `check-docs.py` | cheap documentation structure/link check, no Unity | after touching docs |
| `run-release-smoke.sh` | official deterministic built-player smoke (pinned seeds, `release_smoke_manifest.csv`) | RELEASE |
| `run-coop-release-smoke.sh` | Duo real host+client built-player smoke | RELEASE |
| `run-coop-expedition-proof.sh` | one real-peer co-op expedition proof (host/client) | HIGH_RISK networking proof, RELEASE |
| `run-smoke-loop.sh` | N runs of one smoke scenario/seed to measure flakiness | diagnosis only |
| `probe-audio-session.ps1` | Windows audio-session probe (below) | diagnosis only |

One-off audit matrix generators from past release passes live in `production/archive/scripts/` (historical).

## Unity Test Harness

These scripts are the canonical RUINRAIL Unity test commands.

- Windows: `./scripts/run-unity-tests.ps1 -TestPlatform EditMode|PlayMode|All [-TestFilter "<filter>"]`
- macOS/Linux/WSL: `./scripts/run-unity-tests.sh EditMode|PlayMode|All ["<filter>"]`

The optional filter is passed to Unity's `-testFilter` (`;`-separated fixture/test names or a regex) and is the default
for day-to-day work. A filtered run writes `TestResults/<Platform>-targeted-results.xml` / `-targeted-unity.log`, so it
never overwrites the full-suite XML that `FinalMvpAudit` reads. The same PASS rules apply (zero matched tests = FAIL).

Set `UNITY_PATH` when Unity `6000.3.24f1` is not in a standard Unity Hub path.

A suite only reports **PASS** when Unity exits successfully, a result XML exists, at least one test is discovered, at least one test passes, and zero tests fail. A zero-test result is a **FAIL**, because it normally indicates broken test discovery or assembly references.

`All` attempts both EditMode and PlayMode even if the first suite fails, then returns a failing exit code if either suite failed. XML results and Unity logs are written to `TestResults/`.

## Headless behavior

EditMode uses `-nographics` so it is suitable for CI/SSH/headless execution. PlayMode intentionally omits `-nographics` because some PlayMode tests may require graphics/render-loop behavior.

The Bash harness normalizes XML line breaks before reading `<test-run>` attributes, so multi-line XML formatting cannot create a false PASS.

## Audio output probe (Windows)

`./scripts/probe-audio-session.ps1 [-ProcessName RUINRAIL] [-Seconds 20] [-OutFile path.txt]`

Reads the Windows audio-session peak meter for the running player on the **current default render endpoint**, from
outside Unity. Every in-engine audio check (`isPlaying`, mixer gains, `AudioListener.GetOutputData`) samples the
engine's own graph and stays true even when the engine is mixing into a device nobody is listening on; this probe
asks Windows instead. It reports whether the process opened a render session, whether that session is muted, and
whether real signal arrived on it.

It still does not prove a human heard anything. Run the player, run the probe alongside it, and if the probe reports
signal while you hear nothing, the problem is between Windows and the speakers — check which device is the default
playback endpoint.
