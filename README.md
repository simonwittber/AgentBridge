# LLM Dev Tools

Unity Editor tooling for AI-assisted development. `AgentBridge` exposes a
file-based command protocol so external tools (Claude Code, scripts, CI) can
drive the Unity Editor — and MCP turns every command into a tool your LLM can
call directly.

## Quickstart

### 1. Install the package

**Window → Package Manager → + → Add package from git URL**

```
https://github.com/simonwittber/AgentBridge.git
```

Or for a specific version:

```
https://github.com/simonwittber/AgentBridge.git#v0.1.0
```

### 2. Build the CLI

Download a pre-built binary from the [latest release](https://github.com/simonwittber/AgentBridge/releases/latest)
and place it on your `PATH`. Or build from source:

```bash
cd path/to/AgentBridge/Harness~/dffrnt-agent
go build -o dffrnt-agent .        # macOS / Linux
go build -o dffrnt-agent.exe .    # Windows
```

### 3. Wire into Claude Code

Add to `.claude/settings.json` inside your Unity project:

```json
{
  "mcpServers": {
    "unity": {
      "command": "dffrnt-agent",
      "args": ["serve"]
    }
  }
}
```

Run `dffrnt-agent` from your Unity project root, or pass `--project <path>` to
point it at the project directory.

### 4. Open Unity and verify

Start (or focus) your Unity project. In Claude Code, the `unity` MCP server
will appear in `/mcp` with all bridge commands available as tools.

### 5. Send your first command

```
dffrnt-agent status
```

Expected output:

```json
{
  "cmd": "status",
  "status": "ok",
  "uptime_s": 42.3,
  "queued": 0,
  "busy": false
}
```

---

## Requirements

- Unity 6000.0 or later
- Go 1.25+ (only needed to build from source)

---

## Built-in commands

### Core

| Command | Description |
|---|---|
| `status` | Bridge liveness, uptime, queue depth |
| `compile` | Request script compilation; returns structured errors and warnings |
| `refresh` | Trigger `AssetDatabase.Refresh()` and wait for completion |
| `commands` | List all available commands and their arguments |

### Scene

| Command | Description |
|---|---|
| `scene_info` | Name, path, dirty flag, root count |
| `scene_open` | Open a scene by asset path |
| `scene_save` | Save the active scene |
| `scene_new` | Create a new empty or default scene |

### Hierarchy & objects

| Command | Description |
|---|---|
| `hierarchy` | Scene tree as JSON (configurable depth) |
| `object_find` | Find a GameObject by path; returns components |
| `objects_find` | Find all objects with a given component type |
| `object_create` | Create a GameObject or primitive |
| `object_delete` | Delete a GameObject |
| `object_active` | Activate or deactivate a GameObject |
| `object_rename` | Rename a GameObject |
| `object_select` | Select one or more objects in the editor |

### Components & assets

| Command | Description |
|---|---|
| `component_get` | Get all serialized fields of a component |
| `component_set` | Set a serialized field on a component |
| `component_add` | Add a component by type name |
| `prefab_open` | Open a prefab in prefab stage |
| `prefab_save` | Save and exit the current prefab stage |
| `asset_info` | GUID and importer settings for an asset |
| `asset_set` | Set an importer field and reimport |
| `asset_find` | Find assets by type / label filter |
| `material_get` | Get all shader properties of a material |
| `material_set` | Set a shader property on a material |

### Editor & console

| Command | Description |
|---|---|
| `console_logs` | Recent Unity console messages (ring buffer, newest first) |
| `play_mode` | Enter, exit, or query play mode |
| `menu_item` | Invoke a Unity menu item by path |
| `run_tests` | Run edit-mode or play-mode tests; returns pass/fail/skip |
| `uuid` | Generate a UUID v4 |

---

## Adding custom commands

Implement `IAgentCommand` in any Editor assembly:

```csharp
using System.Text.Json.Nodes;
using LLMDevTools;
using UnityEditor;

[InitializeOnLoad]
public class MyCommand : IAgentCommand
{
    static MyCommand() => AgentBridge.Register(new MyCommand());

    public string    Cmd         => "my_cmd";
    public string    Description => "Does something useful.";
    public ArgSpec[] Args        => new[]
    {
        new ArgSpec("message", "string", "", "Text to log"),
    };

    public JsonObject Execute(string uid, string requestJson)
    {
        var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
        resp["echoed"] = requestJson;
        return resp;
    }
}
```

---

## Protocol

Commands are newline-delimited JSON written to `Temp/agent_input`:

```json
{"uid":"a1b2c3d4","cmd":"compile"}
```

Responses are appended to `Temp/agent_output`:

```json
{"uid":"a1b2c3d4","cmd":"compile","status":"ok","session_id":1749123456789,"errors":[],"warnings":[]}
```

Unity also writes `Temp/agent_session` every 5 seconds:

```json
{"pid":12345,"state":"idle","active_scene":"Main","play_mode":false,"compile_errors":0,"written_at":1749123456789}
```

`dffrnt-agent` reads this file to verify Unity is alive before sending any command.
Output rotates at 2 MB; input is truncated on Unity startup.

---

## Testing

### Unit tests (Go)

```bash
cd Harness~/dffrnt-agent
go test ./...
```

### End-to-end tests (MCP → Unity)

These tests spawn the installed `dffrnt-agent` binary (must be on `PATH`) and drive
it over the MCP protocol against a live Unity session. Build and install the binary
first, open the `Example~` project in Unity, then:

```bash
cd Harness~/dffrnt-agent
UNITY_PROJECT=/path/to/AgentBridge/Example~ go test -v -run TestMCP -timeout 300s
```

On Windows (PowerShell):

```powershell
$env:UNITY_PROJECT = "C:\path\to\AgentBridge\Example~"
go test -v -run TestMCP -timeout 300s
```

If `UNITY_PROJECT` is not set, the tests default to `../../Example~` relative to
the test directory. Tests are automatically skipped if Unity is not running or the
session file is missing or stale.

> **Note:** `TestMCP_Refresh` triggers a full `AssetDatabase.Refresh()` which can
> take 2+ minutes on a fresh project. Subsequent runs are much faster.

---

## LLM Agent Log window

Open via **Window → General → LLM Agent Log**. Live scrolling view of all
commands and responses — green = ok, red = error.
