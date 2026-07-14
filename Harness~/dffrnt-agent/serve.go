package main

import (
	"context"
	"encoding/json"
	"fmt"
	"log"
	"os"
	"strings"

	"github.com/mark3labs/mcp-go/mcp"
	"github.com/mark3labs/mcp-go/server"
)

var coreMCPTools = map[string]bool{
	"status": true, "compile": true, "refresh": true, "focus": true,
	"commands": true, "console_logs": true,
	"hierarchy": true, "object_find": true, "objects_find": true,
	"object_create": true, "object_delete": true, "object_active": true,
	"object_rename": true, "object_select": true,
	"component_get": true, "component_set": true, "component_add": true,
	"scene_info": true, "scene_open": true, "scene_save": true, "scene_new": true,
	"asset_write_text": true, "asset_create": true, "asset_delete": true,
	"asset_move": true, "asset_copy": true, "asset_find": true,
	"undo": true, "redo": true, "play_mode": true, "run_tests": true,
}

func runServe(cfg Config) {
	s := server.NewMCPServer("unity-agentbridge", "0.1.0")

	registerProjectTools(cfg, s)

	if err := loadTools(cfg, s); err != nil {
		log.Printf("warning: %v — serving with no tools until Unity connects", err)
	}

	invokeTool := mcp.NewTool("invoke",
		mcp.WithDescription("Call any Unity command by name. Use 'commands' to list all available commands."),
		mcp.WithString("cmd", mcp.Description("Command name")),
		mcp.WithString("args", mcp.Description("JSON object of arguments")),
	)
	s.AddTool(invokeTool, func(ctx context.Context, req mcp.CallToolRequest) (*mcp.CallToolResult, error) {
		toolArgs, _ := req.Params.Arguments.(map[string]any)
		cmd, _ := toolArgs["cmd"].(string)
		if cmd == "" {
			return nil, fmt.Errorf("cmd is required")
		}
		args := map[string]any{}
		if argsStr, _ := toolArgs["args"].(string); argsStr != "" {
			if err := json.Unmarshal([]byte(argsStr), &args); err != nil {
				return nil, fmt.Errorf("args: invalid JSON: %w", err)
			}
		}
		resp, err := send(cfg, cmd, args)
		if err != nil {
			return nil, err
		}
		out, _ := json.MarshalIndent(resp, "", "  ")
		return mcp.NewToolResultText(string(out)), nil
	})

	if err := server.ServeStdio(s); err != nil {
		fmt.Fprintf(os.Stderr, "serve: %v\n", err)
		os.Exit(1)
	}
}

func loadTools(cfg Config, s *server.MCPServer) error {
	resp, err := send(cfg, "commands", nil)
	if err != nil {
		return loadToolsFromCache(cfg, s)
	}

	cmds, _ := resp["commands"].([]any)
	registerTools(cfg, s, cmds)

	if data, marshalErr := json.Marshal(cmds); marshalErr == nil {
		os.WriteFile(cfg.SchemaFile, data, 0644) //nolint:errcheck
	}

	return nil
}

func loadToolsFromCache(cfg Config, s *server.MCPServer) error {
	data, err := os.ReadFile(cfg.SchemaFile)
	if err != nil {
		return fmt.Errorf("Unity unavailable and no cached schema at %s", cfg.SchemaFile)
	}
	var cmds []any
	if err := json.Unmarshal(data, &cmds); err != nil {
		return fmt.Errorf("invalid schema cache: %w", err)
	}
	registerTools(cfg, s, cmds)
	log.Printf("loaded %d tool(s) from schema cache", len(cmds))
	return nil
}

func registerTools(cfg Config, s *server.MCPServer, cmds []any) {
	for _, raw := range cmds {
		cmdMap, ok := raw.(map[string]any)
		if !ok {
			continue
		}
		name, _ := cmdMap["cmd"].(string)
		if name == "" || !coreMCPTools[name] {
			continue
		}
		desc, _ := cmdMap["description"].(string)
		rawArgs, _ := cmdMap["args"].([]any)

		opts := []mcp.ToolOption{mcp.WithDescription(desc)}
		jsonArgs := map[string]bool{}
		for _, ra := range rawArgs {
			argMap, ok := ra.(map[string]any)
			if !ok {
				continue
			}
			argName, _ := argMap["name"].(string)
			argType, _ := argMap["type"].(string)
			argDesc, _ := argMap["description"].(string)
			if argName == "" {
				continue
			}

			var propOpts []mcp.PropertyOption
			if argDesc != "" {
				propOpts = append(propOpts, mcp.Description(argDesc))
			}
			switch strings.ToLower(argType) {
			case "int", "float":
				opts = append(opts, mcp.WithNumber(argName, propOpts...))
			case "bool":
				opts = append(opts, mcp.WithBoolean(argName, propOpts...))
			case "any":
				opts = append(opts, mcp.WithString(argName, propOpts...))
				jsonArgs[argName] = true
			default:
				opts = append(opts, mcp.WithString(argName, propOpts...))
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
			resp, err := send(cfg, toolName, args)
			if err != nil {
				return nil, err
			}
			out, _ := json.MarshalIndent(resp, "", "  ")
			return mcp.NewToolResultText(string(out)), nil
		})
	}
}
