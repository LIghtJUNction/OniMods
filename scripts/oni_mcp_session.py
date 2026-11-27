"""One explicitly scoped OniMcp Streamable HTTP session for a Python game driver."""

import http.client
import json
import math
import os
from pathlib import Path
import threading
from urllib.parse import urlsplit


PROTOCOL = "2025-11-25"


class McpClientError(Exception):
    pass


class OutcomeUnknown(McpClientError):
    """The client cannot establish what happened to the last operation."""


def read_settings(config):
    path = Path(config) if config is not None else Path.home() / ".config" / "onim" / "mcp.toml"
    settings = {}
    if path.exists():
        try:
            import tomllib
        except ImportError:
            raise McpClientError("Reading OniMcp TOML settings requires Python 3.11 or newer.") from None
        try:
            with path.open("rb") as stream:
                settings = tomllib.load(stream)
        except (OSError, ValueError):
            raise McpClientError("Cannot read OniMcp settings.") from None
    url = settings.get("url", "http://localhost:8788/mcp/")
    secret = os.environ.get("ONIM_MCP_SECRET", settings.get("secret"))
    if secret is not None and (not isinstance(secret, str) or not secret.isascii()
                               or any(ord(c) < 32 or ord(c) == 127 for c in secret)):
        raise McpClientError("Invalid bearer credential in OniMcp settings.")
    try:
        endpoint = urlsplit(url)
        if endpoint.scheme not in ("http", "https") or not endpoint.hostname or endpoint.username is not None or endpoint.fragment:
            raise ValueError()
        endpoint.port
    except (ValueError, TypeError, AttributeError):
        raise McpClientError("Invalid HTTP(S) endpoint in OniMcp settings.") from None
    return endpoint, secret


def rpc_reply(response, request_id):
    if response.getheader("Content-Type", "").split(";")[0].strip().lower() != "text/event-stream":
        return json.loads(response.read())
    parts = []
    for raw in response:
        line = raw.decode("utf-8").rstrip("\r\n")
        if line.startswith("data:"):
            value = line[5:]
            parts.append(value[1:] if value.startswith(" ") else value)
        elif not line and parts:
            message = json.loads("\n".join(parts))
            parts.clear()
            if isinstance(message, dict) and message.get("id") == request_id:
                return message
    raise ValueError("No matching response")


class OniMcpSession:
    def __init__(self, config=None, timeout=60):
        self._endpoint, self._secret = read_settings(config)
        if not math.isfinite(timeout) or timeout <= 0:
            raise McpClientError("The HTTP timeout must be a positive finite number.")
        self._timeout = timeout
        self._state = threading.Condition()
        self._sid = None
        self._dead = False
        self._closed = False
        self._active = 0
        self._next = 1

    def _http(self, verb, sid, payload=None):
        url = self._endpoint
        kind = http.client.HTTPSConnection if url.scheme == "https" else http.client.HTTPConnection
        conn = kind(url.hostname, url.port, timeout=self._timeout)
        headers = {"Accept": "application/json, text/event-stream", "Mcp-Protocol-Version": PROTOCOL}
        if self._secret is not None:
            headers["Authorization"] = "Bearer " + self._secret
        if sid is not None:
            headers["Mcp-Session-Id"] = sid
        body = None
        if payload is not None:
            body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
            headers["Content-Type"] = "application/json"
        try:
            conn.request(verb, (url.path or "/") + ("?" + url.query if url.query else ""), body, headers)
            response = conn.getresponse()
            status = response.status
            if verb == "DELETE" and status in (404, 410):
                return None, None
            if sid and status in (404, 410):
                raise OutcomeUnknown("MCP session expired; the command outcome is unknown.")
            if not 200 <= status < 300:
                if status >= 500:
                    raise OutcomeUnknown(f"MCP returned HTTP {status}; the command outcome is unknown.")
                raise McpClientError(f"MCP returned HTTP {status}.")
            new_sid = response.getheader("Mcp-Session-Id")
            if payload is None or "id" not in payload:
                return None, new_sid
            message = rpc_reply(response, payload["id"])
            if not isinstance(message, dict) or message.get("jsonrpc") != "2.0" or type(message.get("id")) is not int or message["id"] != payload["id"]:
                raise ValueError()
            if "error" in message:
                raise McpClientError("The server returned a JSON-RPC error.")
            return message["result"], new_sid
        except (OSError, http.client.HTTPException, ValueError, KeyError):
            raise OutcomeUnknown("No valid MCP receipt; the command outcome is unknown.") from None
        finally:
            conn.close()

    def _request(self, method, params):
        request = {"jsonrpc": "2.0", "id": self._next, "method": method, "params": params}
        self._next += 1
        return request

    def _release(self):
        sid, self._sid = self._sid, None
        self._dead = False
        if sid:
            self._http("DELETE", sid)

    def _start(self):
        init = self._request("initialize", {"protocolVersion": PROTOCOL, "capabilities": {},
                                           "clientInfo": {"name": "oni-play", "version": "1"}})
        try:
            result, sid = self._http("POST", None, init)
        except OutcomeUnknown:
            raise McpClientError("MCP initialization is unconfirmed. The game command was not sent.") from None
        if not sid:
            raise McpClientError("OniMcp did not establish a session.")
        self._sid = sid
        try:
            if not isinstance(result, dict) or result.get("protocolVersion") != PROTOCOL:
                raise McpClientError("OniMcp negotiated an unsupported protocol version.")
            self._http("POST", sid, {"jsonrpc": "2.0", "method": "notifications/initialized"})
        except OutcomeUnknown:
            self._dead = True
            raise McpClientError("MCP initialization is unconfirmed. The game command was not sent.") from None
        except BaseException:
            self._dead = True
            raise

    def call(self, method, params):
        with self._state:
            while self._dead and self._active and not self._closed:
                self._state.wait()
            if self._closed:
                raise McpClientError("The OniMcp client scope is closed.")
            if self._dead:
                try:
                    self._release()
                except OutcomeUnknown:
                    pass  # The old transport is unavailable. A fresh initialize is still subject to normal admission.
            if self._sid is None:
                self._start()
            sid = self._sid
            request = self._request(method, params)
            self._active += 1
        try:
            # Independent HTTP connections let a camera read complete during a waiting game step.
            # Each logical command is sent once, regardless of the transport outcome.
            result, _ = self._http("POST", sid, request)
            return result
        except OutcomeUnknown:
            with self._state:
                self._dead = True
            raise
        finally:
            with self._state:
                self._active -= 1
                self._state.notify_all()

    def close(self):
        with self._state:
            self._closed = True
            self._state.notify_all()
            while self._active:
                self._state.wait()
            self._release()
