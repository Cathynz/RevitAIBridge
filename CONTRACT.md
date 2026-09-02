# RevitAI Bridge — Folder Contract v1

A folder is the entire interface. Any agent that can read and write files can
drive Revit through it. No port, no server, no authentication, no vendor.

## Root

```
%LOCALAPPDATA%\RevitAIBridge\
```

Override with the environment variable `REVITAI_BRIDGE_ROOT`.

`%LOCALAPPDATA%` is deliberate — it is local to the machine and not part of a
roaming profile, so it survives the same restrictions that stop pyRevit saving
its own settings.

## Files

| File | Written by | Purpose |
|---|---|---|
| `current_task.csx` | Agent | The C# to execute |
| `run.flag` | Agent | Trigger. Written **last**. |
| `last_result.txt` | Bridge | Result or error of the most recent task |
| `bridge_status.json` | Bridge | Heartbeat — is Revit alive, and which version |

## Sequence

1. Agent writes `current_task.csx`.
2. Agent writes `run.flag` (contents ignored — existence is the signal).
3. Bridge notices the flag, deletes it immediately, and executes the task.
4. Bridge writes `last_result.txt` atomically.
5. Agent polls for `last_result.txt` changing.

The flag is deleted **before** execution, not after. A task that crashes Revit
must not re-run on restart.

## `bridge_status.json`

```json
{
  "state": "idle",
  "revit_version": "2024",
  "document": "AB_PIT38_TO_PIT37",
  "updated_utc": "2026-08-21T09:14:02Z",
  "contract_version": 1
}
```

`state` is one of `idle`, `busy`, `error`.

Written at startup and after every task. An agent should read this **first** —
a stale `updated_utc` means Revit is closed, and there is no point writing a
task.

## `last_result.txt`

Plain text. First line is the status token, everything after is payload.

```
OK
<whatever the script returned>
```

```
ERROR
<exception type and message>
<line number where available>
```

The error payload is deliberately verbose. The retry loop depends on the agent
being able to read its own compile error and correct the script.

## Atomicity

The bridge writes `last_result.tmp`, then moves it over `last_result.txt`.
Agents must never read a partially written file.

Agents should do the same: write `current_task.tmp`, move to `current_task.csx`,
*then* create `run.flag`.

## Non-goals

- No queue. One task at a time. A second flag arriving mid-task is ignored.
- No authentication. Anyone who can write to the folder can drive Revit. Folder
  permissions are the security boundary.
- No streaming. Fire, wait, read.
