#!/usr/bin/env python3
"""Regression boundaries for final Workshop description text."""

import contextlib
import io
import unittest

from generate_workshop_description import validate_description


class DescriptionByteLimitTests(unittest.TestCase):
    def test_exact_byte_limit_is_accepted(self):
        for text in ("a" * 8000, "界" * 2666 + "aa", "🚀" * 2000):
            with self.subTest(prefix=text[:1]), contextlib.redirect_stderr(io.StringIO()):
                validate_description(text)

    def test_one_byte_over_limit_is_rejected(self):
        for text in ("a" * 8001, "界" * 2667, "🚀" * 2000 + "a"):
            with self.subTest(prefix=text[:1]), contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaisesRegex(ValueError, "8001 > 8000 UTF-8 bytes"):
                    validate_description(text)

    def test_prefix_bytes_count_toward_final_text(self):
        prefix = "[b]由 AI 生成[/b]\n"
        text = prefix + "a" * (8000 - len(prefix.encode("utf-8")))
        with contextlib.redirect_stderr(io.StringIO()):
            validate_description(text, "zh")
            with self.assertRaises(ValueError):
                validate_description(text + "a", "zh")


if __name__ == "__main__":
    unittest.main()
