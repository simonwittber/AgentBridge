---
name: unity-task
description: Execute a Unity task using AgentBridge MCP, tracking issues in improvements.md for iterative refinement.
---

You are completing a Unity task using the AgentBridge MCP server for the relevant project. Follow these steps:

1. **Identify the correct MCP server** from `.mcp.json` in the project directory.

2. **Maintain `improvements.md`** in the project root throughout the task. Log anything that:
   - Didn't go smoothly
   - Required a workaround
   - Could be done better
   - Revealed a gap in tooling or workflow

   Format each entry as:
   ```
   ## <short title>
   **What happened:** ...
   **Suggested improvement:** ...
   ```

3. **Complete the task** using AgentBridge tools (hierarchy inspection, component wiring, script creation, scene saving, etc.).

4. **After completion**, read `improvements.md` aloud and summarise the top issues found. Ask the user if they want to address any before moving on.

---

The task is:

{{args}}
