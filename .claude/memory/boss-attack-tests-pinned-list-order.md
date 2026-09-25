---
name: boss-attack-tests-pinned-list-order
description: 19 boss/elite PlayMode assertions pinned "first ready in-band attack wins" across 8 files; changing MovesetActorController.SelectAttack breaks all of them — convert to a band contract, don't re-pin the order
metadata:
  node_type: memory
  type: project
---

`MovesetActorController.SelectAttack` used to return the first ready attack in moveset order whose trigger band held the
target. Nineteen assertions across eight PlayMode files encoded that as an exact answer per distance:

```csharp
player.transform.position = new Vector2(3f, 0f);
Assert.AreEqual("Combat Roll", boss.SelectAttack().DisplayName, "Mid range: reposition first.");
```

Files: `ScrapKingBossTests`, `TunnelMawBossTests`, `FoundryTitanBossTests`, `LabsBossesTests`, `LabsElitesTests`,
`CrusherUnitEliteTests`, `RailguardEliteTests`, `ScrapExecutionerEliteTests`.

**Why it matters:** any change to selection (weighting, no-repeat, phase ordering) fails all nineteen at once, and the
obvious "fix" — updating each expected name — silently re-freezes list-order selection as the contract.

**How to apply:** assert what survives a selection change instead — the chosen attack is ready and its authored band
really contains that distance, and the named attack is among the candidates there. `BossSelectionAssert` in the PlayMode
assembly holds `InBandAt`, `CanSelectAt` and `OnlySelectableAt` (the last for a distance only one attack can reach, where
an exact answer is still correct). A distribution proof needs `ClearCooldownsForDiagnostics()`, or cooldowns rather than
selection decide what was reachable.

Related: the same pass found `PriceService.ScaleCoinReward` had no gameplay caller at all — see
[[combat-source-no-class-switch-scan]] for the sibling habit of pinning implementation shape in tests.
