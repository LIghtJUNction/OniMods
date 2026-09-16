#!/usr/bin/env python3
"""Exercise whitespace gating against real, isolated Git repositories."""

import contextlib
import io
from pathlib import Path
import subprocess
import tempfile
import unittest

from check_git_whitespace import ZERO_SHA, change_range, check, git


class WhitespaceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="oni-whitespace-")
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        git(self.repo, "init", "-q")
        git(self.repo, "config", "user.email", "test@example.invalid")
        git(self.repo, "config", "user.name", "Whitespace Test")
        self.first = self.save("base.txt", "base\n")

    def save(self, name, content):
        (self.repo / name).write_text(content, encoding="utf-8")
        git(self.repo, "add", "--", name)
        git(self.repo, "-c", "commit.gpgsign=false", "commit", "-qm", "fixture")
        return git(self.repo, "rev-parse", "HEAD")

    def run_check(self, name, event):
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            return check(self.repo, name, event)

    def test_clean_push(self):
        head = self.save("clean.txt", "clean\n")
        self.assertEqual(self.run_check("push", {"before": self.first, "after": head}), 0)

    def test_push_checks_all_commits(self):
        self.save("bad.txt", "trailing space \n")
        head = self.save("clean.txt", "later commit\n")
        self.assertNotEqual(self.run_check("push", {"before": self.first, "after": head}), 0)

    def test_pr_checks_full_diff(self):
        self.save("bad.txt", "trailing tab\t\n")
        head = self.save("clean.txt", "later commit\n")
        event = {"pull_request": {"base": {"sha": self.first}, "head": {"sha": head}}}
        self.assertNotEqual(self.run_check("pull_request", event), 0)

    def test_pr_does_not_blame_unrelated_base_changes(self):
        base = self.save("unrelated.txt", "bad on main \n")
        git(self.repo, "checkout", "-q", "--detach", self.first)
        head = self.save("feature.txt", "clean feature\n")
        event = {"pull_request": {"base": {"sha": base}, "head": {"sha": head}}}
        self.assertEqual(change_range(self.repo, "pull_request", event), (self.first, head))
        self.assertEqual(self.run_check("pull_request", event), 0)

    def test_new_branch_uses_empty_tree(self):
        head = self.save("bad.txt", "bad \n")
        self.assertNotEqual(self.run_check("push", {"before": ZERO_SHA, "after": head}), 0)

    def test_manual_and_schedule(self):
        self.save("bad.txt", "bad \n")
        for event_name in ("workflow_dispatch", "schedule"):
            with self.subTest(event_name=event_name):
                self.assertNotEqual(self.run_check(event_name, {}), 0)

    def test_root_manual_run(self):
        self.assertEqual(self.run_check("workflow_dispatch", {}), 0)

    def test_deleted_bad_file_is_allowed(self):
        base = self.save("bad.txt", "bad \n")
        git(self.repo, "rm", "-q", "bad.txt")
        git(self.repo, "-c", "commit.gpgsign=false", "commit", "-qm", "remove")
        head = git(self.repo, "rev-parse", "HEAD")
        self.assertEqual(self.run_check("push", {"before": base, "after": head}), 0)

    def test_missing_or_invalid_history_fails(self):
        for bad in ("--help", "f" * 40, None):
            with self.subTest(sha=bad):
                with self.assertRaises((ValueError, subprocess.CalledProcessError)):
                    change_range(self.repo, "push", {"before": bad, "after": self.first})
        with self.assertRaises(KeyError):
            change_range(self.repo, "push", {"after": self.first})

    def test_untrusted_event_is_rejected(self):
        with self.assertRaises(ValueError):
            change_range(self.repo, "pull_request_target", {})


if __name__ == "__main__":
    unittest.main()
