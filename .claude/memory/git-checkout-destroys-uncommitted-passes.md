---
name: git-checkout-destroys-uncommitted-passes
description: RUINRAIL's working tree carries many uncommitted files from earlier passes; `git checkout <file>` to undo your own edit silently deletes that earlier work
metadata:
  type: feedback
---

The RUINRAIL tree is persistently dirty: ~260 modified/untracked paths from previous completion passes sit
uncommitted on `main` (last commit `e1d41b0`). `git checkout -- <file>` therefore does **not** mean "undo my edit",
it means "throw away everything anyone did to this file since the last commit".

On 2026-09-23 this deleted the uncommitted adjustable-control API from
`Assets/Game/Scripts/UI/Navigation/FocusNavigation.cs` (`FocusItem.Adjust` / `IsAdjustable` / `Adjustments` /
`TryAdjust`, `FocusList.AddAdjustable` / `AdjustFocused`, `FocusStack.Adjust`), which the Settings sliders depend on.
It only surfaced as `error CS1061: 'FocusList' does not contain a definition for 'AddAdjustable'` and had to be
reconstructed from an earlier Read of the file in the same session.

**Why:** there is no clean baseline to revert to, so git's notion of "unchanged" is several passes behind reality.

**How to apply:** to undo your own edit, reverse it with Edit or re-apply the text you replaced — keep a `cp` backup
to `/tmp` before a risky rewrite. Before any `git checkout` / `git restore` on a file, run
`git status --short <file>`: if it is `M` or `??`, do not check it out. See [[memory-lives-in-repo]].
