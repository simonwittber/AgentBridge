using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    public static class ScriptCommand
    {
        static ScriptCommand() => AgentBridge.Register(new ExecuteScriptCmd());

        internal static string FindDotnet()
        {
            var exe  = Application.platform == RuntimePlatform.WindowsEditor ? "dotnet.exe" : "dotnet";
            var path = Path.Combine(EditorApplication.applicationContentsPath, "NetCoreRuntime", exe);
            return File.Exists(path) ? path : null;
        }

        internal static string FindCsc()
        {
            var sdkRoot = Path.Combine(EditorApplication.applicationContentsPath, "DotNetSdk", "sdk");
            if (!Directory.Exists(sdkRoot)) return null;
            foreach (var dir in Directory.GetDirectories(sdkRoot).OrderByDescending(d => d))
            {
                var csc = Path.Combine(dir, "Roslyn", "bincore", "csc.dll");
                if (File.Exists(csc)) return csc;
            }
            return null;
        }

        internal static string WrapCode(string code) =>
            "using System;\n" +
            "using System.Collections.Generic;\n" +
            "using UnityEngine;\n" +
            "using UnityEditor;\n" +
            "public static class AgentScript {\n" +
            "    public static string Run() {\n" +
            code + "\n" +
            "    }\n" +
            "}\n";

        internal static Assembly CompileAndLoad(string source, out string error)
        {
            error      = null;
            var dotnet = FindDotnet();
            var csc    = FindCsc();
            if (dotnet == null) { error = "dotnet not found in Unity install."; return null; }
            if (csc    == null) { error = "csc.dll not found in Unity install."; return null; }

            var tempDir = Path.Combine(Path.GetTempPath(), "AgentBridgeScript", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var srcPath = Path.Combine(tempDir, "Script.cs");
                var outPath = Path.Combine(tempDir, "Script.dll");
                var rspPath = Path.Combine(tempDir, "csc.rsp");

                File.WriteAllText(srcPath, source, Encoding.UTF8);
                File.WriteAllText(rspPath, BuildRsp(srcPath, outPath), Encoding.UTF8);

                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                var psi = new ProcessStartInfo
                {
                    FileName               = dotnet,
                    Arguments              = $"\"{Esc(csc)}\" -noconfig /shared:false @\"{Esc(rspPath)}\"",
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    WorkingDirectory       = tempDir,
                };

                using var process = new Process { StartInfo = psi };
                process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                process.ErrorDataReceived  += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                try { process.Start(); }
                catch (Exception ex) { error = $"Failed to start compiler: {ex.Message}"; return null; }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (!process.HasExited && DateTime.UtcNow < deadline)
                    System.Threading.Thread.Sleep(25);

                if (!process.HasExited)
                {
                    try { process.Kill(); } catch { }
                    error = "Compilation timed out after 15s.";
                    return null;
                }

                System.Threading.Thread.Sleep(25); // let async reads drain

                if (process.ExitCode != 0)
                {
                    var output = (stdout.ToString() + stderr.ToString()).Trim();
                    error = string.IsNullOrEmpty(output)
                        ? $"Compiler exited with code {process.ExitCode}."
                        : output;
                    return null;
                }

                if (!File.Exists(outPath)) { error = "Compiler produced no output file."; return null; }

                return Assembly.Load(File.ReadAllBytes(outPath));
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static string BuildRsp(string srcPath, string outPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("-nologo");
            sb.AppendLine("-target:library");
            sb.AppendLine("-langversion:preview");
            sb.AppendLine("-nostdlib");
            sb.AppendLine("-optimize-");
            sb.AppendLine("-debug-");
            sb.AppendLine($"-out:\"{Esc(outPath)}\"");

            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                try
                {
                    var loc = asm.Location;
                    if (!string.IsNullOrEmpty(loc) && File.Exists(loc) && seen.Add(loc))
                        sb.AppendLine($"-r:\"{Esc(loc)}\"");
                }
                catch { }
            }

            sb.AppendLine($"\"{Esc(srcPath)}\"");
            return sb.ToString();
        }

        private static string Esc(string s) => s.Replace("\"", "\\\"");

        // ── execute_script ────────────────────────────────────────────────────

        private sealed class ExecuteScriptCmd : IAgentCommand
        {
            public string    Cmd         => "execute_script";
            public string    Description => "Compile and run a C# snippet in the Unity Editor context. The code body is placed inside a static Run() method with access to UnityEngine and UnityEditor. Return a string or leave void.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("code", "string", "", "C# statements to execute inside the Run() method body."),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);
                if (string.IsNullOrEmpty(p.code))
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = "code is required";
                    return err;
                }

                var source = ScriptCommand.WrapCode(p.code);
                var logs   = new JsonArray();

                Application.LogCallback capture = (msg, _, type) =>
                    logs.Add(new JsonObject { ["type"] = type.ToString(), ["message"] = msg });

                Application.logMessageReceived += capture;
                string returnValue = null;
                try
                {
                    var assembly = ScriptCommand.CompileAndLoad(source, out var compileError);
                    if (assembly == null)
                    {
                        var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                        err["message"] = compileError;
                        return err;
                    }

                    var type   = assembly.GetType("AgentScript");
                    var method = type?.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                    if (method == null)
                    {
                        var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                        err["message"] = "Compiled assembly is missing AgentScript.Run()";
                        return err;
                    }

                    var result = method.Invoke(null, null);
                    returnValue = result?.ToString();
                }
                catch (TargetInvocationException tex)
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = tex.InnerException?.Message ?? tex.Message;
                    return err;
                }
                catch (Exception ex)
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = ex.Message;
                    return err;
                }
                finally
                {
                    Application.logMessageReceived -= capture;
                }

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["logs"]        = logs;
                resp["returnValue"] = returnValue;
                return resp;
            }

            [Serializable]
            private class Params { public string code = ""; }
        }
    }
}
