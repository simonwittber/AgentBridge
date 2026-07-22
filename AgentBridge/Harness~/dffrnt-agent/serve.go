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

func runServe(cfg Config) {
	state := &serveState{cfg: cfg}
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
		out, _ := json.MarshalIndent(map[string]any{"status": "ok", "project": path}, "", "  ")
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
			out, _ := json.MarshalIndent(map[string]any{"list_commands": state.getRawCmds()}, "", "  ")
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
	raw, _ := resp["list_commands"].([]any)
	state.mu.Lock()
	state.rawCmds = raw
	state.mu.Unlock()
	registerTools(state, s, raw)
	log.Printf("registered %d core tool(s) from Unity", len(raw))
	return nil
}

func registerTools(state *serveState, s *server.MCPServer, cmds []any) {
	for _, raw := range cmds {
		cmdMap, ok := raw.(map[string]any)
		if !ok {
			continue
		}
		name, _ := cmdMap["cmd"].(string)
		core, _ := cmdMap["core"].(bool)
		if name == "" || !core {
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
			if imageData, ok := resp["imageData"].(string); ok && imageData != "" {
				mimeType, _ := resp["mimeType"].(string)
				if mimeType == "" {
					mimeType = "image/png"
				}
				delete(resp, "imageData")
				delete(resp, "mimeType")
				out, _ := json.MarshalIndent(resp, "", "  ")
				return mcp.NewToolResultImage(string(out), imageData, mimeType), nil
			}
			out, _ := json.MarshalIndent(resp, "", "  ")
			return mcp.NewToolResultText(string(out)), nil
		})
	}
}

