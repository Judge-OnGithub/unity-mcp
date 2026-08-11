from pathlib import Path

import pytest

from services.coordination_identity import build_coordination_identity


def test_identity_returns_exact_public_binding_facts(tmp_path):
    project = tmp_path / "Space Miner Incremental"
    project.mkdir()

    identity = build_coordination_identity(
        "fastmcp-session",
        "Miner@abc123",
        {
            "editor_instance_id": "stable-editor-instance",
            "project_path": str(project),
            "unity_pid": 1234,
            "unity_start_identity": "987654321",
        },
    )

    assert identity == {
        "session_id": "fastmcp-session",
        "unity_instance_name": "Miner",
        "unity_instance_hash": "abc123",
        "editor_instance_id": "stable-editor-instance",
        "project_path": str(project.resolve()),
        "unity_pid": 1234,
        "unity_start_identity": "987654321",
    }


@pytest.mark.parametrize("selected_instance", [None, "", "ambiguous-hash", "@hash", "Name@"])
def test_identity_rejects_absent_or_ambiguous_selection(tmp_path, selected_instance):
    with pytest.raises(RuntimeError, match="unambiguous Unity instance"):
        build_coordination_identity(
            "fastmcp-session",
            selected_instance,
            {"project_path": str(tmp_path)},
        )


def test_identity_rejects_missing_or_invalid_registered_facts(tmp_path):
    with pytest.raises(RuntimeError, match="missing coordinator identity facts"):
        build_coordination_identity("session", "Miner@hash", {"project_path": str(tmp_path)})
    with pytest.raises(RuntimeError, match="invalid coordinator identity facts"):
        build_coordination_identity(
            "session",
            "Miner@hash",
            {
                "editor_instance_id": "stable",
                "project_path": str(tmp_path / "missing"),
                "unity_pid": 0,
                "unity_start_identity": "start",
            },
        )


@pytest.mark.asyncio
async def test_tool_reads_only_the_injected_session_and_registered_instance(monkeypatch, tmp_path):
    from services.tools.unity_coordination_identity import unity_coordination_identity

    project = tmp_path / "Project"
    project.mkdir()

    class Context:
        session_id = "exact-fastmcp-session"

        async def get_state(self, key):
            return {
                "unity_instance": "Miner@abc123",
                "user_id": None,
            }.get(key)

    async def registered_facts(cls, instance, *, user_id=None):
        assert instance == "Miner@abc123"
        assert user_id is None
        return {
            "editor_instance_id": "stable-editor",
            "project_path": str(project),
            "unity_pid": 4321,
            "unity_start_identity": "start-id",
        }

    monkeypatch.setattr(
        "services.tools.unity_coordination_identity.PluginHub.get_authority_for_instance",
        classmethod(registered_facts),
    )

    assert await unity_coordination_identity(Context()) == {
        "session_id": "exact-fastmcp-session",
        "unity_instance_name": "Miner",
        "unity_instance_hash": "abc123",
        "editor_instance_id": "stable-editor",
        "project_path": str(project.resolve()),
        "unity_pid": 4321,
        "unity_start_identity": "start-id",
    }
