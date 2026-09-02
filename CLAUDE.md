# RevitAIBridge — instructions for Claude

To read or drive the user's live Revit session, use the **RevitAIBridge folder protocol**.
Do **not** use the `revit-pyrevit` MCP tool (`mcp__revit-pyrevit__*`) — it depends on pyRevit
Routes, which is off in this environment and will always fail to connect.

Full contract: [CONTRACT.md](CONTRACT.md). Quick reference:

## Location

```
%LOCALAPPDATA%\RevitAIBridge\
```

## Check Revit is alive first

Read `bridge_status.json` and check `updated_utc` is recent (it heartbeats every ~30s while
Revit is open and idle, even between tasks). A stale timestamp means Revit is closed — don't
bother writing a task.

## Running a script

1. Write C# script content to `current_task.tmp`, then rename/move it to `current_task.csx`
   (atomic write — never write `current_task.csx` directly).
2. Touch `run.flag` (empty file, contents ignored — its existence is the trigger).
3. Poll `last_result.txt`'s mtime until it changes. First line is `OK` or `ERROR`; the rest is
   the payload (return value, or the full exception + stack trace on error).

The script runs via Roslyn (`RoslynScriptExecutor`) with the real Revit API in scope:

- Globals available with no setup: `Doc`, `UiDoc`, `UiApp`, `App`
- Imports already in scope: `Autodesk.Revit.DB`, `Autodesk.Revit.UI`,
  `Autodesk.Revit.ApplicationServices`, `System.Linq`, `System.Collections.Generic`, `System.Text`
- **The final line must have no trailing semicolon** to be captured as the script's return value
  (Roslyn scripting quirk — with a semicolon it's treated as a statement and the result comes
  back null, even though the script ran fine).
- The executor does not wrap a `Transaction` for you. Read-only queries need none; anything that
  edits the model needs its own `using (var t = new Transaction(Doc, "...")) { t.Start(); ...; t.Commit(); }`.

## Known gotchas (hit these already, don't rediscover them)

- `CAD import GraphicsStyleId` resolves per-object, not inherited from the enclosing
  `GeometryInstance` — walk every object's own `GraphicsStyleId`, don't assume a parent's style
  propagates to children.
- `GeometryInstance.GetInstanceGeometry()` gives world-transformed coordinates;
  `GetSymbolGeometry()` gives the same geometry in local/untransformed symbol space. Don't walk
  both — it silently doubles every count.
- Some DWG entities (notably certain text/annotation objects) import as zero-volume, zero-face,
  invalid-bounding-box `Solid` placeholders with no usable position data. If `Solid.Faces.Size == 0`
  and `Volume == 0`, there's nothing recoverable there — don't spend time on it.
- Not every `FloorType` supports slab shape editing (`SlabShapeEditor.Enable()` can silently
  no-op — no exception, `IsEnabled` just stays `false`, 0 vertices). Verify by re-reading
  `IsEnabled` and `SlabShapeVertices.Size` after enabling, not by the absence of an exception.
