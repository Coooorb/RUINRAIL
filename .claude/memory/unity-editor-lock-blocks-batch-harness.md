---
name: unity-editor-lock-blocks-batch-harness
description: An open Unity Editor on the project makes every scripts/run-unity-tests.sh run fail with "Multiple Unity instances cannot open the same project"; wait for the user to close it rather than killing it
metadata:
  node_type: memory
  type: project
---

`./scripts/run-unity-tests.sh` (and any `-batchmode -executeMethod` call) exits 1 with
`Multiple Unity instances cannot open the same project` whenever the Editor GUI has the project open. The log gives no
compiler output, so it is easy to misread as a build break.

Detect it before blaming the code:

```bash
pgrep -f "Unity.app/Contents/MacOS/Unity -projectpath" | wc -l   # main editor (note: lowercase 'path')
```

The AssetImportWorker children use `-projectPath` with a capital P, so a case-insensitive grep over-counts.

**How to wait:** arm a background waiter and keep writing code meanwhile —

```bash
until ! pgrep -f "Unity.app/Contents/MacOS/Unity -projectpath" >/dev/null 2>&1; do sleep 15; done; echo editor-closed
```

**Do not quit the Editor.** It is the user's session and may hold unsaved scene or inspector state; ask them to close
it (`! osascript -e 'quit app "Unity"'` puts it in their hands) and resume when the waiter fires.

**Related:** an EditMode run also rewrites generated assets — `Assets/Game/Resources/GameContentCatalog.asset` picks up
appended `VfxSprites`/`WorldSprites` entries because a test rebuilds the catalog. That diff is expected noise, not an
edit, and is worth naming in a report so it is not mistaken for a content change. See
[[final-audit-report-regenerated-on-mac]].
