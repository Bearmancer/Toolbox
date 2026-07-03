# Phase 4: MCP Config + Test

## Tasks

### Task 11: Configure AWS MCP servers in opencode.jsonc

**What to do:**
Add five MCP server entries to `~/.config/opencode/opencode.jsonc` under the `"mcp"` key:

1. `"aws-knowledge"` — type remote, url `https://knowledge-mcp.global.api.aws`, enabled true
2. `"aws-documentation"` — type local, command `["uv", "tool", "run", "--from", "awslabs.aws-documentation-mcp-server@latest", "awslabs.aws-documentation-mcp-server.exe"]`, environment: `{"FASTMCP_LOG_LEVEL": "ERROR", "AWS_DOCUMENTATION_PARTITION": "aws"}`, enabled true
3. `"amazon-translate"` — type local, command `["uv", "tool", "run", "--from", "awslabs.amazon-translate-mcp-server@latest", "awslabs.amazon-translate-mcp-server.exe"]`, environment: `{"AWS_REGION": "{env:AWS_REGION}", "AWS_PROFILE": "{env:AWS_PROFILE}", "FASTMCP_LOG_LEVEL": "ERROR"}`, enabled true
4. `"billing-cost-management"` — type local, command `["uv", "tool", "run", "--from", "awslabs.billing-cost-management-mcp-server@latest", "awslabs.billing-cost-management-mcp-server.exe"]`, environment: `{"AWS_REGION": "{env:AWS_REGION}", "AWS_PROFILE": "{env:AWS_PROFILE}", "FASTMCP_LOG_LEVEL": "ERROR"}`, enabled true
5. `"document-loader"` — type local, command `["uv", "tool", "run", "--from", "awslabs.document-loader-mcp-server@latest", "awslabs.document-loader-mcp-server.exe"]`, environment: `{"FASTMCP_LOG_LEVEL": "ERROR"}`, enabled true

**IMPORTANT:** Before committing to the .exe command names, run `uv tool run --from <package>@latest <package>.exe --help` to verify the entry point exists.

**Must NOT:**
- Modify existing MCP entries
- Add `"autoApprove"` or `"disabled"` keys
- Hardcode AWS credentials or profile names

**References:**
- `~/.config/opencode/opencode.jsonc:13-70`

**Acceptance criteria:**
- opencode.jsonc is valid JSON (parseable)
- All five entries present
- `uv --version` returns successfully

**QA:**
```bash
uv --version
# Verify opencode.jsonc is valid JSON
Get-Content ~/.config/opencode/opencode.jsonc | ConvertFrom-Json | Out-Null
```
Expected: No errors

**Commit:** `feat(config): add AWS MCP servers to opencode`

---

### Task 12: Run translation test

**What to do:**
Execute the translation test:

**Primary (MCP):**
Ask the assistant to use the `amazon-translate` MCP `translate_text` tool:
- `text="Jahan Teri Yeh Nazar Hai Mujhe Hai Jaan Mujhe Khabar Hai"`
- `source_language="auto"`
- `target_language="de"`

**Fallback (CLI):**
```bash
dotnet run --project src/App -- aws translate "Jahan Teri Yeh Nazar Hai Mujhe Hai Jaan Mujhe Khabar Hai" --to de
```

**Must NOT:**
- Skip verification
- Assert the exact translation

**References:**
- `src/CLI/Azure/TranslateCommand.cs:14-19`

**Acceptance criteria:**
- German text is printed to stdout
- The detected language is Hindi or "auto"
- No exception is thrown

**QA:**
```bash
$env:AWS_REGION="us-east-1"
dotnet run --project src/App -- aws translate "Jahan Teri Yeh Nazar Hai Mujhe Hai Jaan Mujhe Khabar Hai" --to de
```
Expected: German output appears (contains umlauts or common German words)

**Commit:** N/A (test only, no commit needed)

---

## Final Verification

```bash
dotnet build
dotnet run --project src/App -- aws translate "Hello" --to de
```

Full solution builds. Translation test passes.

**Dependencies:** Phase 3, Task 11
**Blocks:** None (final phase)
