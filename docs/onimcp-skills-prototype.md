# Experimental MCP Skills package

The modern 2026-07-28 OniMcp endpoint has a read-only Skills extension prototype.

It is **disabled by default**. Enable `SkillsExtensionEnabled` only on a test instance. No new write tools are exposed. Existing legacy 2025 clients and the seven-tool gameplay surface are unchanged.

## Bundled package

The one canonical URI is `skill://oni-mcp-control/SKILL.md`. The package also contains:

- `skill://oni-mcp-control/references/world-editor.md`
- `skill://oni-mcp-control/references/world-editor.zh.md`
- `skill://oni-mcp-control/references/tool-reference.md`
- `skill://oni-mcp-control/references/tool-reference.zh.md`

The source remains the files under `.agents/skills/oni-gameplay/`. The build embeds **only** these five files under matching canonical resource names. The skill frontmatter advertises `name: oni-mcp-control`. The optional sibling colony-strategy skill is not bundled.

The server takes one immutable in-memory byte snapshot from the embedded assembly resources. `skills/list` and `skills/get` expose its exact file sizes and SHA-256 digests; `resources/read` returns those same bytes decoded as UTF-8. A fixed URI allowlist rejects all other paths. The existing HTTP authentication gate applies equally to listing, direct lookup and reading.

The extension does not advertise `directoryRead`. A request with a pagination cursor is rejected for `skills/list`, since the package is returned as one complete page. Unknown `skills/get` URIs return Invalid params (`-32602`). Enabling this option changes only the modern stateless surface.

## Validation before general release

The host test project checks the disabled/enabled HTTP route, response metadata, manifest equality, raw-file digest/size consistency, traversals and missing URIs, and the legacy transport test suite. Both OniMcp quality and pinned ONI reference builds must pass.

Before enabling it by default, record all of the following:

1. Run the official MCP Skills extension conformance suite against a real OniMcp modern HTTP endpoint and record its pinned runner revision.
2. Run MCP Inspector 2.6.0 (or a pinned newer supported release) `skills/list --verify`; the verification must confirm the served frontmatter and all five digest/size values.
3. Import this skill through one independent consuming client. Check all four references can be read without a session ID.
4. Test that the legacy session transport and gameplay policy are unchanged in a real ONI build.

No successful external Inspector or game-runtime run is claimed by the host-only tests. Until this evidence exists, keep the experimental option disabled on production colonies.
