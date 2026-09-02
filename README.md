# RevitAI Bridge

A standalone Revit add-in that lets any AI agent (or any script, or any human with a text
editor) drive Revit through a watched folder — full Revit API access, no MCP server, no port,
no config file that needs to persist.

```
Agent writes current_task.csx  →  touches run.flag  →  Revit runs it  →  reads last_result.txt
```

That's the whole interface. If your tool can read and write files, it can drive Revit.

## Why a folder instead of pyRevit Routes / MCP

The pyRevit Routes server needs its "enabled" state to persist in `pyRevit_config.ini`, and on
a locked-down machine that setting won't save across restarts — so Routes never stays on. A
folder has no state to persist: the files *are* the state. No service to keep alive, no port to
firewall, no server process to crash.

## Features

- **Full Revit API access.** Scripts run as real C# via Roslyn, with `Doc`, `UiDoc`, `UiApp`, and
  `App` available with zero setup — not a fixed set of pre-built commands.
- **No UI, by design.** The add-in has no ribbon button, no dialog, nothing to click. It's a
  silent background watcher. Proof of life is `bridge_status.json`'s heartbeat, which updates
  every ~30s even while idle, not just at startup.
- **Zero persistent server.** Nothing runs except Revit itself. Close Revit, the bridge is gone;
  no cleanup, no orphaned process.
- **Verbose errors on purpose.** A failed script gets its full exception type, message, and
  stack trace back in `last_result.txt` — enough for an agent (or you) to fix the script and
  retry without switching tools.

## Quick start

1. **Build.** Open `RevitAIBridge.sln`, build the project matching your Revit version
   (2025 or 2026 — see [Supported versions](#supported-versions)).
2. **Deploy the DLL.** Copy the build output (`RevitAIBridge.dll`, `.pdb`, `.runtimeconfig.json`,
   and — since it ships with Roslyn — the `Microsoft.CodeAnalysis.*.dll` files and their
   language-resource subfolders) to a folder of your choice, e.g. `C:\Tools\RevitAIBridge\2026\`.
3. **Point the manifest at it.** Open `addins\RevitAIBridge.2026.addin` and set `<Assembly>` to
   the path you used above.
4. **Install the manifest.** Copy that `.addin` file to
   `%APPDATA%\Autodesk\Revit\Addins\2026\`.
5. **Start Revit.** First launch will show Revit's standard "unsigned add-in" prompt (this DLL
   isn't code-signed) — choose **Always Load** so it stops asking.
6. **Verify.** Check `%LOCALAPPDATA%\RevitAIBridge\bridge_status.json` — a fresh `updated_utc`
   means it's running.

Repeat for each Revit version you use; each has its own project, output folder, and `.addin`
file so 2025 and 2026 can run side by side without conflicting.

## Running a script

```bash
# 1. Write your script (never write current_task.csx directly — atomic write)
cat > "%LOCALAPPDATA%\RevitAIBridge\current_task.tmp" << 'EOF'
Doc.Title
EOF
move "%LOCALAPPDATA%\RevitAIBridge\current_task.tmp" "%LOCALAPPDATA%\RevitAIBridge\current_task.csx"

# 2. Trigger it
echo. > "%LOCALAPPDATA%\RevitAIBridge\run.flag"

# 3. Poll last_result.txt until its mtime changes, then read it
```

A read-only query needs nothing else. A script that edits the model manages its own transaction:

```csharp
using (var t = new Transaction(Doc, "My edit"))
{
    t.Start();
    // ... modify the model ...
    t.Commit();
}
```

**One quirk to know:** the final line of your script must have **no trailing semicolon** to be
captured as the return value — this is a Roslyn scripting behavior, not a bridge bug. With a
semicolon it's treated as a statement and `last_result.txt` comes back empty even though the
script ran fine.

## The folder contract

Full spec: [`CONTRACT.md`](CONTRACT.md). Summary:

| File | Written by | Purpose |
|---|---|---|
| `current_task.csx` | Agent | The C# to execute |
| `run.flag` | Agent | Trigger (empty file — existence is the signal, written last) |
| `last_result.txt` | Bridge | `OK`/`ERROR` + payload, written atomically |
| `bridge_status.json` | Bridge | Heartbeat: is Revit alive, which version, which document |

No queue (one task at a time), no authentication (folder permissions are the security boundary),
no streaming (fire, wait, read).

## Supported versions

| Revit | Target framework | Status |
|---|---|---|
| 2025 | `net8.0-windows` | Supported |
| 2026 | `net8.0-windows` | Supported |
| 2027 | — | Not yet added — same steps as 2025/2026 when needed |

## Project layout

```
RevitAIBridge.sln
Directory.Build.props        shared build settings, no CPM (see file for why)
CONTRACT.md                  the folder protocol — read this first
CLAUDE.md                    instructions for AI coding agents working on this repo
src/                         shared source, compiled into both version projects
  BridgeApp.cs                  IExternalApplication — watcher, idling heartbeat, wiring
  TaskHandler.cs                 IExternalEventHandler — runs inside Revit's API context
  BridgePaths.cs                  paths, atomic writes, JSON escaping
  BridgeStatus.cs                  heartbeat writer
  IScriptExecutor.cs                execution abstraction + Echo fallback
  RoslynScriptExecutor.cs            compiles and runs current_task.csx with the real Revit API
RevitAIBridge.2025/           net8.0-windows, Nice3point.Revit.Api 2025.*
RevitAIBridge.2026/           net8.0-windows, Nice3point.Revit.Api 2026.*
addins/                       .addin manifests, one per version, fresh GUIDs
```

## Security notes

- **Unsigned binary.** This project isn't code-signed, so Revit's unsigned-add-in prompt will
  appear on every fresh install (once per machine if you choose *Always Load*). That prompt is
  about the publisher, not about whether the code works — it's expected for any dev-built add-in
  without a purchased signing certificate.
- **No authentication on the folder.** Anyone who can write to `%LOCALAPPDATA%\RevitAIBridge\`
  can run arbitrary code inside your Revit session, with full API access including model edits.
  That's the entire security model — treat write access to that folder like you'd treat shell
  access to the machine.
- **Scripts run unsandboxed.** `RoslynScriptExecutor` gives a script the same permissions Revit
  itself has. There's no allow-list of "safe" operations; a script can do anything the Revit API
  can do, including deleting elements or overwriting files the process can reach.

## Known limitations

- Reading text *content* from linked/imported CAD (DWG) files isn't reliably possible through
  the standard Revit API — some text entities import as empty, zero-volume placeholder solids
  with no recoverable position or string data. Points, lines, and solids with real geometry are
  fine; text strings are not guaranteed to be.
- Not every `FloorType` supports slab shape editing (`SlabShapeEditor.Enable()` can silently
  no-op with no exception). If you need sloped/point-edited floors, verify `IsEnabled` and
  `SlabShapeVertices.Size` after enabling rather than trusting the absence of an error.

## Contributing

Source is under `src/`, shared by both version projects via `<Compile Include="..\src\**\*.cs" />`
in each `.csproj` — edit once, both versions rebuild from the same code. If you're using an AI
coding agent (Claude Code or similar) to work on this repo, see [`CLAUDE.md`](CLAUDE.md) for
environment-specific context it should know before touching the code.

## License

[MIT](LICENSE)
