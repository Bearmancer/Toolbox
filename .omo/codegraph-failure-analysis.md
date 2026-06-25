# CodeGraph Failure Analysis

## Timeline of Failures

### Phase 1: "CodeGraph doesn't exist"

**User claim:** oh-my-openagent added CodeGraph.

**Investigation:** Searched `oh-my-openagent.jsonc`, `oh-my-openagent-default.jsonc`, `opencode.jsonc`, and `node_modules/` for any reference to "codegraph".

**Finding:** Zero matches in all config files and plugin source. The word "codegraph" did not appear anywhere in the local config tree.

**Proposed fix:** None — the feature appeared to not exist.

**Actual situation:** CodeGraph IS a first-class feature of oh-my-openagent (verified via GitHub source), but the local config files don't contain it because it's injected at runtime by the plugin's bootstrap hook, not written to static config files.

---

### Phase 2: "autouse is not a real config"

**User claim:** CodeGraph has an "autouse" config option.

**Investigation:** Read `oh-my-openagent-default.jsonc` (6683-line schema).

**Finding:** `autouse` does not exist. The schema has `codegraph.enabled`, `codegraph.auto_provision`, `codegraph.auto_init`, `codegraph.telemetry`, `codegraph.watch_debounce_ms`. No `autouse`.

**Proposed fix:** Correction — the actual config keys are `enabled`, `auto_provision`, `auto_init`.

**Actual situation:** I was wrong. The user was right that CodeGraph exists, but "autouse" was never a real key. The real key is `enabled` (default: `true`).

---

### Phase 3: MCP server connected but no tools

**User claim:** CodeGraph is connected (visible in sidebar).

**Investigation:** `list_mcp_resources` returned "MCP server 'codegraph' does not support resources."

**Finding:** MCP server process was running (sidebar showed "Connected") but had no tools to expose.

**Proposed fix:** Initialize the codegraph index.

**Actual situation:** The MCP server connected before the index existed, so its initial handshake reported zero tools. The server never re-scanned after the index was created.

---

### Phase 4: Broken symlink blocks initialization

**User claim:** CodeGraph should work.

**Investigation:** `codegraph status --json` returned `"initialized": false`.

**Finding:** `.codegraph` was a symlink pointing to `~/.omo/codegraph/projects/New-848ac712ac0eeb34` — but the target directory did not exist. The symlink was broken.

**Proposed fix:** Remove broken symlink, then run `codegraph init`.

**Actual situation:** oh-my-openagent's `codegraph-bootstrap` hook creates the symlink before ensuring the index directory exists. If `codegraph init` fails or is skipped (binary not yet provisioned, timing race), you get a broken symlink and a connected-but-empty MCP server.

**Recurrent failure:** This is the same root cause as Phase 3 — the bootstrap hook is incomplete.

---

### Phase 5: Initialization succeeds, tools still missing

**User claim:** Restart session, try again.

**Investigation:** `codegraph init` ran successfully (43 files, 496 nodes, 876 edges). `codegraph status --json` returned `"initialized": true`.

**Finding:** Index was built. MCP server still showed "Connected" in sidebar. But codegraph tools (`codegraph_search`, `codegraph_explore`, `codegraph_callers`) were not in any agent's tool manifest.

**Proposed fix:** Restart session to force MCP re-handshake.

**Actual situation:** Session restart did not fix it. The MCP server process remained alive with its original (empty) tool list cached from the pre-index handshake.

---

### Phase 6: Explore agent confirms zero tools

**User claim:** Delegate to explore agent to test codegraph tools.

**Investigation:** Launched explore agent with explicit instructions to call `codegraph_search`, `codegraph_explore`, `codegraph_callers`.

**Finding:** All three tool calls failed — "Tool not found." The explore agent's tool manifest contained zero codegraph tools. The only connected MCP server was `exa`.

**Proposed fix:** Add codegraph MCP entry to `opencode.jsonc`.

**Actual situation:** oh-my-openagent injects codegraph at runtime via its plugin system, but the injection isn't producing a working MCP server. The static config file has no codegraph entry.

---

### Phase 7: Missing MCP registration in config

**User claim:** Fix it.

**Investigation:** Searched `opencode.jsonc` and `oh-my-openagent.jsonc` for "codegraph".

**Finding:** Zero matches. No codegraph MCP server entry exists in either config file.

**Proposed fix:** Add to `opencode.jsonc`:
```json
"codegraph": {
  "type": "local",
  "command": ["codegraph", "serve", "--mcp"],
  "enabled": true
}
```

**Actual situation:** This is the root cause of all tool-availability failures. oh-my-openagent's bootstrap hook provisions the binary and creates the symlink, but never writes the MCP server definition to the config. The sidebar "Connected" status is cosmetic — the server process runs but isn't registered as a tool-providing MCP.

---

## Summary of All Failures

| # | Failure | Root Cause | Fix | Recurrent? |
|---|---------|-----------|-----|-----------|
| 1 | CodeGraph "doesn't exist" in config | Runtime injection, not static config | None needed — feature exists | No |
| 2 | "autouse" config key doesn't exist | Hallucinated key | Use `enabled`, `auto_provision`, `auto_init` | No |
| 3 | MCP connected but no tools | Server started before index existed | Initialize index first | Yes (Phase 5, 6) |
| 4 | Broken symlink blocks init | Bootstrap hook creates symlink before directory exists | Remove broken symlink, run `codegraph init` | Yes (Phase 3) |
| 5 | Index built, tools still missing | MCP server cached empty tool list from first handshake | Need MCP entry in config | Yes (Phase 6, 7) |
| 6 | Explore agent has no codegraph tools | No MCP registration in config | Add MCP entry to `opencode.jsonc` | Yes (Phase 7) |
| 7 | No codegraph in config files | oh-my-openagent bootstrap hook doesn't write MCP entry | Manual config entry required | **Current** |

---

## Intended Behavior (from oh-my-openagent source)

1. **Session start** → `codegraph-bootstrap` hook fires
2. **Hook resolves binary** → bundled, PATH, or auto-provisioned to `~/.omo/codegraph/`
3. **Hook runs `codegraph status --json`** → checks if index exists
4. **If not initialized and `auto_init: true`** → runs `codegraph init` + `codegraph sync`
5. **Hook registers MCP server** → `codegraph serve --mcp` as local stdio MCP
6. **MCP handshake** → server reports available tools to OpenCode
7. **Agent receives tools** → `codegraph_search`, `codegraph_explore`, `codegraph_callers`, etc.

## Actual Behavior

1. **Session start** → `codegraph-bootstrap` hook fires
2. **Hook resolves binary** → succeeds (binary exists at `~/.omo/codegraph/`)
3. **Hook creates symlink** → `.codegraph` → `~/.omo/codegraph/projects/<hash>` — **target directory doesn't exist**
4. **Hook runs `codegraph status`** → returns `"initialized": false` (broken symlink)
5. **`codegraph init` should run** → **either skipped or fails silently** (broken symlink blocks `mkdir`)
6. **MCP server starts** → process alive, sidebar shows "Connected"
7. **MCP handshake** → server reports **zero tools** (no index to serve from)
8. **Agent receives tools** → no codegraph tools in manifest
9. **User creates index manually** → `Remove-Item .codegraph; codegraph init` → index built
10. **MCP server still running** → cached original empty tool list, never re-scans
11. **Session restart** → MCP server reconnects with **still empty tool list** (config has no MCP entry)
12. **Final state** → index exists, server runs, tools unavailable

---

## Bug in Logic

The bootstrap hook has a **chicken-and-egg problem**:

1. It creates the symlink BEFORE checking if the target exists
2. It checks `codegraph status` which reads through the symlink — if symlink is broken, status reports "not initialized"
3. It should then run `codegraph init`, but the broken symlink blocks `mkdir` in `codegraph init`
4. Even if init succeeds, the MCP server was already started with empty tools
5. The MCP server never re-scans — there's no mechanism to refresh the tool list after index creation

**The fix requires two things:**
1. Remove broken symlink before `codegraph init`
2. Either restart MCP server after init, or add the MCP entry to static config so it reconnects on session start

---

## Recommended Fix

Add to `C:\Users\Lance\.config\opencode\opencode.jsonc` under `"mcp"`:

```json
"codegraph": {
  "type": "local",
  "command": ["codegraph", "serve", "--mcp"],
  "enabled": true
}
```

This ensures the MCP server is registered statically, not just via the buggy bootstrap hook. On session start, OpenCode will start the MCP server fresh, which will find the existing index and expose all tools.
