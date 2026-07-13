package main

import (
	"context"
	"encoding/json"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"sort"

	"github.com/mark3labs/mcp-go/mcp"
	"github.com/mark3labs/mcp-go/server"
)

func registerProjectTools(cfg Config, s *server.MCPServer) {
	findTool := mcp.NewTool("find_unity_installs",
		mcp.WithDescription("List Unity Editor installations found on this machine."),
	)
	s.AddTool(findTool, func(ctx context.Context, req mcp.CallToolRequest) (*mcp.CallToolResult, error) {
		installs := findUnityInstalls()
		out, _ := json.MarshalIndent(map[string]any{"installs": installs}, "", "  ")
		return mcp.NewToolResultText(string(out)), nil
	})

	createTool := mcp.NewTool("create_project",
		mcp.WithDescription("Create a new Unity project at the given path. Unity opens in the background."),
		mcp.WithString("path", mcp.Description("Absolute path where the new project should be created.")),
		mcp.WithString("unity_path", mcp.Description("Path to the Unity executable. Auto-detected if omitted.")),
	)
	s.AddTool(createTool, func(ctx context.Context, req mcp.CallToolRequest) (*mcp.CallToolResult, error) {
		args, _ := req.Params.Arguments.(map[string]any)
		path, _ := args["path"].(string)
		if path == "" {
			return nil, fmt.Errorf("path is required")
		}
		unityExe, err := resolveUnityExe(cfg, args)
		if err != nil {
			return nil, err
		}
		absPath, err := filepath.Abs(path)
		if err != nil {
			return nil, fmt.Errorf("invalid path: %w", err)
		}
		cmd := exec.Command(unityExe, "-createProject", absPath)
		if err := cmd.Start(); err != nil {
			return nil, fmt.Errorf("failed to launch Unity: %w", err)
		}
		out, _ := json.MarshalIndent(map[string]any{
			"status":     "ok",
			"message":    "Unity is creating the project",
			"path":       absPath,
			"unity_path": unityExe,
			"pid":        cmd.Process.Pid,
		}, "", "  ")
		return mcp.NewToolResultText(string(out)), nil
	})

	openTool := mcp.NewTool("open_project",
		mcp.WithDescription("Open an existing Unity project. Unity opens in the background."),
		mcp.WithString("path", mcp.Description("Absolute path to the Unity project to open.")),
		mcp.WithString("unity_path", mcp.Description("Path to the Unity executable. Auto-detected if omitted.")),
	)
	s.AddTool(openTool, func(ctx context.Context, req mcp.CallToolRequest) (*mcp.CallToolResult, error) {
		args, _ := req.Params.Arguments.(map[string]any)
		path, _ := args["path"].(string)
		if path == "" {
			return nil, fmt.Errorf("path is required")
		}
		unityExe, err := resolveUnityExe(cfg, args)
		if err != nil {
			return nil, err
		}
		absPath, err := filepath.Abs(path)
		if err != nil {
			return nil, fmt.Errorf("invalid path: %w", err)
		}
		cmd := exec.Command(unityExe, "-projectPath", absPath)
		if err := cmd.Start(); err != nil {
			return nil, fmt.Errorf("failed to launch Unity: %w", err)
		}
		out, _ := json.MarshalIndent(map[string]any{
			"status":     "ok",
			"message":    "Unity is opening the project",
			"path":       absPath,
			"unity_path": unityExe,
			"pid":        cmd.Process.Pid,
		}, "", "  ")
		return mcp.NewToolResultText(string(out)), nil
	})
}

func resolveUnityExe(cfg Config, args map[string]any) (string, error) {
	if p, _ := args["unity_path"].(string); p != "" {
		return p, nil
	}
	if cfg.UnityPath != "" {
		return cfg.UnityPath, nil
	}
	if p := os.Getenv("UNITY_EDITOR"); p != "" {
		return p, nil
	}
	installs := findUnityInstalls()
	if len(installs) > 0 {
		return installs[len(installs)-1]["path"].(string), nil
	}
	return "", fmt.Errorf("Unity executable not found; specify unity_path, set UNITY_EDITOR, or use --unity flag")
}

func findUnityInstalls() []map[string]any {
	var bases []string
	switch runtime.GOOS {
	case "windows":
		bases = []string{`C:\Program Files\Unity\Hub\Editor`}
	case "darwin":
		bases = []string{"/Applications/Unity/Hub/Editor"}
	default:
		home, _ := os.UserHomeDir()
		bases = []string{filepath.Join(home, "Unity/Hub/Editor")}
	}

	var results []map[string]any
	for _, base := range bases {
		entries, err := os.ReadDir(base)
		if err != nil {
			continue
		}
		for _, e := range entries {
			if !e.IsDir() {
				continue
			}
			version := e.Name()
			exe := unityExePath(base, version)
			if _, statErr := os.Stat(exe); statErr == nil {
				results = append(results, map[string]any{
					"version": version,
					"path":    exe,
				})
			}
		}
	}
	sort.Slice(results, func(i, j int) bool {
		return results[i]["version"].(string) < results[j]["version"].(string)
	})
	return results
}

func unityExePath(base, version string) string {
	switch runtime.GOOS {
	case "windows":
		return filepath.Join(base, version, "Editor", "Unity.exe")
	case "darwin":
		return filepath.Join(base, version, "Unity.app", "Contents", "MacOS", "Unity")
	default:
		return filepath.Join(base, version, "Editor", "Unity")
	}
}
