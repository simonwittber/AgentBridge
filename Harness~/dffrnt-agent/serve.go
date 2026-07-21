package main

import (
	"bytes"
	"context"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"image"
	"image/png"
	"log"
	"math"
	"os"
	"path/filepath"
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

	screenshotTool := mcp.NewTool("screenshot",
		mcp.WithDescription("Render scene view or main camera to a PNG."),
		mcp.WithString("path"),
		mcp.WithNumber("width"),
		mcp.WithNumber("height"),
		mcp.WithNumber("max_size"),
	)
	s.AddTool(screenshotTool, func(ctx context.Context, req mcp.CallToolRequest) (*mcp.CallToolResult, error) {
		args, _ := req.Params.Arguments.(map[string]any)
		maxSize := 0
		if v, ok := args["max_size"]; ok {
			if f, ok := v.(float64); ok {
				maxSize = int(f)
			}
			delete(args, "max_size")
		}
		resp, err := state.send("screenshot", args)
		if err != nil {
			return nil, err
		}
		out, _ := json.MarshalIndent(resp, "", "  ")
		if status, _ := resp["status"].(string); status != "ok" {
			return mcp.NewToolResultText(string(out)), nil
		}
		imgPath, _ := resp["path"].(string)
		if !filepath.IsAbs(imgPath) {
			imgPath = filepath.Join(state.getConfig().Project, imgPath)
		}
		imgBytes, err := os.ReadFile(imgPath)
		if err != nil {
			return nil, fmt.Errorf("read screenshot file: %w", err)
		}
		if maxSize > 0 {
			if scaled, err := scalePNG(imgBytes, maxSize); err == nil {
				imgBytes = scaled
			}
		}
		return mcp.NewToolResultImage(string(out), base64.StdEncoding.EncodeToString(imgBytes), "image/png"), nil
	})

	invokeTool := mcp.NewTool("invoke",
		mcp.WithDescription("Call any Unity command by name."),
		mcp.WithString("cmd"),
		mcp.WithString("args"),
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
		resp, err := state.send(cmd, args)
		if err != nil {
			return nil, err
		}
		out, _ := json.MarshalIndent(resp, "", "  ")
		return mcp.NewToolResultText(string(out)), nil
	})

	helpTool := mcp.NewTool("help",
		mcp.WithDescription("Get full description and argument details for any command."),
		mcp.WithString("command", mcp.Description("Command name to look up")),
	)
	s.AddTool(helpTool, func(ctx context.Context, req mcp.CallToolRequest) (*mcp.CallToolResult, error) {
		args, _ := req.Params.Arguments.(map[string]any)
		name, _ := args["command"].(string)
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
	resp, err := state.send("commands", nil)
	if err != nil {
		return fmt.Errorf("commands call failed: %w", err)
	}
	raw, _ := resp["commands"].([]any)
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
			out, _ := json.MarshalIndent(resp, "", "  ")
			return mcp.NewToolResultText(string(out)), nil
		})
	}
}

// scalePNG decodes a PNG and re-encodes it scaled so the longest edge is at
// most maxSize pixels, preserving aspect ratio. Returns the original bytes on
// any decode error so the caller can still return something.
func scalePNG(data []byte, maxSize int) ([]byte, error) {
	src, err := png.Decode(bytes.NewReader(data))
	if err != nil {
		return nil, err
	}
	b := src.Bounds()
	w, h := b.Dx(), b.Dy()
	if w <= maxSize && h <= maxSize {
		return data, nil
	}
	scale := float64(maxSize) / math.Max(float64(w), float64(h))
	newW := int(math.Round(float64(w) * scale))
	newH := int(math.Round(float64(h) * scale))

	dst := image.NewRGBA(image.Rect(0, 0, newW, newH))
	scaleX := float64(w) / float64(newW)
	scaleY := float64(h) / float64(newH)
	for y := 0; y < newH; y++ {
		for x := 0; x < newW; x++ {
			srcX := int(float64(x)*scaleX) + b.Min.X
			srcY := int(float64(y)*scaleY) + b.Min.Y
			dst.Set(x, y, src.At(srcX, srcY))
		}
	}

	var buf bytes.Buffer
	if err := png.Encode(&buf, dst); err != nil {
		return nil, err
	}
	return buf.Bytes(), nil
}
