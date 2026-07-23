package main

import (
	"context"
	"encoding/json"
	"fmt"
	"log"
	"os"
	"strings"
	"sync"

	"github.com/mark3labs/mcp-go/mcp"
	"github.com/mark3labs/mcp-go/server"
)

// serveState holds mutable config shared across all MCP tool handler closures.
// Project can be changed at runtime via the set_project tool.
type serveState struct {
	mu      sync.RWMutex
	cfg     Config
	rawCmds []any
}

func (s *serveState) getConfig() Config {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.cfg
}

func (s *serveState) setProject(path string) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.cfg.Project = path
}

func (s *serveState) getRawCmds() []any {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.rawCmds
}

func (s *serveState) send(cmd string, args map[string]any) (map[string]any, error) {
	cfg := s.getConfig()
	if cfg.Project == defaultProject {
		return nil, fmt.Errorf("no Unity project set — call set_project first")
	}
	return send(cfg, cmd, args)
}

// staticCmds describes the always-available Go-side tools so help() always
// returns them regardless of whether Unity is running.
var staticCmds = []any{
	map[string]any{
		"cmd":         "set_project",
		"description": "Set the Unity project path.",
		"args":        []any{map[string]any{"name": "path", "type": "string", "default": "", "description": "Absolute path to the Unity project root"}},
	},
	map[string]any{
		"cmd":         "open_project",
		"description": "Open an existing Unity project. Unity opens in the background.",
		"args": []any{
			map[string]any{"name": "path", "type": "string", "default": "", "description": "Absolute path to the Unity project to open"},
			map[string]any{"name": "unity_path", "type": "string", "default": "", "description": "Path to the Unity executable. Auto-detected if omitted"},
		},
	},
	map[string]any{
		"cmd":         "create_project",
		"description": "Create a new Unity project at the given path. Unity opens in the background.",
		"args": []any{
			map[string]any{"name": "path", "type": "string", "default": "", "description": "Absolute path where the new project should be created"},
			map[string]any{"name": "unity_path", "type": "string", "default": "", "description": "Path to the Unity executable. Auto-detected if omitted"},
		},
	},
	map[string]any{
		"cmd":         "find_unity_installs",
		"description": "List Unity Editor installations found on this machine.",
		"args":        []any{},
	},
	map[string]any{
		"cmd":         "help",
		"description": "Get full description and argument details for any command. Omit command to list all available commands.",
		"args":        []any{map[string]any{"name": "command", "type": "string", "default": "", "description": "Command name to look up"}},
	},
}

func runServe(cfg Config) {
	state := &serveState{cfg: cfg, rawCmds: staticCmds}
	s := server.NewMCPServer("unity-agentbridge", "0.1.0")

	registerProjectTools(cfg, s)

	setProjectTool := mcp.NewTool("set_project",
		mcp.WithDescription("Set the Unity project path."),
		mcp.WithString("path"),
	)
	s.AddTool(setProjectTool, func(ctx context.Context, req mcp.CallToolRequest) (*mcp.CallToolResult, error) {
		args, _ := req.Params.Arguments.(map[string]any)
		path, _ := args["path"].(string)
		if path == "" {
			return nil, fmt.Errorf("path is required")
		}
		state.setProject(path)
		result := map[string]any{"status": "ok", "project": path}
		if err := loadToolsFromUnity(state, s); err != nil {
			result["warning"] = "Unity commands not loaded: " + err.Error()
		} else {
			result["commands_loaded"] = len(state.getRawCmds()) - len(staticCmds)
		}
		out, _ := json.MarshalIndent(result, "", "  ")
		return mcp.NewToolResultText(string(out)), nil
	})

	if cfg.Project != defaultProject {
		if err := loadToolsFromUnity(state, s); err != nil {
			log.Printf("warning: could not load tools from Unity: %v", err)
		}
	}

	helpTool := mcp.NewTool("help",
		mcp.WithDescription("Get full description and argument details for any command. Omit command to list all available commands."),
		mcp.WithString("command", mcp.Description("Command name to look up")),
	)
	s.AddTool(helpTool, func(ctx context.Context, req mcp.CallToolRequest) (*mcp.CallToolResult, error) {
		args, _ := req.Params.Arguments.(map[string]any)
		name, _ := args["command"].(string)
		if name == "" {
			cmds := state.getRawCmds()
			payload := map[string]any{"list_commands": cmds}
			if len(cmds) <= len(staticCmds) {
				payload["_warning"] = "Unity commands not loaded. Call set_project with the path to your Unity project root, then Unity commands will appear here."
			}
			out, _ := json.MarshalIndent(payload, "", "  ")
			return mcp.NewToolResultText(string(out)), nil
		}
		for _, raw := range state.getRawCmds() {
			cmdMap, ok := raw.(map[string]any)
			if !ok {
				continue
			}
			if cmdMap["cmd"] == name {
				out, _ := json.MarshalIndent(cmdMap, "", "  ")
				return mcp.NewToolResultText(string(out)), nil
			}
		}
		return nil, fmt.Errorf("unknown command: %s", name)
	})

	if err := server.ServeStdio(s); err != nil {
		fmt.Fprintf(os.Stderr, "serve: %v\n", err)
		os.Exit(1)
	}
}

func loadToolsFromUnity(state *serveState, s *server.MCPServer) error {
	resp, err := state.send("list_commands", nil)
	if err != nil {
		return fmt.Errorf("commands call failed: %w", err)
	}
	unityCmds, _ := resp["list_commands"].([]any)
	if unityCmds == nil {
		return fmt.Errorf("list_commands returned null")
	}
	merged := append(staticCmds, unityCmds...)
	state.mu.Lock()
	state.rawCmds = merged
	state.mu.Unlock()
	registerTools(state, s, unityCmds)
	log.Printf("registered %d tool(s) from Unity", len(unityCmds))
	return nil
}

func registerTools(state *serveState, s *server.MCPServer, cmds []any) {
	for _, raw := range cmds {
		cmdMap, ok := raw.(map[string]any)
		if !ok {
			continue
		}
		name, _ := cmdMap["cmd"].(string)
		if name == "" {
			continue
		}
		desc, _ := cmdMap["description"].(string)
		rawArgs, _ := cmdMap["args"].([]any)

		var opts []mcp.ToolOption
		if desc != "" {
			opts = append(opts, mcp.WithDescription(desc))
		}
		jsonArgs := map[string]bool{}
		for _, ra := range rawArgs {
			argMap, ok := ra.(map[string]any)
			if !ok {
				continue
			}
			argName, _ := argMap["name"].(string)
			argType, _ := argMap["type"].(string)
			if argName == "" {
				continue
			}

			switch strings.ToLower(argType) {
			case "int", "float":
				opts = append(opts, mcp.WithNumber(argName))
			case "bool":
				opts = append(opts, mcp.WithBoolean(argName))
			case "any":
				opts = append(opts, mcp.WithString(argName))
				jsonArgs[argName] = true
			default:
				opts = append(opts, mcp.WithString(argName))
			}
		}

		tool := mcp.NewTool(name, opts...)
		toolName := name
		localJSONArgs := jsonArgs
		s.AddTool(tool, func(ctx context.Context, req mcp.CallToolRequest) (*mcp.CallToolResult, error) {
			args, _ := req.Params.Arguments.(map[string]any)
			for k := range localJSONArgs {
				if s, ok := args[k].(string); ok {
					var v any
					if json.Unmarshal([]byte(s), &v) == nil {
						args[k] = v
					}
				}
			}
			resp, err := state.send(toolName, args)
			if err != nil {
				return nil, err
			}
			out, _ := json.MarshalIndent(resp, "", "  ")
			return mcp.NewToolResultText(string(out)), nil
		})
	}
}

