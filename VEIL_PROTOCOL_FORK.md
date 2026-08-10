# Veil Protocol MCP fork

This repository is the project-controlled MCP for Unity fork used by
Veil Protocol. Its package remains named `com.coplaydev.unity-mcp` so Unity
replaces the upstream package without changing project integrations.

## Baseline

- Upstream: `CoplayDev/unity-mcp`
- Upstream tag: `v10.1.2`
- Upstream tag object: `caf40172a3ba3a0920be0a5d0f6fa45946685eac`
- Upstream commit: `4ce7dd3cc54e37e2ed6dc59cb5a047f3dccb3f50`
- Fork package version: `10.1.2-veil.1`
- Supported Veil Editor: Unity `6000.5.4f1`
- Matching Python server: `mcpforunityserver==10.1.2`

Veil Protocol pins the package to an immutable commit from this fork. The
branch name is not the dependency contract.

## Why the fork exists

An Editor session accumulated one hidden
`UnityEditor.TestTools.TestRunner.Api.TestRunnerApi` and associated view data
after each domain reload. After 366 reloads, the session contained 365 leaked
test-runner APIs and reload time had grown from roughly 4–5 seconds to
61–64 seconds. MCP's scripts-only refresh path could then double that cost by
requesting compilation before the AssetDatabase imported an externally edited
script, causing a public-API reload followed by an asset-triggered reload.

HDRP postprocessing measured about 1.1–1.2 seconds in the same refreshes and
was not the root cause.

## Fork changes

1. `TestRunnerNoThrottle` retains its callback, unregisters it, and destroys
   its owned `TestRunnerApi` before assembly reload and Editor shutdown. Its
   initialization is idempotent and removes stale fork-owned instances.
2. `MCPServiceLocator` disposes and clears the lazy `TestRunnerService` before
   assembly reload and Editor shutdown without resetting bridge or transport
   services. The service API is named and its constructor removes any stale
   service-owned API restored by Unity before creating the current instance.
3. `refresh_unity` imports filesystem changes synchronously before compilation.
   A direct `CompilationPipeline.RequestScriptCompilation()` is used only when
   the caller explicitly requests compilation without an asset refresh.
4. A bounded eight-frame post-reload cleanup keeps one Unity 6.5
   `EditorWindowViewData` singleton per preferences key. Unity otherwise leaves
   a new toolbar and Inspector view-data object after each reload; the short
   multi-frame window also catches toolbar data created after the first delayed
   Editor callback.
5. The same bounded cleanup removes additional unnamed, non-persistent
   `TestRunnerApi` objects abandoned by other reload initializers, including
   Unity Performance Testing. It preserves named APIs, persistent APIs, APIs
   referenced directly by open Editor windows, and one unreferenced callback
   registration API.
6. EditMode regressions cover lifecycle cleanup, service disposal, view-data
   and orphaned-API deduplication, and refresh planning.

## Port verification status

This `v10.1.2` port was reviewed statically only. It does not claim a clean
Unity import, reload-loop measurement, bridge reconnection, or EditMode or
PlayMode test execution. The coordinator must obtain that evidence before
updating a consumer pin.

## Required verification

Before Veil Protocol moves this pin to another fork commit or upstream release:

1. Run the fork's focused EditMode regression tests.
2. Resolve the candidate package in a clean Veil project import.
3. Run at least ten ordinary external script edits/domain reloads.
4. Record reload timings and hidden `TestRunnerApi`,
   `EditorWindowViewData`, and `PlaymodeTestsController` counts before and
   after the loop; counts must remain bounded.
5. Confirm each script edit produces one compilation/domain reload.
6. Run one EditMode and one PlayMode test through MCP.
7. Confirm the HTTP bridge reconnects after a domain reload and can read the
   Editor Console.

Do not accept “the project compiles” as sufficient migration evidence.

## Updating from upstream

Create a new branch from the currently pinned fork commit, fetch the intended
upstream tag, and review the three patched areas explicitly. Retain the fixes
unless the candidate contains an equivalent implementation and the full Veil
verification above passes. Publish a new immutable fork commit, update the
Veil manifest and lockfile together, and update the Veil bridge decision and
verification record.

Never develop the durable fix in a consumer project's `Library/PackageCache`;
Unity replaces that directory during package resolution.

## Rollback

Restore the previously accepted immutable fork commit in both
`Packages/manifest.json` and `Packages/packages-lock.json`, close Unity, allow
its package/import workers to exit, and reopen the project. Do not roll back to
the upstream `v10.0.0` tag: it contains the reload leak and refresh-order bug.
