"""Pure validation for the public MCP-to-coordinator identity bootstrap."""
from __future__ import annotations

from pathlib import Path
from typing import Any


def build_coordination_identity(
    session_id: str | None,
    selected_instance: str | None,
    facts: dict[str, Any] | None,
) -> dict[str, Any]:
    """Return only the public facts required to pre-bind a coordinator lease."""
    if not isinstance(session_id, str) or not session_id:
        raise RuntimeError("FastMCP session identity is unavailable")
    if not isinstance(selected_instance, str) or "@" not in selected_instance:
        raise RuntimeError(
            "No unambiguous Unity instance is selected. Call set_active_instance with Name@hash first."
        )
    instance_name, marker, instance_hash = selected_instance.rpartition("@")
    if not marker or not instance_name or not instance_hash:
        raise RuntimeError(
            "No unambiguous Unity instance is selected. Call set_active_instance with Name@hash first."
        )
    if not isinstance(facts, dict):
        raise RuntimeError("Selected Unity instance is not currently registered")
    editor_instance_id = facts.get("editor_instance_id")
    project_path = facts.get("project_path")
    unity_pid = facts.get("unity_pid")
    unity_start_identity = facts.get("unity_start_identity")
    if (
        not isinstance(editor_instance_id, str) or not editor_instance_id
        or not isinstance(project_path, str) or not project_path
        or not isinstance(unity_start_identity, str) or not unity_start_identity
    ):
        raise RuntimeError("Selected Unity instance is missing coordinator identity facts")
    try:
        pid = int(unity_pid)
        canonical_project_path = str(Path(project_path).resolve(strict=True))
    except (OSError, TypeError, ValueError) as exc:
        raise RuntimeError("Selected Unity instance has invalid coordinator identity facts") from exc
    if pid <= 0:
        raise RuntimeError("Selected Unity instance has invalid coordinator identity facts")
    return {
        "session_id": session_id,
        "unity_instance_name": instance_name,
        "unity_instance_hash": instance_hash,
        "editor_instance_id": editor_instance_id,
        "project_path": canonical_project_path,
        "unity_pid": pid,
        "unity_start_identity": unity_start_identity,
    }
