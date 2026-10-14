# AgentBridge

AgentBridge is an MCP server for the Unity Editor.
Install the package, run `dffrnt-agent serve`, and your LLM can inspect scenes, write scripts, run tests, and control the Editor from the chat.

## Why AgentBridge

Most Unity MCP tools embed a WebSocket or TCP server inside the Unity process.
That approach breaks on every domain reload.
It needs background threads to work around Unity's single-threaded API.
It also requires a free port on every machine.

AgentBridge uses a file queue instead:

- Commands land in `Temp/agent/requests/` and run on Unity's main thread. No sockets, no marshalling, no port conflicts.
- A heartbeat file (`Temp/agent/session.json`) tells the agent whether Unity is idle, compiling, or in play mode. The agent reads this file before each command.
- Domain reloads are transparent. The queue stays on disk and Unity replays any pending request after reload.
- New commands need only one `IAgentCommand` class in any Editor assembly. No changes to the bridge or the Go CLI are needed.

## Requirements

- Unity 6000.0 or later
- Go 1.23 or later (only to build from source)

## Quickstart

### 1. Install the package

Go to **Window > Package Manager > + > Add package from git URL** and enter:

```
https://github.com/simonwittber/AgentBridge.git?path=/AgentBridge
```

For a specific version:

```
https://github.com/simonwittber/AgentBridge.git?path=/AgentBridge#v0.3.0
```

### 2. Get the CLI

Download a pre-built binary from the [latest release](https://github.com/simonwittber/AgentBridge/releases/latest) and place it on your `PATH`.

To build from source:

```bash
cd AgentBridge/Harness~/dffrnt-agent
go build -o dffrnt-agent .        # macOS / Linux
go build -o dffrnt-agent.exe .    # Windows
```

### 3. Configure Claude Code

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

Run `dffrnt-agent` from your Unity project root, or pass `--project <path>` to set the project directory.

### 4. Open Unity and verify

Open or focus your Unity project.
The `unity` MCP server appears in `/mcp` in Claude Code with all bridge commands available as tools.

### 5. Check status

```bash
dffrnt-agent status
```

Expected output:

```json
{
  "cmd": "status",
  "status": "ok",
  "uptime_s": 42.3,
  "busy": false
}
```

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

### Hierarchy and objects

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

### Components and assets

| Command | Description |
|---|---|
| `component_get` | Get all serialized fields of a component |
| `component_set` | Set a serialized field on a component |
| `component_add` | Add a component by type name |
| `prefab_open` | Open a prefab in prefab stage |
| `prefab_save` | Save and exit the current prefab stage |
| `asset_info` | GUID and importer settings for an asset |
| `asset_set` | Set an importer field and reimport |
| `asset_find` | Find assets by type or label filter |
| `asset_create` | Create a new folder or material asset |
| `asset_delete` | Delete an asset |
| `asset_move` | Move an asset to a new path |
| `asset_copy` | Copy an asset to a new path |
| `asset_write_text` | Write a text file under `Assets/` and reimport |
| `material_get` | Get all shader properties of a material |
| `material_set` | Set a shader property on a material |
| `scriptable_get` | Get a named serialized field from a ScriptableObject asset |
| `scriptable_set` | Set a named serialized field on a ScriptableObject asset and save |

### Editor and console

| Command | Description |
|---|---|
| `console_logs` | All Unity console messages (ring buffer, newest first) |
| `play_enter` | Enter play mode |
| `play_exit` | Exit play mode |
| `menu_item` | Invoke a Unity menu item by path |
| `run_editor_tests` | Run edit-mode tests; returns pass/fail/skip |
| `run_playmode_tests` | Run play-mode tests; returns pass/fail/skip |
| `screenshot` | Capture the current view to a PNG under `Temp/agent/screenshots/`; returns the absolute file path immediately and fires a `screenshot_ready` notification when the file is written. The path cannot be chosen. |
| `execute_script` | Compile and run a C# snippet in the Editor |
| `selection_get` | Return the currently selected GameObjects and assets |
| `undo` | Perform an undo operation |
| `redo` | Perform a redo operation |
| `uuid` | Generate a UUID v4 |

### Profiler

| Command | Description |
|---|---|
| `profiler_start` | Begin recording named `ProfilerMarker` samples. Works in edit mode and play mode. Use `execute_script` to fire custom markers, then call `profiler_get_samples`. |
| `profiler_stop` | Stop the current recording session |
| `profiler_clear` | Stop and dispose all recorders |
| `profiler_get_samples` | Return summary stats (and optionally raw values) for recorded markers |

### Player settings and editor prefs

| Command | Description |
|---|---|
| `player_settings_get` | Return current PlayerSettings values |
| `player_settings_set` | Set a PlayerSettings value by key |
| `editor_pref_get` | Get a value from EditorPrefs |
| `editor_pref_set` | Set a value in EditorPrefs |

### Tags and layers

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

public class MyCommand : IAgentCommand
{
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

AgentBridge discovers the class automatically on the next domain reload.
No `[InitializeOnLoad]` attribute or manual registration call is needed.

---

## Protocol

Commands are JSON objects written to `Temp/agent/requests/<timestamp>-<uid>.json`:

```json
{"uid":"a1b2c3d4","cmd":"compile","agent_id":"agent-1"}
```

Responses appear in `Temp/agent/responses/<uid>.json`:

```json
{"uid":"a1b2c3d4","cmd":"compile","status":"ok","errors":[],"warnings":[]}
```

Unity also writes `Temp/agent/session.json` every 5 seconds:

```json
{
  "pid": 12345,
  "state": "idle",
  "active_scene": "Main",
  "play_mode": false,
  "compile_errors": 0,
  "agent_id": "",
  "written_at": 1749123456789
}
```

`dffrnt-agent` reads this file to check that Unity is alive before each command.
`agent_id` is empty when no command is active and contains the current agent identifier when a command runs.

### Notifications

Unity writes compiler and asset-import lifecycle events to `Temp/agent/notifications/<ts>-<uid>.json`.
In `serve` mode, `dffrnt-agent` polls this directory every 500 ms and forwards each event to MCP clients as a `notifications/message`.

Example notification file:

```json
{"type":"compile_finished","data":{"error_count":0},"written_at":1749123456789}
```

Event types: `compile_started`, `compile_finished`, `compile_failure`, `refresh_started`, `refresh_finished`, `play_mode_entered`, `play_mode_exited`, `scene_opened`, `screenshot_ready`.

Commands that trigger async state changes note their notifications in the tool description.

---

## Testing

Build and install the `dffrnt-agent` binary, open `AgentBridge/Example~` in Unity, then:

```bash
cd AgentBridge/Harness~/dffrnt-agent
go test -timeout 300s
```

The tests run against a live Unity session over the MCP protocol.
They skip automatically if Unity is not running or the session file is missing.

---

## LLM Agent Log window

Open via **Window > General > LLM Agent Log**.
This window shows a live view of all commands and responses: green for success, red for error.
