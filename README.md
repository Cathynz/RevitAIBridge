# RevitAI Bridge

**Lets an AI helper (or you) talk to Revit.**

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

## Quick start: install it in about 5 minutes

**What you need**

- A Windows computer with **Revit 2025 or 2026**.
- The internet (the installer downloads a few small things).
- A free Microsoft tool called **.NET**. If you don't have it, the installer tells you exactly
  what to type.

**Steps**

1. **Get the files.** On this GitHub page, click the green **Code** button, then
   **Download ZIP**. When it finishes, right-click the ZIP file and choose **Extract All**.
2. **Close Revit.** Save your work first.
3. **Open a blue PowerShell window in the new folder.** Open the folder in File Explorer, click
   the address bar at the top, type `powershell`, and press Enter.
4. **Copy this line, paste it into the blue window, and press Enter:**

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```

   Wait. The first time it takes a few minutes. When it says **Done.** it worked.
5. **Open Revit and open any project.** Revit shows a box saying it can't check who made the
   add-on. Click **Always Load**. Revit asks this about every new add-on. It is normal.
6. **Test it.** Go back to the blue window, paste this, and press Enter:

   ```powershell
   .\scripts\Run-RevitScript.ps1 'Doc.Title'
   ```

   You should see the word **OK** and the name of your project. That's it, it works!

**If something goes wrong**

| What you see | What to do |
|---|---|
| "Revit is open" | Save your work, close Revit, and do step 4 again. |
| It says .NET is missing | It shows one line starting with `winget`. Paste that line, press Enter, then do step 4 again. |
| "Revit looks closed" in step 6 | Open Revit with a project, wait 30 seconds, and try step 6 again. |
| You never saw the **Always Load** box | Revit only asks the first time. Try step 6 anyway. |

**To update later:** download the new ZIP and do steps 2 to 4 again.

**To remove it:** close Revit, then run
`powershell -ExecutionPolicy Bypass -File .\uninstall.ps1`.

### For developers: doing it by hand

The installer does these steps for each Revit year it finds:

1. Compile `RevitAIBridge.<year>\RevitAIBridge.<year>.csproj` in Release mode.
2. Copy the output to `%LOCALAPPDATA%\RevitAIBridge\bin\<year>\`. That folder must include
   `RevitAIBridge.dll`, `.runtimeconfig.json` and the `Microsoft.CodeAnalysis.*.dll` files.
3. Copy `addins\RevitAIBridge.<year>.addin` to `%APPDATA%\Autodesk\Revit\Addins\<year>\` and
   change its `<Assembly>` line to the full path of that DLL.

## Running a script

You give Revit a small piece of C# code, and it runs that code inside your open project and
hands back the answer. The code is called a *script*. You don't need to be a programmer to try
the examples below.

**Before you start:** Revit must be open with a project loaded, and the add-in must be
installed (see [Quick start](#quick-start)).

### The easy way: one command

Open **PowerShell** in this repo's folder and run:

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

The helper is just doing the [folder protocol](CONTRACT.md) for you. You can do the same by hand
in PowerShell, which is also what any other tool or AI agent would do:

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

**One quirk to know:** the final line of your script must have **no trailing semicolon** to be
captured as the return value — this is a Roslyn scripting behavior, not a bridge bug. With a
semicolon it's treated as a statement and `last_result.txt` comes back empty even though the
script ran fine.

### Troubleshooting

| What you see | What it means |
|---|---|
| "Revit looks closed" | The bridge hasn't reported in for over two minutes. Start Revit and open a project. |
| "No bridge_status.json" | The add-in has never started. Re-check the [Quick start](#quick-start) steps. |
| Timed out | Revit only runs tasks when it's idle. Close any open dialog, finish the command you're in, and try again. |
| `OK` followed by `(script completed, no return value)` | Your last line ended with a semicolon. Remove it. |
| `ERROR` followed by a long message | The script has a mistake. The message says what and where. Fix it and run again. |
| `Doc` is null / "Object reference not set" | No project is open in Revit. Open one first. |

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
coding agent to work on this repo, see [`AGENTS.md`](AGENTS.md) for environment-specific context
it should know before touching the code.

## License

[MIT](LICENSE)
