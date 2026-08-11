using System;
using System.Diagnostics;
using MCPForUnity.Editor.Helpers;
using UnityEditor;

namespace MCPForUnity.Editor.Services
{
    /// <summary>Stable editor-process identity for coordinator authority checks.</summary>
    [InitializeOnLoad]
    internal static class CoordinatorEditorIdentity
    {
        private const string EditorInstanceSessionKey = "MCPForUnity.Coordinator.EditorInstanceId";
        private static readonly string EditorInstanceId;
        private static readonly int ProcessId;
        private static readonly string ProcessStartIdentity;

        static CoordinatorEditorIdentity()
        {
            EditorInstanceId = SessionState.GetString(EditorInstanceSessionKey, string.Empty);
            if (string.IsNullOrEmpty(EditorInstanceId))
            {
                EditorInstanceId = Guid.NewGuid().ToString("N");
                SessionState.SetString(EditorInstanceSessionKey, EditorInstanceId);
            }

            try
            {
                using var process = Process.GetCurrentProcess();
                ProcessId = process.Id;
                ProcessStartIdentity = process.StartTime.ToUniversalTime().Ticks.ToString();
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Could not establish coordinator process identity: {ex.Message}");
                ProcessId = 0;
                ProcessStartIdentity = string.Empty;
            }
        }

        internal static string InstanceId => EditorInstanceId;
        internal static int UnityProcessId => ProcessId;
        internal static string UnityStartIdentity => ProcessStartIdentity;
    }
}
