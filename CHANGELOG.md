# Changelog

## [Unreleased]

### Added
- `addToProject.ps1`: links the package into a Unity project's `Packages` folder as an embedded package, and adds the link to `.git/info/exclude` so it is never committed. Uses a symbolic link, or a directory junction when symlinks are not allowed.

### Fixed
- Notifications now carry a human-readable `message` field. Previously they shipped only a structured `data` payload, so MCP clients that surface `notifications/message` as conversation content could emit an empty content block, which Anthropic rejects and LiteLLM masks as `[System: Empty message content sanitised to satisfy protocol]`. `AgentBridge.Notify` (C#) synthesises the message from the notification type and key data fields, and the Go notification watcher lifts it to the top level with a fallback for older payloads.

## [0.5.1] - 2026-09-10

### Fixed
- MCP tool handlers no longer return empty text content blocks. Anthropic's API rejects empty content, and LiteLLM masks the failure by substituting `[System: Empty message content sanitised to satisfy protocol]`. A new `toolResultJSON` helper marshals responses to indented JSON and substitutes an explicit success message when the output is empty, `null`, `{}`, or `[]`. All tool handlers route through it.
- `TestRunner` now exits play mode before running tests. Previously a test run requested while the editor was playing never started, and the caller waited for a reply that never came, wedging the editor. If a domain reload interrupts leaving play mode, the caller receives an `interrupted` response instead of hanging.

## [0.5.0] - 2026-08-26

### Added
- `register_command` tool: creates a persistent custom `IAgentCommand` from C# source. The command is saved to `Assets/AgentBridgeScripts/`, compiled by the included asmdef, and discovered via `TypeCache` after domain reload. Supports `string`, `int`, `float`, and `bool` parameters with JSON marshaling.

### Changed
- Server startup instructions in `serve.go` now include guidance on `register_command` for domain-specific tasks that need to run more than once.

## [0.3.1] - 2026-08-11

### Added
- `screenshot` now uses `ScreenCapture.CaptureScreenshotAsTexture()` in play mode and a synchronous scene-view camera render in edit mode. The command returns immediately and fires a `screenshot_ready` notification with `path`, `width`, and `height` when the image is saved. `max_size` still applies.
- Notification system: AgentBridge writes `Temp/agent/notifications/<ts>-<uid>.json` files for lifecycle events. In `serve` mode, `dffrnt-agent` polls this directory every 500 ms and forwards each event to MCP clients as a `notifications/message`. Events: `compile_started`, `compile_finished` (includes `error_count`), `compile_failure` (includes error details), `refresh_started`, `refresh_finished`, `play_mode_entered`, `play_mode_exited`, `scene_opened` (includes `name`, `path`, `mode`), `screenshot_ready` (includes `path`, `width`, `height`), `script_started`, `script_failed`.
- Unity project auto-discovery: `dffrnt-agent` checks the current working directory and all immediate subdirectories for a Unity project (presence of `Assets/` and `ProjectSettings/`). Directories without a `~` suffix take priority. No `--project` flag needed when running from the project root or a parent folder.
- Tools list refresh: after `set_project` loads commands from Unity, the server sends a `notifications/tools/list_changed` event so MCP clients pick up the new tools without a reconnect.
- `agent_id` field: requests and responses include an optional `agent_id` string. Pass `--agent-id <id>` to `dffrnt-agent` to tag all commands from one agent instance. The session heartbeat also includes the current `agent_id` so multiple agents can see which agent holds the bridge.
- Auto-compile timer: when a script file change is detected and no manual `compile` command arrives within 10 seconds, AgentBridge triggers compilation automatically. This prevents stale compile state when the user switches back to the Editor after an agent writes scripts.
- Global project registry: AgentBridge writes `%APPDATA%/AgentBridge/<hash>.json` on each heartbeat and deletes it on quit. The file contains the project path, PID, and timestamp. `dffrnt-agent` uses this to discover running projects without a `set_project` call.
- `list_projects` MCP tool: returns all Unity projects currently running with AgentBridge. Validates each entry by PID liveness and a 30-second timestamp window.
- MCP server startup instructions: the server now tells MCP clients to call `list_projects` then `set_project` when no project is connected.
- `MainThread` static class in `ScriptCommand.cs`: provides `MT.Run(() => ...)` for scripts to marshal Unity API calls from the background thread back to the main thread. Available in all `execute_script` snippets via the `using MT = LLMDevTools.MainThread` alias injected by the wrapper.
- `console_logs` now accepts an optional `filter` string argument. When provided, only entries whose message contains the filter string (case-insensitive) are returned. Omitting the argument returns all entries as before.

### Changed
- `execute_script` now runs the compiled script on the Unity main thread. Unity API calls are safe without `MT.Run`. The command is still non-blocking: compilation runs on a background thread, and the script runs on the main thread once compiled.
- `execute_script` now compiles with debug symbols (`-debug+`) and loads the PDB alongside the assembly. Exception stack traces include source line numbers.
- `execute_script` rejects code containing `Thread.Sleep`: it blocks the main thread and freezes the Unity Editor.
- `profiler_start` now works in edit mode and play mode. The play-mode-only restriction is removed.
- `profiler_get_samples` returns a `unit` field (`"ms"` or `"bytes"`) and includes aliased `lastMs`/`avgMs`/`minMs`/`maxMs` fields for time markers.
- When scripts need recompilation or assets need reimport, AgentBridge now triggers `compile` or `refresh` automatically and replays the blocked command after completion. Previously a hard error was returned. Commands that are always safe to run (log checks, script execution, asset writes) are not affected.
- Renamed `SceneBridge.SilentlyClearDirtyScenes` to `SaveDirtyScenesWithoutPrompt`. The old name implied that the method discards changes, but it saves named scenes without a prompt.
- Removed the unused `_wasPlayingBeforeReload` field from `AgentBridge`. Unity fires `ExitingPlayMode` before `OnBeforeReload`, so the field was always `false` when read.
- Renamed `SceneBridge.F` to `ToFloat` across all callers, including `MaterialCommands`.
- Renamed `PathUtils.IsWritable` to `IsAllowedWritePath`. The method checks whether a path is in an allowed write location, not whether the file system permits writes.
- `AppendLog` now logs a Unity Console warning on failure instead of silently swallowing the exception.
- `AgentLogWindow` now displays notification events (from the notifications directory) as distinct log entries. Non-error notifications appear in yellow; error notifications appear in red.
- `console_logs` now reads only the entries currently visible in the Unity Console window using the internal `UnityEditor.LogEntries` API, instead of parsing the entire `Editor.log` file. Returns only current console state, not historical data.

### Removed
- `profiler_measure`: removed. Use `execute_script` to fire a `ProfilerMarker` and `profiler_get_samples` to read the results.
- Historical log file parsing from `ConsoleBridge`. Replaced with internal `LogEntries` API that returns only current console window entries.

## [0.3.0] - 2026-07-28

### Added
- `scriptable_get`: read a named serialized field from a ScriptableObject asset by path
- `scriptable_set`: write a named serialized field on a ScriptableObject asset and save it to disk

### Changed
- `screenshot` no longer returns base64 image data inline. It saves to a PNG file and returns the absolute path. Use `max_size` to downscale before saving.
- `execute_script` now loads compiled assemblies into a collectible `AssemblyLoadContext` so they are garbage-collected after each call, preventing domain reload slowdown over time.
- `set_project` now calls `list_commands` immediately after setting the project path, so Unity commands are available without a separate discovery step. The response includes `commands_loaded` on success or a `warning` if Unity is not reachable.
- `open_project` and `create_project` now return a `choose_unity_version` response listing all detected installs when multiple Unity versions are installed and `unity_path` was not specified. Re-call with `unity_path` set to the desired entry.
- `help` (no argument) always returns at least the Go-side commands (`set_project`, `open_project`, `create_project`, `find_unity_installs`) and includes a `_warning` with instructions to call `set_project` when Unity commands have not been loaded yet.

### Removed
- `invoke` tool removed from the MCP server. All Unity commands are now registered as discrete named tools at startup. LLMs can no longer route arbitrary command names through a generic escape hatch.
- `IAgentCommand.Core` property removed. All registered commands are now exposed as named MCP tools; there is no longer a distinction between core and non-core commands.

## [0.2.0] - 2026-07-22

### Added
- Transform tools: `set_transform`, `duplicate_object`, `reparent_object`
- Profiler tools: `profiler_start`, `profiler_stop`, `profiler_clear`, `profiler_get_samples` using `Unity.Profiling.ProfilerRecorder`
- `profiler_benchmark`: fires a named `ProfilerMarker` with real CPU work to produce measurable timing samples (use with `profiler_start`)
- `help` tool: returns full description and argument details for any command on demand
- `screenshot` now returns the image inline (base64 PNG) with optional `max_size` downscale
- `execute_script`: compile and run C# code snippets in the Editor
- `IAgentCommand.Core` property: command classes declare themselves as core with `bool Core => true`
- Go integration tests for profiler commands and `execute_script`, including a live data-capture test that fires a `ProfilerMarker` and verifies `sampleCount > 0`

### Changed
- `play_mode` (single command with `action` parameter) replaced by `play_enter` and `play_exit` (no arguments each)
- `run_tests` (single command with `mode` parameter) replaced by `run_editor_tests` and `run_playmode_tests`
- `commands` renamed to `list_commands`
- `console_logs` simplified: `limit` and `type` filter arguments removed; always returns all buffered logs
- MCP tools/list context cost reduced by 31% (from ~3,000 to ~2,063 tokens) by moving argument descriptions behind the `help` tool
- `serve.go` now loads core tools dynamically from Unity at startup via `list_commands` instead of a hardcoded allowlist. No Go recompile needed when adding new Unity commands.
- `agent_schema.json` removed; no offline schema cache needed.
- Dropped `profiler_set_deep` and `profiler_get_frame` (required internal Unity APIs with no public equivalent)
- Removed `--schema` CLI flag from `dffrnt-agent`

## [0.1.0] - 2026-07-20

### Added
- File-based IPC queue with FIFO ordering, session heartbeat, and domain-reload recovery
- Go harness (`dffrnt-agent`) with `send` and `serve` (MCP stdio) subcommands
- Core: `status`, `compile`, `refresh`, `commands`, `focus`
- Scene management: `scene_info`, `scene_open`, `scene_save`, `scene_new`
- Hierarchy and objects: `hierarchy`, `object_find/create/delete/active/rename/select`, `objects_find`
- Components: `component_get`, `component_set`, `component_add`
- Assets: `asset_find`, `asset_info`, `asset_set`, `asset_create`, `asset_delete`, `asset_move`, `asset_copy`, `asset_write_text`
- Materials: `material_get`, `material_set`
- Prefabs: `prefab_open`, `prefab_save`
- Packages: `package_list`, `package_add`, `package_remove`, `package_search`
- Reflection: `reflect_assemblies`, `reflect_types`, `reflect_members`
- Editor utilities: `menu_item`, `uuid`, `undo`, `redo`, `console_logs`, `play_mode`, `screenshot`, `selection_get`, `run_tests`
- Editor preferences and player settings: `editor_pref_get/set`, `player_settings_get/set`
- Tags and layers: `tags_layers`, `tag_add`, `layer_add`
- Build: `build`

[0.5.0]: https://github.com/simonwittber/AgentBridge/compare/v0.3.1...v0.5.0
[0.3.1]: https://github.com/simonwittber/AgentBridge/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/simonwittber/AgentBridge/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/simonwittber/AgentBridge/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/simonwittber/AgentBridge/releases/tag/v0.1.0
