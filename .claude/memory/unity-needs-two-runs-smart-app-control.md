---
name: unity-needs-two-runs-smart-app-control
description: "Unity fails the first batch run after any C# change on this machine; run the test harness twice"
metadata: 
  node_type: memory
  type: project
  originSessionId: 2bc3884a-f7a9-4abc-aeda-d17bada85130
  modified: 2026-09-16T19:07:55.119Z
---

On this machine Smart App Control is enforcing (`HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy` →
`VerifiedAndReputablePolicyState = 1`) and blocks Unity's unsigned `Bee.TundraBackend.dll`. The first
`scripts/run-unity-tests.ps1` launch after any source edit regenerates the build graph, hits the block and reports
"Scripts have compiler errors" with **no C# error anywhere in the log**. The second launch finds the graph valid and
compiles and runs normally.

**Why:** the signed `ScriptCompilationBuildProgram.exe` cannot load its unsigned backend DLL; both installed Unity
versions (6000.3.24f1 and 6000.6.0f1) ship the same unsigned file, so switching Editor version does not help.

**How to apply:** run the harness twice and read the second result — `try { ./scripts/run-unity-tests.ps1 ... } catch {}`
then the real run. Do not add a retry inside the harness (it would hide genuine failures), and do not disable Smart App
Control without asking: it is system-wide and Windows cannot re-enable it without a reinstall. If a run reports
compiler errors, grep the log for `error CS` before believing it. Recorded in full in
`production/archive/UI_AND_BIOME_POLISH_REPORT.md` section 1.
