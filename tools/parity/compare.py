#!/usr/bin/env python3
"""Compare two CHK logs by the rule and state each line carries — the ad-hoc twin of the xunit comparator.

    python tools/parity/compare.py expected.txt actual.txt

The line grammar is CHK|<name>|<value>|<rule>, and in a recording optionally |<state>|<baseline>.
The rules, and what a pass means:

    exact        the values are the same number (or, if not numbers, the same text)
    shape        the values are the same text once whitespace is normalised, e.g. `[19 2]`
    rel=<tol>    |actual - expected| <= tol * |expected|   (|actual| <= tol when expected is 0)
    abs=<tol>    |actual - expected| <= tol
    div=ADRnnnn  a recorded divergence: the recording must be stamped `|diverges|<output>`, and the
                 line passes only on exactly that output, which must differ from MATLAB's — agreement
                 means the divergence is retired and the line and its ADR entry go
    bits         a whole array to the bit: the fixture prints file:<absolute path> naming the
                 num2hex file its writebits helper wrote, and whoever captures the output
                 (resolve_bits here) replaces the path with the file's SHA-256 and deletes the
                 file and its tempname folder. A path that is missing, relative, or outside the
                 temp folder becomes missing:<path>, which nothing matches.

The states (the ratchet): `|pending Vn|<baseline>` says JGraph is known to print <baseline> until
stage Vn lands — the line passes only on exactly that text; a different wrong answer is a
regression, and an answer that now agrees with MATLAB fails until the owning stage's commit removes
the marker. A recording may open with `RUN|pending Vn|<message>`: the run is known to fail with that
message, must fail with exactly it, and the lines it never reached are excused. In a log captured
from the CLI with both streams (`2>&1`), the failure is read off the `jgraph: script failed` line.

A line on one side with no partner on the other is a problem, as is a line whose rule differs
between the two sides: a fixture and its recording are the same script, so the rules must match.
This module is imported by check-ratchet.py and test_compare.py; MatlabParityComparer carries the
same rules in C#, and a change to one is a change to both.
"""

from __future__ import annotations

import hashlib
import math
import os
import re
import sys
import tempfile
from pathlib import Path

LINE = re.compile(r"^CHK\|([^|]+)\|([^|]*)\|([^|]*)(?:\|([^|]*)\|([^|]*))?$")
RUN_LINE = re.compile(r"^RUN\|pending ([A-Z]+\d+)\|(.*)$")
STAGE = re.compile(r"^pending ([A-Z]+\d+)$")
BITS_LINE = re.compile(r"^(CHK\|[^|\r\n]+\|)file:([^|\r\n]*)(\|bits)(?=[ \t]*\r?$)", re.MULTILINE)
CLI_FAILURE = re.compile(r"^jgraph: script failed [—-] (.*)$", re.MULTILINE)


def _digest(path: str) -> str:
    path = path.strip()
    if not path or not os.path.isabs(path) or not os.path.isfile(path):
        return "missing:" + path
    full = os.path.normcase(os.path.realpath(path))
    temp = os.path.normcase(os.path.realpath(tempfile.gettempdir()))
    if not full.startswith(temp + os.sep):
        return "missing:" + path
    with open(full, "rb") as handle:
        digest = hashlib.sha256(handle.read()).hexdigest()
    os.remove(full)
    folder = os.path.dirname(full)
    if folder != temp and os.path.isdir(folder) and not os.listdir(folder):
        os.rmdir(folder)
    return digest


def resolve_bits(text: str) -> str:
    """Every `file:<path>` of a bits line replaced by the file's SHA-256; the file deleted."""
    return BITS_LINE.sub(lambda m: m.group(1) + _digest(m.group(2)) + m.group(3), text)


def parse(text: str) -> dict[str, tuple[str, str, str | None, str | None]]:
    """name -> (value, rule, state, baseline); state and baseline are None on a plain line."""
    out: dict[str, tuple[str, str, str | None, str | None]] = {}
    for raw in text.splitlines():
        m = LINE.match(raw.strip())
        if m:
            out[m.group(1)] = (m.group(2), m.group(3) or "exact", m.group(4), m.group(5))
    return out


def malformed(text: str) -> list[str]:
    """Every line that opens as CHK| and does not parse — skipped silently before, a problem now."""
    return [raw.strip() for raw in text.splitlines()
            if raw.strip().startswith("CHK|") and not LINE.match(raw.strip())]


def parse_run(text: str) -> tuple[str, str] | None:
    """The recording's (stage, message) RUN line, or None when the run must succeed."""
    for raw in text.splitlines():
        m = RUN_LINE.match(raw.rstrip("\r"))
        if m:
            return m.group(1), m.group(2).strip()
    return None


def cli_failure(actual_text: str) -> str | None:
    """The message a CLI run failed with, read off its closing line; None when there is none."""
    m = CLI_FAILURE.search(actual_text)
    return m.group(1).strip() if m else None


def number(text: str) -> float | None:
    t = text.strip()
    for word, value in (("Inf", math.inf), ("-Inf", -math.inf), ("+Inf", math.inf), ("NaN", math.nan)):
        if t == word:
            return value
    try:
        return float(t)
    except ValueError:
        return None


def _differs(expected: str, actual: str) -> bool:
    e, a = number(expected), number(actual)
    if e is not None and a is not None:
        return not ((e == a) or (math.isnan(e) and math.isnan(a)))
    return expected.strip() != actual.strip()


def _same_text(printed: str, baseline: str, rule: str) -> bool:
    if rule == "bits":
        return printed.strip().lower() == baseline.strip().lower()
    return printed.strip() == baseline.strip()


def check(name: str, expected: str, actual: str, rule: str) -> str | None:
    """None when the line passes its rule, otherwise the reason it does not."""
    e, a = number(expected), number(actual)
    if rule == "exact":
        if e is not None and a is not None:
            same = (e == a) or (math.isnan(e) and math.isnan(a))
            return None if same else f"{name}: {actual} is not exactly {expected}"
        return None if expected.strip() == actual.strip() else f"{name}: '{actual}' is not '{expected}'"
    if rule == "shape":
        norm = lambda s: re.sub(r"\s+", " ", s.strip())
        return None if norm(expected) == norm(actual) else f"{name}: shape {actual} is not {expected}"
    if rule == "bits":
        for side, value in (("recorded", expected.strip()), ("printed", actual.strip())):
            if value.startswith("missing:"):
                return f"{name}: bits file missing ({side} {value[8:]})"
            if value.startswith("file:"):
                return f"{name}: bits file not resolved ({side} {value}) — resolve_bits must run before compare"
            if len(value) != 64 or any(c not in "0123456789abcdefABCDEF" for c in value):
                return f"{name}: '{value}' ({side}) is not a SHA-256 digest"
        return None if expected.strip().lower() == actual.strip().lower() else f"{name}: bits {actual} are not {expected}"
    if rule.startswith("div="):
        return None if _differs(expected, actual) else f"{name}: agrees with MATLAB ({actual}) — divergence {rule[4:]} is retired; delete the line and its ADR entry"
    if rule.startswith(("rel=", "abs=")):
        if e is None or a is None:
            return f"{name}: '{actual}' or '{expected}' is not a number under rule {rule}"
        tol = float(rule[4:])
        if math.isnan(e) or math.isnan(a) or math.isinf(e) or math.isinf(a):
            same = (math.isnan(e) and math.isnan(a)) or (e == a)
            return None if same else f"{name}: {actual} is not {expected}"
        allowed = tol * abs(e) if rule.startswith("rel=") else tol
        if rule.startswith("rel=") and e == 0:
            allowed = tol
        diff = abs(a - e)
        return None if diff <= allowed else f"{name}: {actual} is {diff:.3e} from {expected}, more than the {allowed:.3e} the rule {rule} allows"
    return f"{name}: unknown rule '{rule}'"


def check_state(name: str, value: str, rule: str, state: str | None, baseline: str | None, printed: str) -> str | None:
    """A line's verdict under its state: the rule alone, a pending baseline, or an accepted divergence."""
    divergent = rule.startswith("div=")
    if state is None:
        if divergent:
            return f"{name}: divergence {rule[4:]} is not stamped with JGraph's output — stamp it (JGRAPH_PARITY_STAMP) so any other output fails"
        return check(name, value, printed, rule)
    if state == "diverges":
        if not divergent:
            return f"{name}: state 'diverges' on a {rule} line — only a div=ADRnnnn line diverges"
        if not _same_text(printed, baseline or "", rule):
            return f"{name}: diverges — printed '{printed}', recorded output '{baseline}'"
        return None if _differs(value, printed) else f"{name}: agrees with MATLAB ({printed}) — divergence {rule[4:]} is retired; delete the line and its ADR entry"
    m = STAGE.match(state)
    if not m:
        return f"{name}: unknown state '{state}'"
    if divergent:
        return f"{name}: a div= line cannot be pending — it diverges or it is retired"
    owner = m.group(1)
    if not _same_text(printed, baseline or "", rule):
        if check(name, value, printed, rule) is None:
            return f"{name}: now agrees with MATLAB ({printed}) — the pending {owner} marker comes off in the owning stage's commit, which records the flip"
        return f"{name}: pending {owner} printed '{printed}', baseline '{baseline}' — a different wrong answer is a regression"
    if check(name, value, baseline or "", rule) is None:
        return f"{name}: pending {owner} but its baseline ({baseline}) agrees with MATLAB — remove the marker"
    return None


def compare(expected_text: str, actual_text: str, run_failure: str | None = None) -> list[str]:
    expected, actual = parse(expected_text), parse(actual_text)
    run = parse_run(expected_text)
    problems: list[str] = []
    for side, text in (("recorded", expected_text), ("printed", actual_text)):
        for line in malformed(text):
            problems.append(f"malformed {side} line '{line}' — a value must not contain '|'")
    if run is None and run_failure is not None:
        problems.append(f"the run failed ({run_failure}) and the recording has no RUN|pending line")
    elif run is not None and run_failure is None:
        problems.append(f"RUN: recorded as failing until {run[0]} but the run succeeded — remove the RUN line in the owning stage's commit and record the flip")
    elif run is not None and run_failure is not None and run[1] != run_failure.strip():
        problems.append(f"RUN: the run failed with '{run_failure}', recorded '{run[1]}' (pending {run[0]}) — a different failure is a regression")
    for name, (value, rule, state, baseline) in expected.items():
        if name not in actual:
            if run is None:
                problems.append(f"{name}: recorded but not printed")
            continue
        a_value, a_rule, a_state, _ = actual[name]
        if a_state is not None:
            problems.append(f"{name}: printed a state ({a_state}) — only a recording carries one")
            continue
        if a_rule != rule:
            problems.append(f"{name}: rule is {a_rule} here and {rule} in the recording")
            continue
        p = check_state(name, value, rule, state, baseline, a_value)
        if p:
            problems.append(p)
    for name in actual:
        if name not in expected:
            problems.append(f"{name}: printed but not recorded — re-run record-matlab.ps1")
    return problems


def main(argv: list[str]) -> int:
    if len(argv) != 3:
        print(__doc__)
        return 2
    expected = resolve_bits(Path(argv[1]).read_text(encoding="utf-8-sig"))
    actual_raw = Path(argv[2]).read_text(encoding="utf-8-sig")
    actual = resolve_bits(actual_raw)
    problems = compare(expected, actual, cli_failure(actual_raw))
    total = len(parse(expected))
    if problems:
        print(f"{len(problems)} problem(s) over {total} lines")
        for p in problems:
            print("  -", p)
        return 1
    print(f"OK: {total} lines agree by their rules")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
