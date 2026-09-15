#!/usr/bin/env python3
"""Extract stable API signatures for Harmony target types without loading game DLLs."""

from __future__ import annotations

import argparse
import difflib
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[1]
PATCH_RE = re.compile(r"HarmonyPatch\s*\(\s*typeof\((?P<type>[A-Za-z_][A-Za-z0-9_.]*)\)")
ASSEMBLY_NAMES = ("Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll")
TYPE_DECL_RE_TEMPLATE = (
    r"(?m)^[ \t]*(?P<header>"
    r"(?:(?:public|private|protected|internal|static|sealed|abstract|partial|readonly|ref|unsafe)\s+)*"
    r"(?P<kind>class|struct|interface|enum)\s+{name}\b[^{{\n]*"
    r"(?:\n\s*where\s+[^{{]+)?)\s*\{{"
)
ATTRIBUTE_PREFIX_RE = re.compile(r"\A\s*(?:\[[^\]]*\]\s*)+", re.S)
COMMENT_RE = re.compile(r"//[^\n]*|/\*.*?\*/", re.S)
TOKEN_RE = re.compile(
    r'"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\''
    r"|[A-Za-z_][A-Za-z0-9_]*|0x[0-9A-Fa-f]+|\d+(?:\.\d+)?(?:[A-Za-z]+)?"
    r"|::|=>|\?\?|\?\.|==|!=|<=|>=|\+\+|--|&&|\|\||<<|>>"
    r"|[{}()\[\]<>.,:;=?!&*+\-/|%^~]"
)


def digest_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def digest_text(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def target_types() -> list[str]:
    found: set[str] = set()
    for root in (ROOT / "mods" / "OniMcp", ROOT / "mods" / "CycleTrim"):
        for path in root.rglob("*.cs"):
            source = path.read_text(encoding="utf-8")
            found.update(match.group("type") for match in PATCH_RE.finditer(source))
    return sorted(found)


def type_candidates(type_name: str) -> list[str]:
    candidates = [type_name]
    if "." in type_name:
        parts = type_name.split(".")
        for split in range(len(parts) - 1, 0, -1):
            candidates.append(".".join(parts[:split]) + "+" + "+".join(parts[split:]))
    return candidates


def decompile_type(
    ilspycmd: str, assemblies: list[Path], type_name: str
) -> tuple[Path, str, str]:
    errors = []
    for assembly in assemblies:
        for candidate in type_candidates(type_name):
            result = subprocess.run(
                [ilspycmd, "-t", candidate, str(assembly)],
                cwd=ROOT,
                capture_output=True,
                text=True,
                check=False,
                timeout=60,
            )
            if result.returncode == 0 and result.stdout.strip():
                text = result.stdout.replace("\r\n", "\n").strip() + "\n"
                return assembly, candidate, text
            error = " ".join(result.stderr.split())
            errors.append(f"{assembly.name}:{candidate}: {error}")
    raise RuntimeError(
        f"could not decompile Harmony target {type_name}: " + "; ".join(errors)
    )


def _matching_brace(text: str, opening: int) -> int:
    depth = 0
    quote = None
    escaped = False
    for index in range(opening, len(text)):
        char = text[index]
        if quote is not None:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == quote:
                quote = None
            continue
        if char in ('"', "'"):
            quote = char
        elif char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return index
    raise ValueError("unmatched type/member brace in ILSpy output")


def _find_requested_type(source: str, requested_type: str) -> tuple[str, str, str]:
    start = 0
    end = len(source)
    kind = ""
    header = ""
    body = ""
    for part in requested_type.replace("+", ".").split("."):
        region = source[start:end]
        pattern = re.compile(TYPE_DECL_RE_TEMPLATE.format(name=re.escape(part)))
        match = pattern.search(region)
        if match is None:
            raise ValueError(
                f"could not isolate requested type {requested_type!r}; missing {part!r}"
            )
        opening = start + match.end() - 1
        closing = _matching_brace(source, opening)
        kind = match.group("kind")
        header = match.group("header")
        body = source[opening + 1 : closing]
        start = opening + 1
        end = closing
    return kind, header, body


def _strip_leading_attributes(text: str) -> str:
    previous = None
    while previous != text:
        previous = text
        text = ATTRIBUTE_PREFIX_RE.sub("", text)
    return text.strip()


def _canonical_declaration(text: str) -> str:
    text = COMMENT_RE.sub(" ", text)
    text = _strip_leading_attributes(text)
    tokens = []
    for token in TOKEN_RE.findall(text):
        # Reference assemblies decompile methods as `extern`; it is an artifact of
        # the reference binary and not part of the source API contract we patch.
        if token == "extern":
            continue
        tokens.append(token)
    if tokens and tokens[-1] == ";":
        tokens.pop()
    return " ".join(tokens)


def _property_accessors(block: str) -> list[str]:
    block = re.sub(r"\[[^\]]*\]", " ", block, flags=re.S)
    accessors = []
    for match in re.finditer(
        r"\b(?:(public|private|protected|internal)\s+)?"
        r"(get|set|init|add|remove)\s*;",
        block,
    ):
        visibility = match.group(1)
        accessors.append((visibility + " " if visibility else "") + match.group(2))
    return accessors


def _enum_members(body: str) -> list[str]:
    members = []
    start = 0
    quote = None
    escaped = False
    paren_depth = 0
    bracket_depth = 0
    for index, char in enumerate(body):
        if quote is not None:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == quote:
                quote = None
            continue
        if char in ('"', "'"):
            quote = char
        elif char == "(":
            paren_depth += 1
        elif char == ")":
            paren_depth -= 1
        elif char == "[":
            bracket_depth += 1
        elif char == "]":
            bracket_depth -= 1
        elif char == "," and paren_depth == 0 and bracket_depth == 0:
            declaration = _canonical_declaration(body[start:index])
            if declaration:
                members.append("enum-member " + declaration)
            start = index + 1
    declaration = _canonical_declaration(body[start:])
    if declaration:
        members.append("enum-member " + declaration)
    return members


def _direct_members(body: str) -> list[str]:
    members = []
    start = 0
    index = 0
    quote = None
    escaped = False
    while index < len(body):
        char = body[index]
        if quote is not None:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == quote:
                quote = None
            index += 1
            continue
        if char in ('"', "'"):
            quote = char
            index += 1
            continue
        if char == ";":
            declaration = _canonical_declaration(body[start : index + 1])
            if declaration:
                members.append("member " + declaration)
            start = index + 1
            index += 1
            continue
        if char != "{":
            index += 1
            continue

        header = _strip_leading_attributes(body[start:index])
        closing = _matching_brace(body, index)
        canonical_header = _canonical_declaration(header)
        if canonical_header:
            if re.search(r"\b(class|struct|interface|enum)\b", header):
                # The nested type's presence/base contract is visible, but its own
                # members are intentionally outside the direct target surface.
                members.append("nested " + canonical_header)
            elif "(" in header:
                # Works for reference-assembly extern declarations and remains
                # method-body insensitive if a full assembly is supplied later.
                members.append("member " + canonical_header)
            else:
                accessors = _property_accessors(body[index + 1 : closing])
                if accessors:
                    members.append(
                        "property "
                        + canonical_header
                        + " { "
                        + " ; ".join(accessors)
                        + " ; }"
                    )
                else:
                    members.append("member-block " + canonical_header)
        index = closing + 1
        while index < len(body) and body[index].isspace():
            index += 1
        if index < len(body) and body[index] == ";":
            index += 1
        start = index

    trailing = _canonical_declaration(body[start:])
    if trailing:
        members.append("member " + trailing)
    return members


def canonical_type_surface(source: str, requested_type: str) -> list[str]:
    """Return a formatting/body-insensitive direct API surface for one target type."""
    kind, header, body = _find_requested_type(source, requested_type)
    signatures = ["type " + _canonical_declaration(header)]
    members = _enum_members(body) if kind == "enum" else _direct_members(body)
    signatures.extend(sorted(set(members)))
    return signatures


def compare_baseline(current: dict, baseline_path: Path) -> bool:
    baseline = json.loads(baseline_path.read_text(encoding="utf-8"))
    failures = []
    if baseline.get("schema") != 1:
        failures.append(f"unsupported baseline schema: {baseline.get('schema')!r}")

    expected_assemblies = baseline.get("assemblies", [])
    if expected_assemblies != current["assemblies"]:
        failures.append(
            "pinned assembly identity changed:\n"
            + "\n".join(
                difflib.unified_diff(
                    json.dumps(expected_assemblies, indent=2, sort_keys=True).splitlines(),
                    json.dumps(current["assemblies"], indent=2, sort_keys=True).splitlines(),
                    fromfile="baseline assemblies",
                    tofile="current assemblies",
                    lineterm="",
                )
            )
        )

    expected = {item["requested_type"]: item for item in baseline.get("targets", [])}
    actual = {item["requested_type"]: item for item in current["targets"]}
    if set(expected) != set(actual):
        failures.append(
            "Harmony target set changed: "
            f"removed={sorted(set(expected) - set(actual))}, "
            f"added={sorted(set(actual) - set(expected))}"
        )

    for requested_type in sorted(set(expected) & set(actual)):
        old = expected[requested_type]
        new = actual[requested_type]
        for key in ("assembly", "resolved_type"):
            if old.get(key) != new.get(key):
                failures.append(
                    f"{requested_type}: {key} changed from {old.get(key)!r} to {new.get(key)!r}"
                )
        if old.get("signatures") != new.get("signatures"):
            failures.append(
                f"{requested_type}: API signature drift:\n"
                + "\n".join(
                    difflib.unified_diff(
                        old.get("signatures", []),
                        new.get("signatures", []),
                        fromfile=f"baseline {requested_type}",
                        tofile=f"current {requested_type}",
                        lineterm="",
                    )
                )
            )

    if failures:
        print("FAIL: Harmony target API baseline drifted", file=sys.stderr)
        for failure in failures:
            print(failure, file=sys.stderr)
        return False
    print(f"PASS Harmony target API baseline ({len(actual)} target types)")
    return True


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "managed_dir", help="Directory containing pinned ONI game reference assemblies"
    )
    parser.add_argument("--output", default="artifacts/oni-api-surface")
    parser.add_argument("--ilspycmd", default="ilspycmd")
    parser.add_argument(
        "--baseline",
        help="Checked-in JSON signature baseline to verify; never modified automatically",
    )
    args = parser.parse_args()

    managed_dir = Path(args.managed_dir)
    assemblies = [managed_dir / name for name in ASSEMBLY_NAMES]
    missing = [str(path) for path in assemblies if not path.is_file()]
    if missing:
        parser.error("required assembly not found: " + ", ".join(missing))

    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)

    entries = []
    for requested in target_types():
        assembly, resolved, text = decompile_type(args.ilspycmd, assemblies, requested)
        safe_base = assembly.stem + "__" + requested.replace(".", "_")
        decompiled_name = safe_base + ".cs"
        signature_name = safe_base + ".signatures.txt"
        (output / decompiled_name).write_text(text, encoding="utf-8")
        signatures = canonical_type_surface(text, requested)
        signature_text = "\n".join(signatures) + "\n"
        (output / signature_name).write_text(signature_text, encoding="utf-8")
        entries.append(
            {
                "requested_type": requested,
                "resolved_type": resolved,
                "assembly": assembly.name,
                "file": decompiled_name,
                "sha256": digest_text(text),
                "signature_file": signature_name,
                "signature_sha256": digest_text(signature_text),
                "signatures": signatures,
            }
        )
        print(
            f"SURFACE {requested} -> {assembly.name}:{resolved} "
            f"signature_sha256={entries[-1]['signature_sha256']}"
        )

    current = {
        "schema": 1,
        "assemblies": [
            {"name": assembly.name, "sha256": digest_file(assembly)}
            for assembly in assemblies
        ],
        "target_count": len(entries),
        "targets": entries,
    }
    (output / "index.json").write_text(
        json.dumps(current, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    print(f"Wrote {len(entries)} Harmony target surfaces to {output}")

    if args.baseline and not compare_baseline(current, Path(args.baseline)):
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
