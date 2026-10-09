package main

import "testing"

func TestNotificationMessage(t *testing.T) {
	cases := []struct {
		name    string
		payload map[string]any
		want    string
	}{
		{"uses message from Unity", map[string]any{"type": "compile_started", "message": "Script compilation started."}, "Script compilation started."},
		{"falls back to type when message is missing", map[string]any{"type": "compile_started"}, "Unity notification: compile_started"},
		{"falls back to type when message is empty", map[string]any{"type": "play_mode_entered", "message": ""}, "Unity notification: play_mode_entered"},
		{"never returns empty", map[string]any{}, "Unity notification"},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			if got := notificationMessage(tc.payload); got != tc.want {
				t.Errorf("notificationMessage() = %q, want %q", got, tc.want)
			}
		})
	}
}
