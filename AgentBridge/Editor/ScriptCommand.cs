using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    public static class MainThread
    {
        public static T   Run<T>(Func<T> work) => AgentBridge.RunOnMainThread(work);
        public static void Run(Action work)    => AgentBridge.RunOnMainThread<int>(() => { work(); return 0; });
    }

    public static class ScriptCommand
    {
        internal static string FindDotnet()
        {
            // Unity 6000.x ships dotnet in NetCoreRuntime; Unity 7000.x moved it into DotNetSdk.
            var exe = Application.platform == RuntimePlatform.WindowsEditor ? "dotnet.exe" : "dotnet";
            foreach (var folder in new[] { "NetCoreRuntime", "DotNetSdk" })
            {
                var path = Path.Combine(EditorApplication.applicationContentsPath, folder, exe);
                if (File.Exists(path)) return path;
            }
            return null;
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
            "using MT = LLMDevTools.MainThread;\n" +
            "// Run() executes on the main thread. Unity API calls are safe.\n" +
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

                var dllBytes = File.ReadAllBytes(outPath);
                var pdbPath  = Path.ChangeExtension(outPath, ".pdb");
                var pdbBytes = File.Exists(pdbPath) ? File.ReadAllBytes(pdbPath) : null;
                return CollectibleLoader.Load(dllBytes, pdbBytes);
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
            sb.AppendLine("-debug+");
            sb.AppendLine($"-out:\"{Esc(outPath)}\"");

#pragma warning disable UAC0005, UAC0007
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
#pragma warning restore UAC0005, UAC0007

            sb.AppendLine($"\"{Esc(srcPath)}\"");
            return sb.ToString();
        }

        private static string Esc(string s) => s.Replace("\"", "\\\"");

        // Wraps AssemblyLoadContext via reflection so the asmdef needs no extra reference.
        // Falls back to Assembly.Load if the type is unavailable.
#if UNITY_7000_0_OR_NEWER
        [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
        internal static class CollectibleLoader
        {
            static readonly Type      _alcTypeFull  = Type.GetType("System.Runtime.Loader.AssemblyLoadContext, System.Runtime.Loader")
                                                   ?? Type.GetType("System.Runtime.Loader.AssemblyLoadContext");
            static readonly MethodInfo _loadStream  = _alcTypeFull?.GetMethod("LoadFromStream", new[] { typeof(Stream) });
            static readonly MethodInfo _loadStream2 = _alcTypeFull?.GetMethod("LoadFromStream", new[] { typeof(Stream), typeof(Stream) });
            static readonly MethodInfo _getCtx      = _alcTypeFull?.GetMethod("GetLoadContext", BindingFlags.Public | BindingFlags.Static);
            static readonly MethodInfo _unload      = _alcTypeFull?.GetMethod("Unload");
            static readonly EventInfo  _resolving   = _alcTypeFull?.GetEvent("Resolving");

            internal static Assembly Load(byte[] bytes, byte[] pdbBytes = null)
            {
                var ctor = _alcTypeFull?.GetConstructor(new[] { typeof(string), typeof(bool) });
                if (ctor == null || _loadStream == null)
                {
#pragma warning disable UAC0020
                    return pdbBytes != null
                        ? Assembly.Load(bytes, pdbBytes)
                        : Assembly.Load(bytes);
#pragma warning restore UAC0020
                }
                var alc = ctor.Invoke(new object[] { "AgentBridgeScript", true });
                AttachResolver(alc);
                if (pdbBytes != null && _loadStream2 != null)
                    return (Assembly)_loadStream2.Invoke(alc, new object[] { new MemoryStream(bytes), new MemoryStream(pdbBytes) });
                return (Assembly)_loadStream.Invoke(alc, new object[] { new MemoryStream(bytes) });
            }

            // Unity 7000.x loads engine and project assemblies into its own load context, not the default one.
            // The script's load context cannot find them by itself, so hand back the already-loaded assembly with the same name.
            static void AttachResolver(object alc)
            {
                if (_resolving == null) return;
                var method = typeof(CollectibleLoader).GetMethod(nameof(ResolveLoaded), BindingFlags.NonPublic | BindingFlags.Static);
                _resolving.AddEventHandler(alc, Delegate.CreateDelegate(_resolving.EventHandlerType, method));
            }

            static Assembly ResolveLoaded(object alc, AssemblyName name)
            {
#pragma warning disable UAC0005
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(asm.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase))
                        return asm;
                }
#pragma warning restore UAC0005
                return null;
            }

            internal static void Unload(Assembly asm)
            {
                if (asm == null || _getCtx == null || _unload == null) return;
                var ctx = _getCtx.Invoke(null, new object[] { asm });
                if (ctx != null) _unload.Invoke(ctx, null);
            }
        }

        internal static Exception EnrichThreadException(Exception ex)
        {
            if (ex?.Message?.Contains("main thread") == true)
                return new Exception(ex.Message + "\nUse MT.Run(() => ...) to call Unity API from the script.");
            return ex;
        }

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

                if (p.code.Contains("Thread.Sleep"))
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = "Thread.Sleep is not allowed: it blocks the Unity main thread.";
                    return err;
                }

                var source = ScriptCommand.WrapCode(p.code);
                var logs   = new JsonArray();

                Application.LogCallback capture = (msg, _, type) =>
                    logs.Add(new JsonObject { ["type"] = type.ToString(), ["message"] = msg });
                Application.logMessageReceivedThreaded += capture;

                Task.Run(() =>
                {
                    var assembly = ScriptCommand.CompileAndLoad(source, out var compileError);
                    if (assembly == null)
                    {
                        AgentBridge.RunOnMainThread<int>(() =>
                        {
                            Application.logMessageReceivedThreaded -= capture;
                            var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                            err["message"] = compileError;
                            AgentBridge.Respond(err);
                            return 0;
                        });
                        return;
                    }

                    var scriptType = assembly.GetType("AgentScript");
                    var method     = scriptType?.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                    if (method == null)
                    {
                        AgentBridge.RunOnMainThread<int>(() =>
                        {
                            Application.logMessageReceivedThreaded -= capture;
                            CollectibleLoader.Unload(assembly);
                            var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                            err["message"] = "Compiled assembly is missing AgentScript.Run()";
                            AgentBridge.Respond(err);
                            return 0;
                        });
                        return;
                    }

                    AgentBridge.RunOnMainThread<int>(() =>
                    {
                        AgentBridge.Notify("script_started", new JsonObject());

                        string returnValue = null;
                        Exception scriptEx = null;
                        try
                        {
                            var result = method.Invoke(null, null);
                            returnValue = result?.ToString();
                        }
                        catch (TargetInvocationException tex) { scriptEx = EnrichThreadException(tex.InnerException ?? tex); }
                        catch (Exception ex)                  { scriptEx = EnrichThreadException(ex); }

                        Application.logMessageReceivedThreaded -= capture;
                        CollectibleLoader.Unload(assembly);
                        if (scriptEx != null)
                        {
                            AgentBridge.Notify("script_failed", new JsonObject { ["message"] = scriptEx.Message });
                            var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                            err["message"] = scriptEx.Message;
                            AgentBridge.Respond(err);
                        }
                        else
                        {
                            AgentBridge.Notify("script_completed", new JsonObject { ["returnValue"] = returnValue });
                            var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                            resp["logs"]        = logs;
                            resp["returnValue"] = returnValue;
                            AgentBridge.Respond(resp);
                        }
                        return 0;
                    });
                });

                return null; // async — Task.Run calls Respond() when done
            }

            [Serializable]
            private class Params { public string code = ""; }
        }
    }
}
