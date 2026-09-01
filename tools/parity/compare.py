#!/usr/bin/env python3
"""Compare two CHK logs by the rule each line carries — the ad-hoc twin of the xunit comparator.

    python tools/parity/compare.py expected.txt actual.txt

The line grammar is CHK|<name>|<value>|<rule>. The rules, and what a pass means:

    exact        the values are the same number (or, if not numbers, the same text)
    shape        the values are the same text once whitespace is normalised, e.g. `[19 2]`
    rel=<tol>    |actual - expected| <= tol * |expected|   (|actual| <= tol when expected is 0)
    abs=<tol>    |actual - expected| <= tol
    div=ADRnnnn  the values MUST differ — a recorded divergence; agreement means the divergence
                 has been closed and the line (and the ADR entry) should be retired

A line on one side with no partner on the other is a problem, as is a line whose rule differs
between the two sides: a fixture and its recording are the same script, so the rules must match.
This module is imported by nothing; MatlabParityFixtureTests carries the same rules in C#, and a
change to one is a change to both.
"""

from __future__ import annotations

import math
import re
import sys
from pathlib import Path

LINE = re.compile(r"^CHK\|([^|]+)\|([^|]*)\|([^|]*)$")


def parse(text: str) -> dict[str, tuple[str, str]]:
    out: dict[str, tuple[str, str]] = {}
    for raw in text.splitlines():
        m = LINE.match(raw.strip())
        if m:
            out[m.group(1)] = (m.group(2), m.group(3) or "exact")
    return out


def number(text: str) -> float | None:
    t = text.strip()
    for word, value in (("Inf", math.inf), ("-Inf", -math.inf), ("+Inf", math.inf), ("NaN", math.nan)):
        if t == word:
            return value
    try:
        return float(t)
    except ValueError:
        return None


def check(name: str, expected: str, actual: str, rule: str) -> str | None:
    """None when the line passes, otherwise the reason it does not."""
    e, a = number(expected), number(actual)
    if rule == "exact":
        if e is not None and a is not None:
            same = (e == a) or (math.isnan(e) and math.isnan(a))
            return None if same else f"{name}: {actual} is not exactly {expected}"
        return None if expected.strip() == actual.strip() else f"{name}: '{actual}' is not '{expected}'"
    if rule == "shape":
        norm = lambda s: re.sub(r"\s+", " ", s.strip())
        return None if norm(expected) == norm(actual) else f"{name}: shape {actual} is not {expected}"
    if rule.startswith("div="):
        if e is not None and a is not None:
            differs = not ((e == a) or (math.isnan(e) and math.isnan(a)))
        else:
            differs = expected.strip() != actual.strip()
        return None if differs else f"{name}: agrees with MATLAB ({actual}) — divergence {rule[4:]} retired; delete the line and its ADR entry"
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


def compare(expected_text: str, actual_text: str) -> list[str]:
    expected, actual = parse(expected_text), parse(actual_text)
    problems: list[str] = []
    for name, (value, rule) in expected.items():
        if name not in actual:
            problems.append(f"{name}: recorded but not printed")
            continue
        a_value, a_rule = actual[name]
        if a_rule != rule:
            problems.append(f"{name}: rule is {a_rule} here and {rule} in the recording")
            continue
        p = check(name, value, a_value, rule)
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
    expected = Path(argv[1]).read_text(encoding="utf-8-sig")
    actual = Path(argv[2]).read_text(encoding="utf-8-sig")
    problems = compare(expected, actual)
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
