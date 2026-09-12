"""The high-precision reference for ADR 0154's mixed-radix FFT, written once, read by the tests.

    python tools/transforms/fft_reference.py            # writes tests/JGraph.Tests/Numerics/fft_reference.json

For every length in FULL — 5-smooth lengths that take the mixed-radix road, chosen so every radix
the kernel has (2, 3, 4, 5) and every mixture of them is exercised — the input is the LCG sequence
dct_reference.py uses (the same numbers on both engines and in this script), and the file holds
every bin of the forward transform and every sample of the unscaled inverse transform of that
input, summed directly in mpmath at 30 digits and printed with 17 significant digits. For the
production length in SELECTED the file holds two bins of each, chosen away from the trivial ones.

The cosines and sines come from the recurrence c_{j+1} = 2 cos t · c_j − c_{j−1} (and the same for
the sines), evaluated at 30 digits: the recurrence loses about log10(n) digits over n steps, which
leaves more than twenty at four million.

The input is real, so the inverse transform here is of the *same real sequence* (not of the
forward's output): x[j] as a spectrum, the unscaled sum Σ_k x[k] e^{+2πi jk/n}. That is what the
kernel is asked for with inverse: true, before the 1/n the caller applies; the test scales.
"""

from __future__ import annotations

import json
import sys
import time
from pathlib import Path

import mpmath as mp

mp.mp.dps = 30

FULL = [96, 100, 360, 1000, 1080]
SELECTED = {4_000_000: [7, 123_457]}
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "tests" / "JGraph.Tests" / "Numerics" / "fft_reference.json"


def lcg(n: int) -> list[float]:
    x = 12345
    out = []
    for _ in range(n):
        x = (1103515245 * x + 12345) % (1 << 31)
        out.append(x / (1 << 31) - 0.5)
    return out


def bin_of(values: list[float], n: int, k: int, inverse: bool) -> tuple[mp.mpf, mp.mpf]:
    """Σ_j values[j] e^{∓2πi jk/n} as (real, imaginary); the angle by recurrence."""
    step = 2 * mp.pi * k / n
    two_cos = 2 * mp.cos(step)
    c_prev, c = mp.cos(-step), mp.mpf(1)
    s_prev, s = mp.sin(-step), mp.mpf(0)
    re = mp.mpf(0)
    im = mp.mpf(0)
    for j in range(n):
        v = mp.mpf(values[j])
        re += v * c
        im += v * s
        c_prev, c = c, two_cos * c - c_prev
        s_prev, s = s, two_cos * s - s_prev
    # Forward is e^{-iθ} = cos θ − i sin θ; inverse is e^{+iθ}.
    return (re, im) if inverse else (re, -im)


def fmt(v: mp.mpf) -> float:
    return float(mp.nstr(v, 17))


def main() -> int:
    started = time.time()
    record = {
        "digits": mp.mp.dps,
        "lcg": "x0=12345; x=(1103515245*x+12345) mod 2^31; value=x/2^31-0.5",
        "note": "inverse is the unscaled sum over the same real input",
        "full": {},
        "selected": {},
    }
    for n in FULL:
        x = lcg(n)
        entry = {}
        for name, inverse in (("forward", False), ("inverse", True)):
            re, im = [], []
            for k in range(n):
                r, i = bin_of(x, n, k, inverse)
                re.append(fmt(r))
                im.append(fmt(i))
            entry[name] = {"re": re, "im": im}
        record["full"][str(n)] = entry
        print(f"full {n}: done at {time.time() - started:.0f} s", file=sys.stderr)
    for n, bins in SELECTED.items():
        x = lcg(n)
        entry = {}
        for name, inverse in (("forward", False), ("inverse", True)):
            entry[name] = {}
            for k in bins:
                r, i = bin_of(x, n, k, inverse)
                entry[name][str(k)] = {"re": fmt(r), "im": fmt(i)}
        record["selected"][str(n)] = entry
        print(f"selected {n}: done at {time.time() - started:.0f} s", file=sys.stderr)
    OUT.write_text(json.dumps(record), encoding="utf-8")
    print(f"wrote {OUT} ({OUT.stat().st_size} bytes) in {time.time() - started:.0f} s")
    return 0


if __name__ == "__main__":
    sys.exit(main())
