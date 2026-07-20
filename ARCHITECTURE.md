# AgentBridge Architecture

## IPC — file queue

One file per message under `Temp/agent/`:

```
requests/   {uid}.json   pending command (written by Go, deleted by Unity on respond)
responses/  {uid}.json   completed response (written by Unity, deleted by Go on read)
session.json             Unity heartbeat: pid, state (idle/busy/reloading), written_at
log                      append-only NDJSON audit trail
```

File existence is state. No position pointers, no rotation.

---

## Go → Unity

```
send(cmd, args)
  waitForReady()              block while session.json state == "reloading"
  write requests/{uid}.json
  poll responses/{uid}.json   every 250 ms
    found   → delete file, return payload
    timeout → delete request, return error
```

---

## Unity dispatch loop

`EditorApplication.update` fires every 250 ms:

```
Idle?  → ReadNext()
           sort requests/*.json, pick first
           Dispatch(uid, cmd)
             handler.Execute() → synchronous: call Respond() immediately
                               → async (compile/refresh): Respond() called from callback

Busy + pendingCmd=="refresh"?
       → two-phase isUpdating check → Respond() when done
```

`Respond()` writes `responses/{uid}.json`, deletes the request, sets state idle.

---

## Domain reload recovery

Request files survive domain reloads on disk. After reload:

1. All `[InitializeOnLoad]` constructors run — all handlers re-register.
2. `EditorApplication.delayCall` fires → `ReplayPending()` re-dispatches any pending request.

`delayCall` (not a direct call in the static ctor) ensures all handlers from other
`[InitializeOnLoad]` types are registered before replay.

---

## MCP

On `serve`, the agent calls `commands` to get the handler list from Unity, registers
each as an MCP tool, then forwards every `tools/call` through `send()`.
