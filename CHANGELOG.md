# Changelog

## [Unreleased]

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

[0.2.0]: https://github.com/simonwittber/AgentBridge/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/simonwittber/AgentBridge/releases/tag/v0.1.0
