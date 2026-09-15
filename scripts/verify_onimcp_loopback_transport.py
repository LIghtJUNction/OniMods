#!/usr/bin/env python3
"""Verify OniMcp runtime helpers ignore ambient proxies only for loopback URLs."""

from contextlib import contextmanager
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import sys
import threading


ROOT = Path(__file__).resolve().parents[1]
HELPER_DIR = ROOT / ".agents/skills/oni-mcp-autonomous-iteration/scripts"
sys.path.insert(0, str(HELPER_DIR))

import loopback_http  # noqa: E402
import runtime_smoke  # noqa: E402
import survival_watch  # noqa: E402


class FixtureHandler(BaseHTTPRequestHandler):
    def log_message(self, *_args):
        pass

    def do_GET(self):
        if self.path == "/http-error":
            self.send_response(503)
            self.end_headers()
            return
        self.send_response(200)
        self.end_headers()
        self.wfile.write(b"ready")

    def do_POST(self):
        length = int(self.headers.get("Content-Length", "0"))
        body = self.rfile.read(length)
        self.server.requests.append(
            {
                "path": self.path,
                "headers": dict(self.headers.items()),
                "body": json.loads(body.decode()),
            }
        )
        payload = json.dumps({"jsonrpc": "2.0", "id": 1, "result": {"ok": True}}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Mcp-Session-Id", "fixture-session")
        self.end_headers()
        self.wfile.write(payload)


class ProxyHandler(BaseHTTPRequestHandler):
    def log_message(self, *_args):
        pass

    def do_GET(self):
        self.server.requests.append(self.path)
        self.send_response(418, "proxy reached")
        self.end_headers()


@contextmanager
def running_server(handler):
    server = ThreadingHTTPServer(("127.0.0.1", 0), handler)
    server.requests = []
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        yield server
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=2)


@contextmanager
def hostile_proxy_environment(proxy_url):
    names = ("HTTP_PROXY", "http_proxy", "NO_PROXY", "no_proxy")
    previous = {name: os.environ.get(name) for name in names}
    os.environ["HTTP_PROXY"] = proxy_url
    os.environ["http_proxy"] = proxy_url
    os.environ.pop("NO_PROXY", None)
    os.environ.pop("no_proxy", None)
    try:
        yield
    finally:
        for name, value in previous.items():
            if value is None:
                os.environ.pop(name, None)
            else:
                os.environ[name] = value


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def main():
    require(loopback_http.is_loopback_url("http://localhost:8788/mcp/"), "localhost not recognized")
    require(loopback_http.is_loopback_url("http://127.0.0.1:8788/mcp/"), "IPv4 loopback not recognized")
    require(loopback_http.is_loopback_url("http://[::1]:8788/mcp/"), "IPv6 loopback not recognized")
    require(
        not loopback_http.is_loopback_url("http://127.0.0.1.example.invalid/mcp/"),
        "lookalike hostname incorrectly bypasses proxies",
    )

    closed_fixture_url = None
    with running_server(ProxyHandler) as proxy, running_server(FixtureHandler) as fixture:
        proxy_url = f"http://127.0.0.1:{proxy.server_port}"
        fixture_url = f"http://127.0.0.1:{fixture.server_port}/mcp/"
        closed_fixture_url = fixture_url
        with hostile_proxy_environment(proxy_url):
            with loopback_http.open_url(fixture_url, timeout=2) as response:
                require(response.read() == b"ready", "direct loopback GET failed")
            require(not proxy.requests, "loopback GET leaked through ambient proxy")

            client = runtime_smoke.McpClient(fixture_url)
            result = client.post({"jsonrpc": "2.0", "id": 1, "method": "fixture"})
            require(result.get("result", {}).get("ok") is True, "runtime_smoke POST failed")
            require(client.session_id == "fixture-session", "runtime_smoke session header regressed")

            old_url = survival_watch.URL
            survival_watch.URL = fixture_url
            try:
                _, result = survival_watch.post({"jsonrpc": "2.0", "id": 1, "method": "fixture"})
            finally:
                survival_watch.URL = old_url
            require(result.get("result", {}).get("ok") is True, "survival_watch POST failed")
            require(not proxy.requests, "loopback POST leaked through ambient proxy")

            request_headers = [item["headers"] for item in fixture.requests]
            require(
                all(item.get("Mcp-Protocol-Version") == runtime_smoke.PROTOCOL for item in request_headers),
                "MCP protocol header changed while fixing transport",
            )

            try:
                loopback_http.open_url("http://example.invalid/proxy-check", timeout=2)
            except RuntimeError as exc:
                require("HTTP 418" in str(exc), f"unexpected remote proxy error: {exc}")
            else:
                raise AssertionError("non-loopback request bypassed ambient proxy")
            require(
                any("example.invalid/proxy-check" in path for path in proxy.requests),
                "non-loopback request did not use ambient proxy",
            )

            try:
                loopback_http.open_url(
                    f"http://127.0.0.1:{fixture.server_port}/http-error", timeout=2
                )
            except RuntimeError as exc:
                require("HTTP 503" in str(exc), f"HTTP response not distinguished: {exc}")
            else:
                raise AssertionError("loopback HTTP 503 unexpectedly succeeded")

    try:
        loopback_http.open_url(closed_fixture_url, timeout=0.5)
    except RuntimeError as exc:
        require("loopback MCP request" in str(exc), f"connection failure not identified: {exc}")
    else:
        raise AssertionError("closed loopback fixture unexpectedly accepted a request")

    print("PASS OniMcp loopback transport bypasses proxies without disabling remote proxy policy")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (AssertionError, OSError, RuntimeError) as exc:
        print(f"FAIL {exc}", file=sys.stderr)
        sys.exit(1)
