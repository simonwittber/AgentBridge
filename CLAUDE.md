# AgentBridge

Unity package (`com.dffrnt.llm-dev-tools`) that exposes a file-based command bridge
so LLM agents can control a running Unity Editor via MCP.

## Structure

```
Editor/         C# EditorWindow + AgentBridge (Unity-side bridge)
Harness~/       Go agent CLI + tools (excluded from Unity import)
Tests~/         Test package (excluded from Unity import)
```

## Running integration tests

```bash
cd Harness~/dffrnt-agent
go test -timeout 300s
```

Use `-timeout 300s` minimum. The full suite takes ~120s; the default 30s timeout will kill it mid-run.

## Building the Go agent

```bash
cd Harness~/dffrnt-agent
go build -o dffrnt-agent .        # Linux/macOS
go build -o dffrnt-agent.exe .    # Windows
```

Run `go get ./...` first if dependencies are missing.

## After editing C# files

Open Unity and let it compile. Check the Console for errors.
There is no automated compile trigger — Unity auto-compiles on focus or file change.

## Key files

| File | Purpose |
|---|---|
| `Editor/AgentBridge.cs` | Main bridge loop — reads `Temp/agent_input`, writes `Temp/agent_output` |
| `Editor/AgentLogWindow.cs` | Window > General > LLM Agent Log |
| `Harness~/dffrnt-agent/main.go` | CLI entry point (`dffrnt-agent send` / `dffrnt-agent serve`) |
| `Harness~/dffrnt-agent/serve.go` | MCP server (stdio transport) |
| `Harness~/dffrnt-agent/bridge.go` | File I/O helpers, session check, UID generation |

## Protocol

- `Temp/agent_input` — newline-delimited JSON written by the agent CLI
- `Temp/agent_output` — newline-delimited JSON written by Unity
- `Temp/agent_session` — JSON heartbeat written by Unity every 5 s: `pid`, `state`, `active_scene`, `play_mode`, `compile_errors`, `written_at`
- Output rotates at 2 MB; input is truncated on Unity startup
