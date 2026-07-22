using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    public class AgentLogWindow : EditorWindow
    {
        private const int    MaxEntries   = 500;
        private const double PollInterval = 0.5;

        private readonly List<LogEntry> _entries  = new();
        private          Vector2        _scroll;
        private          long           _readPos;
        private          double         _nextPoll;
        private          bool           _autoScroll = true;

        [MenuItem("Window/General/LLM Agent Log")]
        public static void Open() => GetWindow<AgentLogWindow>("LLM Agent Log");

        private void OnEnable()
        {
            // Read from the beginning to restore history across domain reloads.
            _readPos  = 0;
            _nextPoll = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnUpdate;
        }

        private void OnDisable() => EditorApplication.update -= OnUpdate;

        private void OnUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextPoll) return;
            _nextPoll = now + PollInterval;

            if (!File.Exists(AgentBridge.LogPath)) return;

            try
            {
                using var stream = new FileStream(AgentBridge.LogPath, FileMode.Open, FileAccess.Read,
                                                  FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length < _readPos) _readPos = 0; // rotated
                if (stream.Length == _readPos) return;

                stream.Seek(_readPos, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                string line;
                long safePos = _readPos;
                bool changed = false;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    _entries.Add(Parse(line));
                    if (_entries.Count > MaxEntries)
                        _entries.RemoveAt(0);
                    safePos = stream.Position;
                    changed = true;
                }
                _readPos = safePos;
                if (changed) Repaint();
            }
            catch (IOException) { }
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawLog();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _autoScroll = GUILayout.Toggle(_autoScroll, "Auto-scroll", EditorStyles.toolbarButton, GUILayout.Width(80));
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
                _entries.Clear();
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{_entries.Count} entries", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private static readonly GUILayoutOption W_TIME   = GUILayout.Width(60);
        private static readonly GUILayoutOption W_CMD    = GUILayout.Width(160);
        private static readonly GUILayoutOption W_UID    = GUILayout.Width(65);
        private static readonly GUILayoutOption W_STATUS = GUILayout.Width(40);

        private void DrawLog()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            var style = EditorStyles.miniLabel;
            foreach (var e in _entries)
            {
                Color prev = GUI.color;
                GUI.color = e.Color;
                if (e.IsMarker)
                {
                    GUILayout.Label(e.Extra, style);
                }
                else
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(e.Time,   style, W_TIME);
                    GUILayout.Label(e.Cmd,    style, W_CMD);
                    GUILayout.Label(e.Uid,    style, W_UID);
                    GUILayout.Label(e.Status, style, W_STATUS);
                    GUILayout.Label(e.Extra,  style);
                    EditorGUILayout.EndHorizontal();
                }
                GUI.color = prev;
            }

            if (_autoScroll && Event.current.type == EventType.Repaint)
                _scroll.y = float.MaxValue;

            EditorGUILayout.EndScrollView();
        }

        private static LogEntry Parse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("_marker", out var marker))
                {
                    long ts = root.TryGetProperty("_ts", out var tsEl) ? tsEl.GetInt64() : 0;
                    string time = ts > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime.ToString("HH:mm:ss")
                        : "??:??:??";
                    string markerVal = marker.GetString() ?? "";
                    if (markerVal == "domain_reload_start")
                    {
                        string pending = root.TryGetProperty("pending_cmd", out var pc) ? pc.GetString() : "";
                        string suffix  = string.IsNullOrEmpty(pending) ? "" : $"  (interrupted: {pending})";
                        return LogEntry.Marker($"[{time}]  ── domain reload starting{suffix} ──", new Color(1f, 0.5f, 0.1f));
                    }
                    return LogEntry.Marker($"[{time}]  ── domain reload complete ──────────────", new Color(0.5f, 0.8f, 1f));
                }

                long logTs = root.TryGetProperty("_ts", out var logTsEl) ? logTsEl.GetInt64() : 0;
                string timestamp = logTs > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(logTs).LocalDateTime.ToString("HH:mm:ss")
                    : DateTime.Now.ToString("HH:mm:ss");

                string uid = root.TryGetProperty("uid",    out var u) ? u.GetString() ?? "?" : "?";
                string cmd = root.TryGetProperty("cmd",    out var c) ? c.GetString() ?? "?" : "?";
                string sts = root.TryGetProperty("status", out var s) ? s.GetString() ?? ""  : "";

                string extra = "";
                if (sts == "error" && root.TryGetProperty("message", out var msgEl))
                {
                    string msg = msgEl.GetString() ?? "";
                    if (msg != "") extra = msg;
                }
                if (root.TryGetProperty("_req_args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object)
                {
                    var parts = new List<string>();
                    foreach (var prop in argsEl.EnumerateObject())
                    {
                        string val = prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString() ?? ""
                            : prop.Value.ToString();
                        if (!string.IsNullOrEmpty(val))
                            parts.Add($"{prop.Name}={val}");
                    }
                    if (parts.Count > 0)
                    {
                        string argsStr = string.Join(" ", parts);
                        const int maxArgs = 100;
                        if (argsStr.Length > maxArgs) argsStr = argsStr[..maxArgs] + "…";
                        extra += (extra.Length > 0 ? "  " : "") + argsStr;
                    }
                }

                Color color = sts == "error" ? new Color(1f, 0.4f, 0.4f)
                            : sts == "ok"    ? new Color(0.6f, 1f, 0.6f)
                            :                  Color.white;

                return new LogEntry(timestamp, cmd, uid[..Math.Min(8, uid.Length)], sts, extra, color);
            }
            catch
            {
                return LogEntry.Marker($"(unparseable) {json[..Math.Min(80, json.Length)]}", Color.gray);
            }
        }

        private readonly struct LogEntry
        {
            public readonly string Time, Cmd, Uid, Status, Extra;
            public readonly Color  Color;
            public readonly bool   IsMarker;

            public LogEntry(string time, string cmd, string uid, string status, string extra, Color color)
            { Time = time; Cmd = cmd; Uid = uid; Status = status; Extra = extra; Color = color; IsMarker = false; }

            private LogEntry(string text, Color color, bool marker)
            { Time = ""; Cmd = ""; Uid = ""; Status = ""; Extra = text; Color = color; IsMarker = marker; }

            public static LogEntry Marker(string text, Color color) => new(text, color, true);
        }
    }
}
