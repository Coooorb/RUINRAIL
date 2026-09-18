---
name: perl-pipe-delimiter-alternation
description: "perl -0pi 's|...|...|' with `\\|\\|` or `\\|=` in the PATTERN silently becomes regex alternation and inserts the replacement at line 1 of the file"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 81da767d-c3fc-43cb-b61d-2b2a9953d7bf
  modified: 2026-09-16T21:26:30.877Z
---

When the substitution delimiter is `|`, an escaped `\|` inside the *pattern* is a literal `|` for the delimiter
parser but then a regex alternation — so `if \(a \|\| b\)` matches the empty alternative at offset 0 and the
replacement lands at the very top of the file, glued to `using System;`. Happened twice in one session
(UiKit.cs, NetworkPlayerCombat.cs, DungeonHudViewModel.cs `|=`).

**Why:** cost a compile cycle each time and is easy to miss because perl exits 0.
**How to apply:** for any C# line containing `||`, `|=` or `|` use the Edit tool (or a `#`/`~` delimiter); after
bulk perl edits run `head -1` on the touched files. Related: [[ps1-patch-single-pair-flatten]].
