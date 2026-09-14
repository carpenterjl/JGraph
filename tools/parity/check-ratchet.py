#!/usr/bin/env python3
"""The ratchet's gate over the recordings themselves — no engine runs here.

    python tools/parity/check-ratchet.py [--expected tests/JGraph.Tests/MatlabParity/expected] [--adr docs/adr]

Every recording is read for its states. It fails when:

  * a pending line names a stage this script does not know (the stage-to-ADR table below is the
    plan's; a new stage is added here in the commit that creates it);
  * a pending line — or a RUN line — is still pending on a stage whose ADR has landed under docs/adr
    (the stage's commit must flip or re-own every line it owns, so a leftover is a line the stage
    forgot);
  * a line's grammar is wrong: an unknown state, a div= line without its diverges stamp, a pending
    baseline that already satisfies the rule against MATLAB's value.

It prints the count of pending lines per stage either way, which is the ratchet's progress.
"""

from __future__ import annotations

import argparse
import re
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import compare  # noqa: E402

REPO = Path(__file__).resolve().parents[2]

# The plan's stages and the ADR each one lands under (docs/plans/zfit-copies-and-temporaries-plan.md).
STAGE_ADR = {
    "V1": "0162", "V2": "0163", "V3": "0164", "V4": "0165", "V5": "0166", "V6": "0167",
    "V7": "0168", "V8": "0169", "V9": "0170", "V10": "0171", "V11": "0172", "Z2": "0173",
}


def landed(adr_dir: Path, number: str) -> bool:
    return any(adr_dir.glob(f"{number}-*.md"))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--expected", type=Path, default=REPO / "tests/JGraph.Tests/MatlabParity/expected")
    parser.add_argument("--adr", type=Path, default=REPO / "docs/adr")
    args = parser.parse_args()

    problems: list[str] = []
    pending: Counter[str] = Counter()
    diverging = 0
    for path in sorted(args.expected.glob("*.txt")):
        if path.name == "matlab_version.txt":
            continue
        text = path.read_text(encoding="utf-8-sig")
        for line in compare.malformed(text):
            problems.append(f"{path.name}: malformed line '{line}'")
        run = compare.parse_run(text)
        if run is not None:
            stage = run[0]
            pending[stage] += 1
            if stage not in STAGE_ADR:
                problems.append(f"{path.name}: RUN pending on unknown stage {stage}")
            elif landed(args.adr, STAGE_ADR[stage]):
                problems.append(f"{path.name}: RUN still pending on {stage}, whose ADR {STAGE_ADR[stage]} has landed")
        for name, (value, rule, state, baseline) in compare.parse(text).items():
            if state is None:
                if rule.startswith("div="):
                    problems.append(f"{path.name}: {name}: divergence {rule[4:]} is not stamped")
                continue
            if state == "diverges":
                diverging += 1
                if not rule.startswith("div="):
                    problems.append(f"{path.name}: {name}: 'diverges' on a {rule} line")
                elif not compare._differs(value, baseline or ""):
                    problems.append(f"{path.name}: {name}: stamped output agrees with MATLAB — divergence {rule[4:]} is retired")
                continue
            m = compare.STAGE.match(state)
            if not m:
                problems.append(f"{path.name}: {name}: unknown state '{state}'")
                continue
            stage = m.group(1)
            pending[stage] += 1
            if stage not in STAGE_ADR:
                problems.append(f"{path.name}: {name}: pending on unknown stage {stage}")
            elif landed(args.adr, STAGE_ADR[stage]):
                problems.append(f"{path.name}: {name}: still pending on {stage}, whose ADR {STAGE_ADR[stage]} has landed")
            if compare.check(name, value, baseline or "", rule) is None:
                problems.append(f"{path.name}: {name}: pending {stage} but its baseline agrees with MATLAB")

    for stage in sorted(pending, key=lambda s: (s[0], int(s[1:]))):
        mark = "landed" if stage in STAGE_ADR and landed(args.adr, STAGE_ADR[stage]) else "open"
        print(f"  {stage:<4} {pending[stage]:5d} pending  ({mark})")
    print(f"  {diverging} accepted divergence line(s)")
    if problems:
        print(f"{len(problems)} problem(s)")
        for p in problems:
            print("  -", p)
        return 1
    print("ratchet OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
