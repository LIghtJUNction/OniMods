#!/usr/bin/env python3
"""Regression-test stable Harmony API surface extraction without game binaries."""

import contextlib
import copy
import io
import json
from pathlib import Path
import tempfile

from extract_harmony_api_surface import canonical_type_surface, compare_baseline


BASE_SOURCE = r'''
using System;
public class Outer {
  public class Sibling { public int Noise; }
  [Obsolete]
  public class Target : BaseType, IFoo {
    [Whatever]
    public int Value;
    public static extern string Run(int count, string format = "{0:0.#}");
    public extern int Number { [CompilerGenerated] get; private set; }
    private extern void Hidden(ref int x);
  }
}
'''

FORMAT_ONLY_SOURCE = r'''
using Different.Namespace;
public class Outer
{
    public class Sibling { public int DifferentNoise; }
    public class Target : BaseType, IFoo
    {
        public int Value ;
        public static string Run(
            int count,
            string format = "{0:0.#}") { throw null; }
        public int Number { get; private set; }
        private void Hidden(ref int x) { x++; }
    }
}
'''

CHANGED_SOURCE = FORMAT_ONLY_SOURCE.replace(
    "private void Hidden(ref int x)",
    "protected void Hidden(ref long x)",
)


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def main() -> int:
    baseline_surface = canonical_type_surface(BASE_SOURCE, "Outer.Target")
    formatted_surface = canonical_type_surface(FORMAT_ONLY_SOURCE, "Outer.Target")
    changed_surface = canonical_type_surface(CHANGED_SOURCE, "Outer.Target")

    require(
        baseline_surface == formatted_surface,
        "formatting, attributes, method bodies, and reference-assembly extern must be ignored",
    )
    require(
        baseline_surface != changed_surface,
        "visibility/parameter type drift must change the canonical surface",
    )
    require(
        all("Sibling" not in line for line in baseline_surface),
        "sibling nested types must not leak into a requested nested target surface",
    )
    require(
        any("private set" in line for line in baseline_surface),
        "property accessor visibility must be preserved",
    )
    require(
        any('"{0:0.#}"' in line for line in baseline_surface),
        "braces inside default string literals must not break member parsing",
    )

    current = {
        "schema": 1,
        "assemblies": [{"name": "A.dll", "sha256": "abc"}],
        "targets": [
            {
                "requested_type": "Outer.Target",
                "assembly": "A.dll",
                "resolved_type": "Outer.Target",
                "signatures": baseline_surface,
            }
        ],
    }
    with tempfile.TemporaryDirectory() as temp_dir:
        baseline_path = Path(temp_dir) / "baseline.json"
        baseline_path.write_text(json.dumps(current), encoding="utf-8")
        with contextlib.redirect_stdout(io.StringIO()):
            require(
                compare_baseline(current, baseline_path),
                "an identical baseline must pass",
            )

        drifted = copy.deepcopy(current)
        drifted["targets"][0]["signatures"] = changed_surface
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(
            io.StringIO()
        ):
            require(
                not compare_baseline(drifted, baseline_path),
                "signature drift must fail baseline comparison",
            )

    print("PASS stable Harmony API surface regression")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
