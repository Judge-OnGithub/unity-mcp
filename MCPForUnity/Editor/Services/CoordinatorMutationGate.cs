using System;
using System.Diagnostics;
using System.IO;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>Final coordinator gate immediately before Unity command execution.</summary>
    internal static class CoordinatorMutationGate
    {
        private static bool _serverCoordinatedMode;
        internal static Func<JObject, bool> ValidationOverride;

        internal static void SetServerCoordinatedMode(bool enabled)
        {
            _serverCoordinatedMode = enabled;
        }

        internal static bool RequiresAuthority(string commandName, JObject parameters)
        {
            string command = commandName?.Trim().ToLowerInvariant() ?? string.Empty;
            string action = parameters?.Value<string>("action")?.Trim().ToLowerInvariant() ?? string.Empty;
            if (command is "debug_request_context" or "find_in_file" or "find_gameobjects" or "get_sha"
                or "manage_script_capabilities" or "read_console" or "set_active_instance" or "unity_docs"
                or "unity_reflect" or "validate_script") return false;
            return command switch
            {
                "manage_asset" => action is not ("search" or "get_info" or "get_components"),
                "manage_build" => action is not ("status" or "settings" or "scenes" or "profiles"),
                "manage_editor" => action is not ("telemetry_status" or "telemetry_ping"),
                "manage_material" => action is not ("ping" or "get_material_info"),
                "manage_packages" => action is not ("list_packages" or "search_packages" or "get_package_info" or "ping" or "status"),
                "manage_prefabs" => action is not ("get_info" or "get_hierarchy"),
                "manage_scene" => action is not ("get_hierarchy" or "get_active" or "get_build_settings" or "get_loaded_scenes" or "scene_view_frame"),
                "manage_script" => action is not ("read" or "get_sha" or "validate"),
                "manage_shader" => action != "read",
                "manage_ui" => action is not ("ping" or "read"),
                _ => true,
            };
        }

        internal static bool TryValidateAndConsume(JObject parameters, out string error)
        {
            error = "mutation_denied";
            if (!IsCoordinatedMode()) return true;
            if (ValidationOverride != null)
            {
                bool accepted = ValidationOverride(parameters);
                if (accepted) parameters.Remove("__mcp_authorization");
                return accepted;
            }

            JObject envelope = parameters?["__mcp_authorization"] as JObject;
            if (envelope == null) return false;
            try
            {
                string coordinator = ResolveCoordinatorScript();
                if (string.IsNullOrEmpty(coordinator)) return false;
                string python = Environment.GetEnvironmentVariable("UNITY_COORDINATOR_PYTHON");
                if (string.IsNullOrWhiteSpace(python)) python = "python";
                string arguments = string.Join(" ", new[]
                {
                    Quote(coordinator), "--state-root", Quote(envelope.Value<string>("coordination_state_root")), "validate-envelope",
                    "--lease", Quote(envelope.Value<string>("lease_id")),
                    "--generation", Quote(envelope.Value<string>("generation")),
                    "--session", Quote(envelope.Value<string>("mcp_session_id")),
                    "--instance", Quote(envelope.Value<string>("editor_instance_id")),
                    "--project", Quote(envelope.Value<string>("project_path")),
                    "--unity-pid", Quote(envelope.Value<string>("unity_pid")),
                    "--unity-start-identity", Quote(envelope.Value<string>("unity_start_identity")),
                    "--operation", Quote(envelope.Value<string>("operation_id")),
                });
                var start = new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var process = Process.Start(start);
                if (process == null || !process.WaitForExit(3000) || process.ExitCode != 0) return false;
                parameters.Remove("__mcp_authorization");
                return true;
            }
            catch (Exception ex)
            {
                McpLog.Warn($"Coordinator mutation validation failed: {ex.GetType().Name}");
                return false;
            }
        }

        private static string ResolveCoordinatorScript()
        {
            string configured = Environment.GetEnvironmentVariable("UNITY_COORDINATOR_CLI");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string candidate = Path.GetFullPath(Path.Combine(projectRoot ?? string.Empty, "..", "tools", "unity_coordinator.py"));
            return File.Exists(candidate) ? candidate : null;
        }

        private static bool IsCoordinatedMode()
        {
            if (_serverCoordinatedMode) return true;
            string value = Environment.GetEnvironmentVariable("UNITY_MCP_COORDINATED_MODE");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }

        private static string Quote(string value) => $"\"{(value ?? string.Empty).Replace("\"", "\\\"")}\"";
    }
}
