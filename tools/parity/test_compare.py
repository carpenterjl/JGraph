#!/usr/bin/env python3
"""The ratchet's failure modes, proven on the Python twin the way ParityRatchetTests proves the C# one.

    python -m unittest tools/parity/test_compare.py
"""

from __future__ import annotations

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import compare  # noqa: E402

PENDING = "CHK|a|[1 2 3]|exact|pending V3|[7 2 3]\n"


class PendingLines(unittest.TestCase):
    def test_passes_on_its_baseline_only(self) -> None:
        self.assertEqual([], compare.compare(PENDING, "CHK|a|[7 2 3]|exact\n"))
        (different,) = compare.compare(PENDING, "CHK|a|[9 2 3]|exact\n")
        self.assertIn("a different wrong answer is a regression", different)
        (flipped,) = compare.compare(PENDING, "CHK|a|[1 2 3]|exact\n")
        self.assertIn("now agrees with MATLAB", flipped)
        (missing,) = compare.compare(PENDING, "")
        self.assertTrue(missing.startswith("a: recorded but not printed"))

    def test_baseline_that_agrees_is_refused(self) -> None:
        (problem,) = compare.compare("CHK|a|5|exact|pending V3|5\n", "CHK|a|5|exact\n")
        self.assertIn("baseline (5) agrees with MATLAB", problem)

    def test_baseline_is_text(self) -> None:
        self.assertEqual([], compare.compare("CHK|a|1|exact|pending V2|1.0000000000000002\n", "CHK|a|1.0000000000000002|exact\n"))
        self.assertEqual(1, len(compare.compare("CHK|a|1|exact|pending V2|2\n", "CHK|a|2.0|exact\n")))
        self.assertEqual([], compare.compare("CHK|a|1|exact|pending V2|ERR no such road\n", "CHK|a|ERR no such road|exact\n"))

    def test_bits_baseline_is_a_digest(self) -> None:
        a, b = "a" * 64, "b" * 64
        self.assertEqual([], compare.compare(f"CHK|x|{a}|bits|pending V6|{b}\n", f"CHK|x|{b.upper()}|bits\n"))
        self.assertEqual(1, len(compare.compare(f"CHK|x|{a}|bits|pending V6|{b}\n", f"CHK|x|{a}|bits\n")))


class Divergences(unittest.TestCase):
    def test_passes_on_stamped_output_only(self) -> None:
        stamped = "CHK|d|9.99|div=ADR0123|diverges|9.79\n"
        self.assertEqual([], compare.compare(stamped, "CHK|d|9.79|div=ADR0123\n"))
        (moved,) = compare.compare(stamped, "CHK|d|9.59|div=ADR0123\n")
        self.assertIn("diverges — printed '9.59', recorded output '9.79'", moved)
        (unstamped,) = compare.compare("CHK|d|9.99|div=ADR0123\n", "CHK|d|9.79|div=ADR0123\n")
        self.assertIn("not stamped", unstamped)

    def test_retired_when_it_agrees(self) -> None:
        (problem,) = compare.compare("CHK|d|9.99|div=ADR0123|diverges|9.99\n", "CHK|d|9.99|div=ADR0123\n")
        self.assertIn("retired", problem)

    def test_states_on_the_wrong_rule(self) -> None:
        (p,) = compare.compare("CHK|a|1|exact|diverges|2\n", "CHK|a|2|exact\n")
        self.assertIn("only a div=ADRnnnn line diverges", p)
        (p,) = compare.compare("CHK|a|1|div=ADR0001|pending V3|2\n", "CHK|a|2|div=ADR0001\n")
        self.assertIn("cannot be pending", p)
        (p,) = compare.compare("CHK|a|1|exact|maybe V3|2\n", "CHK|a|2|exact\n")
        self.assertIn("unknown state", p)
        (p,) = compare.compare("CHK|a|1|exact\n", "CHK|a|1|exact|pending V3|1\n")
        self.assertIn("printed a state", p)


class Runs(unittest.TestCase):
    RECORDING = "RUN|pending V6|Expected an expression, but found '.'.\nCHK|first|1|exact\nCHK|later|2|exact\n"

    def test_must_fail_with_the_recorded_message(self) -> None:
        msg = "Expected an expression, but found '.'."
        self.assertEqual([], compare.compare(self.RECORDING, "CHK|first|1|exact\n", msg))
        (p,) = compare.compare(self.RECORDING, "CHK|first|9|exact\n", msg)
        self.assertIn("is not exactly", p)
        (other,) = compare.compare(self.RECORDING, "", "Something else")
        self.assertIn("a different failure is a regression", other)
        (succeeded,) = compare.compare(self.RECORDING, "CHK|first|1|exact\nCHK|later|2|exact\n", None)
        self.assertIn("the run succeeded", succeeded)

    def test_unrecorded_failure_fails(self) -> None:
        problems = compare.compare("CHK|a|1|exact\n", "", "boom")
        self.assertTrue(any("no RUN|pending line" in p for p in problems))
        self.assertTrue(any(p.startswith("a: recorded but not printed") for p in problems))

    def test_cli_failure_is_read_off_the_closing_line(self) -> None:
        log = "CHK|first|1|exact\nfixture.m(3,1): boom\njgraph: script failed — boom\n"
        self.assertEqual("boom", compare.cli_failure(log))
        self.assertIsNone(compare.cli_failure("CHK|first|1|exact\n"))


class Malformed(unittest.TestCase):
    def test_a_pipe_in_a_value_is_a_problem_not_a_skip(self) -> None:
        recording = "CHK|a|x|y|z|w|exact\nCHK|b|1|exact\n"
        problems = compare.compare(recording, recording)
        self.assertEqual(2, len(problems))
        self.assertTrue(all("malformed" in p for p in problems))
        self.assertEqual([], compare.malformed("CHK|a|1|exact\nCHK|b|1|exact|pending V3|2\nnot a chk line\n"))


class Rules(unittest.TestCase):
    def test_plain_rules_still_hold(self) -> None:
        expected = "CHK|a|1.5|exact\nCHK|b|[2 3]|shape\nCHK|c|100|rel=1e-3\nCHK|d|0.5|abs=1e-6\n"
        actual = "CHK|a|1.5|exact\nCHK|b|[2  3]|shape\nCHK|c|100.05|rel=1e-3\nCHK|d|0.5000005|abs=1e-6\n"
        self.assertEqual([], compare.compare(expected, actual))
        (p,) = compare.compare("CHK|a|1.5|rel=1e-12\n", "CHK|a|1.5000001|rel=1e-12\n")
        self.assertTrue(p.startswith("a: 1.5000001 is"))
        problems = compare.compare("CHK|a|1|exact\n", "CHK|b|1|exact\n")
        self.assertEqual(2, len(problems))
        (p,) = compare.compare("CHK|a|1|exact\n", "CHK|a|1|rel=1e-9\n")
        self.assertIn("rule is rel=1e-9 here and exact in the recording", p)


class Overlays(unittest.TestCase):
    RECORDING = "CHK|a|1|exact|pending V2|7\nCHK|b|2|exact\nCHK|c|3|exact|pending V6|0\n"

    def test_replaces_only_the_state_of_a_line_it_names(self) -> None:
        merged, problems = compare.apply_overlay(self.RECORDING, "CHK|a|1|exact\nCHK|b|2|exact|pending V3|9\n")
        self.assertEqual([], problems)
        self.assertEqual("CHK|a|1|exact\nCHK|b|2|exact|pending V3|9\nCHK|c|3|exact|pending V6|0", merged)
        self.assertEqual([], compare.compare(merged, "CHK|a|1|exact\nCHK|b|9|exact\nCHK|c|0|exact\n"))
        self.assertEqual(1, len(compare.compare(merged, "CHK|a|7|exact\nCHK|b|9|exact\nCHK|c|0|exact\n")))

    def test_one_that_does_not_fit_is_a_problem(self) -> None:
        merged, problems = compare.apply_overlay(self.RECORDING, "CHK|a|5|exact\nCHK|b|2|exact\nCHK|z|1|exact|pending V3|2\nCHK|bad\n")
        self.assertEqual(4, len(problems))
        self.assertTrue(any("a: value or rule differs" in p for p in problems))
        self.assertTrue(any("b: equal to the recording's line" in p for p in problems))
        self.assertTrue(any("z: not a line of the recording" in p for p in problems))
        self.assertTrue(any("malformed line 'CHK|bad'" in p for p in problems))
        self.assertEqual(self.RECORDING.rstrip("\n"), merged)

    def test_carries_the_run_line(self) -> None:
        added, none = compare.apply_overlay("CHK|a|1|exact\n", "RUN|pending V6|boom\n")
        self.assertEqual([], none)
        self.assertEqual("RUN|pending V6|boom\nCHK|a|1|exact", added)
        replaced, _ = compare.apply_overlay("RUN|pending V6|boom\nCHK|a|1|exact\n", "RUN|pending V6|bang\n")
        self.assertEqual("RUN|pending V6|bang\nCHK|a|1|exact", replaced)


if __name__ == "__main__":
    unittest.main()
