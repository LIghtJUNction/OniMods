"""Exercise a normal, shared OniMcp lifecycle with wholly synthetic HTTP responses."""

from concurrent.futures import ThreadPoolExecutor
from io import BytesIO
import json
import os
from pathlib import Path
import sys
import tempfile
from threading import Event
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
import oni_mcp_session
import oni_play


class Response:
    def __init__(self, status=200, result=None, sid=None, stream=False):
        self.status = status
        self.headers = {"Content-Type": "text/event-stream" if stream else "application/json",
                        "Mcp-Session-Id": sid}
        encoded = json.dumps(result).encode() if result is not None else b""
        self.body = BytesIO(b"event: message\ndata: " + encoded + b"\n\n" if stream else encoded)

    def getheader(self, key, default=None):
        return self.headers.get(key, default)

    def read(self):
        return self.body.read()

    def __iter__(self):
        return iter(self.body)


class SessionTests(unittest.TestCase):
    def test_shared_step_camera_read_and_teardown(self):
        step_waiting = Event()
        camera_done = Event()
        names = []
        ids = []
        test = self

        class Wire:
            def __init__(self, host, port, timeout):
                test.assertEqual((host, port, timeout), ("game.invalid", 8788, 5))

            def request(self, verb, path, body, headers):
                test.assertEqual(path, "/mcp/")
                test.assertTrue(headers.get("Authorization") == "Bearer fixture-environment-token")
                test.assertEqual(headers["Mcp-Protocol-Version"], oni_mcp_session.PROTOCOL)
                args = json.loads(body) if body is not None else {}
                self.name = args.get("method", verb)
                rid = args.get("id")
                if rid is not None:
                    ids.append(rid)
                if self.name == "initialize":
                    test.assertNotIn("Mcp-Session-Id", headers)
                    self.response = Response(result={"jsonrpc":"2.0","id":rid,
                        "result":{"protocolVersion":oni_mcp_session.PROTOCOL}}, sid="fixture-session")
                else:
                    test.assertTrue(headers.get("Mcp-Session-Id") == "fixture-session")
                    if self.name == "tools/call":
                        self.name = args["params"]["name"]
                    reply = {"content":[{"type":"text","text":self.name}]}
                    self.response = (Response(202) if rid is None else
                        Response(result={"jsonrpc":"2.0","id":rid,"result":reply}, stream=self.name=="camera"))
                names.append(self.name)

            def getresponse(self):
                if self.name == "step":
                    step_waiting.set()
                    test.assertTrue(camera_done.wait(3), "camera should finish while step waits")
                return self.response

            def close(self):
                pass

        with tempfile.TemporaryDirectory() as tmp:
            config = Path(tmp) / "mcp.toml"
            config.write_text('url = "http://game.invalid:8788/mcp/"\nsecret = "fixture-file-token"\n')
            with patch.dict(os.environ, ONIM_MCP_SECRET="fixture-environment-token"), \
                    patch.object(oni_mcp_session.http.client, "HTTPConnection", Wire), \
                    patch.object(oni_play.subprocess, "run", side_effect=AssertionError("unexpected subprocess")):
                with oni_play.persistent_session(config, timeout=5), ThreadPoolExecutor(2) as pool:
                    step = pool.submit(oni_play.call, "step", "wait for game step")
                    try:
                        self.assertTrue(step_waiting.wait(3))
                        self.assertEqual(oni_play.call("camera", "observe colony"), "camera")
                    finally:
                        camera_done.set()
                    self.assertEqual(step.result(3), "step")
                    self.assertEqual(oni_play.invoke("read", "oni://research/status"), "resources/read")
        self.assertEqual(names, ["initialize","notifications/initialized","step","camera","resources/read","DELETE"])
        self.assertEqual(len(ids), len(set(ids)))

    def test_default_still_calls_cli(self):
        response = '{"contents":[{"text":"colony"}]}'
        with patch.object(oni_play.subprocess, "run") as cli:
            cli.return_value.returncode = 0
            cli.return_value.stdout = response
            self.assertEqual(oni_play.invoke("read","oni://colony/state"), "colony")
            self.assertEqual(cli.call_args.args[0], ["oni-mcp","read","oni://colony/state"])


if __name__ == "__main__":
    unittest.main()
