using System.Linq;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Tools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace MCPForUnityTests.Editor.Tools
{
    [TestFixture]
    public sealed class DomainReloadLifecycleTests
    {
        [TearDown]
        public void TearDown()
        {
            MCPServiceLocator.ResetTests();
            TestRunnerNoThrottle.Cleanup();
            TestRunnerNoThrottle.Initialize();
        }

        [Test]
        public void NoThrottleApi_InitializeIsIdempotent_AndCleanupDestroysOwnedObject()
        {
            TestRunnerNoThrottle.Cleanup();
            Assert.That(CountNoThrottleApis(), Is.Zero);

            TestRunnerNoThrottle.Initialize();
            TestRunnerNoThrottle.Initialize();
            Assert.That(CountNoThrottleApis(), Is.EqualTo(1));

            TestRunnerNoThrottle.Cleanup();
            Assert.That(CountNoThrottleApis(), Is.Zero);
        }

        [Test]
        public void TestService_ResetTests_DisposesItsTestRunnerApi()
        {
            MCPServiceLocator.ResetTests();
            int baseline = UnityEngine.Resources.FindObjectsOfTypeAll<TestRunnerApi>().Length;

            var first = MCPServiceLocator.Tests;
            Assert.That(
                UnityEngine.Resources.FindObjectsOfTypeAll<TestRunnerApi>().Length,
                Is.EqualTo(baseline + 1));

            MCPServiceLocator.ResetTests();
            Assert.That(
                UnityEngine.Resources.FindObjectsOfTypeAll<TestRunnerApi>().Length,
                Is.EqualTo(baseline));

            var second = MCPServiceLocator.Tests;
            Assert.That(second, Is.Not.SameAs(first));
        }

        [Test]
        public void ScriptRefresh_WithCompilationRequest_ImportsBeforeCompilation()
        {
            var plan = RefreshUnity.CreatePlan("if_dirty", "scripts", "request");

            Assert.That(plan.RefreshAssets, Is.True);
            Assert.That(
                plan.RefreshOptions & ImportAssetOptions.ForceSynchronousImport,
                Is.EqualTo(ImportAssetOptions.ForceSynchronousImport));
            Assert.That(plan.CompilationRequested, Is.True);
            Assert.That(plan.RequestCompilationDirectly, Is.False);
        }

        [Test]
        public void ExplicitCompilation_WithoutRefresh_UsesPublicRequest()
        {
            var plan = RefreshUnity.CreatePlan("none", "scripts", "request");

            Assert.That(plan.RefreshAssets, Is.False);
            Assert.That(plan.CompilationRequested, Is.True);
            Assert.That(plan.RequestCompilationDirectly, Is.True);
        }

        private static int CountNoThrottleApis()
        {
            return UnityEngine.Resources.FindObjectsOfTypeAll<TestRunnerApi>()
                .Count(api => api != null && api.name == TestRunnerNoThrottle.ApiObjectName);
        }
    }
}
