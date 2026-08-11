"""Audited lease-free reads must never refresh/import Unity state in preflight."""

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import services.tools.find_gameobjects as find_gameobjects_module
import services.tools.manage_asset as manage_asset_module
import services.tools.manage_prefabs as manage_prefabs_module
import services.tools.manage_scene as manage_scene_module


class _Gate:
    def model_dump(self):
        return {"success": False, "error": "test-gate"}


def _preflight_kwargs(module, function, **kwargs):
    preflight = AsyncMock(return_value=_Gate())
    with (
        patch.object(module, "get_unity_instance_from_context", AsyncMock(return_value="instance")),
        patch.object(module, "preflight", preflight),
    ):
        asyncio.run(function(SimpleNamespace(), **kwargs))
    return preflight.await_args.kwargs


def test_declared_reads_do_not_refresh_if_dirty():
    assert _preflight_kwargs(
        find_gameobjects_module,
        find_gameobjects_module.find_gameobjects,
        search_term="Player",
    )["refresh_if_dirty"] is False
    assert _preflight_kwargs(
        manage_asset_module,
        manage_asset_module.manage_asset,
        action="search",
        path="Assets",
    )["refresh_if_dirty"] is False
    assert _preflight_kwargs(
        manage_prefabs_module,
        manage_prefabs_module.manage_prefabs,
        action="get_info",
        prefab_path="Assets/Test.prefab",
    )["refresh_if_dirty"] is False
    assert _preflight_kwargs(
        manage_scene_module,
        manage_scene_module.manage_scene,
        action="get_active",
    )["refresh_if_dirty"] is False


def test_scene_view_frame_is_a_mutation_and_may_refresh():
    assert _preflight_kwargs(
        manage_scene_module,
        manage_scene_module.manage_scene,
        action="scene_view_frame",
        scene_view_target="Player",
    )["refresh_if_dirty"] is True


def test_asset_preview_generation_is_a_mutation_and_may_refresh():
    assert _preflight_kwargs(
        manage_asset_module,
        manage_asset_module.manage_asset,
        action="get_info",
        path="Assets/Test.prefab",
        generate_preview=True,
    )["refresh_if_dirty"] is True
