# Changelog

## [Unreleased]

### Added
- Transform tools: `set_transform`, `duplicate_object`, `reparent_object`
- Profiler tools: `profiler_start`, `profiler_stop`, `profiler_clear`, `profiler_set_deep`, `profiler_get_frame`, `profiler_get_samples`
- `help` tool: returns full description and argument details for any command on demand
- `screenshot` now returns the image inline (base64 PNG) with optional `max_size` downscale
- `execute_script`: compile and run C# code snippets in the Editor

### Changed
- MCP tools/list context cost reduced by 31% (from ~3,000 to ~2,063 tokens) by moving argument descriptions behind the `help` tool

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

[0.1.0]: https://github.com/simonwittber/AgentBridge/releases/tag/v0.1.0
