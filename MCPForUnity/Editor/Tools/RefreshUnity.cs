using System;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Explicitly refreshes Unity's asset database and optionally requests a script compilation.
    /// This is side-effectful and should be treated as a tool.
    /// </summary>
    [McpForUnityTool("refresh_unity", AutoRegister = false)]
    public static class RefreshUnity
    {
        private const int DefaultWaitTimeoutSeconds = 60;

        internal readonly struct RefreshPlan
        {
            public RefreshPlan(
                bool refreshAssets,
                ImportAssetOptions refreshOptions,
                bool compilationRequested,
                bool requestCompilationDirectly)
            {
                RefreshAssets = refreshAssets;
                RefreshOptions = refreshOptions;
                CompilationRequested = compilationRequested;
                RequestCompilationDirectly = requestCompilationDirectly;
            }

            public bool RefreshAssets { get; }
            public ImportAssetOptions RefreshOptions { get; }
            public bool CompilationRequested { get; }
            public bool RequestCompilationDirectly { get; }
        }

        public static async Task<object> HandleCommand(JObject @params)
        {
            string mode = @params?["mode"]?.ToString() ?? "if_dirty";
            string scope = @params?["scope"]?.ToString() ?? "all";
            string compile = @params?["compile"]?.ToString() ?? "none";
            bool waitForReady = ParamCoercion.CoerceBool(@params?["wait_for_ready"], false);

            if (TestRunStatus.IsRunning)
            {
                return new ErrorResponse("tests_running", new
                {
                    reason = "tests_running",
                    retry_after_ms = 5000
                });
            }

            bool refreshTriggered = false;
            RefreshPlan plan = CreatePlan(mode, scope, compile);
            bool compileRequested = plan.CompilationRequested;

            try
            {
                if (plan.RefreshAssets)
                {
                    AssetDatabase.Refresh(plan.RefreshOptions);
                    refreshTriggered = true;
                }

                if (plan.RequestCompilationDirectly)
                {
                    CompilationPipeline.RequestScriptCompilation();
                }
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"refresh_failed: {ex.Message}");
            }

            // Unity 6+ fix: Skip wait_for_ready when compile was requested.
            // The EditorApplication.update polling in WaitForUnityReadyAsync doesn't survive
            // domain reloads properly in Unity 6+, causing infinite compilation loops.
            // When compilation is requested, return immediately and let client poll editor_state.
            // Earlier Unity versions retain the original behavior.
#if UNITY_6000_0_OR_NEWER
            bool shouldWaitForReady = waitForReady && !compileRequested;
#else
            bool shouldWaitForReady = waitForReady;
#endif
            if (shouldWaitForReady)
            {
                try
                {
                    await WaitForUnityReadyAsync(
                        TimeSpan.FromSeconds(DefaultWaitTimeoutSeconds)).ConfigureAwait(true);
                }
                catch (TimeoutException)
                {
                    return new ErrorResponse("refresh_timeout_waiting_for_ready", new
                    {
                        refresh_triggered = refreshTriggered,
                        compile_requested = compileRequested,
                        resulting_state = "unknown",
                    });
                }
                catch (Exception ex)
                {
                    return new ErrorResponse($"refresh_wait_failed: {ex.Message}");
                }
            }

            string resultingState = EditorApplication.isCompiling
                ? "compiling"
                : (EditorApplication.isUpdating ? "asset_import" : "idle");

            return new SuccessResponse("Refresh requested.", new
            {
                refresh_triggered = refreshTriggered,
                compile_requested = compileRequested,
                resulting_state = resultingState,
                hint = shouldWaitForReady
                    ? "Unity refresh completed; editor should be ready."
                    : "If Unity enters compilation/domain reload, poll editor_state until ready_for_tools is true."
            });
        }

        internal static RefreshPlan CreatePlan(string mode, string scope, string compile)
        {
            // Best-effort semantics: if_dirty currently behaves like force unless future dirty signals are added.
            bool shouldRefresh = string.Equals(mode, "force", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(mode, "if_dirty", StringComparison.OrdinalIgnoreCase);
            bool compilationRequested = string.Equals(compile, "request", StringComparison.OrdinalIgnoreCase);

            ImportAssetOptions refreshOptions = ImportAssetOptions.ForceSynchronousImport;
            if (string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase))
            {
                refreshOptions |= ImportAssetOptions.ForceUpdate;
            }

            // Importing filesystem changes is what should request compilation. Calling
            // RequestScriptCompilation before the import makes Unity compile once through
            // the public API and then reload again when the AssetDatabase observes the script.
            bool requestCompilationDirectly = compilationRequested && !shouldRefresh;

            return new RefreshPlan(
                shouldRefresh,
                refreshOptions,
                compilationRequested,
                requestCompilationDirectly);
        }

        private static Task WaitForUnityReadyAsync(TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var start = DateTime.UtcNow;

            void Tick()
            {
                try
                {
                    if (tcs.Task.IsCompleted)
                    {
                        EditorApplication.update -= Tick;
                        return;
                    }

                    if ((DateTime.UtcNow - start) > timeout)
                    {
                        EditorApplication.update -= Tick;
                        tcs.TrySetException(new TimeoutException());
                        return;
                    }

                    if (!EditorApplication.isCompiling
                        && !EditorApplication.isUpdating
                        && !TestRunStatus.IsRunning
                        && !EditorApplication.isPlayingOrWillChangePlaymode)
                    {
                        EditorApplication.update -= Tick;
                        tcs.TrySetResult(true);
                    }
                }
                catch (Exception ex)
                {
                    EditorApplication.update -= Tick;
                    tcs.TrySetException(ex);
                }
            }

            EditorApplication.update += Tick;
            // Nudge Unity to pump once in case update is throttled.
            try { EditorApplication.QueuePlayerLoopUpdate(); } catch { }
            return tcs.Task;
        }
    }
}
