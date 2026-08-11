using MCPForUnity.Editor.Services;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace MCPForUnityTests.Editor.Services
{
    public sealed class CoordinatorMutationGateTests
    {
        [TearDown]
        public void TearDown()
        {
            CoordinatorMutationGate.ValidationOverride = null;
            CoordinatorMutationGate.SetServerCoordinatedMode(false);
        }

        [Test]
        public void AuditedReadActions_DoNotRequireAuthority()
        {
            Assert.That(CoordinatorMutationGate.RequiresAuthority("read_console", new JObject()), Is.False);
            Assert.That(
                CoordinatorMutationGate.RequiresAuthority("manage_asset", new JObject { ["action"] = "search" }),
                Is.False);
            Assert.That(
                CoordinatorMutationGate.RequiresAuthority("manage_build", new JObject { ["action"] = "status" }),
                Is.False);
            Assert.That(
                CoordinatorMutationGate.RequiresAuthority("manage_build", new JObject { ["action"] = "platform" }),
                Is.False);
            Assert.That(
                CoordinatorMutationGate.RequiresAuthority("manage_build", new JObject { ["action"] = "settings", ["property"] = "version" }),
                Is.False);
            Assert.That(
                CoordinatorMutationGate.RequiresAuthority("manage_build", new JObject { ["action"] = "scenes" }),
                Is.False);
            Assert.That(
                CoordinatorMutationGate.RequiresAuthority("manage_build", new JObject { ["action"] = "profiles", ["profile"] = "Assets/Profile.asset" }),
                Is.False);
            Assert.That(
                CoordinatorMutationGate.RequiresAuthority("manage_packages", new JObject { ["action"] = "list_registries" }),
                Is.False);
            Assert.That(CoordinatorMutationGate.RequiresAuthority("unity_coordination_identity", new JObject()), Is.False);
            Assert.That(CoordinatorMutationGate.RequiresAuthority("set_active_instance", new JObject()), Is.False);
        }

        [Test]
        public void UnknownAndWriteActions_RequireAuthority()
        {
            Assert.That(CoordinatorMutationGate.RequiresAuthority("future_tool", new JObject()), Is.True);
            Assert.That(
                CoordinatorMutationGate.RequiresAuthority("manage_scene", new JObject { ["action"] = "load" }),
                Is.True);
            Assert.That(
                CoordinatorMutationGate.RequiresAuthority("manage_scene", new JObject { ["action"] = "scene_view_frame" }),
                Is.True);
            Assert.That(
                CoordinatorMutationGate.RequiresAuthority("manage_asset", new JObject { ["action"] = "search", ["generatePreview"] = true }),
                Is.True);
            Assert.That(
                CoordinatorMutationGate.RequiresAuthority("manage_asset", new JObject { ["action"] = "get_info", ["generatePreview"] = true }),
                Is.True);
            Assert.That(
                CoordinatorMutationGate.RequiresAuthority("manage_build", new JObject { ["action"] = "platform", ["target"] = "windows64" }),
                Is.True);
            Assert.That(
                CoordinatorMutationGate.RequiresAuthority("manage_build", new JObject { ["action"] = "settings", ["property"] = "version", ["value"] = "2.0" }),
                Is.True);
            Assert.That(
                CoordinatorMutationGate.RequiresAuthority("manage_build", new JObject { ["action"] = "scenes", ["scenes"] = new JArray() }),
                Is.True);
            Assert.That(
                CoordinatorMutationGate.RequiresAuthority("manage_build", new JObject { ["action"] = "profiles", ["profile"] = "Assets/Profile.asset", ["activate"] = true }),
                Is.True);
            Assert.That(
                CoordinatorMutationGate.RequiresAuthority("read_console", new JObject { ["action"] = "clear" }),
                Is.True);
            Assert.That(CoordinatorMutationGate.RequiresAuthority("manage_script_capabilities", new JObject()), Is.True);
        }

        [Test]
        public void BatchAuthority_RequiresLeaseIfAnyChildMutates()
        {
            var readBatch = new JObject
            {
                ["commands"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "read_console",
                        ["params"] = new JObject { ["action"] = "get" },
                    },
                },
            };
            Assert.That(CoordinatorMutationGate.RequiresAuthority("batch_execute", readBatch), Is.False);

            readBatch["commands"] = new JArray
            {
                new JObject { ["tool"] = "read_console", ["params"] = new JObject() },
                new JObject { ["tool"] = "execute_code", ["params"] = new JObject() },
            };
            Assert.That(CoordinatorMutationGate.RequiresAuthority("batch_execute", readBatch), Is.True);
            Assert.That(
                CoordinatorMutationGate.RequiresAuthority("batch_execute", new JObject { ["commands"] = new JArray() }),
                Is.True);
        }

        [Test]
        public void AcceptedEnvelope_IsRemovedBeforeHandlerDispatch()
        {
            var parameters = new JObject { ["__mcp_authorization"] = new JObject() };
            CoordinatorMutationGate.ValidationOverride = _ => true;

            Assert.That(CoordinatorMutationGate.TryValidateAndConsume(parameters, out _), Is.True);
            Assert.That(parameters.Property("__mcp_authorization"), Is.Null);
        }

        [Test]
        public void ForkFinalGate_DeniesWithoutEnvelopeEvenIfServerDoesNotAdvertiseCoordination()
        {
            CoordinatorMutationGate.SetServerCoordinatedMode(false);
            Assert.That(
                CoordinatorMutationGate.TryValidateAndConsume(new JObject(), out _),
                Is.False);
        }
    }
}
