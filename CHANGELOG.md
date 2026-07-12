# Changelog

## [Unreleased]

### Added
- MAILDIR-style IPC queue with FIFO ordering, session heartbeat, and domain-reload recovery
- Go harness (`dffrnt-agent`) with `send` and `serve` (MCP stdio) subcommands
- Scene management: `scene_info`, `scene_open`, `scene_save`, `scene_new`
- Hierarchy and objects: `hierarchy`, `object_find/create/delete/active/rename/select`, `objects_find`
- Components: `component_get`, `component_set`, `component_add`
- Assets: `asset_find/info/set/create/delete/move/copy`
- Materials: `material_get`, `material_set`
- Prefabs: `prefab_open`, `prefab_save`
- Editor utilities: `menu_item`, `uuid`, `undo`, `redo`, `console_logs`, `play_mode`, `screenshot`, `selection_get`
- Editor preferences and player settings: `editor_pref_get/set`, `player_settings_get/set`
- Tags and layers: `tags_layers`, `tag_add`, `layer_add`
- Build: `build`

[Unreleased]: https://github.com/simonwittber/AgentBridge/compare/HEAD
