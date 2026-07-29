# Architecture

AgentBridge has two components: a C# Unity Editor package and a Go CLI called `dffrnt-agent`.
The C# package runs inside the Unity Editor process.
The Go CLI runs outside Unity as an MCP stdio server.
They communicate through files in the `Temp/agent/` directory inside the Unity project.

## Directory layout

```
AgentBridge/
  Editor/     C# bridge and built-in commands
  Harness~/   Go CLI (excluded from Unity import)
  Example~/   Example Unity project (excluded from Unity import)
  Tests~/     Integration tests (excluded from Unity import)
```

## File protocol

All communication uses paths inside `<project>/Temp/agent/`:

| Path | Written by | Read by | Purpose |
|---|---|---|---|
| `requests/<timestamp>-<uid>.json` | Go CLI | Unity C# | One JSON object per request |
| `responses/<uid>.json` | Unity C# | Go CLI | One JSON object per response |
| `session.json` | Unity C# | Go CLI | Heartbeat written every 5 seconds |
| `log` | Unity C# | (debug) | Append-only NDJSON audit trail |

File existence is the only state. There are no position pointers and no rotation.

## Request lifecycle

1. The Go CLI writes `requests/<timestamp>-<uid>.json` with `{"uid":"...","cmd":"...","arg":...}`.
2. `AgentBridge.OnUpdate()` runs on Unity's main thread every 250 ms.
3. When idle, it reads the oldest request file and calls `Dispatch(uid, cmd)`.
4. `Dispatch()` finds the handler and calls `handler.Execute()`.
5. Synchronous handlers return a `JsonObject`. `Respond()` writes it to `responses/<uid>.json` and deletes the request file.
6. The Go CLI polls for the response file, reads it, deletes it, and returns the result to the MCP caller.

`compile` and `refresh` are asynchronous. Their `Execute()` returns `null` and a later callback calls `AgentBridge.Respond()`. The Go CLI waits for Unity liveness rather than a fixed deadline for these two commands.

## Domain reload recovery

Request files stay on disk during a domain reload.
Before the reload, `AgentBridge` deletes any stale response for the pending command to prevent mismatched results.
After the reload, all `IAgentCommand` classes re-register automatically.
`EditorApplication.delayCall` fires `ReplayPending()`, which re-dispatches any surviving request.
The `delayCall` (not a direct call in the static constructor) makes sure all handlers from other assemblies are registered before replay.

## Command registration

At boot, `AgentBridge` calls `TypeCache.GetTypesDerivedFrom<IAgentCommand>()` to find all command classes in all loaded Editor assemblies.
It instantiates each class and registers it in the `_handlers` dictionary.
No attribute or manual wiring is needed.
The next domain reload re-discovers any new command class automatically.

## Warning injection

After each response, `AgentBridge.InjectWarnings()` may add a `_warning` field.
It adds this warning if scripts changed since the last compile, or if assets changed since the last `refresh`.
A fixed set of commands (such as `compile` and `refresh`) never receive these warnings.

## Session heartbeat

Unity writes `session.json` every 5 seconds and on every state change.
The file includes: `pid`, `session_id`, `state` (idle/busy/reloading), `active_scene`, `scene_dirty`, `play_mode`, `compile_errors`, `console_errors`, and `written_at`.
The Go CLI reads this file before each command.
If the file is missing or older than 30 seconds, the CLI reports Unity as unresponsive.
The CLI also blocks on any command while `state == "reloading"` and waits until Unity finishes the reload.

## MCP server (Go)

`dffrnt-agent serve` starts a stdio MCP server using the `mcp-go` library.
At start, it calls `list_commands` to get the full command list from Unity and registers each as an MCP tool.
The `set_project` tool changes the project path at runtime and reloads commands from Unity.
Five static tools (`set_project`, `open_project`, `create_project`, `find_unity_installs`, `help`) work without a live Unity session.
