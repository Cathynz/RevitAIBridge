# RevitAI Bridge

Lets an AI helper (or you) talk to Revit.

Revit is a program for designing buildings. This small add-on lets another program, such as an
AI helper, give Revit a little job and get the answer back. For example: "What is this project
called?" or "How many walls are there?"

It works like a mailbox, using a folder on your computer:

1. Someone puts a note in the folder: "please do this".
2. Revit sees the note and does the job.
3. Revit puts the answer in the folder.

There is nothing to click inside Revit. It works quietly in the background.

*(For developers: that's a folder watcher with full Revit API access through C# scripts. No
server, no port, no config file. See [CONTRACT.md](CONTRACT.md).)*

## Why a folder instead of pyRevit Routes / MCP

The pyRevit Routes server needs its "enabled" setting saved in `pyRevit_config.ini`. On a
locked-down machine that setting doesn't survive a restart, so Routes never stays on. A folder
has nothing to save, because the files are the state. There is no service to keep alive and no
port to open in a firewall.

## Features

- Scripts are real C#, run through Roslyn. `Doc`, `UiDoc`, `UiApp` and `App` are ready to use
  with no setup, so the full Revit API is available. There is no fixed list of commands.
- The add-on has no ribbon button and no dialog. To see that it's running, check
  `bridge_status.json`. It updates about every 30 seconds, even while Revit is idle, not only at
  startup.
- Nothing runs except Revit. When you close Revit the bridge is gone, with nothing left to
  clean up.
- When a script fails, `last_result.txt` holds the full exception type, message and stack
  trace. That is enough for an agent (or you) to fix the script and try again without
  switching tools.

## Quick start: install it in about 5 minutes

### What you need

- A Windows computer with **Revit 2025 or 2026**.
- The internet (the installer downloads a few small things).
- A free Microsoft tool called .NET. If you don't have it, the installer tells you exactly
  what to type.

### Steps

1. On this GitHub page, click the green **Code** button, then **Download ZIP**. When it
   finishes, right-click the ZIP file and choose **Extract All**.
2. Save your work and close Revit.
3. Open a blue PowerShell window in the new folder. Open the folder in File Explorer, click the
   address bar at the top, type `powershell`, and press Enter.
4. Copy this line, paste it into the blue window, and press Enter:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```

   Wait. The first time takes a few minutes. When it says **Done.**, it worked.
5. Open Revit and open any project. Revit shows a box saying it can't check who made the
   add-on. Click **Always Load**. Revit asks this about every new add-on, so it's normal.
6. Go back to the blue window, paste this, and press Enter:

   ```powershell
   .\scripts\Run-RevitScript.ps1 'Doc.Title'
   ```

   You should see **OK** and the name of your project.

### If something goes wrong

| What you see | What to do |
|---|---|
| "Revit is open" | Save your work, close Revit, and do step 4 again. |
| It says .NET is missing | It shows one line starting with `winget`. Paste that line, press Enter, then do step 4 again. |
| "Revit looks closed" in step 6 | Open Revit with a project, wait 30 seconds, and try step 6 again. |
| You never saw the **Always Load** box | Revit only asks the first time. Try step 6 anyway. |

To update later, download the new ZIP and do steps 2 to 4 again.

To remove it, close Revit, then run
`powershell -ExecutionPolicy Bypass -File .\uninstall.ps1`.

### For developers: doing it by hand

The installer does these steps for each Revit year it finds:

1. Compile `RevitAIBridge.<year>\RevitAIBridge.<year>.csproj` in Release mode.
2. Copy the output to `%LOCALAPPDATA%\RevitAIBridge\bin\<year>\`. That folder must include
   `RevitAIBridge.dll`, `.runtimeconfig.json` and the `Microsoft.CodeAnalysis.*.dll` files.
3. Copy `addins\RevitAIBridge.<year>.addin` to `%APPDATA%\Autodesk\Revit\Addins\<year>\` and
   change its `<Assembly>` line to the full path of that DLL.

## Running a script

You give Revit a small piece of C# code. It runs that code inside your open project and hands
back the answer. The code is called a *script*. You don't need to be a programmer to try the
examples below.

Before you start, Revit must be open with a project loaded, and the add-on must be installed
(see [Quick start](#quick-start-install-it-in-about-5-minutes)).

### The easy way: one command

Open PowerShell in this repo's folder and run:

```powershell
.\scripts\Run-RevitScript.ps1 'Doc.Title'
```

You should see something like:

```
OK
MyProject
```

`Doc.Title` is the whole script: `Doc` means "the project that's open in Revit", and `.Title`
is its name. The first line of the reply is always `OK` or `ERROR`. Everything after it is the
answer, or the error message if something went wrong.

To run a script saved in a file, such as the ready-made ones in [`examples/`](examples):

```powershell
.\scripts\Run-RevitScript.ps1 -File .\examples\list-levels.csx
.\scripts\Run-RevitScript.ps1 -File .\examples\count-by-category.csx
```

If PowerShell says running scripts is disabled, start it like this instead:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Run-RevitScript.ps1 'Doc.Title'
```

The helper checks that Revit is alive, waits up to two minutes for the answer, and exits with
code 0 on `OK` and 1 on `ERROR` or a timeout, so you can use it from other scripts too.

### What it does behind the scenes

The helper does the [folder protocol](CONTRACT.md) for you. You can do the same by hand in
PowerShell, which is also what any other tool or AI agent would do:

```powershell
$root = "$env:LOCALAPPDATA\RevitAIBridge"

# 1. Write the script to a temp file, then rename it. (The rename is atomic, so Revit never
#    sees a half-written file. Don't write current_task.csx directly.)
Set-Content "$root\current_task.tmp" 'Doc.Title'
Move-Item "$root\current_task.tmp" "$root\current_task.csx" -Force

# 2. Create an empty file called run.flag. Its existence is the "go" signal.
New-Item "$root\run.flag" -ItemType File -Force | Out-Null

# 3. Wait a second or two, then read the answer.
Start-Sleep 2
Get-Content "$root\last_result.txt"
```

In step 3, `last_result.txt` is rewritten each time a script finishes. If you read it too early
you'll see the previous result, so check that its "last modified" time has changed. The helper
does this for you.

### Scripts that change the model

A read-only query needs nothing else. A script that edits the model manages its own transaction:

```csharp
using (var t = new Transaction(Doc, "My edit"))
{
    t.Start();
    // ... modify the model ...
    t.Commit();
}
```

The last line of your script must not end with a semicolon, or its value won't come back as the
result. This is how Roslyn scripting works. With a semicolon the line counts as a statement, so
`last_result.txt` shows no return value even though the script ran fine.

### Troubleshooting

| What you see | What it means |
|---|---|
| "Revit looks closed" | The bridge hasn't reported in for over two minutes. Start Revit and open a project. |
| "No bridge_status.json" | The add-in has never started. Re-check the [Quick start](#quick-start-install-it-in-about-5-minutes) steps. |
| Timed out | Revit only runs tasks when it's idle. Close any open dialog, finish the command you're in, and try again. |
| `OK` followed by `(script completed, no return value)` | Your last line ended with a semicolon. Remove it. |
| `ERROR` followed by a long message | The script has a mistake. The message says what and where. Fix it and run again. |
| `Doc` is null / "Object reference not set" | No project is open in Revit. Open one first. |

## The folder contract

Full spec: [`CONTRACT.md`](CONTRACT.md). Summary:

| File | Written by | Purpose |
|---|---|---|
| `current_task.csx` | Agent | The C# to execute |
| `run.flag` | Agent | Trigger. An empty file; its existence is the signal. Written last. |
| `last_result.txt` | Bridge | `OK` or `ERROR` plus the payload, written atomically |
| `bridge_status.json` | Bridge | Heartbeat: is Revit alive, which version, which document |

There is no queue, so Revit runs one task at a time. There is no authentication, so folder
permissions are the only protection. Results are not streamed: you send a task, wait, then read
the result.

## Supported versions

| Revit | Target framework | Status |
|---|---|---|
| 2025 | `net8.0-windows` | Supported |
| 2026 | `net8.0-windows` | Supported |
| 2027 | n/a | Not added yet. It would follow the same steps as 2025 and 2026. |

## Project layout

```
RevitAIBridge.sln
Directory.Build.props        shared build settings, no CPM (see file for why)
CONTRACT.md                  the folder protocol — read this first
AGENTS.md                    instructions for AI coding agents working on this repo
CLAUDE.md                    pointer to AGENTS.md, for Claude Code's file-lookup convention
src/                         shared source, compiled into both version projects
  BridgeApp.cs                  IExternalApplication — watcher, idling heartbeat, wiring
  TaskHandler.cs                 IExternalEventHandler — runs inside Revit's API context
  BridgePaths.cs                  paths, atomic writes, JSON escaping
  BridgeStatus.cs                  heartbeat writer
  IScriptExecutor.cs                execution abstraction + Echo fallback
  RoslynScriptExecutor.cs            compiles and runs current_task.csx with the real Revit API
scripts/                      Run-RevitScript.ps1: run a script in one command
examples/                     ready-to-run sample scripts (read-only, safe to try)
RevitAIBridge.2025/           net8.0-windows, Nice3point.Revit.Api 2025.*
RevitAIBridge.2026/           net8.0-windows, Nice3point.Revit.Api 2026.*
addins/                       .addin manifests, one per version, fresh GUIDs
```

## Security notes

- This project isn't code-signed, so Revit shows its unsigned add-in prompt on a fresh install.
  If you choose *Always Load*, it appears once per machine. The prompt only says Revit can't
  identify the publisher. Any add-in built without a purchased signing certificate triggers it.
- Anyone who can write to `%LOCALAPPDATA%\RevitAIBridge\` can run arbitrary code inside your
  Revit session, with full API access including model edits. Treat write access to that folder
  like shell access to the machine.
- `RoslynScriptExecutor` gives a script the same permissions Revit itself has. There is no
  allow-list of safe operations. A script can do anything the Revit API can do, including
  deleting elements or overwriting files the process can reach.

## Known limitations

- Reading text *content* from linked or imported CAD (DWG) files isn't reliably possible
  through the standard Revit API. Some text entities import as empty, zero-volume placeholder
  solids with no recoverable position or string data. Points, lines and solids with real
  geometry work; text strings aren't guaranteed.
- Not every `FloorType` supports slab shape editing (`SlabShapeEditor.Enable()` can silently
  do nothing, with no exception). If you need sloped or point-edited floors, check
  `IsEnabled` and `SlabShapeVertices.Size` after enabling instead of trusting the absence of an
  error.

## Contributing

Source is under `src/`, shared by both version projects via `<Compile Include="..\src\**\*.cs" />`
in each `.csproj`. Edit once and both versions rebuild from the same code. If you're using an AI
coding agent to work on this repo, see [`AGENTS.md`](AGENTS.md) for context it should know
before touching the code.

## License

[MIT](LICENSE)
