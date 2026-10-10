#!/usr/bin/env python3
"""Keep game-context invalidation ahead of ONI's save-load teardown."""

from pathlib import Path
import re

from verify_restart_load_contract import body, ordered


root = Path(__file__).resolve().parents[1]
source = (root / "mods/OniMcp/Tools/Impl/Core/GameSaveTools.cs").read_text(encoding="utf-8")
load_save = body(source, "public static McpTool LoadSave()")

ordered(
    load_save,
    "ResolveLoadTarget(args, out error)",
    "if (target == null)",
    "GameContextLifecycle.BeginSaveLoad()",
    "LoadScreen.DoLoad(target)",
    "GameContextLifecycle.SaveLoadFailed(contextGeneration)",
)

admission = (root / "mods/OniMcp/Server/McpHttpServerAdmission.cs").read_text(encoding="utf-8")
routes = (root / "mods/OniMcp/Tools/Core/OniResourceRegistryWorldAndColonyRoutes.cs").read_text(encoding="utf-8")
registry = (root / "mods/OniMcp/Tools/Core/OniResourceRegistry.cs").read_text(encoding="utf-8")
policy = body(admission, "private static bool IsGameContextBoundResourceRead")
catalog_paths = re.findall(r'case "(/[^\"]+)":', policy)
assert catalog_paths and not any(path.startswith("/read/") for path in catalog_paths)
for path in catalog_paths:
    route = body(routes, f'if (parsed.Host == "tools" && parsed.AbsolutePath == "{path}")')
    ordered(route, 'query["domain"] = "catalog"', 'ReadToolResource(uri, "server_control"')

sessions = registry.index('Resource("oni://mcp/sessions", "server_control"')
assert '["domain"] = "diagnostics", ["action"] = "capabilities"' in registry[sessions : sessions + 280]

print("PASS OniMcp save-load context invalidation precedes scene teardown")
