#!/usr/bin/env python3
from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[1]
TOOLS = ROOT / "mods" / "oni_mcp" / "Tools"
FAILURES: list[str] = []


def source(relative: str) -> str:
    path = ROOT / relative
    if not path.is_file():
        FAILURES.append(f"missing file: {relative}")
        return ""
    return path.read_text(encoding="utf-8")


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        FAILURES.append(f"missing {label}: {needle}")


def forbid(text: str, needle: str, label: str) -> None:
    if needle in text:
        FAILURES.append(f"forbidden {label}: {needle}")


def require_order(text: str, first: str, second: str, label: str) -> None:
    first_index = text.find(first)
    second_index = text.find(second)
    if first_index < 0 or second_index < 0 or first_index >= second_index:
        FAILURES.append(f"missing ordered {label}: {first} -> {second}")


def verify_blueprint_paths() -> None:
    safety = source("mods/oni_mcp/Tools/WorldEditorBlueprintPathSafety.cs")
    conversion = source("mods/oni_mcp/Tools/WorldEditorBlueprintConversion.cs")
    files = source("mods/oni_mcp/Tools/WorldEditorBlueprintFiles.cs")
    for needle in (
        "Path.IsPathRooted(value)",
        "Path.GetFullPath(Path.Combine(root, value))",
        "canonical.StartsWith(root",
        'Path.GetExtension(path), ".blueprint"',
        "ValidateBlueprintPathComponents",
        "string root = Path.GetPathRoot(canonical)",
        "ValidateBlueprintIoPath(path",
        "File.Replace(temp, path, null)",
        "File.Move(temp, path)",
        "FileAttributes.ReparsePoint",
    ):
        require(safety, needle, "blueprint path/atomic guard")
    forbid(conversion, "if (File.Exists(name))", "host absolute blueprint read")
    forbid(files, "File.WriteAllText(path", "non-atomic blueprint write")
    require(files, "AtomicWriteBlueprint(path", "atomic blueprint caller")
    require(files, "ValidateBlueprintMarkdownRoundTrip", "lossless blueprint guard")
    require(conversion, "ReadBlueprintText(path)", "guarded blueprint conversion read")
    require_order(safety, "ValidateBlueprintIoPath(path", "File.ReadAllText(path)", "blueprint read prevalidation")
    require_order(safety, "File.ReadAllText(path)", "Blueprint path changed during read validation", "blueprint read postvalidation")
    require_order(safety, "Blueprint path changed before atomic replace", "File.Replace(temp, path, null)", "blueprint atomic prevalidation")
    require_order(safety, "File.Move(temp, path)", "Blueprint path changed after atomic replace", "blueprint atomic postvalidation")


def verify_execution_policy() -> None:
    policy = source("mods/oni_mcp/Tools/WorldEditorExecutionPolicy.cs")
    edits = source("mods/oni_mcp/Tools/WorldEditorEdits.cs")
    dupe = source("mods/oni_mcp/Tools/WorldEditorDupeFiles.cs")
    management = source("mods/oni_mcp/Tools/WorldEditorManagementFiles.cs")
    blueprint = source("mods/oni_mcp/Tools/WorldEditorBlueprintFiles.cs")
    require(policy, '!ToolUtil.GetBool(args, "dryRun", false)', "dry-run execution gate")
    require(policy, 'ToolUtil.GetBool(args, "confirm", false)', "confirm execution gate")
    require(policy, 'parent?["taskDescription"]', "taskDescription inheritance")
    require(policy, 'obj["ok"]?.Type == JTokenType.Boolean', "structured child failure detection")
    require(policy, 'obj["actionable"]?.Type == JTokenType.Boolean', "build preflight failure detection")
    require(policy, 'ResultFieldInt(obj, "hardFailed")', "structured hard failure detection")
    require(policy, '!obj.Value<bool>("actionable") || hardFailed > 0', "actionable hard-failure semantics")
    require(policy, "return result.IsError", "CallToolResult error fallback")
    require_order(policy, "if (string.IsNullOrWhiteSpace(text))", "var obj = JObject.Parse(text)", "empty result fallback before structured parsing")
    require_order(policy, 'obj["actionable"]?.Type == JTokenType.Boolean', 'int failed = ResultFieldInt(obj, "failed")', "actionable result precedence")
    require(policy, 'ResultFieldInt(obj, "failed")', "structured failed-count detection")
    require(policy, 'ResultFieldInt(obj, "deferred")', "structured deferred-count detection")
    require(policy, 'ResultFieldInt(obj, "remainingCells")', "structured remaining-cell detection")
    require(policy, 'ToolUtil.GetBool(obj, "throttled", false)', "structured throttling detection")
    require(policy, 'new[] { "planned", "succeeded", "marked", "executedCells", "applied" }', "structured applied-count preference")
    require(edits, "PreflightSingleEditBlock", "all-block preflight")
    require(edits, "WorldEditorExecutionAllowed(args)", "top-level edit gate")
    require(edits, 'relative == "buildings/plans.oni" ? "build_area" : "auto_connect"', "real build dry-run preflight")
    require(
        source("mods/oni_mcp/Tools/BuildingControlTools.cs"),
        'action == "build_area"',
        "public build_area hard guard",
    )
    require(
        source("mods/oni_mcp/Tools/BuildingControlTools.cs"),
        'Direct building_control build_area planning is forbidden',
        "public build_area rejection guidance",
    )
    require(
        source("mods/oni_mcp/Tools/WorldEditorMapEditTools.cs"),
        "BuildingControlTools.ControlBuildingFromVirtualFile(buildArgs)",
        "virtual-file build internal authorization",
    )
    dig = source("mods/oni_mcp/Tools/Impl/Orders/OrdersDigTools.cs")
    require(dig, "ApplyPriority(digPlacer, args)", "dig priority application")
    require(dig, "priorityVerified", "dig priority verification result")
    require(dig, "priorityFailed > 0 ? CallToolResult.Error", "dig priority failure is not silent")
    require(dig, "existingUpdated", "existing dig orders update priority")
    require(dig, "ApplyPriority(existingDig, args)", "existing dig priority application")
    reachability = source("mods/oni_mcp/Tools/WorldEditorReachabilitySummary.cs")
    require(reachability, "SummarizeMapEditReachability", "map edit reachability aggregation")
    require(reachability, '"no_targets_reachable"', "explicit unreachable status")
    require(reachability, "build a ladder/floor", "compact unreachable recovery warning")
    require(reachability, "CompactMapEditResults", "nested map edit result compaction")
    tools = source("mods/oni_mcp/Tools/WorldEditorTools.cs")
    forbid(tools, '["editCells"] = new McpToolParameter', "coordinate cell-edit public schema")
    forbid(tools, '["editLines"] = new McpToolParameter', "coordinate line-edit public schema")
    require(tools, "Coordinate map edits are forbidden", "top-level coordinate edit rejection")
    require(edits, "Coordinate map edits are forbidden", "edit-path coordinate rejection")
    if (ROOT / "mods/oni_mcp/Tools/WorldEditorExplicitCellEdits.cs").exists():
        FAILURES.append("forbidden legacy coordinate-edit implementation: WorldEditorExplicitCellEdits.cs")
    map_edits = source("mods/oni_mcp/Tools/WorldEditorMapEditTools.cs")
    require(map_edits, '?? 512', "full viewport map edit default budget")
    require(map_edits, 'Math.Min(requested, 2500)', "map edit budget matches maximum zoom viewport")
    zoom = source("mods/oni_mcp/Tools/WorldEditorZoomViews.cs")
    require(zoom, "TryGetSynchronizedViewportBounds", "exact synchronized viewport bounds")
    viewport = source("mods/oni_mcp/Tools/WorldEditorVirtualFileReader.cs")
    require(viewport, "TryGetSynchronizedViewportBounds", "viewport reuses exact synchronized bounds")
    view_read = source("mods/oni_mcp/Tools/WorldEditorViewReadTools.cs")
    require(view_read, "TryGetSynchronizedViewportBounds", "world_editor read reuses exact synchronized bounds")
    cell = source("mods/oni_mcp/Tools/WorldEditorCellSnapshot.cs")
    require(cell, "ObjectLayer.DigPlacer", "cell snapshot reads dig designation")
    require(cell, "FormatDigOrder", "cell snapshot reports dig priority")
    forbid(edits, 'relative == "buildings/plans.oni" ? "parse_plan"', "parse-only build preflight")
    forbid(dupe, 'renameArgs["confirm"] = true', "dupe confirm escalation")
    require(management, "InheritWorldEditorExecutionPolicy", "management policy inheritance")
    require(blueprint, "WorldEditorExecutionAllowed(args)", "blueprint policy gate")


def verify_batch_safety() -> None:
    batch = source("mods/oni_mcp/Tools/WorldEditorBatchSafety.cs")
    tools = source("mods/oni_mcp/Tools/WorldEditorTools.cs")
    require(batch, "PreflightBatchStep", "batch preflight helper")
    require(batch, "StepMayMutate", "batch mutation classifier")
    require(batch, "nested world_editor batch is not supported", "nested batch rejection")
    require(batch, "game_control batch step is not in the supported speed/state action set", "game batch action restriction")
    require(batch, "navigation_control batch step is not in the supported view action set", "navigation batch action restriction")
    require(batch, 'case "cd":', "world-editor cwd mutation classification")
    require(batch, "GameStepMayMutate", "game mutation classification")
    require(batch, "CameraStepMayMutate", "navigation mutation classification")
    require(batch, "ReadStepMayMutate", "read camera-side-effect classification")
    require(batch, "CameraSyncExplicitlyDisabled", "explicit camera-sync opt-out")
    require(batch, '"view", "activeView", "displayView", "x", "y", "x1", "y1", "x2", "y2"', "read view parameter classification")
    require(tools, "var step = InheritWorldEditorExecutionPolicy(args, rawStep)", "batch policy inheritance")
    require(tools, "if (!PreflightBatchStep(tool, step, out string error))", "batch full preflight")
    require(tools, "batch supports at most one potentially mutating step", "single batch mutation limit")
    require(tools, "the potentially mutating batch step must be last", "batch mutation ordering")
    require(tools, 'if (args["stopOnError"] == null)', "batch stop-on-error default")
    require(tools, '["partial"] = partial', "batch partial summary")
    require(tools, '["applied"] = applied', "batch applied summary")
    require(tools, '["mutatedSteps"] = mutatedSteps', "batch mutation count summary")
    require(tools, "int childActual = ResultAppliedCount(result)", "batch child actual count")
    require(tools, "mutating && (!failed || childActual > 0)", "batch actual mutation count gate")
    require(tools, "if (failed)\n                return childActual", "failed mutation actual applied count")
    require(tools, "return childActual > 0 ? childActual : 1", "successful mutation applied fallback")
    require(tools, "return failed ? childActual > 0 : ResultReportsPartial(result)", "batch zero-actual failure partial guard")
    require(tools, 'step["syncView"] = false', "read-only batch camera sync disable")
    require(tools, 'step["focusCamera"] = false', "read-only batch camera focus disable")
    require(tools, '["mutating"] = mutating', "batch step mutation summary")
    require(tools, "anyFailure && mutatedSteps > 0", "batch partial-after-mutation semantics")
    require(tools, '["allowPartial"] = new McpToolParameter', "batch partial opt-in surface")
    require_order(tools, "if (!PreflightBatchStep(tool, step, out string error))", "if (!WorldEditorExecutionAllowed(args))", "batch preflight before execution gate")


def verify_operations() -> None:
    ops = source("mods/oni_mcp/Tools/WorldEditorOperationFiles.cs")
    registry = source("mods/oni_mcp/Tools/Core/OniToolRegistry.cs")
    require(ops, "ApplyOperationMarkdownEdit(JObject parentArgs", "ops parent args")
    require(ops, "PreflightOperationMarkdownEdit", "ops full preflight")
    require(ops, "CallToolFromWorldEditor", "internal world-editor dispatcher")
    require(ops, "if (lines.Count > 1)", "single operation command limit")
    require(ops, "semanticCoordinates = true", "semantic coordinate capability")
    require(ops, "Raw ops calls cannot pass coordinates to ordinary tools", "raw coordinate rejection")
    require(ops, "ops cannot invoke child world_editor commands", "recursive world-editor rejection")
    require(ops, "batch/program/script/flow are forbidden", "recursive server program rejection")
    require(ops, "partial = partial || ResultReportsPartial(result)", "operation child partial accumulation")
    require(ops, 'partial || (anyError && executed > 0)', "operation partial summary")
    require(ops, 'parentArgs?["stopOnError"] == null', "stopOnError default true")
    require(registry, "CallToolFromWorldEditor(string name, JObject arguments, bool allowValidatedCoordinates)", "explicit internal coordinate capability")
    require(registry, "HasCoordinateArguments", "raw coordinate detector")
    forbid(registry, "CallToolFromWorldEditor(string name, JObject arguments)\n", "implicit coordinate elevation overload")
    forbid(ops, 'action=jump query="printing pod"', "removed navigation action example")
    forbid(ops, "tool=game_control domain=camera", "invalid game camera example")
    require(ops, 'relative != "ops/tools.md"', "ops tools read-only")


def verify_map_safety() -> None:
    map_tools = source("mods/oni_mcp/Tools/WorldEditorMapEditTools.cs")
    preflight = source("mods/oni_mcp/Tools/WorldEditorMapEditPreflight.cs")
    search = source("mods/oni_mcp/Tools/WorldEditorSearchReplace.cs")
    patch = source("mods/oni_mcp/Tools/WorldEditorMapPatchCoordinates.cs")
    reads = source("mods/oni_mcp/Tools/WorldEditorReadSearch.cs")
    edits = source("mods/oni_mcp/Tools/WorldEditorEdits.cs")
    require(map_tools, "ExpandMapRowToken", "RLE expansion")
    require(patch, "Stale map snapshot at", "current snapshot comparison")
    require(patch, "row.Value.Length != searchX.Length", "strict row width")
    require(patch, "MapTokensEquivalent(actual, replacementSymbols[i]", "true-difference filtering ignores @(x,y)")
    token_parsing = source("mods/oni_mcp/Tools/WorldEditorMapEditTokenParsing.cs")
    require(token_parsing, "NormalizeMapCompareToken", "map token coordinate suffix normalization")
    require(token_parsing, "MapTokensEquivalent", "map token equivalence helper")
    require(token_parsing, "SearchTokenMatches", "token match with @(x,y) normalization")
    require(
        source("mods/oni_mcp/Tools/WorldEditorMapEditFootprints.cs"),
        "component.Count == 1 && actualWidth == 1 && actualHeight == 1",
        "single-cell lower-left multi-cell anchor shorthand",
    )
    require(preflight, "ValidateExplicitMapChangesAgainstSource", "explicit viewport validation")
    require(preflight, "Connection glyph edits are refused", "connection touched-cell fail-closed policy")
    require(search, "TryReadVirtualFileText(JObject request", "request-aware virtual snapshot read")
    require(search, "var readArgs = request == null ? new JObject() : (JObject)request.DeepClone()", "same-request snapshot parameters")
    require(search, "TryGetPatchExplicitBounds(search", "explicit patch bounds derivation")
    require(search, "var patched = request == null ? new JObject() : (JObject)request.DeepClone();", "exact patch argument clone")
    for axis in ("x1", "y1", "x2", "y2"):
        require(search, f'patched["{axis}"] = {axis};', f"exact patch {axis} bound")
    require(search, 'patched["syncView"] = false;', "exact patch camera-sync disable")
    require(search, 'patched["focusCamera"] = false;', "exact patch camera-focus disable")
    require(search, 'patched["_patchRectRender"] = true;', "exact patch rectangle render")
    require(search, 'patched["compact"] = false;', "exact patch expanded output")
    require(search, 'patched["format"] = "edit";', "exact patch editable output")
    require(
        search,
        "return TryReadVirtualFileText(patched, path, out text, out error);",
        "exact patch read result propagation",
    )
    forbid(
        search,
        "if (TryReadVirtualFileText(patched, path, out text, out error))",
        "exact patch camera-viewport fallback",
    )
    require(patch, 'attemptedHundreds |= line.StartsWith("百位X"', "malformed hundreds header detection")
    require(patch, 'attemptedTens |= line.StartsWith("十位X"', "malformed tens header detection")
    require(patch, 'attemptedOnes |= line.StartsWith("个位X"', "malformed ones header detection")
    require(patch, "headersAttempted = attemptedHundreds || attemptedTens || attemptedOnes;", "explicit X header attempt detection")
    require(patch, "Explicit X coordinate headers require 百位X, 十位X, and 个位X together.", "incomplete X header error")
    require(patch, "Invalid explicit X coordinate headers:", "invalid X header error")
    require(search, "if (headersAttempted)", "malformed explicit X header fail-closed gate")
    require(search, 'error = boundsError ?? "Invalid explicit X coordinate headers.";', "malformed explicit X header propagation")
    require_order(
        search,
        "if (headersAttempted)",
        "return TryReadVirtualFileText(request, path, out text, out error);",
        "malformed header rejection before legacy viewport fallback",
    )
    require_order(reads, "TryReadExactPatchRectangle(args", 'if (relative == "index.md")', "central patch render before path routing")
    patch_start = reads.find("private static bool TryReadExactPatchRectangle")
    patch_end = reads.find("private static CallToolResult Search", patch_start)
    if patch_start < 0 or patch_end < 0:
        FAILURES.append("missing exact patch rectangle read helper")
        patch_read = ""
    else:
        patch_read = reads[patch_start:patch_end]
    require(patch_read, "TryReadMapFocusBounds(args", "exact patch bound validation")
    require(patch_read, 'requestedView = "default";', "viewport default patch view")
    require(patch_read, "TryResolveZoomView(requestedView", "viewport requested patch view")
    require(patch_read, 'relative.StartsWith("map/layers/"', "layer patch route")
    require(patch_read, "OverlayScreen.Instance.mode", "layer active overlay preservation")
    require(patch_read, "ModeForInfrastructurePath(relative)", "infrastructure patch overlay")
    require(patch_read, "GetMapMd(", "direct exact patch map render")
    require(patch_read, "ShouldCompactMap(args)", "exact patch compact policy")
    forbid(patch_read, "SyncZoomCameraAndView", "exact patch camera sync")
    forbid(patch_read, "TryGetCameraBounds", "exact patch camera bounds fallback")
    forbid(patch_read, "ReadFileDirectly", "exact patch path-specific fallback")
    require(map_tools, "FindUniqueTokenSequence", "unique row fallback")
    require(map_tools, "is ambiguous in the current map", "ambiguous row rejection")
    require(map_tools, "item.Value.Length != replacementSymbols.Length", "exact expanded row length")
    require(map_tools, "offset + count > currentSymbols.Length || offset + count > width", "fallback row bounds")
    forbid(map_tools, "int count = Math.Min(item.Value.Length, replacementSymbols.Length);", "truncated fallback row edit")
    require(edits, "if (!ValidateVirtualFileSearch(args, path, relative, search", "current SEARCH validation")
    require(edits, 'edits.Count > 1 && !ToolUtil.GetBool(args, "allowPartial", false)', "multi-block partial opt-in")
    forbid(edits, "!pinnedMapPatch", "pinned SEARCH bypass")
    require(map_tools, "Re-read the map, then submit a fresh patch", "accurate partial guidance")


def verify_virtual_file_symmetry() -> None:
    listing = source("mods/oni_mcp/Tools/WorldEditorListing.cs")
    reads = source("mods/oni_mcp/Tools/WorldEditorReadSearch.cs")
    edits = source("mods/oni_mcp/Tools/WorldEditorEdits.cs")
    require(listing, 'add("viewport.md"', "listed viewport")
    require(listing, 'add("dupes.md"', "listed management dupes")
    require(reads, 'relative == "screenshots/index.md"', "listed screenshot reader")
    require(reads, "This legacy file is read-only", "orders pseudo-entry removal")
    forbid(edits, 'relative == "orders/orders.oni"', "orders pseudo editor")
    forbid(edits, 'relative == "map/terrain.oni"', "terrain orphan editor")
    forbid(edits, 'relative == "dupes/index.oni"', "dupe index pseudo editor")
    require(edits, "IsEditableManagementMarkdown", "management index exclusion")
    require(edits, "IsEditableOperationMarkdown", "ops index exclusion")


def verify_single_write_limits() -> None:
    management = source("mods/oni_mcp/Tools/WorldEditorManagementFiles.cs")
    operations = source("mods/oni_mcp/Tools/WorldEditorOperationFiles.cs")
    require(management, "Management edits support exactly one write command", "single management write limit")
    require(operations, "Operation edits support exactly one executable command", "single operation write limit")


def verify_line_limits() -> None:
    for path in (ROOT / "mods" / "oni_mcp").rglob("*.cs"):
        count = len(path.read_text(encoding="utf-8").splitlines())
        if count > 500:
            FAILURES.append(f"C# file exceeds 500 lines: {path.relative_to(ROOT)} ({count})")


def main() -> int:
    verify_blueprint_paths()
    verify_execution_policy()
    verify_batch_safety()
    verify_operations()
    verify_map_safety()
    verify_virtual_file_symmetry()
    verify_single_write_limits()
    verify_line_limits()
    if FAILURES:
        for failure in FAILURES:
            print(f"FAIL: {failure}", file=sys.stderr)
        return 1
    print("OK: world_editor path, policy, ops, map, symmetry, examples, and line-cap guards")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
