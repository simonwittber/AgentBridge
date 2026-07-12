using NUnit.Framework;

namespace LLMDevTools.Tests
{
    [TestFixture]
    public class ReflectionCommandTests
    {
        // ── reflect_assemblies ────────────────────────────────────────────────

        [Test]
        public void ReflectAssemblies_ReturnsOkWithAssemblies()
        {
            var resp = AgentBridge.TestInvoke("reflect_assemblies");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp["assemblies"]?.AsArray(),      Is.Not.Null);
            Assert.That(resp["assemblies"].AsArray().Count,  Is.GreaterThan(0));
        }

        [Test]
        public void ReflectAssemblies_ContainsUnityEngine()
        {
            var resp       = AgentBridge.TestInvoke("reflect_assemblies");
            var assemblies = resp["assemblies"]?.AsArray();
            Assume.That(assemblies, Is.Not.Null);

            bool found = false;
            foreach (var asm in assemblies)
                if (asm?["name"]?.GetValue<string>()?.Contains("UnityEngine") == true) { found = true; break; }

            Assert.That(found, Is.True, "UnityEngine assembly not found");
        }

        // ── reflect_types ─────────────────────────────────────────────────────

        [Test]
        public void ReflectTypes_QueryTransform_ReturnsResults()
        {
            var resp = AgentBridge.TestInvoke("reflect_types", "{\"query\":\"Transform\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp["types"]?.AsArray().Count,     Is.GreaterThan(0));
        }

        [Test]
        public void ReflectTypes_Limit_CapsResultsAndSetsTruncated()
        {
            var resp = AgentBridge.TestInvoke("reflect_types", "{\"limit\":2}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp["types"]?.AsArray().Count,     Is.LessThanOrEqualTo(2));
            Assert.That(resp["truncated"]?.GetValue<bool>(), Is.True);
        }

        [Test]
        public void ReflectTypes_NamespaceFilter_ReturnsMatchingNamespace()
        {
            var resp  = AgentBridge.TestInvoke("reflect_types", "{\"namespace\":\"UnityEngine\",\"limit\":5}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var types = resp["types"]?.AsArray();
            Assert.That(types,       Is.Not.Null);
            Assert.That(types.Count, Is.GreaterThan(0));

            foreach (var t in types)
            {
                var fullName = t?["fullName"]?.GetValue<string>() ?? "";
                Assert.That(fullName, Does.Contain("UnityEngine"), $"{fullName} not in UnityEngine namespace");
            }
        }

        [Test]
        public void ReflectTypes_NoMatches_ReturnsEmptyArrayNotTruncated()
        {
            var resp = AgentBridge.TestInvoke("reflect_types", "{\"query\":\"NoSuchTypeNameXYZ999\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp["types"]?.AsArray().Count,     Is.EqualTo(0));
            Assert.That(resp["truncated"]?.GetValue<bool>(), Is.False);
        }

        [Test]
        public void ReflectTypes_AssemblyFilter_RestrictsToAssembly()
        {
            var resp  = AgentBridge.TestInvoke("reflect_types", "{\"assembly\":\"UnityEngine.CoreModule\",\"limit\":5}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var types = resp["types"]?.AsArray();
            Assert.That(types,       Is.Not.Null);
            Assert.That(types.Count, Is.GreaterThan(0));

            foreach (var t in types)
            {
                var asmName = t?["assemblyName"]?.GetValue<string>() ?? "";
                Assert.That(asmName, Does.Contain("UnityEngine"), $"Unexpected assembly: {asmName}");
            }
        }

        // ── reflect_members ───────────────────────────────────────────────────

        [Test]
        public void ReflectMembers_Transform_ContainsPosition()
        {
            var resp = AgentBridge.TestInvoke("reflect_members", "{\"type\":\"UnityEngine.Transform\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var members = resp["members"]?.AsArray();
            Assert.That(members, Is.Not.Null);

            bool found = false;
            foreach (var m in members)
                if (m?["name"]?.GetValue<string>() == "position") { found = true; break; }

            Assert.That(found, Is.True, "property 'position' not found on UnityEngine.Transform");
        }

        [Test]
        public void ReflectMembers_MissingTypeParam_ReturnsError()
        {
            var resp = AgentBridge.TestInvoke("reflect_members", "{}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("error"));
        }

        [Test]
        public void ReflectMembers_UnknownType_ReturnsError()
        {
            var resp = AgentBridge.TestInvoke("reflect_members", "{\"type\":\"NoSuchType_x9z999\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("error"));
        }

        [Test]
        public void ReflectMembers_KindMethod_ReturnsOnlyMethods()
        {
            var resp = AgentBridge.TestInvoke("reflect_members",
                "{\"type\":\"UnityEngine.Transform\",\"kind\":\"method\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var members = resp["members"]?.AsArray();
            Assert.That(members, Is.Not.Null);
            foreach (var m in members)
                Assert.That(m?["kind"]?.GetValue<string>(), Is.EqualTo("method"),
                    $"Expected method, got '{m?["kind"]}' for '{m?["name"]}'");
        }

        [Test]
        public void ReflectMembers_KindProperty_ReturnsOnlyProperties()
        {
            var resp = AgentBridge.TestInvoke("reflect_members",
                "{\"type\":\"UnityEngine.Transform\",\"kind\":\"property\"}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var members = resp["members"]?.AsArray();
            Assert.That(members, Is.Not.Null);
            foreach (var m in members)
                Assert.That(m?["kind"]?.GetValue<string>(), Is.EqualTo("property"),
                    $"Expected property, got '{m?["kind"]}' for '{m?["name"]}'");
        }

        [Test]
        public void ReflectMembers_StaticOnly_ReturnsOnlyStaticMembers()
        {
            var resp = AgentBridge.TestInvoke("reflect_members",
                "{\"type\":\"UnityEngine.Application\",\"static_only\":true,\"include_inherited\":true}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var members = resp["members"]?.AsArray();
            Assert.That(members,       Is.Not.Null);
            Assert.That(members.Count, Is.GreaterThan(0));
            foreach (var m in members)
                Assert.That(m?["isStatic"]?.GetValue<bool>(), Is.True,
                    $"Member '{m?["name"]}' is not static");
        }

        [Test]
        public void ReflectMembers_IncludeInherited_HasMoreMembersThanDefault()
        {
            var respDeclared  = AgentBridge.TestInvoke("reflect_members",
                "{\"type\":\"UnityEngine.Transform\",\"limit\":200}");
            var respInherited = AgentBridge.TestInvoke("reflect_members",
                "{\"type\":\"UnityEngine.Transform\",\"include_inherited\":true,\"limit\":200}");

            Assume.That(respDeclared?["status"]?.GetValue<string>(),  Is.EqualTo("ok"));
            Assume.That(respInherited?["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            int countDeclared  = respDeclared["members"]?.AsArray()?.Count  ?? 0;
            int countInherited = respInherited["members"]?.AsArray()?.Count ?? 0;
            Assert.That(countInherited, Is.GreaterThan(countDeclared));
        }

        [Test]
        public void ReflectMembers_Limit_CapsResultsAndSetsTruncated()
        {
            var resp = AgentBridge.TestInvoke("reflect_members",
                "{\"type\":\"UnityEngine.Transform\",\"include_inherited\":true,\"limit\":3}");
            Assert.That(resp,                               Is.Not.Null);
            Assert.That(resp["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp["members"]?.AsArray().Count,   Is.LessThanOrEqualTo(3));
            Assert.That(resp["truncated"]?.GetValue<bool>(), Is.True);
        }

        [Test]
        public void ReflectMembers_MethodEntry_HasParametersArray()
        {
            var resp = AgentBridge.TestInvoke("reflect_members",
                "{\"type\":\"UnityEngine.Transform\",\"kind\":\"method\"}");
            Assume.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("ok"));

            var members = resp["members"]?.AsArray();
            Assume.That(members?.Count, Is.GreaterThan(0));

            foreach (var m in members)
                Assert.That(m?["parameters"]?.AsArray(), Is.Not.Null,
                    $"Method '{m?["name"]}' is missing parameters array");
        }
    }
}
