---
name: telegraph-fairness-contract
description: "Enemy damage reaches players only via CombatHurtbox.AttachPlayer + DamageTargets.Resolve; how the fairness proofs judge \"visibly inside the red\""
metadata:
  node_type: memory
  type: project
  originSessionId: ea73b466-b9f7-4504-a19e-c303ef222fb6
  modified: 2026-10-03T18:30:01.270Z
---

Since 2026-10-03 the red telegraph is a strict contract: telegraphed damage ⇒ the player's drawn body overlaps drawn red.

- Player hit surface = `CombatHurtbox.AttachPlayer` (two boxes measured from player_sheet.png to lie inside every live frame; called in PlayerEntityBuilder.Build and NetworkPlayerObject.OnNetworkSpawn). The 0.4 movement circle is NOT a damage surface (it reached 12 px below the drawn feet).
- `DamageTargets.Resolve` is the one hit-target rule (resolver, area damage, projectiles): projectile colliders are never their shooter's body — pooled shots are children of the shooter, so a slam on a player's bullet used to hurt the player across the room.
- Dash hits are filtered by the drawn lane (Physics2D.OverlapCapsule); boss summons get presentation via BossController.Summoned in ExpeditionScene.BindActorPresentation (they had none).
- Co-op: owner reconciliation is sequence-based (PlayerNetMotion.SequenceCorrection); drift under 0.75 used to persist.

**How to apply:** new enemy damage paths must resolve targets through `DamageTargets.Resolve`. Fairness proofs: `TelegraphFairnessLiveTests` (sprite-pixel oracle; damaging floors attributed by HazardVolume.Ticked frame+amount, not by proximity) and `run-coop-expedition-proof.sh … 2 27 <port> telegraph` (expect 33/33; client judges with an RTT look-back). See [[coop-expedition-proof-runner]].
