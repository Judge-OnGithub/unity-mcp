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
                CoordinatorMutationGate.RequiresAuthority("read_console", new JObject { ["action"] = "clear" }),
                Is.True);
            Assert.That(CoordinatorMutationGate.RequiresAuthority("manage_script_capabilities", new JObject()), Is.True);
        }

        [Test]
        public void AcceptedEnvelope_IsRemovedBeforeHandlerDispatch()
        {
            var parameters = new JObject { ["__mcp_authorization"] = new JObject() };
            CoordinatorMutationGate.ValidationOverride = _ => true;

            Assert.That(CoordinatorMutationGate.TryValidateAndConsume(parameters, out _), Is.True);
            Assert.That(parameters["__mcp_authorization"], Is.Null);
        }

        [Test]
        public void ServerRegistration_EnablesTheFinalGateWithoutEditorEnvironment()
        {
            CoordinatorMutationGate.SetServerCoordinatedMode(true);
            Assert.That(
                CoordinatorMutationGate.TryValidateAndConsume(new JObject(), out _),
                Is.False);
        }
    }
}
