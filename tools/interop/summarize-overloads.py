"""Reduces probe_net_conversions.out.txt to one fitness order per MATLAB value (interop plan, step 0).

For each probed value: the .NET types it converts to (the Only_ family), then those types ordered
by pairwise preference (the Pair_ family). If the pairwise table is not a strict total order on
the convertible types, the cycle or the tie is printed, because the converter must reproduce
the table, not a guessed ranking.

Run: python tools/interop/summarize-overloads.py [out.txt] > overload-order.txt
"""

import sys
from collections import defaultdict
from pathlib import Path


def main() -> None:
    root = Path(__file__).resolve().parents[2]
    src = Path(sys.argv[1]) if len(sys.argv) > 1 else (
        root / "tools" / "matlab-checklist" / "interop-probes" / "probe_net_conversions.out.txt")
    only = defaultdict(dict)
    pair = defaultdict(dict)
    order = []
    for line in src.read_text(encoding="utf-8").splitlines():
        parts = line.split("\t")
        if parts[0] == "only" and len(parts) == 4:
            _, val, typ, res = parts
            if val not in only:
                order.append(val)
            only[val][typ] = res
        elif parts[0] == "pair" and len(parts) == 4:
            _, val, ab, res = parts
            pair[val][ab] = res

    for val in order:
        ok = [t for t, r in only[val].items() if r == t]
        odd = {t: r for t, r in only[val].items() if r != t and not r.startswith("ERR")}
        wins = {t: 0 for t in ok}
        anomalies = []
        types = list(only[val].keys())
        for i, a in enumerate(types):
            for b in types[i + 1:]:
                r = pair[val].get(f"{a}_{b}")
                if r is None:
                    continue
                if a in ok and b in ok:
                    if r == a:
                        wins[a] += 1
                    elif r == b:
                        wins[b] += 1
                    else:
                        anomalies.append(f"{a}|{b}->{r}")
                elif (a in ok) != (b in ok) and not r.startswith("ERR"):
                    want = a if a in ok else b
                    if r != want:
                        anomalies.append(f"{a}|{b}->{r} (only {want} converts)")
                elif a not in ok and b not in ok and not r.startswith("ERR"):
                    anomalies.append(f"{a}|{b}->{r} (neither converts alone)")
        ranked = sorted(ok, key=lambda t: -wins[t])
        counts = [wins[t] for t in ranked]
        total = len(ranked) - 1
        strict = counts == list(range(total, -1, -1))
        print(f"{val}: {' > '.join(ranked) if ranked else '(nothing)'}")
        if not strict and ranked:
            print(f"    NOT A STRICT ORDER: wins {dict(zip(ranked, counts))}")
        if odd:
            print(f"    Only_ answered another type: {odd}")
        for a in anomalies:
            print(f"    anomaly {a}")


if __name__ == "__main__":
    main()
