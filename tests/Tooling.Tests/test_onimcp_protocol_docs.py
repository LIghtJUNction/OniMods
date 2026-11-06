from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
from verify_onimcp_protocol_docs import validate_document


class ProtocolDocumentationTests(unittest.TestCase):
    guidance = "Use 2025-11-25 initialize and the returned Mcp-Session-Id.\n"

    def test_historical_note_does_not_advertise_an_active_example(self):
        validate_document(
            self.guidance
            + "2026-07-28 and server/discover have been retired.\n"
            + '```json\n{"method":"initialize","params":{"protocolVersion":"2025-11-25"}}\n```',
            "2025-11-25",
        )

    def test_retired_request_examples_are_rejected(self):
        for example in (
            '{"method":"initialize","params":{"protocolVersion":"2025-06-18"}}',
            '{"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28"}}',
            "curl -H 'MCP-Protocol-Version: 2026-07-28'",
            '{"method":"server/discover"}',
        ):
            with self.subTest(example=example), self.assertRaises(ValueError):
                validate_document(self.guidance + f"```text\n{example}\n```", "2025-11-25")

    def test_session_guidance_is_required(self):
        with self.assertRaisesRegex(ValueError, "Mcp-Session-Id"):
            validate_document("Use 2025-11-25 initialize.", "2025-11-25")


if __name__ == "__main__":
    unittest.main()
