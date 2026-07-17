#!/usr/bin/env python3
"""Static contract for opt-in authentication and reachable PLib options."""

from pathlib import Path
import re
import sys
import traceback


ROOT = Path(__file__).resolve().parents[1]


def method_body(source: str, marker: str) -> str:
    start = source.find(marker)
    if start < 0:
        raise AssertionError(f"missing method: {marker}")
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


def main() -> int:
    options = (ROOT / "mods/oni_mcp/Config/OniMcpOptions.cs").read_text(encoding="utf-8")
    transport = (ROOT / "mods/oni_mcp/Server/McpHttpServerPostTransport.cs").read_text(encoding="utf-8")
    settings = (ROOT / "mods/oni_mcp/Server/McpHttpServerSettingsPage.cs").read_text(encoding="utf-8")
    server = (ROOT / "mods/oni_mcp/Server/McpHttpServer.cs").read_text(encoding="utf-8")
    virtual_world = (ROOT / "mods/oni_mcp/Server/McpHttpServerVirtualWorld.cs").read_text(encoding="utf-8")
    project = (ROOT / "mods/oni_mcp/OniMcp.csproj").read_text(encoding="utf-8")
    readme = (ROOT / "mods/oni_mcp/README_EN.md").read_text(encoding="utf-8")

    assert re.search(r"AuthEnabled\s*\{[^}]+\}\s*=\s*false;", options, re.S)
    assert "CurrentSecurityMigrationVersion = 1" in options
    migration = method_body(options, "private static void ApplySecurityMigration")
    assert 'raw["SecurityMigrationVersion"]' in migration
    assert "options.AuthEnabled = false" in migration
    assert "options.SecurityMigrationVersion = CurrentSecurityMigrationVersion" in migration
    assert "ApplySecurityMigration(loaded, raw)" in options

    sanitize = method_body(options, "private static OniMcpOptions Sanitize")
    assert "options.AuthEnabled && string.IsNullOrEmpty(options.AuthToken)" in sanitize
    assert "options.AuthToken = CreateAuthToken()" in sanitize
    assert 'options.AuthToken = ""' not in options

    validate = method_body(transport, "private bool ValidateAuth")
    assert "!options.AuthEnabled" in validate and "return true" in validate
    empty_token = validate.find("string.IsNullOrWhiteSpace(expected)")
    slow_equals = validate.find("SlowEquals")
    assert 0 <= empty_token < slow_equals
    assert "return false" in validate[empty_token:slow_equals]
    assert "private static bool SlowEquals" in transport

    expected_options = {
        "Host": "Server",
        "PortInput": "Server",
        "AuthEnabled": "Security",
        "AuthToken": "Security",
        "ScreenshotCleanupEnabled": "Screenshots",
        "ScreenshotRetentionMinutesInput": "Screenshots",
        "ScreenshotMaxFilesInput": "Screenshots",
    }
    for property_name, category in expected_options.items():
        pattern = rf'\[Option\([^\n]+"{category}"\)\][\s\S]{{0,160}}public [^\n]+ {property_name}\b'
        assert re.search(pattern, options), f"{property_name} missing from {category}"
    create_options = method_body(options, "public IEnumerable<IOptionsEntry> CreateOptions")
    for entry in ("OniMcpStatus", "OpenBrowser", "RestartMcpServer", "OpenConfigFolder"):
        assert entry in create_options

    assert '<PackageReference Include="PLib" Version="4.24.0"' in project
    assert "PLib scrolls the dialog when needed" in options
    assert "Disabled by default" in settings
    dispatch = method_body(server, "private void ProcessRequest")
    assert dispatch.find("TryHandleSettingsRequest") < dispatch.find("ValidateAuth")
    settings_handler = method_body(settings, "private bool TryHandleSettingsRequest")
    assert "IPAddress.IsLoopback" in settings_handler
    assert 'request.HttpMethod == "GET"' in settings_handler
    assert 'request.HttpMethod != "POST"' in settings_handler
    save_at = settings_handler.find("OniMcpOptions.Save(options)")
    send_at = settings_handler.find("SendHtml(response", save_at)
    restart_at = settings_handler.find("ScheduleSettingsRestartAfterResponse", send_at)
    assert 0 <= save_at < send_at < restart_at
    assert 'string.IsNullOrEmpty(replacement) ? current.AuthToken : replacement' in settings_handler
    render = method_body(settings, "private string RenderSettingsHtml")
    assert 'type=""password""' in render and 'name=""authTokenReplacement""' in render
    assert "options.AuthToken" not in render
    assert 'path == "/settings"' not in virtual_world
    assert "only after" in readme and "manually enabled" in readme

    print("auth/options contract passed")
    print("manual UI check: open OniMcp Options; resize vertically; scroll and expand Status, Server, Security, and Screenshots; verify every listed control is reachable")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except AssertionError as error:
        print(f"auth/options contract FAILED: {error}")
        traceback.print_exc()
        sys.exit(1)
