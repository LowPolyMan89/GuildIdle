# GuildIdle Unity MCP (confirmation-gated first mutation)

This integration lets a local Codex host inspect the open GuildIdle Unity Editor without screen automation. Existing inspection tools remain bounded and read-only. The only mutation is one narrowly scoped operation that can add a named `RectTransform` GameObject under an explicitly named parent in an already-loaded scene. It cannot execute arbitrary code, use generic reflection, enter play mode, change selection, open scenes, modify prefabs, import assets, or create/overwrite asset files.

## How it works

- `Assets/Scripts/Editor/Mcp/UnityMcpBridge.cs` starts inside the Unity Editor after scripts compile.
- It listens only on `127.0.0.1` (port `8963` by default), creates a random token for each editor/domain session, and stores connection data under ignored `Library/UnityMcp/bridge.json`.
- `unity-mcp-stdio.js` is the MCP stdio process launched by Codex. It converts MCP tool calls into authenticated loopback requests and returns both text and structured JSON results.
- All Unity API access is marshalled onto the Editor main thread. Requests, searches, hierarchies, and the mutation-plan cache are bounded.
- Mutation requires a read-only preview plan followed by a separately approved apply call using the exact one-time token within 90 seconds.

## Connect from Codex

The repository includes `.codex/config.toml` with a project-scoped server entry and an explicit tool allowlist. Trust/open `E:/repo/GuildIdle` as the Codex project, leave Unity Editor open, and wait for script compilation to finish. Then restart the local Codex client so it reloads MCP configuration.

In the ChatGPT desktop app or Codex IDE extension, the equivalent manual server setup is:

- Name: `guildidle_unity`
- Transport: `STDIO`
- Command: `node`
- Arguments: `E:/repo/GuildIdle/Tools/UnityMcp/unity-mcp-stdio.js`
- Working directory: `E:/repo/GuildIdle`
- Environment: `GUILDIDLE_UNITY_PROJECT=E:/repo/GuildIdle`

Use `/mcp` to confirm that `guildidle_unity` is connected. The adapter requires Node.js 18 or newer and uses no npm packages.

## Tools

- `unity_editor_status`: Unity/editor/play/compile state and active-scene summary.
- `unity_selection`: current Editor selection with asset and hierarchy identity.
- `unity_find_assets`: bounded `AssetDatabase.FindAssets` search with optional type and folder filters.
- `unity_scene_hierarchy`: bounded flattened hierarchy for a scene that is already loaded.
- `unity_inspect_prefab`: bounded prefab hierarchy and component/script summary without opening Prefab Mode.
- `unity_plan_create_ui_object`: read-only validation and preview; stores at most 32 plans and returns a one-time token valid for 90 seconds.
- `unity_apply_create_ui_object`: write-capable apply; requires the exact token plus `confirmation: "APPLY"`, consumes the token once, and is explicitly configured for Codex approval.

A useful first request is: `Inspect Assets/Prefabs/UI/UIRoot.prefab with unity_inspect_prefab and summarize its layers and attached scripts.`

For a future HUD addition, first request a plan with the exact loaded scene, parent hierarchy path, and object name. Review the returned scope, defaults, save policy, and expiry before confirming apply. The default `RectTransform` is centered with anchors/pivot `0.5,0.5`, position `0,0`, size `100,100`, and scale `1,1,1`.

### Save and test behavior

- `save_scene` defaults to `false`; a normal apply then marks the named scene dirty and registers Unity Undo without saving.
- `save_scene: true` is accepted only for the exact named loaded scene when it was clean at plan and apply time and is open for edit. It saves only after the separately confirmed addition.
- `test_only: true` constructs and validates a hidden `HideAndDontSave` object, destroys it immediately, never parents it into the scene, and cannot be combined with saving.
- Plans are rejected if the parent is missing/ambiguous, a same-name child exists, paths/names are unsafe, the Editor is playing/compiling/updating, or target/dirty state changed before apply.

## Validate the adapter

From the repository root:

```powershell
node --test Tools/UnityMcp/tests/protocol.test.js
```

This runs a fake authenticated Unity bridge and validates MCP initialization, deterministic tool discovery, read/write annotations, plan/apply argument mapping, and structured results. Unity itself compiles the Editor bridge when the project is open; check the Console for `[UnityMcpBridge] Confirmation-gated bridge listening...` or a compile error.

For a direct smoke check after Unity compiles:

```powershell
$request = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke","version":"1"}}}'
$request | node Tools/UnityMcp/unity-mcp-stdio.js
```

## Safety properties

- Loopback binding only; remote endpoints are rejected.
- A fresh 256-bit token is required and is kept under ignored `Library/`.
- No generic reflection/execution endpoint exists; the single mutation can only add one safe-default `RectTransform`.
- Scene paths are restricted to existing `.unity` assets under `Assets/`; the bridge never opens an unloaded scene.
- Search input, HTTP bodies, folder counts, hierarchy depth, node counts, and timeouts are bounded.
- Mutation plans are random, in-memory, one-time, expire after 90 seconds, and are capped at 32 entries.
- MCP tools declare accurate read/write, destructive, idempotent, and open-world annotations.
- The apply tool rejects stale parent identity/children and changed scene dirty state, rejects name collisions, and never creates or overwrites asset files.
- Codex configuration uses an explicit `enabled_tools` allowlist and `default_tools_approval_mode = "writes"`, so a future write-capable tool is not silently treated like these reads.
- `unity_apply_create_ui_object` also has an explicit per-tool `approval_mode = "approve"`.

## Limitations

- Unity Editor must be open on this project. Script compilation/domain reloads briefly disconnect the bridge.
- Plans do not survive a domain reload and cannot be replayed.
- Only already-loaded scenes can be inspected. This first mutation never opens a scene or changes a prefab asset.
- Prefab inspection reports hierarchy, component types, component enabled state, and script paths; it does not dump arbitrary serialized fields or object contents.
- Results are snapshots and can become stale immediately as a developer edits the project.
- The default port supports one GuildIdle Editor instance. Set `GUILDIDLE_UNITY_MCP_PORT` in Unity's environment before launch if a different port is required; the adapter discovers the actual port from the descriptor.
- The MCP adapter implements the stable initialization/tool subset used by local Codex clients. It does not implement resources, prompts, subscriptions, sampling, elicitation, server-to-client requests, or streaming results.
