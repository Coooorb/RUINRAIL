---
name: ruinrail-persistence
description: RUINRAIL saves, profiles, storage, save migrations, autosave, and any transaction that moves items/coins/XP. HIGH_RISK by default; load with ruinrail-qa.
---

# RUINRAIL persistence

**Contract:** `docs/technical/113_SAVE_PERSISTENCE.md`. Code: `Assets/Game/Scripts/Persistence/` (`SaveSlot`
`CurrentVersion`, `SaveMigrations`, `SaveSlotService`, file store in `ISaveStore.cs`). Saves happen at safe points
(`SaveNow(...)`: leaving the Shelter, expedition end, return to menu) plus the autosave flusher.

**Rules.**
- **Migrations:** bumping `SaveSlot.CurrentVersion` requires a migration from the previous version; the chain must stay
  unbroken (the release validator checks it). Old saves load; unsupported ones are left untouched with a diagnostic,
  never overwritten.
- **Atomic writes:** the store writes `<path>.tmp`, reads it back, then `File.Replace`s with a backup. Keep that path;
  a crash mid-save must leave the previous save intact.
- **No silent loss, no duplicate grants:** every item/coin/XP movement is one transaction with a single owner. Snapshot
  quantities before trades (a stackable buy once zeroed its source). Rewards are granted exactly once across
  save/reload, death, extraction and co-op.
- Save only stable definition IDs and runtime state — never asset paths or display names.
- Save at safe points only; mid-combat state is not persisted unless the spec says so.
- Backward compatibility is a requirement, not a nice-to-have.

**Proof = real consequence.** Save → reload (fresh process/profile where it matters) → assert the actual state
(inventory, storage, coins, level, deepest depth). Cover the failure path too (death, disconnect, quit mid-run).
Existing coverage: `rg -l "Persistence|SaveSlot|Migration" Assets/Game/Tests`.
