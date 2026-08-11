from core.mutation_policy import MutationPolicy, classify_command


def test_audited_reads_are_lease_free():
    assert classify_command("read_console", {}) is MutationPolicy.READ
    assert classify_command("read_console", {"action": "get"}) is MutationPolicy.READ
    assert classify_command("manage_asset", {"action": "search"}) is MutationPolicy.READ
    assert classify_command("manage_scene", {"action": "get_hierarchy"}) is MutationPolicy.READ
    # Bootstrap selection changes only this FastMCP session's routing state;
    # it cannot enqueue a Unity command.
    assert classify_command("set_active_instance", {"instance": "Project@hash"}) is MutationPolicy.READ
    assert classify_command("unity_coordination_identity", {}) is MutationPolicy.READ


def test_unknown_and_write_actions_fail_closed():
    assert classify_command("future_plugin_command", {}) is MutationPolicy.MUTATE
    assert classify_command("execute_custom_tool", {"tool_name": "unknown"}) is MutationPolicy.MUTATE
    assert classify_command("manage_asset", {"action": "preview"}) is MutationPolicy.MUTATE
    assert classify_command("manage_scene", {"action": "load"}) is MutationPolicy.MUTATE
    assert classify_command("read_console", {"action": "clear"}) is MutationPolicy.MUTATE
    assert classify_command("manage_script_capabilities", {}) is MutationPolicy.MUTATE


def test_batch_is_read_only_only_when_every_child_is_audited_read():
    assert classify_command(
        "batch_execute",
        {"commands": [{"tool": "read_console", "params": {}}]},
    ) is MutationPolicy.READ
    assert classify_command(
        "batch_execute",
        {"commands": [{"tool": "read_console", "params": {}}, {"tool": "execute_code", "params": {}}]},
    ) is MutationPolicy.MUTATE
