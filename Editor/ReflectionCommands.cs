using System;
using System.Reflection;
using System.Text.Json.Nodes;
using UnityEditor;
using UnityEngine;

namespace LLMDevTools
{
    [InitializeOnLoad]
    public static class ReflectionCommands
    {
        static ReflectionCommands()
        {
            AgentBridge.Register(new ReflectAssembliesCmd());
            AgentBridge.Register(new ReflectTypesCmd());
            AgentBridge.Register(new ReflectMembersCmd());
        }

        // Uses TypeCache (Unity-safe) for exact then suffix match.
        internal static Type FindType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            foreach (var t in TypeCache.GetTypesDerivedFrom(typeof(object)))
                if (string.Equals(t.FullName, typeName, StringComparison.OrdinalIgnoreCase))
                    return t;

            foreach (var t in TypeCache.GetTypesDerivedFrom(typeof(object)))
                if (t.FullName?.EndsWith(typeName, StringComparison.OrdinalIgnoreCase) == true)
                    return t;

            return null;
        }

        // ── reflect_assemblies ────────────────────────────────────────────────

        private sealed class ReflectAssembliesCmd : IAgentCommand
        {
            public string    Cmd         => "reflect_assemblies";
            public string    Description => "List loaded assemblies.";
            public ArgSpec[] Args        => new ArgSpec[0];

            public JsonObject Execute(string uid, string requestJson)
            {
                var seen = new System.Collections.Generic.HashSet<string>();
                var arr  = new JsonArray();

                foreach (var t in TypeCache.GetTypesDerivedFrom(typeof(object)))
                {
                    var asm  = t.Assembly;
                    var name = asm.GetName().Name;
                    if (!seen.Add(name)) continue;

                    arr.Add(new JsonObject
                    {
                        ["name"]     = name,
                        ["fullName"] = asm.FullName,
                    });
                }

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["assemblies"] = arr;
                return resp;
            }
        }

        // ── reflect_types ─────────────────────────────────────────────────────

        private sealed class ReflectTypesCmd : IAgentCommand
        {
            public string    Cmd         => "reflect_types";
            public string    Description => "Search for public types across all loaded assemblies.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("query",     "string", "",   "Substring to match against type name"),
                new ArgSpec("namespace", "string", "",   "Namespace substring filter"),
                new ArgSpec("assembly",  "string", "",   "Assembly name filter"),
                new ArgSpec("limit",     "int",    "50", "Max results (default 50)"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p     = JsonUtility.FromJson<Params>(requestJson);
                int limit = p.limit > 0 ? p.limit : 50;

                var  results   = new JsonArray();
                bool truncated = false;

                foreach (var t in TypeCache.GetTypesDerivedFrom(typeof(object)))
                {
                    if (!string.IsNullOrEmpty(p.query) &&
                        t.Name.IndexOf(p.query, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    string ns = t.Namespace ?? "";
                    if (!string.IsNullOrEmpty(p.@namespace) &&
                        ns.IndexOf(p.@namespace, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (!string.IsNullOrEmpty(p.assembly) &&
                        (t.Assembly.GetName().Name?.IndexOf(p.assembly, StringComparison.OrdinalIgnoreCase) ?? -1) < 0)
                        continue;

                    if (results.Count >= limit) { truncated = true; break; }

                    results.Add(new JsonObject
                    {
                        ["fullName"]     = t.FullName,
                        ["assemblyName"] = t.Assembly.GetName().Name,
                        ["isClass"]      = t.IsClass,
                        ["isInterface"]  = t.IsInterface,
                        ["isEnum"]       = t.IsEnum,
                    });
                }

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["types"]     = results;
                resp["truncated"] = truncated;
                return resp;
            }

            [System.Serializable]
            private class Params
            {
                public string query      = "";
                public string @namespace = "";
                public string assembly   = "";
                public int    limit      = 50;
            }
        }

        // ── reflect_members ───────────────────────────────────────────────────

        private sealed class ReflectMembersCmd : IAgentCommand
        {
            public string    Cmd         => "reflect_members";
            public string    Description => "List public members of a named type.";
            public ArgSpec[] Args        => new[]
            {
                new ArgSpec("type",              "string", "",      "Full or partial type name (required)"),
                new ArgSpec("kind",              "string", "all",   "Member kind filter: all, method, property, field"),
                new ArgSpec("static_only",       "bool",   "false", "Only return static members"),
                new ArgSpec("instance_only",     "bool",   "false", "Only return instance members"),
                new ArgSpec("include_inherited", "bool",   "false", "Include members declared on base types"),
                new ArgSpec("limit",             "int",    "100",   "Max results (default 100)"),
            };

            public JsonObject Execute(string uid, string requestJson)
            {
                var p = JsonUtility.FromJson<Params>(requestJson);

                if (string.IsNullOrEmpty(p.type))
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = "type is required";
                    return err;
                }

                var t = FindType(p.type);
                if (t == null)
                {
                    var err = AgentBridge.MakeResponse(uid, Cmd, "error");
                    err["message"] = $"Type not found: {p.type}";
                    return err;
                }

                int  limit     = p.limit > 0 ? p.limit : 100;
                var  flags     = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
                var  members   = new JsonArray();
                bool truncated = false;

                foreach (var m in t.GetMembers(flags))
                {
                    if (m.MemberType == MemberTypes.Constructor ||
                        m.MemberType == MemberTypes.Event        ||
                        m.MemberType == MemberTypes.NestedType)
                        continue;

                    if (!p.include_inherited && m.DeclaringType != t) continue;
                    if (!MatchesKind(m, p.kind))                       continue;

                    bool isStatic = IsStatic(m);
                    if (p.static_only   && !isStatic) continue;
                    if (p.instance_only &&  isStatic) continue;

                    if (members.Count >= limit) { truncated = true; break; }

                    members.Add(Serialize(m));
                }

                var resp = AgentBridge.MakeResponse(uid, Cmd, "ok");
                resp["typeName"]  = t.FullName;
                resp["members"]   = members;
                resp["truncated"] = truncated;
                return resp;
            }

            static bool MatchesKind(MemberInfo m, string kind)
            {
                if (string.IsNullOrEmpty(kind) || kind == "all") return true;
                return kind switch
                {
                    "method"   => m.MemberType == MemberTypes.Method,
                    "property" => m.MemberType == MemberTypes.Property,
                    "field"    => m.MemberType == MemberTypes.Field,
                    _          => true,
                };
            }

            static bool IsStatic(MemberInfo m) => m switch
            {
                MethodInfo   mi => mi.IsStatic,
                PropertyInfo pi => pi.GetGetMethod()?.IsStatic ?? false,
                FieldInfo    fi => fi.IsStatic,
                _               => false,
            };

            static JsonObject Serialize(MemberInfo m)
            {
                var obj = new JsonObject { ["name"] = m.Name };
                switch (m)
                {
                    case MethodInfo mi:
                        var parms = new JsonArray();
                        foreach (var param in mi.GetParameters())
                            parms.Add(new JsonObject
                            {
                                ["name"] = param.Name,
                                ["type"] = param.ParameterType.FullName ?? param.ParameterType.Name,
                            });
                        obj["kind"]       = "method";
                        obj["returnType"] = mi.ReturnType.FullName ?? mi.ReturnType.Name;
                        obj["parameters"] = parms;
                        obj["isStatic"]   = mi.IsStatic;
                        break;

                    case PropertyInfo pi:
                        obj["kind"]       = "property";
                        obj["returnType"] = pi.PropertyType.FullName ?? pi.PropertyType.Name;
                        obj["isStatic"]   = pi.GetGetMethod()?.IsStatic ?? false;
                        break;

                    case FieldInfo fi:
                        obj["kind"]       = "field";
                        obj["returnType"] = fi.FieldType.FullName ?? fi.FieldType.Name;
                        obj["isStatic"]   = fi.IsStatic;
                        break;

                    default:
                        obj["kind"] = m.MemberType.ToString().ToLowerInvariant();
                        break;
                }
                return obj;
            }

            [System.Serializable]
            private class Params
            {
                public string type              = "";
                public string kind              = "all";
                public bool   static_only       = false;
                public bool   instance_only     = false;
                public bool   include_inherited = false;
                public int    limit             = 100;
            }
        }
    }
}
