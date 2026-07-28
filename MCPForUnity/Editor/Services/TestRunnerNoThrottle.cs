// TestRunnerNoThrottle.cs
// Sets Unity Editor to "No Throttling" mode during test runs.
// This helps tests that don't trigger compilation run smoothly in the background.
// Note: Tests that trigger mid-run compilation may still stall due to OS-level throttling.

using System;
using System.Collections.Generic;
using System.Reflection;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Automatically sets the editor to "No Throttling" mode during test runs.
    /// 
    /// This helps prevent background stalls for normal tests. However, tests that trigger
    /// script compilation mid-run may still stall because:
    /// - Internal Unity coroutine waits rely on editor ticks
    /// - OS-level throttling affects the main thread when Unity is backgrounded
    /// - No amount of internal nudging can overcome OS thread scheduling
    /// 
    /// The MCP workflow is unaffected because socket messages provide external stimulus
    /// that wakes Unity's main thread.
    /// </summary>
    [InitializeOnLoad]
    public static class TestRunnerNoThrottle
    {
        private const string ApplicationIdleTimeKey = "ApplicationIdleTime";
        private const string InteractionModeKey = "InteractionMode";

        // SessionState keys to persist across domain reload
        private const string SessionKey_TestRunActive = "TestRunnerNoThrottle_TestRunActive";
        private const string SessionKey_PrevIdleTime = "TestRunnerNoThrottle_PrevIdleTime";
        private const string SessionKey_PrevInteractionMode = "TestRunnerNoThrottle_PrevInteractionMode";
        private const string SessionKey_SettingsCaptured = "TestRunnerNoThrottle_SettingsCaptured";
        private const string EditorWindowViewDataTypeName = "UnityEditor.UIElements.EditorWindowViewData";
        private const string EditorWindowPreferencesFieldName = "m_PreferencesFileName";
        internal const string ApiObjectName = "MCPForUnity.TestRunnerNoThrottle";

        private static TestRunnerApi _api;
        private static TestCallbacks _callbacks;

        static TestRunnerNoThrottle()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.quitting += Cleanup;
            Initialize();
        }

        internal static void Initialize()
        {
            if (_api != null)
            {
                ScheduleReloadArtifactCleanup();
                return;
            }

            try
            {
                DestroyStaleOwnedApis();

                _callbacks = new TestCallbacks();
                _api = ScriptableObject.CreateInstance<TestRunnerApi>();
                _api.name = ApiObjectName;
                _api.hideFlags = HideFlags.HideAndDontSave;
                _api.RegisterCallbacks(_callbacks);
                ScheduleReloadArtifactCleanup();

                // Check if recovering from domain reload during an active test run
                if (IsTestRunActive())
                {
                    McpLog.Info("[TestRunnerNoThrottle] Recovered from domain reload - reapplying No Throttling.");
                    ApplyNoThrottling();
                }
            }
            catch (Exception e)
            {
                McpLog.Warn($"[TestRunnerNoThrottle] Failed to register callbacks: {e}");
            }
        }

        internal static void Cleanup()
        {
            EditorApplication.delayCall -= CleanupReloadArtifacts;

            if (_api == null)
            {
                _callbacks = null;
                return;
            }

            try
            {
                if (_callbacks != null)
                {
                    _api.UnregisterCallbacks(_callbacks);
                }
            }
            catch (Exception e)
            {
                McpLog.Warn($"[TestRunnerNoThrottle] Failed to unregister callbacks: {e.Message}");
            }

            try
            {
                UnityEngine.Object.DestroyImmediate(_api);
            }
            catch (Exception e)
            {
                McpLog.Warn($"[TestRunnerNoThrottle] Failed to destroy TestRunnerApi: {e.Message}");
            }
            finally
            {
                _api = null;
                _callbacks = null;
            }
        }

        private static void ScheduleReloadArtifactCleanup()
        {
            EditorApplication.delayCall -= CleanupReloadArtifacts;
            EditorApplication.delayCall += CleanupReloadArtifacts;
        }

        private static void CleanupReloadArtifacts()
        {
            int destroyed = DestroyDuplicateEditorWindowViewData();
            if (destroyed > 0)
            {
                McpLog.Info(
                    $"[TestRunnerNoThrottle] Removed {destroyed} stale EditorWindowViewData object(s) after domain reload.");
            }
        }

        internal static int DestroyDuplicateEditorWindowViewData()
        {
            Type viewDataType = typeof(EditorWindow).Assembly.GetType(EditorWindowViewDataTypeName);
            FieldInfo preferencesField = viewDataType?.GetField(
                EditorWindowPreferencesFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (viewDataType == null || preferencesField == null)
            {
                return 0;
            }

            var retainedPreferences = new HashSet<string>(StringComparer.Ordinal);
            int destroyed = 0;
            foreach (UnityEngine.Object viewData in UnityEngine.Resources.FindObjectsOfTypeAll(viewDataType))
            {
                if (viewData == null)
                {
                    continue;
                }

                string preferencesFileName = preferencesField.GetValue(viewData) as string ?? string.Empty;
                if (retainedPreferences.Add(preferencesFileName))
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(viewData);
                destroyed++;
            }

            return destroyed;
        }

        private static void DestroyStaleOwnedApis()
        {
            foreach (var api in UnityEngine.Resources.FindObjectsOfTypeAll<TestRunnerApi>())
            {
                if (api != null && string.Equals(api.name, ApiObjectName, StringComparison.Ordinal))
                {
                    UnityEngine.Object.DestroyImmediate(api);
                }
            }
        }

        #region State Persistence

        private static bool IsTestRunActive() => SessionState.GetBool(SessionKey_TestRunActive, false);
        private static void SetTestRunActive(bool active) => SessionState.SetBool(SessionKey_TestRunActive, active);
        private static bool AreSettingsCaptured() => SessionState.GetBool(SessionKey_SettingsCaptured, false);
        private static void SetSettingsCaptured(bool captured) => SessionState.SetBool(SessionKey_SettingsCaptured, captured);
        private static int GetPrevIdleTime() => SessionState.GetInt(SessionKey_PrevIdleTime, 4);
        private static void SetPrevIdleTime(int value) => SessionState.SetInt(SessionKey_PrevIdleTime, value);
        private static int GetPrevInteractionMode() => SessionState.GetInt(SessionKey_PrevInteractionMode, 0);
        private static void SetPrevInteractionMode(int value) => SessionState.SetInt(SessionKey_PrevInteractionMode, value);

        #endregion

        /// <summary>
        /// Apply no-throttling preemptively before tests start.
        /// Call this before Execute() for PlayMode tests to ensure Unity isn't throttled
        /// during the Play mode transition (before RunStarted fires).
        /// </summary>
        public static void ApplyNoThrottlingPreemptive()
        {
            SetTestRunActive(true);
            ApplyNoThrottling();
        }

        private static void ApplyNoThrottling()
        {
            if (!AreSettingsCaptured())
            {
                SetPrevIdleTime(EditorPrefs.GetInt(ApplicationIdleTimeKey, 4));
                SetPrevInteractionMode(EditorPrefs.GetInt(InteractionModeKey, 0));
                SetSettingsCaptured(true);
            }

            // 0ms idle + InteractionMode=1 (No Throttling)
            EditorPrefs.SetInt(ApplicationIdleTimeKey, 0);
            EditorPrefs.SetInt(InteractionModeKey, 1);

            ForceEditorToApplyInteractionPrefs();
            McpLog.Info("[TestRunnerNoThrottle] Applied No Throttling for test run.");
        }

        private static void RestoreThrottling()
        {
            if (!AreSettingsCaptured()) return;

            EditorPrefs.SetInt(ApplicationIdleTimeKey, GetPrevIdleTime());
            EditorPrefs.SetInt(InteractionModeKey, GetPrevInteractionMode());
            ForceEditorToApplyInteractionPrefs();

            SetSettingsCaptured(false);
            SetTestRunActive(false);
            McpLog.Info("[TestRunnerNoThrottle] Restored Interaction Mode after test run.");
        }

        private static void ForceEditorToApplyInteractionPrefs()
        {
            try
            {
                var method = typeof(EditorApplication).GetMethod(
                    "UpdateInteractionModeSettings",
                    BindingFlags.Static | BindingFlags.NonPublic
                );
                method?.Invoke(null, null);
            }
            catch
            {
                // Ignore reflection errors
            }
        }

        private sealed class TestCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                SetTestRunActive(true);
                ApplyNoThrottling();
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                RestoreThrottling();
            }

            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
        }
    }
}
