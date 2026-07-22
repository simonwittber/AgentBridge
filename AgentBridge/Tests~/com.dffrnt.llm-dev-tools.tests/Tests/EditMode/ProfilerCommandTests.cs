using NUnit.Framework;

namespace LLMDevTools.Tests
{
    [TestFixture]
    public class ProfilerCommandTests
    {
        [TearDown]
        public void TearDown()
        {
            AgentBridge.TestInvoke("profiler_clear", "{}");
        }

        [Test]
        public void ProfilerStart_NoMarkers_ReturnsError()
        {
            var resp = AgentBridge.TestInvoke("profiler_start", "{}");
            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("error"));
        }

        [Test]
        public void ProfilerStart_CustomMarkers_ReturnsOk()
        {
            var resp = AgentBridge.TestInvoke("profiler_start", "{\"markers\":[\"Main Thread\"]}");
            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp?["markers"]?.AsArray().Count, Is.EqualTo(1));
        }

        [Test]
        public void ProfilerStop_ReturnsOk()
        {
            AgentBridge.TestInvoke("profiler_start", "{\"markers\":[\"MyMarker\"]}");
            var resp = AgentBridge.TestInvoke("profiler_stop", "{}");
            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("ok"));
        }

        [Test]
        public void ProfilerClear_ReturnsOk()
        {
            AgentBridge.TestInvoke("profiler_start", "{\"markers\":[\"MyMarker\"]}");
            var resp = AgentBridge.TestInvoke("profiler_clear", "{}");
            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("ok"));
        }

        [Test]
        public void ProfilerGetSamples_AfterStart_ReturnsOk()
        {
            AgentBridge.TestInvoke("profiler_start", "{\"markers\":[\"MyMarker\"]}");
            var resp = AgentBridge.TestInvoke("profiler_get_samples", "{\"marker\":\"MyMarker\"}");
            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp?["markers"], Is.Not.Null);
        }

        [Test]
        public void ProfilerGetSamples_MarkerFilter_ReturnsSingleEntry()
        {
            AgentBridge.TestInvoke("profiler_start", "{\"markers\":[\"Main Thread\",\"GC.Alloc\"]}");
            var resp = AgentBridge.TestInvoke("profiler_get_samples", "{\"marker\":\"Main Thread\"}");
            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp?["markers"]?.AsArray().Count, Is.EqualTo(1));
        }

        [Test]
        public void ProfilerGetSamples_UnknownMarker_ReturnsError()
        {
            AgentBridge.TestInvoke("profiler_start", "{\"markers\":[\"MyMarker\"]}");
            var resp = AgentBridge.TestInvoke("profiler_get_samples", "{\"marker\":\"NonExistentMarker_XYZ\"}");
            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("error"));
        }

        [Test]
        public void ProfilerGetSamples_WithRaw_IncludesSamplesField()
        {
            AgentBridge.TestInvoke("profiler_start", "{\"markers\":[\"Main Thread\"]}");
            var resp = AgentBridge.TestInvoke("profiler_get_samples", "{\"marker\":\"Main Thread\",\"raw\":true}");
            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            var markers = resp?["markers"]?.AsArray();
            Assert.That(markers?.Count, Is.EqualTo(1));
            Assert.That(markers?[0]?["samples"], Is.Not.Null);
        }

        [Test]
        public void ProfilerGetSamples_BeforeStart_ReturnsOkWithEmptyMarkers()
        {
            AgentBridge.TestInvoke("profiler_clear", "{}");
            var resp = AgentBridge.TestInvoke("profiler_get_samples", "{}");
            Assert.That(resp?["status"]?.GetValue<string>(), Is.EqualTo("ok"));
            Assert.That(resp?["markers"]?.AsArray().Count, Is.EqualTo(0));
        }
    }
}
