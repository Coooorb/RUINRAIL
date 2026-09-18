---
name: combat-source-no-class-switch-scan
description: "An EditMode test scans every .cs under Assets/Game/Scripts/Combat for \"WeaponClass.BattleRifle|Smg|AssaultRifle\" strings and fails if found — keep per-class tuning as data tables, not enum switches"
metadata: 
  node_type: memory
  type: project
  originSessionId: 81da767d-c3fc-43cb-b61d-2b2a9953d7bf
  modified: 2026-09-16T22:38:09.394Z
---

`RangedWeaponDefinitionTests.SentinelBR_UsesApprovedV1Values_AndClassIdentityFeedsDataNotBehaviour` (EditMode) reads all
source files under `Assets/Game/Scripts/Combat/**` and asserts none contains the literal strings `WeaponClass.BattleRifle`,
`WeaponClass.Smg` or `WeaponClass.AssaultRifle`.

**Why:** design rule "class identity feeds data, not behaviour" — no per-class switches in combat code.

**How to apply:** any per-class tuning that lives in Combat/ (e.g. `AimAssistConfig` cone multipliers) must be a serialized
table (`List<ClassMultiplier>`) looked up generically; naming Sniper/RocketLauncher/Knife/Spear in defaults is tolerated by
the scan, but never the three forbidden ones. Related: [[unity-needs-two-runs-smart-app-control]].
