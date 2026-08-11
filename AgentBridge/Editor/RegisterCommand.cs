using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    public sealed class RegisterCommandCmd : IAgentCommand
    {
        public string Cmd => "register_command";
        public string Description => "Register a custom command: compile C# code and save as a persistent IAgentCommand.";
        public ArgSpec[] Args => new[]
        {
            new ArgSpec("name", "string", "", "Command name (e.g., batch_import_assets). Must be alphanumeric with underscores."),
            new ArgSpec("description", "string", "", "Human-readable description of what the command does."),
            new ArgSpec("code", "string", "", "C# code for the command body. Becomes the Run() method."),
            new ArgSpec("args", "any", "[]", "JSON array of {name, type, default, description} parameter definitions. Types: string, int, float, bool."),
        };

        private static readonly string[] ReservedNames = new[]
        {
            "status", "list_commands", "compile", "refresh", "execute_script", "help",
            "screenshot", "console_logs", "profiler_start", "profiler_stop", "profiler_get_samples",
            "play_enter", "play_exit", "register_command", "delete_command"
        };

        private const string StorageDir = "Assets/AgentBridgeScripts";

        public JsonObject Execute(string uid, string requestJson)
        {
            var p = JsonUtility.FromJson<Params>(requestJson);

            var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");

            if (string.IsNullOrWhiteSpace(p.name))
            {
                resp["status"] = "error";
                resp["message"] = "name is required";
                return resp;
            }

            if (!IsValidCommandName(p.name))
            {
                resp["status"] = "error";
                resp["message"] = "name must contain only alphanumeric characters and underscores, and not start with a digit";
                return resp;
            }

            if (ReservedNames.Contains(p.name, StringComparer.OrdinalIgnoreCase))
            {
                resp["status"] = "error";
                resp["message"] = $"'{p.name}' is a reserved command name";
                return resp;
            }

            if (string.IsNullOrWhiteSpace(p.code))
            {
                resp["status"] = "error";
                resp["message"] = "code is required";
                return resp;
            }

            if (p.code.Contains("Thread.Sleep"))
            {
                resp["status"] = "error";
                resp["message"] = "Thread.Sleep is not allowed: it blocks the Unity main thread";
                return resp;
            }

            ArgSpec[] args = null;
            if (!string.IsNullOrEmpty(p.args))
            {
                try
                {
                    var argsJson = JsonNode.Parse(p.args);
                    if (argsJson is JsonArray argsArray)
                    {
                        var argList = new List<ArgSpec>();
                        foreach (var item in argsArray)
                        {
                            if (item is JsonObject argObj)
                            {
                                var name = argObj["name"]?.GetValue<string>() ?? "";
                                var type = argObj["type"]?.GetValue<string>() ?? "";
                                var @default = argObj["default"]?.GetValue<string>() ?? "";
                                var description = argObj["description"]?.GetValue<string>() ?? "";

                                if (string.IsNullOrEmpty(name))
                                {
                                    resp["status"] = "error";
                                    resp["message"] = "Each arg must have a name";
                                    return resp;
                                }

                                if (!IsValidArgType(type))
                                {
                                    resp["status"] = "error";
                                    resp["message"] = $"Invalid arg type '{type}'. Must be: string, int, float, bool";
                                    return resp;
                                }

                                argList.Add(new ArgSpec(name, type, @default, description));
                            }
                        }
                        args = argList.ToArray();
                    }
                }
                catch (Exception ex)
                {
                    resp["status"] = "error";
                    resp["message"] = $"Invalid args JSON: {ex.Message}";
                    return resp;
                }
            }

            args ??= Array.Empty<ArgSpec>();

            try
            {
                var sourceHash = ComputeHash(p.code);
                var className = ToPascalCase(p.name);
                var sourceCode = GenerateCommandSource(p.name, className, p.description, p.code, args);
                var metadata = GenerateMetadata(p.name, p.description, args, sourceHash);

                var sourceFile = Path.Combine(StorageDir, $"AgentScript_{p.name}.cs");
                var metadataFile = Path.Combine(StorageDir, $"AgentScript_{p.name}.meta.json");

                Directory.CreateDirectory(StorageDir);
                File.WriteAllText(sourceFile, sourceCode);
                File.WriteAllText(metadataFile, metadata);

                resp["status"] = "ok";
                resp["command"] = new JsonObject
                {
                    ["name"] = p.name,
                    ["description"] = p.description,
                    ["storage_path"] = sourceFile.Replace("\\", "/"),
                    ["note"] = "Command registered. It will be compiled and available after domain reload or Editor restart."
                };
            }
            catch (Exception ex)
            {
                resp["status"] = "error";
                resp["message"] = $"Failed to save command: {ex.Message}";
            }

            return resp;
        }

        private bool IsValidCommandName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (char.IsDigit(name[0])) return false;
            return name.All(c => char.IsLetterOrDigit(c) || c == '_');
        }

        private bool IsValidArgType(string type)
        {
            return type switch
            {
                "string" or "int" or "float" or "bool" => true,
                _ => false
            };
        }

        private string ToPascalCase(string name)
        {
            var parts = name.Split('_');
            return string.Concat(parts.Select(p => char.ToUpper(p[0]) + p.Substring(1)));
        }

        private string ComputeHash(string text)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            return "sha256:" + Convert.ToHexString(hash).ToLower();
        }

        private string GenerateCommandSource(string name, string className, string description, string code, ArgSpec[] args)
        {
            var paramFields = new StringBuilder();
            var paramDecls = new List<string>();

            foreach (var arg in args)
            {
                var csharpType = ArgTypeToCSharp(arg.Type);
                paramFields.AppendLine($"        public {csharpType} {arg.Name} = {DefaultValueForType(arg.Type)};");
                paramDecls.Add($"{csharpType} {arg.Name}");
            }

            var argsSpecLines = new StringBuilder();
            foreach (var arg in args)
            {
                var escapedDesc = arg.Description.Replace("\"", "\\\"");
                argsSpecLines.AppendLine($"            new ArgSpec(\"{arg.Name}\", \"{arg.Type}\", \"{arg.Default}\", \"{escapedDesc}\"),");
            }

            var paramPassList = string.Join(", ", args.Select(a => $"p.{a.Name}"));
            var paramDeclList = string.Join(", ", paramDecls);

            var template = $@"using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using LLMDevTools;

namespace LLMDevTools.GeneratedCommands
{{
    public sealed class AgentScript_{className} : IAgentCommand
    {{
        public string Cmd => ""{name}"";
        public string Description => ""{description.Replace("\"", "\\\"")}"";
        public ArgSpec[] Args => new[]
        {{
{argsSpecLines}        }};

        public System.Text.Json.Nodes.JsonObject Execute(string uid, string requestJson)
        {{
            var p = JsonUtility.FromJson<Params>(requestJson);

            try
            {{
                var result = Run({paramPassList});
                var resp = AgentBridge.MakeResponse(uid, Cmd, ""ok"");
                resp[""result""] = result?.ToString() ?? """";
                return resp;
            }}
            catch (Exception ex)
            {{
                var resp = AgentBridge.MakeResponse(uid, Cmd, ""error"");
                resp[""message""] = ex.Message;
                return resp;
            }}
        }}

        private string Run({paramDeclList})
        {{
            {code}
        }}

        [System.Serializable]
        private class Params
        {{
{paramFields}        }}
    }}
}}
";
            return template;
        }

        private string ArgTypeToCSharp(string type) => type switch
        {
            "int" => "int",
            "float" => "float",
            "bool" => "bool",
            _ => "string"
        };

        private string DefaultValueForType(string type) => type switch
        {
            "int" => "0",
            "float" => "0f",
            "bool" => "false",
            _ => "\"\""
        };

        private string GenerateMetadata(string name, string description, ArgSpec[] args, string sourceHash)
        {
            var obj = new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["registered_at"] = DateTime.UtcNow.ToString("O"),
                ["source_hash"] = sourceHash,
            };

            var argsArray = new JsonArray();
            foreach (var arg in args)
            {
                argsArray.Add(new JsonObject
                {
                    ["name"] = arg.Name,
                    ["type"] = arg.Type,
                    ["default"] = arg.Default,
                    ["description"] = arg.Description,
                });
            }
            obj["args"] = argsArray;

            return obj.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        [Serializable]
        private class Params
        {
            public string name = "";
            public string description = "";
            public string code = "";
            public string args = "[]";
        }
    }
}
