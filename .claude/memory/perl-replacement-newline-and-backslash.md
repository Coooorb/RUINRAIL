---
name: perl-replacement-newline-and-backslash
description: "perl -0pi replacements from Bash turn `\\n` inside C# string literals into real newlines and eat `\\\\`; use the Edit tool for lines containing escapes"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: d5015a60-9e8e-4e16-b98d-e472cc531da7
  modified: 2026-09-17T18:34:08.417Z
---

When patching C# with `perl -0pi -e 's/.../.../'` from the Bash tool, a `\n` inside the *replacement* (e.g. `"a,b\n"` for a C# string) becomes a literal newline in the file (CS1010 "Newline in constant"), and `'\\'` char literals collapse to `'\'`. Doubling (`\\n`, `\\\\`) is needed, and it is easy to get wrong.

**Why:** perl interprets escapes in the replacement before writing; the heredoc/quoting layers add another level.

**How to apply:** for any line that must contain `\n`, `\\`, `\"` or `|` in the target file, use the Edit/Write tool instead of perl. Reserve perl for structural edits without escapes. See also [[perl-pipe-delimiter-alternation]] and [[ps1-patch-single-pair-flatten]].
