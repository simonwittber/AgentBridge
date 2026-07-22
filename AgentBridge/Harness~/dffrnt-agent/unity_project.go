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
	"time"

	"github.com/mark3labs/mcp-go/mcp"
	"github.com/mark3labs/mcp-go/server"
)

type unityInstall struct {
	Version string `json:"version"`
	Path    string `json:"path"`
}

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
		return launchUnityProject(cfg, args, "-createProject", "Unity is creating the project")
	})

	openTool := mcp.NewTool("open_project",
		mcp.WithDescription("Open an existing Unity project. Unity opens in the background."),
		mcp.WithString("path", mcp.Description("Absolute path to the Unity project to open.")),
		mcp.WithString("unity_path", mcp.Description("Path to the Unity executable. Auto-detected if omitted.")),
	)
	s.AddTool(openTool, func(ctx context.Context, req mcp.CallToolRequest) (*mcp.CallToolResult, error) {
		args, _ := req.Params.Arguments.(map[string]any)
		path, _ := args["path"].(string)
		if path != "" {
			absPath, _ := filepath.Abs(path)
			sessionFile := filepath.Join(absPath, "Temp", "agent", "session.json")
			if info, err := os.Stat(sessionFile); err == nil && time.Since(info.ModTime()) < 15*time.Second {
				out, _ := json.MarshalIndent(map[string]any{
					"status":  "already_running",
					"message": "Unity is already running with this project",
					"path":    absPath,
				}, "", "  ")
				return mcp.NewToolResultText(string(out)), nil
			}
		}
		return launchUnityProject(cfg, args, "-projectPath", "Unity is opening the project")
	})
}

func launchUnityProject(cfg Config, args map[string]any, flag, message string) (*mcp.CallToolResult, error) {
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
	cmd := exec.Command(unityExe, flag, absPath)
	if err := cmd.Start(); err != nil {
		return nil, fmt.Errorf("failed to launch Unity: %w", err)
	}
	go cmd.Wait() //nolint:errcheck — detached process; Wait reaps the zombie on Unix
	out, _ := json.MarshalIndent(map[string]any{
		"status":     "ok",
		"message":    message,
		"path":       absPath,
		"unity_path": unityExe,
		"pid":        cmd.Process.Pid,
	}, "", "  ")
	return mcp.NewToolResultText(string(out)), nil
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
		return installs[len(installs)-1].Path, nil
	}
	return "", fmt.Errorf("Unity executable not found; specify unity_path, set UNITY_EDITOR, or use --unity flag")
}

func findUnityInstalls() []unityInstall {
	installs := findUnityInstallsFromHub()
	if len(installs) == 0 {
		installs = findUnityInstallsByDirScan()
	}
	sort.Slice(installs, func(i, j int) bool {
		return installs[i].Version < installs[j].Version
	})
	return installs
}

func hubDataDir() string {
	configDir, err := os.UserConfigDir()
	if err != nil {
		return ""
	}
	return filepath.Join(configDir, "UnityHub")
}

type editorsV2File struct {
	Data []struct {
		Version  string   `json:"version"`
		Location []string `json:"location"`
	} `json:"data"`
}

func findUnityInstallsFromHub() []unityInstall {
	hubDir := hubDataDir()
	if hubDir == "" {
		return nil
	}

	for _, name := range []string{"editors-v2.json", "editors.json"} {
		data, err := os.ReadFile(filepath.Join(hubDir, name))
		if err != nil {
			continue
		}
		var f editorsV2File
		if err := json.Unmarshal(data, &f); err != nil || len(f.Data) == 0 {
			continue
		}

		seen := map[string]bool{}
		var results []unityInstall
		for _, entry := range f.Data {
			if len(entry.Location) == 0 {
				continue
			}
			exe := filepath.FromSlash(entry.Location[0])
			if seen[exe] {
				continue
			}
			seen[exe] = true
			if _, err := os.Stat(exe); err != nil {
				continue
			}
			results = append(results, unityInstall{Version: entry.Version, Path: exe})
		}
		if len(results) > 0 {
			return results
		}
	}
	return nil
}

func findUnityInstallsByDirScan() []unityInstall {
	var bases []string
	switch runtime.GOOS {
	case "windows":
		bases = []string{
			`C:\Program Files\Unity\Hub\Editor`,
			`C:\Program Files\Unity`,
		}
	case "darwin":
		bases = []string{
			"/Applications/Unity/Hub/Editor",
			"/Applications/Unity",
		}
	default:
		home, _ := os.UserHomeDir()
		bases = []string{
			filepath.Join(home, "Unity/Hub/Editor"),
			filepath.Join(home, "Unity"),
		}
	}

	seen := map[string]bool{}
	var results []unityInstall
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
			if seen[exe] {
				continue
			}
			seen[exe] = true
			if _, err := os.Stat(exe); err == nil {
				results = append(results, unityInstall{Version: version, Path: exe})
			}
		}
	}
	return results
}

func unityExePath(base, version string) string {
	switch runtime.GOOS {
	case "windows":
		if hub := filepath.Join(base, version, "Editor", "Unity.exe"); fileExists(hub) {
			return hub
		}
		return filepath.Join(base, version, "Unity.exe")
	case "darwin":
		return filepath.Join(base, version, "Unity.app", "Contents", "MacOS", "Unity")
	default:
		return filepath.Join(base, version, "Editor", "Unity")
	}
}

func fileExists(path string) bool {
	_, err := os.Stat(path)
	return err == nil
}
