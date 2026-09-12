"""The high-precision reference for ADR 0153's cosine transforms, written once, read by the tests.

    python tools/transforms/dct_reference.py            # writes tests/JGraph.Tests/Numerics/dct_reference.json

For every length in FULL the input is the fixture's own LCG sequence (no rand; the same numbers on
both engines and in this script) and the file holds every coefficient of the orthonormal DCT-II
and of the orthonormal DCT-III of that input, summed directly in mpmath at 30 digits and printed
with 17 significant digits, which is what a double can carry. For every length in SELECTED — the
production lengths, where a full direct sum is hours — the file holds four coefficients of each
transform, chosen across the spectrum, summed the same way.

The cosines come from the three-term recurrence cos((j+1)t) = 2 cos t · cos(jt) − cos((j−1)t),
evaluated at 30 digits: the recurrence loses about log10(n) digits over n steps, which leaves
more than twenty, and it is what makes a four-million-term sum minutes rather than hours.

The tests read the doubles back and compare the kernel's answer to them within the tolerance the
plan sets (rel=1e-12 at the small lengths, rel=1e-11 at the production ones), forward and inverse
independently: a permutation or sign error can cancel in a round trip and never be seen.

The LCG: x0 = 12345, x = (1103515245 x + 12345) mod 2^31, value = x / 2^31 − 0.5. Every step is an
exact integer, so C# and Python produce the same doubles bit for bit.
"""

from __future__ import annotations

import json
import sys
import time
from pathlib import Path

import mpmath as mp

mp.mp.dps = 30

FULL = [8, 33, 100, 1000, 4096]
SELECTED = [2 ** 20, 4_000_000]
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "tests" / "JGraph.Tests" / "Numerics" / "dct_reference.json"


def lcg(n: int) -> list[float]:
    x = 12345
    out = []
    for _ in range(n):
        x = (1103515245 * x + 12345) % (1 << 31)
        out.append(x / (1 << 31) - 0.5)
    return out


def weights(n: int) -> tuple[mp.mpf, mp.mpf]:
    return mp.sqrt(mp.mpf(1) / n), mp.sqrt(mp.mpf(2) / n)


def cosine_sum(values: list[float], n: int, phase: mp.mpf, step: mp.mpf) -> mp.mpf:
    """sum_i values[i] * cos(phase + i*step), the cosines by recurrence."""
    two_cos = 2 * mp.cos(step)
    c_prev = mp.cos(phase - step)
    c = mp.cos(phase)
    total = mp.mpf(0)
    for i in range(n):
        total += mp.mpf(values[i]) * c
        c_prev, c = c, two_cos * c - c_prev
    return total


def dct2_coefficient(x: list[float], n: int, k: int) -> mp.mpf:
    """Orthonormal DCT-II: w(k) * sum_j x[j] cos(pi k (2j+1) / 2n) = w(k) * sum_j x[j] cos(a + j*2a), a = pi k / 2n."""
    w0, w = weights(n)
    a = mp.pi * k / (2 * n)
    return (w0 if k == 0 else w) * cosine_sum(x, n, a, 2 * a)


def dct3_sample(c: list[float], n: int, j: int) -> mp.mpf:
    """Orthonormal DCT-III: sum_k w(k) c[k] cos(k * b), b = pi (2j+1) / 2n; the k = 0 term weighs w0."""
    w0, w = weights(n)
    b = mp.pi * (2 * j + 1) / (2 * n)
    weighted = [c[k] for k in range(n)]
    total = w * cosine_sum(weighted, n, mp.mpf(0), b)
    # The sum above weighed c[0] by w; the DCT-III weighs it by w0.
    return total + (w0 - w) * mp.mpf(c[0])


def fmt(v: mp.mpf) -> float:
    return float(mp.nstr(v, 17))


def indices(n: int) -> list[int]:
    return sorted({1, 7, n // 2, n - 1})


def main() -> int:
    started = time.time()
    record = {
        "digits": mp.mp.dps,
        "lcg": "x0=12345; x=(1103515245*x+12345) mod 2^31; value=x/2^31-0.5",
        "full": {},
        "selected": {},
    }
    for n in FULL:
        x = lcg(n)
        forward = [fmt(dct2_coefficient(x, n, k)) for k in range(n)]
        inverse = [fmt(dct3_sample(x, n, j)) for j in range(n)]
        record["full"][str(n)] = {"forward": forward, "inverse": inverse}
        print(f"full {n}: done at {time.time() - started:.0f} s", file=sys.stderr)
    for n in SELECTED:
        x = lcg(n)
        idx = indices(n)
        forward = {str(k): fmt(dct2_coefficient(x, n, k)) for k in idx}
        inverse = {str(j): fmt(dct3_sample(x, n, j)) for j in idx}
        record["selected"][str(n)] = {"forward": forward, "inverse": inverse}
        print(f"selected {n}: done at {time.time() - started:.0f} s", file=sys.stderr)
    OUT.write_text(json.dumps(record), encoding="utf-8")
    print(f"wrote {OUT} ({OUT.stat().st_size} bytes) in {time.time() - started:.0f} s")
    return 0


if __name__ == "__main__":
    sys.exit(main())
