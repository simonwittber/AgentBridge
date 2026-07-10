# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-07-09

### Added
- File-based command bridge between LLM agents and Unity Editor
- Go agent CLI (`agent send` / `agent serve`) replacing Python scripts
- MCP server mode (`agent serve`) using stdio transport
- Session file with Unity state: scene, play mode, compile errors, heartbeat
- Staleness detection — warns if Unity session file is >30 s old
- Output log rotation at 2 MB; input truncated on Unity startup
- Cross-platform PID liveness check (Windows + Unix)
- LLM Agent Log editor window (Window > General > LLM Agent Log)
- MIT license

[Unreleased]: https://github.com/simonwittber/AgentBridge/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/simonwittber/AgentBridge/releases/tag/v0.1.0
