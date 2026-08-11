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
        internal static Func<JObject, bool> ValidationOverride;

        internal static void SetServerCoordinatedMode(bool enabled)
        {
            // Registration metadata may confirm coordinated mode, but it can
            // never weaken the project-controlled fork's mandatory final gate.
            _ = enabled;
        }

        internal static bool RequiresAuthority(string commandName, JObject parameters)
        {
            string command = commandName?.Trim().ToLowerInvariant() ?? string.Empty;
            string action = parameters?.Value<string>("action")?.Trim().ToLowerInvariant() ?? string.Empty;
            if (command is "debug_request_context" or "find_in_file" or "find_gameobjects" or "get_sha"
                or "set_active_instance" or "unity_coordination_identity" or "unity_docs"
                or "unity_reflect" or "validate_script") return false;
            return command switch
            {
                "batch_execute" => BatchRequiresAuthority(parameters),
                "manage_asset" => action switch
                {
                    "search" or "get_info" or "get_components" =>
                        IsExplicitTrue(parameters?["generatePreview"] ?? parameters?["generate_preview"]),
                    _ => true,
                },
                "manage_build" => ManageBuildRequiresAuthority(action, parameters),
                "manage_editor" => action is not ("telemetry_status" or "telemetry_ping"),
                "manage_material" => action is not ("ping" or "get_material_info"),
                "manage_packages" => action is not ("list_packages" or "search_packages" or "get_package_info" or "list_registries" or "ping" or "status"),
                "manage_prefabs" => action is not ("get_info" or "get_hierarchy"),
                "manage_scene" => action is not ("get_hierarchy" or "get_active" or "get_build_settings" or "get_loaded_scenes"),
                "manage_script" => action is not ("read" or "get_sha" or "validate"),
                "manage_shader" => action != "read",
                "manage_ui" => action is not ("ping" or "read"),
                "read_console" => action is not ("" or "get"),
                _ => true,
            };
        }

        private static bool BatchRequiresAuthority(JObject parameters)
        {
            if (parameters?["commands"] is not JArray commands || commands.Count == 0) return true;
            foreach (JToken token in commands)
            {
                if (token is not JObject item) return true;
                string tool = item.Value<string>("tool");
                if (string.IsNullOrWhiteSpace(tool)) return true;
                JObject childParameters = item["params"] as JObject ?? new JObject();
                if (RequiresAuthority(tool, childParameters)) return true;
            }
            return false;
        }

        private static bool ManageBuildRequiresAuthority(string action, JObject parameters)
        {
            return action switch
            {
                "status" => false,
                "platform" => HasValue(parameters?["target"]),
                "settings" => HasValue(parameters?["value"]),
                "scenes" => parameters?["scenes"] is JToken scenes
                            && scenes.Type != JTokenType.Null
                            && (scenes.Type != JTokenType.String || !string.IsNullOrWhiteSpace(scenes.Value<string>())),
                "profiles" => IsExplicitTrue(parameters?["activate"]),
                _ => true,
            };
        }

        private static bool HasValue(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null) return false;
            return value.Type != JTokenType.String || !string.IsNullOrWhiteSpace(value.Value<string>());
        }

        private static bool IsExplicitTrue(JToken value)
        {
            if (!HasValue(value)) return false;
            if (value.Type == JTokenType.Boolean) return value.Value<bool>();
            if (value.Type == JTokenType.Integer) return value.Value<long>() != 0;
            if (value.Type == JTokenType.String)
            {
                string normalized = value.Value<string>()?.Trim().ToLowerInvariant();
                if (normalized is "false" or "0" or "no" or "off") return false;
                return normalized is "true" or "1" or "yes" or "on" || !string.IsNullOrEmpty(normalized);
            }
            return true;
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
            // Veil.2 is a coordinator-only fork. A stock/legacy server must not
            // opt the Unity process out of lease enforcement during registration.
            return true;
        }

        private static string Quote(string value) => $"\"{(value ?? string.Empty).Replace("\"", "\\\"")}\"";
    }
}
