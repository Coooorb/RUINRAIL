---
name: memory-lives-in-repo
description: Claude memory is versioned inside the repo at .claude/memory and linked from ~/.claude/projects; sync via git, not by copying
metadata:
  type: project
---

Since 2026-09-18 the Claude memory directory is stored in the repo at `.claude/memory/` and pushed to GitHub (`https://github.com/Coooorb/RUINRAIL`). The per-machine `~/.claude/projects/<path>/memory` folder is only a link to it:

- Windows: NTFS junction `C:\Users\safti\.claude\projects\C--Users-safti-RUINRAIL\memory` -> `C:\Users\safti\RUINRAIL\.claude\memory`
- macOS: symlink `~/.claude/projects/-Users-<user>-RUINRAIL/memory` -> `~/RUINRAIL/.claude/memory`

**Why:** the user works on the project on a Windows PC and a MacBook alternately; the memory folder name is derived from the absolute path, so it would not travel between machines on its own.

**How to apply:** memory edits are ordinary repo changes — remind the user to `git push` after a session that wrote memory, and to `git pull` before starting on the other machine. If `~/.claude/projects/.../memory` is a real directory instead of a link on a new machine, recreate the link rather than duplicating files. See [[unity-needs-two-runs-smart-app-control]] for a Windows-only quirk that does not apply on macOS.
