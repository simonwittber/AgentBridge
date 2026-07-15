// agent — send a command to the Unity AgentBridge and wait for the response.
//
// Usage:
//
//	agent [--project <path>] [--timeout <s>] [--schema <file>] <cmd> [key=value ...]
//	agent [--project <path>] [--timeout <s>] [--schema <file>] serve
//
// Exit codes (CLI mode):
//
//	0  status == "ok"
//	1  status == "error" (Unity returned an error)
//	2  Unity not running (session check failed)
//	3  timeout or I/O error
package main

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strconv"
	"strings"
)

const (
	defaultProject = "."
	defaultTimeout = 120.0
	defaultSchema  = "agent_schema.json"
)

func defaultSchemaPath() string {
	exe, err := os.Executable()
	if err != nil {
		return defaultSchema
	}
	return filepath.Join(filepath.Dir(exe), defaultSchema)
}

func main() {
	cfg := Config{
		Project:    defaultProject,
		Timeout:    defaultTimeout,
		SchemaFile: defaultSchemaPath(),
	}
	var argsJSON string
	var cmd string
	extra := map[string]any{}

	i := 1
	for i < len(os.Args) {
		a := os.Args[i]
		switch a {
		case "--project", "-p":
			i++
			if i >= len(os.Args) {
				die("--project requires a value")
			}
			cfg.Project = os.Args[i]
		case "--timeout", "-t":
			i++
			if i >= len(os.Args) {
				die("--timeout requires a value")
			}
			t, err := strconv.ParseFloat(os.Args[i], 64)
			if err != nil {
				die("--timeout: not a number")
			}
			cfg.Timeout = t
		case "--schema":
			i++
			if i >= len(os.Args) {
				die("--schema requires a value")
			}
			cfg.SchemaFile = os.Args[i]
		case "--unity":
			i++
			if i >= len(os.Args) {
				die("--unity requires a value")
			}
			cfg.UnityPath = os.Args[i]
		case "--args":
			i++
			if i >= len(os.Args) {
				die("--args requires a value")
			}
			argsJSON = os.Args[i]
		default:
			if strings.HasPrefix(a, "-") {
				die("unknown flag: " + a)
			}
			if cmd == "" {
				cmd = a
			} else if k, v, ok := strings.Cut(a, "="); ok {
				extra[k] = v
			} else {
				die("unexpected argument: " + a)
			}
		}
		i++
	}

	if cmd == "" {
		fmt.Fprintln(os.Stderr, "Usage: agent [--project <path>] [--timeout <s>] [--schema <file>] <cmd> [key=value ...]")
		fmt.Fprintln(os.Stderr, "       agent [--project <path>] serve")
		os.Exit(1)
	}

	if cmd == "serve" {
		runServe(cfg)
		return
	}

	// CLI mode
	if argsJSON != "" {
		var m map[string]any
		if err := json.Unmarshal([]byte(argsJSON), &m); err != nil {
			die("--args: invalid JSON: " + err.Error())
		}
		for k, v := range m {
			extra[k] = v
		}
	}

	if err := checkSession(cfg.sessionPath()); err != nil {
		fmt.Fprintf(os.Stderr, "Unity not ready: %v\n", err)
		os.Exit(2)
	}

	resp, err := send(cfg, cmd, extra)
	if err != nil {
		fmt.Fprintf(os.Stderr, "agent: %v\n", err)
		os.Exit(3)
	}

	out, _ := json.MarshalIndent(resp, "", "  ")
	fmt.Println(string(out))
	if s, _ := resp["status"].(string); s == "ok" {
		os.Exit(0)
	}
	os.Exit(1)
}

func die(msg string) {
	fmt.Fprintln(os.Stderr, "agent: "+msg)
	os.Exit(1)
}
