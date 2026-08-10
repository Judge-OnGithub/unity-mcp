using System;
using System.Linq;
using System.Reflection;
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
            TestRunnerService.DestroyStaleOwnedApis();
            int baseline = UnityEngine.Resources.FindObjectsOfTypeAll<TestRunnerApi>().Length;

            var first = MCPServiceLocator.Tests;
            Assert.That(
                UnityEngine.Resources.FindObjectsOfTypeAll<TestRunnerApi>().Length,
                Is.EqualTo(baseline + 1));
            Assert.That(CountTestServiceApis(), Is.EqualTo(1));

            MCPServiceLocator.ResetTests();
            Assert.That(
                UnityEngine.Resources.FindObjectsOfTypeAll<TestRunnerApi>().Length,
                Is.EqualTo(baseline));
            Assert.That(CountTestServiceApis(), Is.Zero);

            var second = MCPServiceLocator.Tests;
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(CountTestServiceApis(), Is.EqualTo(1));
        }

        [Test]
        public void ReloadArtifactCleanup_KeepsOneEditorWindowViewDataPerPreferencesKey()
        {
            Type viewDataType = typeof(EditorWindow).Assembly.GetType(
                "UnityEditor.UIElements.EditorWindowViewData");
            FieldInfo preferencesField = viewDataType?.GetField(
                "m_PreferencesFileName",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(viewDataType, Is.Not.Null);
            Assert.That(preferencesField, Is.Not.Null);

            const string syntheticPreferences = "MCPForUnityTests.DomainReloadLifecycle";
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var viewData = ScriptableObject.CreateInstance(viewDataType);
                    viewData.hideFlags = HideFlags.HideAndDontSave;
                    preferencesField.SetValue(viewData, syntheticPreferences);
                }

                Assert.That(CountViewData(viewDataType, preferencesField, syntheticPreferences), Is.EqualTo(3));

                TestRunnerNoThrottle.DestroyDuplicateEditorWindowViewData();

                Assert.That(CountViewData(viewDataType, preferencesField, syntheticPreferences), Is.EqualTo(1));
            }
            finally
            {
                foreach (UnityEngine.Object viewData in UnityEngine.Resources.FindObjectsOfTypeAll(viewDataType))
                {
                    if (viewData != null &&
                        string.Equals(
                            preferencesField.GetValue(viewData) as string,
                            syntheticPreferences,
                            StringComparison.Ordinal))
                    {
                        UnityEngine.Object.DestroyImmediate(viewData);
                    }
                }
            }
        }

        [Test]
        public void ReloadArtifactCleanup_KeepsSingleUnownedTestRunnerApi()
        {
            for (int i = 0; i < 3; i++)
            {
                ScriptableObject.CreateInstance<TestRunnerApi>();
            }

            Assert.That(CountUnnamedTestRunnerApis(), Is.GreaterThanOrEqualTo(3));

            TestRunnerNoThrottle.DestroyDuplicateUnownedTestRunnerApis();

            Assert.That(CountUnnamedTestRunnerApis(), Is.EqualTo(1));
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

        private static int CountTestServiceApis()
        {
            return UnityEngine.Resources.FindObjectsOfTypeAll<TestRunnerApi>()
                .Count(api => api != null && api.name == TestRunnerService.ApiObjectName);
        }

        private static int CountUnnamedTestRunnerApis()
        {
            return UnityEngine.Resources.FindObjectsOfTypeAll<TestRunnerApi>()
                .Count(api =>
                    api != null &&
                    string.IsNullOrEmpty(api.name) &&
                    !EditorUtility.IsPersistent(api));
        }

        private static int CountViewData(
            Type viewDataType,
            FieldInfo preferencesField,
            string preferencesFileName)
        {
            return UnityEngine.Resources.FindObjectsOfTypeAll(viewDataType)
                .Count(viewData =>
                    viewData != null &&
                    string.Equals(
                        preferencesField.GetValue(viewData) as string,
                        preferencesFileName,
                        StringComparison.Ordinal));
        }
    }
}
