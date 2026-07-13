---
name: unity-task
description: Execute a Unity task using AgentBridge MCP, tracking issues in improvements.md for iterative refinement.
---

We are improving the AgentBridge system. Completing a Unity task using the AgentBridge MCP server in the Example project. Follow these steps:

1. **Identify the correct MCP server** from `.mcp.json` in the project directory.

2. **Set up the Example project** by removing all assets, discarding the current scene, and creating a new scene. This ensures a clean slate for the task.

3. **Maintain `improvements.md`** in the project root throughout the task. We want to improve AgentBridge, so log anything that:
   - Didn't go smoothly
   - Required a workaround
   - Could be done better
   - Revealed a gap in tooling or workflow
   - Had to be tried a number of times in different ways.

   Format each entry as:
   ```
   ## <short title>
   **What happened:** ...
   **Suggested improvement:** ...
   ```

4. **Complete the task** using AgentBridge tools (hierarchy inspection, component wiring, script creation, scene saving, etc.).

5. **After completion**, read `improvements.md` aloud and summarise the top issues found. Ask the user if they want to address any before moving on.

---

The task is:

{{args}}
