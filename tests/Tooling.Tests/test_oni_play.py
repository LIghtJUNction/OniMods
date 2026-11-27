"""Check the TOON receipts consumed by the survival play CLI."""

from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
import oni_play


class ToonReceiptTests(unittest.TestCase):
    def test_reusable_pending_status_read(self):
        receipt = """result:
  pendingStatusRead:
    command: read
    pendingBuildIds[3]: a,b,c
    path: /active/buildings/pending.oni
  idleDuplicants[0]:
  requested: 3"""
        result = list(oni_play.receipts(receipt))[-1]
        self.assertEqual(result["pendingStatusRead"], {
            "command": "read",
            "pendingBuildIds": ["a", "b", "c"],
            "path": "/active/buildings/pending.oni",
        })
        self.assertEqual(result["idleDuplicants"], [])
        self.assertEqual(result["requested"], 3)

    def test_inline_primitive_types_and_quoted_delimiters(self):
        receipt = r'values[11]: "a,b","0",0,-3,1.25,true,null,"","say \"hi\"","one\\two","line\nbreak"'
        values = oni_play.toon_object(receipt)["values"]
        self.assertEqual(values, ["a,b", "0", 0, -3, 1.25, True, None, "", 'say "hi"', "one\\two", "line\nbreak"])
        self.assertIs(type(values[2]), int)
        self.assertIs(values[5], True)

    def test_tabular_rows_and_multiline_items(self):
        receipt = """rows[2]{name,status}:
  "Ada, B",ready
  "0",0
items[2]:
  - name: Ada
    skills[2]: dig,build
  - name: Mae
    skills[0]:
mixed[3]:
  - "false"
  - 0
  - done: true
ok: true"""
        result = oni_play.toon_object(receipt)
        self.assertEqual(result["rows"], [{"name": "Ada, B", "status": "ready"}, {"name": "0", "status": 0}])
        self.assertEqual(result["items"], [{"name": "Ada", "skills": ["dig", "build"]}, {"name": "Mae", "skills": []}])
        self.assertEqual(result["mixed"], ["false", 0, {"done": True}])
        self.assertIs(result["ok"], True)


if __name__ == "__main__":
    unittest.main()
