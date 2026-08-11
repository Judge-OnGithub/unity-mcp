"""Coordinator-backed authority for coordinated MCP mutations.

No client capability is accepted here.  The coordinator has already bound the
FastMCP session to an exact Unity editor instance; this adapter only validates
that durable state and produces the private server-to-Unity envelope.
"""
from __future__ import annotations

from contextvars import ContextVar
import importlib.util
import os
from pathlib import Path
import uuid
from typing import Any


_authorization: ContextVar[dict[str, Any] | None] = ContextVar(
    "mcpforunity_mutation_authorization", default=None
)
_read_request: ContextVar[bool] = ContextVar("mcpforunity_read_request", default=False)


class AuthorityDenied(RuntimeError):
    """Raised when coordinator state does not authorize a mutation."""


def coordinated_mode() -> bool:
    return os.environ.get("UNITY_MCP_COORDINATED_MODE", "").strip().casefold() in {
        "1", "true", "yes", "on"
    }


def _coordinator_path(project_path: str | None) -> Path:
    override = os.environ.get("UNITY_COORDINATOR_CLI", "").strip()
    if override:
        return Path(override).expanduser().resolve()
    # The controlled launcher starts the server from the Unity project child;
    # its repository-level tools directory is one parent above that project.
    candidate = (Path(project_path).resolve().parent if project_path else Path.cwd().parent) / "tools" / "unity_coordinator.py"
    if candidate.is_file():
        return candidate.resolve()
    raise AuthorityDenied("Unity coordinator validator is not configured")


def _load_coordinator_module(project_path: str | None):
    path = _coordinator_path(project_path)
    spec = importlib.util.spec_from_file_location("unity_coordinator_authority", path)
    if spec is None or spec.loader is None:
        raise AuthorityDenied("Unity coordinator validator could not be loaded")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _canonical_instance_id(unity_instance: str | None) -> str:
    if not unity_instance:
        raise AuthorityDenied("A selected Unity instance is required for mutation")
    _, marker, suffix = unity_instance.rpartition("@")
    return suffix if marker and suffix else unity_instance


def validate_for_server(
    *,
    session_id: str | None,
    unity_instance: str | None,
    editor_instance_id: str | None,
    project_path: str | None,
    unity_pid: int | None,
    unity_start_identity: str | None,
) -> dict[str, Any]:
    """Read coordinator state and create a one-operation internal envelope."""
    if not coordinated_mode():
        raise AuthorityDenied("Coordinated mutation authority is required")
    if not session_id or not editor_instance_id or not project_path or not unity_pid or not unity_start_identity:
        raise AuthorityDenied("Unity mutation authority is incomplete")
    module = _load_coordinator_module(project_path)
    try:
        configured_state_root = os.environ.get("UNITY_COORDINATION_STATE_ROOT", "").strip()
        state_root = (
            Path(configured_state_root).expanduser().resolve()
            if configured_state_root
            else module.coordination_state_root(Path(project_path))
        )
        store = module.LeaseStore(state_root)
        lease = store.validate_mcp_authority(
            session_id=session_id,
            instance_id=editor_instance_id,
            project_path=project_path,
            unity_pid=int(unity_pid),
            unity_start_identity=unity_start_identity,
        )
    except Exception as exc:  # coordinator controls the safe public reason
        raise AuthorityDenied("Unity mutation authority was denied") from exc
    if not isinstance(lease, dict):
        raise AuthorityDenied("Unity coordinator returned invalid mutation authority")
    try:
        generation = int(lease["generation"])
        lease_id = str(lease["lease_id"])
        expires_at = str(lease["expires_at"])
    except (KeyError, TypeError, ValueError) as exc:
        raise AuthorityDenied("Unity coordinator returned incomplete mutation authority") from exc
    return {
        "lease_id": lease_id,
        "generation": generation,
        "mcp_session_id": session_id,
        "editor_instance_id": editor_instance_id,
        "unity_instance_id": _canonical_instance_id(unity_instance),
        "project_path": project_path,
        "unity_pid": int(unity_pid),
        "unity_start_identity": unity_start_identity,
        "expires_at": expires_at,
        # This is a machine-local path, not a credential.  Carry it so the
        # Editor's final validation uses the same explicitly selected state
        # root as the server when the steward overrides auto-detection.
        "coordination_state_root": str(state_root),
        "operation_id": uuid.uuid4().hex,
    }


def set_current_authorization(value: dict[str, Any]):
    return _authorization.set(value)


def reset_current_authorization(token) -> None:
    _authorization.reset(token)


def current_authorization() -> dict[str, Any] | None:
    return _authorization.get()


def set_read_request():
    return _read_request.set(True)


def reset_read_request(token) -> None:
    _read_request.reset(token)


def is_read_request() -> bool:
    return _read_request.get()
