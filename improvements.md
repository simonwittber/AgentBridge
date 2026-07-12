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

## No scene saved before new scene was created
**What happened:** Created a new empty scene to replace FireScene without saving the prior scene state. Any work in FireScene was discarded.
**Suggested improvement:** Always call `scene_save` before `scene_new` unless the old scene is intentionally throwaway.
