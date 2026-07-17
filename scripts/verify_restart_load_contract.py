#!/usr/bin/env python3
"""Static lifecycle contract for save -> Steam restart -> exact load."""

from pathlib import Path
import sys
import traceback


ROOT = Path(__file__).resolve().parents[1]
CORE = ROOT / "mods/oni_mcp/Tools/Impl/Core"


def body(source: str, marker: str) -> str:
    start = source.find(marker)
    if start < 0:
        raise AssertionError(f"missing method marker: {marker}")
    opening = source.find("{", start)
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[opening + 1 : index]
    raise AssertionError(f"unbalanced method: {marker}")


def ordered(source: str, *needles: str) -> None:
    position = -1
    for needle in needles:
        position = source.find(needle, position + 1)
        if position < 0:
            raise AssertionError(f"missing or out of order: {needle}")


def simulate_lifecycle(
    readiness_samples: list[tuple[bool, bool, bool]], timeout_tick: int
) -> tuple[str, int | None]:
    """Samples are can_start, UI ready, SaveLoader ready; the last is intentionally irrelevant."""
    for tick, (can_start, ui_ready, _save_loader_ready) in enumerate(readiness_samples):
        if can_start and ui_ready:
            return "loaded", tick
        if tick >= timeout_tick:
            return "failed", None
    raise AssertionError("simulation ended before readiness or timeout")


def simulate_consumer_entry(intent: dict[str, object]) -> tuple[str, str | None]:
    stage = str(intent["stage"])
    if stage in {"loaded", "failed"}:
        return stage, intent.get("error")
    if bool(intent.get("stale")):
        return "failed", "restart intent exceeded the 15 minute lifetime"
    return stage, intent.get("error")


def main() -> int:
    launch = (CORE / "GameLaunchTools.cs").read_text(encoding="utf-8")
    restart = (CORE / "GameRestartTools.cs").read_text(encoding="utf-8")
    store = (CORE / "GameRestartIntentStore.cs").read_text(encoding="utf-8")
    paths = (ROOT / "mods/oni_mcp/Support/OniMcpPaths.cs").read_text(encoding="utf-8")
    mod_info = (ROOT / "mods/oni_mcp/ModInfo.cs").read_text(encoding="utf-8")
    server = (ROOT / "mods/oni_mcp/Server/McpHttpServer.cs").read_text(encoding="utf-8")
    game_control = (CORE / "GameControlTools.cs").read_text(encoding="utf-8")
    batch = (ROOT / "mods/oni_mcp/Tools/Impl/Server/ToolBatchExecutionHelpers.cs").read_text(encoding="utf-8")
    relay = (ROOT / "mods/oni_mcp/assets/restart_oni_steam_relay.sh").read_text(encoding="utf-8")
    project = (ROOT / "mods/oni_mcp/OniMcp.csproj").read_text(encoding="utf-8")

    for action in ("restart_load", "restart_status"):
        assert f'"{action}"' in launch and action in game_control
    assert 'ToolUtil.GetBool(args, "resume", false)' in restart
    assert 'ToolUtil.GetBool(args, "dryRun", false)' in restart

    request = body(restart, "private static CallToolResult RestartLoad")
    ordered(
        request,
        'if (!dryRun && !ToolUtil.GetBool(args, "confirm", false))',
        "TryValidateRestartRelay",
        "SaveLoader.Instance.Save",
        "ExactPathEquals(exactSaved, exactTarget)",
        "File.Exists(exactSaved)",
        "WriteRestartIntent(intent)",
        "StartRestartRelay",
        'intent.Stage = "relay_started"',
        "WriteRestartIntent(intent)",
        "ScheduleQuit",
        '["accepted"] = true',
    )
    assert "App.Quit" not in request, "quit must be delayed by the coordinator"

    quit_after = body(restart, "private IEnumerator QuitAfterResponse")
    ordered(quit_after, "WaitForSecondsRealtime", 'intent.Stage == "relay_started"', "App.Quit")

    consume = body(restart, "private IEnumerator ConsumeRestartIntent")
    ordered(
        consume,
        'intent.Stage == "loaded" || intent.Stage == "failed"',
        "IsRestartIntentStale",
        'intent.Stage != "relay_started"',
        "float deadline = Time.realtimeSinceStartup + RestartLoadReadinessTimeoutSeconds",
        "while (!GameLaunchTools.CanStartExactSaveLoad(intent.ExactSavePath))",
        "IsRestartIntentStale(intent)",
        "Time.realtimeSinceStartup >= deadline",
        "WaitForSecondsRealtime(RestartLoadPollSeconds)",
        'intent.Stage = "loading"',
        "WriteRestartIntent(intent)",
        "GameLaunchTools.StartExactSaveLoad(exactPath",
        "loadCallbackError",
        "ExactPathEquals(active, exactPath)",
        "ApplyPause(intent.Resume)",
        'intent.Stage = "loaded"',
        "WriteRestartIntent(intent)",
    )
    assert "ConsumerProcessId" in consume and "OriginProcessId" in consume
    readiness_loop = consume[consume.index("while (!GameLaunchTools.CanStartExactSaveLoad"):consume.index('intent.Stage = "loading"')]
    assert 'intent.Stage = "loading"' not in readiness_loop
    assert "ConsumerProcessId" not in readiness_loop
    assert "SaveLoader.Instance" not in readiness_loop
    assert "PersistFailureUntilWritten" in readiness_loop
    assert simulate_lifecycle(
        [(False, False, False), (True, False, False), (True, True, False)], timeout_tick=5
    ) == ("loaded", 2)
    assert simulate_lifecycle([(True, True, False)], timeout_tick=5) == ("loaded", 0)
    assert simulate_lifecycle(
        [(False, False, False), (False, False, False), (False, False, False)], timeout_tick=2
    ) == ("failed", None)
    assert simulate_consumer_entry({
        "stage": "loaded",
        "loadedUtc": "2026-07-17T07:43:03Z",
        "stale": True,
        "error": None,
    }) == ("loaded", None)
    assert simulate_consumer_entry({
        "stage": "failed",
        "failedUtc": "2026-07-17T07:43:03Z",
        "stale": True,
        "error": "original failure",
    }) == ("failed", "original failure")
    assert simulate_consumer_entry({
        "stage": "relay_started",
        "stale": True,
        "error": None,
    }) == ("failed", "restart intent exceeded the 15 minute lifetime")
    assert "restart intent exceeded the 15 minute lifetime" in restart
    assert "GameRestartCoordinator.EnsureCreated" in mod_info
    start = body(launch, "private static CallToolResult Start")
    ordered(start, "CanStartExactSaveLoad(target)", "StartExactSaveLoad(target)")
    readiness = body(launch, "internal static bool CanStartExactSaveLoad")
    assert "IsUsableSavePath(path)" in readiness
    assert "ScreenPrefabs.Instance.loadingOverlay" in readiness
    assert "SaveLoader.Instance" not in readiness
    invocation = body(launch, "internal static void StartExactSaveLoad")
    ordered(invocation, "LoadingOverlay.Load", "LoadScreen.DoLoad(path)")
    server_start = body(server, "private void Start()")
    ordered(server_start, "GameRestartCoordinator.EnsureIntentConsumerStarted()", "StartServer()")
    ensure_consumer = body(restart, "internal static void EnsureIntentConsumerStarted()")
    ordered(ensure_consumer, "EnsureCreated()", "_instance.StartIntentConsumerOnce()")
    start_once = body(restart, "private void StartIntentConsumerOnce()")
    ordered(start_once, "if (_intentConsumerStarted)", "_intentConsumerStarted = true",
            "StartCoroutine(ConsumeRestartIntent())")
    coordinator_start = body(restart, "private void Start()")
    assert "StartIntentConsumerOnce()" in coordinator_start
    assert 'action == "restart_status"' in batch
    assert "Task<" not in store and "Task<" not in restart

    write_intent = body(store, "internal static void WriteRestartIntent")
    ordered(write_intent, "File.WriteAllText", "File.Replace")
    for required in ("JobId", "ExactSavePath", "Resume", "Stage", "CreatedUtc", "UpdatedUtc", "Error"):
        assert required in store
    forbidden = ("AuthToken", "Authorization", "Bearer", "X-Oni-Mcp-Token")
    for secret_name in forbidden:
        assert secret_name not in store and secret_name not in relay

    assert 'old_pid="${1:-}"' in relay and 'steam_bin="${2:-}"' in relay
    assert "kill -0" in relay
    assert '[ -x "$steam_bin" ]' in relay
    assert 'exec "$steam_bin" steam://run/457140' in relay
    assert "OxygenNotIncluded" not in relay
    assert "TryResolveSteamExecutable" in restart
    assert "Path.IsPathRooted" in restart and "access(steamExecutable, 1)" in restart
    assert "PersistFailureUntilWritten" in consume
    assert "RestartRelayPath" in paths and "RestartIntentPath" in paths
    assert "restart_oni_steam_relay.sh" in project
    assert "<TargetPath>assets/restart_oni_steam_relay.sh</TargetPath>" in project

    for file in (CORE / "GameRestartTools.cs", CORE / "GameRestartIntentStore.cs"):
        assert len(file.read_text(encoding="utf-8").splitlines()) < 500, f"{file.name} exceeds line cap"

    print("restart/load lifecycle contract passed")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except AssertionError as error:
        print(f"restart/load lifecycle contract FAILED: {error}")
        traceback.print_exc()
        sys.exit(1)
