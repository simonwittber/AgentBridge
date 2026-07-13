# AgentBridge Task Improvements

## Reimport All caused Unity to close and reopen
**What happened:** Called `Assets/Reimport All` menu item hoping to trigger package resolution. This caused Unity to fully close and reopen, losing scene state.
**Suggested improvement:** Never use `Reimport All` for package resolution. Use `Assets/Refresh` (or just focus Unity) and wait for the Package Manager to detect manifest.json changes automatically.

## Package module names require research before use
**What happened:** Added `com.unity.modules.ugui` to manifest.json — this package name doesn't exist in Unity 6. The correct package is `com.unity.ugui`. Also didn't know the correct version (it resolved to 2.6.0 in this project).
**Suggested improvement:** Before editing manifest.json, check the packages-lock.json and the Unity installation's BuiltInPackages directory to confirm exact package IDs and versions. Alternatively, check if the package is already a transitive dependency in the lock file before adding it explicitly.

## Compile errors not caught before scene setup
**What happened:** Started setting up the scene (creating GameObjects, adding components) before confirming the scripts actually compiled successfully in the target project. The `compile` command returned "no errors" but was returning stale state — the actual errors were in the Unity console.
**Suggested improvement:** After writing scripts, always verify via `console_logs` (type=error) rather than trusting `compile` alone. The compile command may report stale results if Unity hasn't reloaded the domain yet.

## Particle System and UI modules missing from Example project
**What happened:** The Example~ project manifest didn't include `com.unity.modules.particlesystem` or UGUI, causing compile failures for the fireworks scripts.
**Suggested improvement:** At the start of any task that uses common Unity modules (ParticleSystem, UI, Physics), check manifest.json first and add missing modules before writing scripts.

## Screenshot tool does not capture ScreenSpaceOverlay UI
**What happened:** The `screenshot` command renders the main camera but ScreenSpaceOverlay canvases are not included in the output. This made it impossible to visually verify the Exit button and Slider were correctly positioned/styled.
**Suggested improvement:** Add a note to the screenshot tool docs that ScreenSpaceOverlay UI won't appear. For UI verification, prefer hierarchy inspection + component_get on RectTransform to confirm anchoring.

## Square particles when creating Material without a texture
**What happened:** Creating `new Material(shader)` for an additive particle shader without assigning a texture results in particles rendering as white squares instead of soft circles.
**Fix applied:** `MakeStarMaterial()` now calls `AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.png")` first — this is the correct Editor-only API. `Resources.GetBuiltinResource` is the runtime equivalent and does not work for Unity's built-in extra resources in Unity 6+. Falls back to a procedural soft-circle PNG asset if the built-in texture is unavailable.

## Y-axis upside-down with manual VP matrix on DX11
**What happened:** Passed `Camera.projectionMatrix * Camera.worldToCameraMatrix` as a manual `_VP` uniform. In Unity's built-in pipeline on DX11, this matrix has platform-specific Y conventions that caused the entire scene to render upside down in the Game view. `GL.GetGPUProjectionMatrix` also didn't fully resolve it.
**Suggested improvement:** For procedural GPU particle shaders, always use Unity's built-in `UNITY_MATRIX_VP` and `UNITY_MATRIX_V` from `UnityCG.cginc` rather than passing a manually-constructed VP matrix. These are already set up correctly for the current platform and render target.

## Uninitialized ComputeBuffer slots render as dim stars
**What happened:** `new ComputeBuffer(total, stride)` zero-fills all slots. Since `type=0` is the star type, rocket and spark slots render as stars at world origin (0,0,0) until overwritten, creating a dim glow artifact at screen center.
**Suggested improvement:** Add an `InitDead` compute kernel that marks non-star slots as `type=1`/`type=2` with `life=0` and `size=0` immediately after buffer creation.

## Runtime errors not surfaced in responses — task declared complete with broken output
**What happened:** After completing the main menu task, the menu was not visible and the console had flooding "Particle Velocity curves must all be in the same mode" errors and a NullReferenceException. The LLM declared the task complete without noticing either problem. Two separate detection gaps allowed this:
1. `compile_errors` was already present in every `MakeResponse` and in `session.json`, but the LLM never checked it. When a recompile produced 7 errors mid-session, the LLM continued as if nothing was wrong.
2. Runtime console errors (errors, exceptions) had no equivalent field. `ConsoleBridge` maintained a ring buffer but nothing injected the error count into responses. The particle velocity errors were spamming from the moment the starfield was configured in edit mode (`prewarm = true`) but were invisible to the LLM.
**Fix applied:** Added `console_errors` (count of error/exception log messages since domain reload) to `MakeResponse` and `session.json`, parallel to `compile_errors`. The LLM now sees both fields on every response and can act on either without needing to remember to poll `console_logs`.
**Convention:** Any non-zero `compile_errors` or `console_errors` in a response must be investigated before proceeding.

## No scene saved before new scene was created
**What happened:** Created a new empty scene to replace FireScene without saving the prior scene state. Any work in FireScene was discarded.
**Suggested improvement:** Always call `scene_save` before `scene_new` unless the old scene is intentionally throwaway.

## scene_new with unsaved changes disconnects MCP server
**What happened:** Calling `scene_new` when the scene had unsaved changes caused Unity to show a "Save current scene?" dialog. The MCP bridge was blocked waiting for a response and the server disconnected entirely. The user had to manually dismiss the dialog and reconnect via `/mcp`.
**Fix applied:** `session.json` heartbeat now includes `scene_dirty` (bool). The LLM can read this before calling `scene_new` or `scene_open` and proactively call `scene_save` first if true. Also added `active_scene`, `play_mode`, and `compile_errors` to the heartbeat while at it.
**Residual:** The bridge itself still hangs on modal dialogs — a deeper fix would require detecting the dialog state natively.

## component_set scene path resolves to GameObject, not component
**What happened:** Tried to set a `Button` field on `MainMenuController` using `{"scene": "Canvas/MenuPanel/SettingsButton"}`. The field remained null — the tool resolved the path to a GameObject reference but the field type is `Button` (a component), so the assignment was silently discarded.
**Fix applied:** `SceneBridge.JsonToSp` now uses reflection to find the declared field type of the `SerializedProperty`. If the type is a `Component` subclass, it calls `go.GetComponent(fieldType)` and returns an error (with a warning log) if the component isn't found, rather than silently assigning the wrong object.

## SerializedObject button wiring silently dropped one field
**What happened:** `WireController` in the Editor builder used `SerializedObject` to assign four button references at once via `ApplyModifiedProperties`. After the build, `settingsButton` came back as null while the other three were set correctly.
**Root cause (best hypothesis):** After ~16 prior `AddComponent` calls, Unity's native serialization layer had not fully flushed the new `MainMenuController` component by the time `new SerializedObject(ctrl)` was created. Without an initial `so.Update()` call the `SerializedObject` was operating on a partially-initialised snapshot, causing the third field assignment to be lost.
**Fix applied:** Added `so.Update()` before setting any fields in `WireController`. Added post-`ApplyModifiedProperties` verification that logs an error for any null fields.

## ConsoleBridge discarded stack traces
**What happened:** `OnLog(string message, string _, LogType type)` discarded the `stackTrace` parameter. When a `NullReferenceException` occurred in play mode, the console_logs tool only returned the one-liner message with no stack trace, making root cause analysis impossible without the fix.
**Fix applied:** `ConsoleBridge` now stores `stackTrace` in a parallel `_stackTraces` ring buffer and includes it as `"stack_trace"` in the `console_logs` response when non-empty.

## `console_logs` type filter "error" misses exceptions
**What happened:** The `console_errors` field in every response counts both `LogType.Error` and `LogType.Exception`, but querying `console_logs` with `type=error` only returns "error" entries — exceptions are a separate type string. The mismatch caused the LLM to query for errors, get an empty list, and miss the active exception.
**Fix applied:** `OnLog` now stores `"error"` for both `LogType.Error` and `LogType.Exception`. The `type` arg description updated to remove `exception` as a valid filter value.

## StandaloneInputModule throws NPE in play mode with UGUI 2.0 (Unity 6)
**What happened:** Adding `StandaloneInputModule` via `AddComponent` to the same GameObject as `EventSystem` worked fine in edit mode but caused a `NullReferenceException` at `BaseInputModule.OnEnable():113` every time play mode was entered. Root cause: in UGUI 2.0, `BaseInputModule.OnEnable()` calls `GetComponent<EventSystem>().UpdateModules()`, but during play-mode scene load, `GetComponent<EventSystem>()` returns null (likely an initialization ordering race between the two components on the same GO during domain reload).
**Fix applied:** Removed `AddComponent<StandaloneInputModule>()` from the builder. UGUI 2.0's `EventSystem` selects an available input module automatically at runtime. Input worked correctly in play mode after removal.
**Suggested improvement:** When building EventSystem GameObjects, only add `EventSystem` — skip explicit `StandaloneInputModule` addition for UGUI 2.x projects.

## `AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.png")` fails in Unity 6
**What happened:** The builder used `AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.png")` to get the soft-circle particle texture (a pattern that worked in prior Unity versions). In Unity 6 this returns null and logs "The resource Default-Particle.png could not be loaded from the resource file!" — so particles rendered as squares.
**Suggested improvement:** Don't rely on built-in extra resource paths for the particle texture. Generate a procedural soft-circle texture at build time (`Texture2D.SetPixel` + `EncodeToPNG` + `AssetDatabase.ImportAsset`) and save it as a project asset. This approach is version-agnostic and self-contained.
