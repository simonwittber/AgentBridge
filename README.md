# AgentBridge

Unity Editor tooling for AI-assisted development. `AgentBridge` exposes a
file-based command protocol so external tools (Claude Code, scripts, CI) can
drive the Unity Editor — and MCP turns every command into a tool your LLM can
call directly.

## Quickstart

### 1. Install the package

**Window > Package Manager > + > Add package from git URL**

```
https://github.com/simonwittber/AgentBridge.git?path=/AgentBridge
```

For a specific version:

```
https://github.com/simonwittber/AgentBridge.git?path=/AgentBridge#v0.2.0
```

### 2. Build the CLI

Download a pre-built binary from the [latest release](https://github.com/simonwittber/AgentBridge/releases/latest)
and place it on your `PATH`. Or build from source:

```bash
cd AgentBridge/Harness~/dffrnt-agent
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
- Go 1.23+ (only needed to build from source)

---

## Built-in commands

### Core

| Command | Description |
|---|---|
| `status` | Bridge liveness, uptime, queue depth |
| `compile` | Request script compilation; returns errors and warnings |
| `refresh` | Trigger `AssetDatabase.Refresh()` and wait for completion |
| `list_commands` | List all available commands and their arguments |
| `help` | Full description and argument details for a named command |
| `focus` | Bring the Unity Editor window to the foreground |

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
| `object_select` | Select one or more objects in the Editor |
| `duplicate_object` | Duplicate a GameObject |
| `reparent_object` | Move a GameObject to a new parent |
| `set_transform` | Set position, rotation, and scale in one call |

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
| `asset_create` | Create a new folder or material asset |
| `asset_delete` | Delete an asset |
| `asset_move` | Move an asset to a new path |
| `asset_copy` | Copy an asset to a new path |
| `asset_write_text` | Write a text file under `Assets/` and reimport |
| `material_get` | Get all shader properties of a material |
| `material_set` | Set a shader property on a material |

### Editor & console

| Command | Description |
|---|---|
| `console_logs` | All Unity console messages (ring buffer, newest first) |
| `play_enter` | Enter play mode |
| `play_exit` | Exit play mode |
| `menu_item` | Invoke a Unity menu item by path |
| `run_editor_tests` | Run edit-mode tests; returns pass/fail/skip |
| `run_playmode_tests` | Run play-mode tests; returns pass/fail/skip |
| `screenshot` | Render scene view or main camera to PNG; returns base64 inline |
| `execute_script` | Compile and run a C# snippet in the Editor |
| `selection_get` | Return the currently selected GameObjects and assets |
| `undo` | Perform an undo operation |
| `redo` | Perform a redo operation |
| `uuid` | Generate a UUID v4 |

### Profiler

| Command | Description |
|---|---|
| `profiler_start` | Begin recording named `ProfilerMarker` samples |
| `profiler_stop` | Stop the current recording session |
| `profiler_clear` | Stop and dispose all recorders |
| `profiler_get_samples` | Return summary stats for recorded markers |
| `profiler_benchmark` | Fire a named marker with real CPU work to produce measurable samples |

### Player settings & editor prefs

| Command | Description |
|---|---|
| `player_settings_get` | Return current PlayerSettings values |
| `player_settings_set` | Set a PlayerSettings value by key |
| `editor_pref_get` | Get a value from EditorPrefs |
| `editor_pref_set` | Set a value in EditorPrefs |

### Tags & layers

| Command | Description |
|---|---|
| `tags_layers` | Return all tags and layers defined in the project |
| `tag_add` | Add a new tag |
| `layer_add` | Add a new layer |

### Packages

| Command | Description |
|---|---|
| `package_list` | List installed Unity packages |
| `package_add` | Add or update a package by identifier |
| `package_remove` | Remove an installed package |
| `package_search` | Search the Unity Package Registry |

### Reflection

| Command | Description |
|---|---|
| `reflect_assemblies` | List loaded assemblies |
| `reflect_types` | Search for public types by name or namespace |
| `reflect_members` | List public members of a named type |

### Build

| Command | Description |
|---|---|
| `build` | Build the Unity player for the specified target |

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
    public bool      Core        => true;
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

Set `Core = false` to hide argument details from `list_commands` — arguments are
then only returned on demand via `help`.

---

## Protocol

Commands are newline-delimited JSON written to `Temp/agent_input`:

```json
{"uid":"a1b2c3d4","cmd":"compile"}
```

Responses are appended to `Temp/agent_output`:

```json
{"uid":"a1b2c3d4","cmd":"compile","status":"ok","errors":[],"warnings":[]}
```

Unity also writes `Temp/agent_session` every 5 seconds:

```json
{"pid":12345,"state":"idle","active_scene":"Main","play_mode":false,"compile_errors":0,"written_at":1749123456789}
```

`dffrnt-agent` reads this file to verify Unity is alive before sending any command.
Output rotates at 2 MB; input is truncated on Unity startup.

---

## Testing

### Integration tests (MCP to Unity)

These tests spawn the installed `dffrnt-agent` binary (must be on `PATH`) and drive
it over the MCP protocol against a live Unity session. Build and install the binary
first, open `AgentBridge/Example~` in Unity, then:

```bash
cd AgentBridge/Harness~/dffrnt-agent
go test -timeout 300s
```

Tests are skipped automatically if Unity is not running or the session file is
missing or stale.

---

## LLM Agent Log window

Open via **Window > General > LLM Agent Log**. Live scrolling view of all
commands and responses — green = ok, red = error.
