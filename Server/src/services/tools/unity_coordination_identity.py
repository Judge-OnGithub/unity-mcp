"""Read-only bootstrap identity for coordinator-bound MCP mutation leases."""
from __future__ import annotations

from typing import Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.coordination_identity import build_coordination_identity
from services.registry import mcp_for_unity_tool
from transport.plugin_hub import PluginHub


@mcp_for_unity_tool(
    unity_target=None,
    group=None,
    description=(
        "Return the current FastMCP session and selected Unity editor identity needed "
        "to bind a coordinator lease. This read-only bootstrap tool never returns a lease capability."
    ),
    annotations=ToolAnnotations(
        title="Unity Coordination Identity",
        readOnlyHint=True,
        destructiveHint=False,
        idempotentHint=True,
        openWorldHint=False,
    ),
)
async def unity_coordination_identity(ctx: Context) -> dict[str, Any]:
    """Expose selected registered identity after normal instance middleware injection."""
    selected_instance = await ctx.get_state("unity_instance")
    # The normal middleware leaves this unset when no instance exists or when
    # selection is ambiguous. Do not query PluginHub with a partial target.
    if (
        not isinstance(selected_instance, str)
        or "@" not in selected_instance
        or not selected_instance.rpartition("@")[0]
        or not selected_instance.rpartition("@")[2]
    ):
        return build_coordination_identity(getattr(ctx, "session_id", None), selected_instance, None)
    user_id = await ctx.get_state("user_id")
    facts = await PluginHub.get_authority_for_instance(selected_instance, user_id=user_id)
    return build_coordination_identity(
        getattr(ctx, "session_id", None),
        selected_instance,
        facts,
    )
