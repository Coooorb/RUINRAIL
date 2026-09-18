---
name: ps1-patch-single-pair-flatten
description: "PowerShell Patch helper silently no-ops when given exactly one (old,new) pair because @( @(a,b) ) flattens"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 9a5295e8-3e01-44c8-96fb-32e68b35d521
  modified: 2026-09-15T03:13:33.122Z
---

The scratchpad `Patch($path, $pairs)` PowerShell helper used for multi-line C# replacements silently does nothing (exit 0, no output) when called with a single pair: `@( @("old","new") )` is flattened by PowerShell into a 2-string array, so the loop iterates characters.

**Why:** cost several wasted Unity harness runs (each ~3-5 min) in the RUINRAIL autonomous run before the cause was found.

**How to apply:** for a single replacement use the Edit tool; for ps1 patches always pass two or more pairs, or write `,@(...)` / `[object[]]` to keep the nesting. Verify with grep before running the harness.
