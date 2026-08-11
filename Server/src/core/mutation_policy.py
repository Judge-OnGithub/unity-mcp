"""Single server-side authority for MCP command mutability.

This module intentionally defaults to ``mutate``.  Tool descriptions,
annotations, and Unity registration are not authority; every new command/action
must be added here with an audited ``read`` classification before it can bypass
the coordinator lease gate.
"""
from __future__ import annotations

from enum import Enum
from typing import Any


class MutationPolicy(str, Enum):
    READ = "read"
    MUTATE = "mutate"


# Server-only tools which cannot enqueue Unity work or modify tool visibility.
READ_TOOLS = frozenset({
    "debug_request_context",
    "find_in_file",
    "find_gameobjects",
    "get_sha",
    # Bootstrap-only: changes session-local routing and cannot enqueue Unity
    # work, so a client can select its instance before creating a lease binding.
    "set_active_instance",
    "unity_coordination_identity",
    "unity_docs",
    "unity_reflect",
    "validate_script",
})

# Commands which are never safe to execute without the lease.  This includes
# refresh/import/preview/test/build/control-plane work even if a particular
# invocation appears observational.
MUTATING_TOOLS = frozenset({
    "apply_text_edits",
    "batch_execute",
    "create_script",
    "delete_script",
    "execute_code",
    "execute_custom_tool",
    "execute_menu_item",
    "generate_audio",
    "generate_image",
    "generate_model",
    "import_model",
    "import_model_file",
    "manage_components",
    "manage_tools",
    "refresh_unity",
    "run_tests",
    "script_apply_edits",
})

# Only audited action values appear here.  Omitted and unknown actions are
# mutations by design.
READ_ACTIONS: dict[str, frozenset[str]] = {
    "manage_asset": frozenset({"search", "get_info", "get_components"}),
    "manage_build": frozenset({"status", "settings", "scenes", "profiles"}),
    "manage_editor": frozenset({"telemetry_status", "telemetry_ping"}),
    "manage_material": frozenset({"ping", "get_material_info"}),
    "manage_packages": frozenset({"list_packages", "search_packages", "get_package_info", "ping", "status"}),
    "manage_prefabs": frozenset({"get_info", "get_hierarchy"}),
    "manage_scene": frozenset({"get_hierarchy", "get_active", "get_build_settings", "get_loaded_scenes", "scene_view_frame"}),
    "manage_script": frozenset({"read", "get_sha", "validate"}),
    "manage_shader": frozenset({"read"}),
    "manage_ui": frozenset({"ping", "read"}),
    # Empty action is the tool's documented default and means `get`.
    "read_console": frozenset({"", "get"}),
}


def _action(params: dict[str, Any] | None) -> str:
    if not isinstance(params, dict):
        return ""
    value = params.get("action")
    return value.strip().casefold() if isinstance(value, str) else ""


def classify_command(name: str | None, params: dict[str, Any] | None = None) -> MutationPolicy:
    """Return the fail-closed policy for a FastMCP or Unity command."""
    normalized = (name or "").strip().casefold()
    if not normalized:
        return MutationPolicy.MUTATE
    if normalized == "batch_execute":
        commands = params.get("commands") if isinstance(params, dict) else None
        if not isinstance(commands, list):
            return MutationPolicy.MUTATE
        return (
            MutationPolicy.READ
            if all(
                isinstance(item, dict)
                and classify_command(item.get("tool"), item.get("params")) is MutationPolicy.READ
                for item in commands
            )
            else MutationPolicy.MUTATE
        )
    if normalized in READ_TOOLS:
        return MutationPolicy.READ
    if normalized in MUTATING_TOOLS:
        return MutationPolicy.MUTATE
    if normalized in READ_ACTIONS:
        return (
            MutationPolicy.READ
            if _action(params) in READ_ACTIONS[normalized]
            else MutationPolicy.MUTATE
        )
    # ``manage_*`` families and all future/custom tool names are unsafe until
    # explicitly audited above.
    return MutationPolicy.MUTATE


def is_mutation(name: str | None, params: dict[str, Any] | None = None) -> bool:
    return classify_command(name, params) is MutationPolicy.MUTATE
