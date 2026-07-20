 Missing Features:

  Highest: Inline image return from screenshot
  Every competitor (Blender, Godot, Unreal, all Unity MCP servers) returns base64 PNG directly in the tool response. AgentBridge returns a file path, so the LLM cannot see the image at all.

  High: execute_script (Roslyn C#)
  Blender has execute_python, every Unreal server has it too. It's the universal escape hatch when a dedicated tool doesn't exist. Without it, any missing command is a dead end for the agent.

  Implementation notes (July 2026):
  - Unity's .NET runtime does NOT implement AssemblyLoadContext.LoadFromStream, which Roslyn scripting requires to load compiled results. CSharpScript.EvaluateAsync fails at the loading step for any input, even simple expressions.
  - netstandard2.0 Roslyn DLLs compile fine in Unity but hit LoadFromStream at runtime. net8.0 DLLs fail to compile because Unity's C# compiler targets an older System.Runtime version.
  - The fix is: switch from CSharpScript.EvaluateAsync to CSharpCompilation, emit to a temp file on disk, and load via Assembly.LoadFrom(path). That bypasses LoadFromStream entirely.
  - Only Microsoft.CodeAnalysis.dll and Microsoft.CodeAnalysis.CSharp.dll (netstandard2.0, 4.10.0) need to be bundled; the Scripting DLLs are not needed with this approach.
  - Partially implemented: DLLs are in Editor/Plugins/Roslyn/, ScriptCommand.cs and tests exist but the eval step is still broken. Resume by replacing CSharpScript.EvaluateAsync with CSharpCompilation + emit-to-file + Assembly.LoadFrom.

  High: Dedicated transform tools
  set_transform, move, rotate, scale as first-class tools. Every competitor has these. Using component_set with raw serialized field names (m_LocalPosition) is fragile.

  Medium: duplicate_object + reparent_object
  Common authoring operations; both CoplayDev and CoderGamester have them.

  Medium: Play-mode input simulation
  Keyboard/mouse/action injection while game is running. Godot-AI and agent-bridge-for-unity have this.

  Medium: MCP Resources + subscriptions
  The MCP spec supports push notifications (resources/updated). A "compile error" or "scene changed" push would let agents react without polling.


New Features: 

Expose some Profiler API

  A few simple commands would cover most cases:

  - profiler_start / profiler_stop: begin and end a capture session
  - profiler_get_frame: return the sample hierarchy for a named frame or the last frame, as JSON with name, selfMs, totalMs, children
  - profiler_find: search by sample name prefix (e.g. "SG.") and return matching rows across all captured frames
  - profiler_get_samples - return flat list of samples, filtered by name prefix, with name, totalMs, selfMs, callCount
  - profiler_clear - clear recorded frames
  - profiler_set_deep - toggle deep profiling on/off separately from capturing

  The main value: I could trigger a generation, pull the timing data directly, and reason about it without you needing to copy anything manually. It would also let me run before/after comparisons automatically when optimising.

  The tricky part is that Deep Profile data is large. Filtering by name prefix on the Unity side before sending it over would keep the payload manageable.

  The most useful shape for the data would be a flat list of samples filtered by prefix, each with name, totalMs, selfMs, and callCount. That gives enough to spot where time is going without needing the full tree.

