#!/usr/bin/env python3
"""V1.1's audit: every place the engine reaches a value's payload, classified.

    python tools/ownership/audit-ownership.py            # check the recorded audit, print the table
    python tools/ownership/audit-ownership.py --write    # re-record it after classifying new sites
    python tools/ownership/audit-ownership.py --list write

The ownership model (M1-M17) puts a holder count on every mutable payload and a gate in front of
every in-place write. Before any of it can be built, the roads that reach a payload have to be
known, because the model's safety is exactly the claim that there are no others. This scans the
engine and the two neighbours a script payload reaches (the numeric kernels, the graphics object
model) and sorts every access into:

  read     the payload is only read.
  fresh    the payload, or the wrapper over it, was allocated right here, so nobody else holds it.
  write    an in-place write into a payload the method did not allocate: an indexed store, a span
           store, a `CopyTo` into it, or the payload handed to a kernel at a destination position.
           These are the sites M7's gated setters must own, and what V1.3's tripwire watches.
  adopt    a new wrapper or payload class minted over a payload the method did not allocate. M2
           says such a site takes a counted share or copies; `IndexStruct` handing a selection the
           caller's own element dictionaries (appendix A #12) is the example the model was
           written around.
  mutate   an M4 wrapper-level mutator (`Reshape`, `MarkTime`, `SetNumericClass`, …) applied to a
           wrapper the method did not mint. M4 allows it only on an entry's own wrapper.
  dispose  a payload released, which M6 allows only at one holder.

A payload is followed through the locals that hold it: `var planes = value.AsPackedComplex;` makes
`planes` a borrowed payload, so `planes.Re.AsSpan()[i] = x` two statements later is a write into
someone else's storage — which is how the appendix's live defects are spelled, and which no scan
of the accessor's own line can see.

Reads are counted but not recorded: there are hundreds, they change with every builtin written,
and none of them can lose a write. Every other site is recorded in `ownership-audit.csv` beside
this script, keyed by file, member, road and statement rather than by line number, so the record
survives edits above it. Running with no argument re-scans and fails on any difference: a write or
an adopt that appears unclassified is the drift this audit exists to catch.

The `rule` and `note` columns are written by hand and kept across a `--write`.
"""

from __future__ import annotations

import argparse
import csv
import re
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
RECORD = Path(__file__).resolve().parent / "ownership-audit.csv"

# The engine, and the graphics object model a script value can be adopted into. The numeric
# kernels are not scanned: they write into the buffer they are handed, and the site that decides
# whose buffer that is belongs to the engine. They are read for their signatures instead, so a
# call that hands one a borrowed payload is caught where the call is written.
ROOTS = [
    REPO / "src/JGraph.Scripting",
    REPO / "src/JGraph.Objects",
]

# The accessors that hand back a payload. `Elements` and `Fields` are reached through
# JgsStructArray and JgsObject rather than through JgsValue, so they are named separately.
ACCESSORS = [
    "AsBuffer", "AsPackedComplex", "AsArray", "AsCell", "AsStructArray", "AsStruct",
    "BoxedElements", "Elements", "Fields",
]
ACCESSOR_RE = re.compile(r"\.(" + "|".join(ACCESSORS) + r")\b")

# M4's wrapper-level mutators: they change a wrapper, never a payload.
MUTATORS = [
    "Reshape", "ReshapeDims", "TakeShapeOf", "SetNumericClass", "MarkStringArray",
    "MarkCharMatrix", "MarkTime", "SetClassName", "DemoteToBoxed",
]
MUTATOR_RE = re.compile(r"(?<![\w.])([\w.]+)\.(" + "|".join(MUTATORS) + r")\(")

# The wrapper's own in-place setters: a write into the payload, reached through the wrapper. M7
# puts the holder check inside these, so the audit lists every caller.
SETTERS = ["SetPackedNumber", "TryGrowInPlace", "CompactInPlace", "SetPackedComplex", "SetSlot"]
SETTER_RE = re.compile(r"(?<![\w.])([\w.]+)\.(" + "|".join(SETTERS) + r")\(")

# V1.2's gated accessors (M7): each detaches the wrapper's payload when another entry holds it
# and then hands back the entry's own storage, so a store into what one returned is the write the
# model sanctions. It is neither fresh (the storage may be years old) nor raw (the gate ran), and
# it is recorded as `write | gated` so the census still lists every road that changes a payload.
GATED = [
    "WritableCell", "WritableArray", "WritableBuffer", "WritablePlanes", "WritableStructArray",
    "WritableStruct", "WritableFields", "WritableElement",
]
GATED_RE = re.compile(r"\.(" + "|".join(GATED) + r")\(")

FACTORIES = ["Packed", "Shaped", "PackedComplexArray", "Cell", "StructArray", "Object", "Array"]
FACTORY_RE = re.compile(r"JgsValue\.(" + "|".join(FACTORIES) + r")\(")

# Expressions that allocate here: the payload cannot be anyone else's.
FRESH_RE = re.compile(
    r"\bnew\s+[\w.]+(\s*<[^>]*>)?\s*[\[({]"
    r"|JgsPacking\.Allocate\("
    r"|BufferAllocator\.\w+\("
    r"|ManagedBuffer\.Adopt\("
    r"|\.ToArray\(\)"
    r"|\.ToList\(\)"
    r"|Array\.Empty<"
    r"|\bstackalloc\b"
    r"|\[\s*\]"
    r"|\[\.\."
    r"|Array\.ConvertAll\("
    r"|\.Select\("
    r"|\.ToDictionary\("
    r"|Enumerable\."
)

# Wrapper-minting expressions: the JgsValue on the left is this method's own.
FRESH_WRAPPER_RE = re.compile(
    r"JgsValue\.\w+\("
    r"|\bnew\s+Jgs\w+\("
    r"|KeepShape\("
    r"|KeepNumericClass\("
    r"|CopyContainer\("
    r"|CopyForBinding\("
    r"|\.Copy\(\)"
    r"|JgsMatrix\.\w+\("
    r"|JgsEmpty\.\w+"
)

SINK_PARAM_NAMES = {"dest", "destination", "into", "output", "result", "target"}

# An expression that hands back a payload itself rather than a wrapper over one: the buffer, the
# planes, the slot array, the field dictionary, one element dictionary of a struct array. Storing
# one of these into a container the method just allocated is M2's case — `IndexStruct` filling a
# fresh array with the caller's own element dictionaries (appendix A #12) is spelled exactly so,
# and no rule that looks only at the accessor's own statement can see it.
PAYLOAD_RE = re.compile(
    r"\.(AsBuffer|AsPackedComplex|AsStruct|AsCell|AsArray|BoxedElements|Fields)\b(?!\s*[\[.])"
    r"|\.Elements\s*\[[^\]]*\](?!\s*\[)"
)

# What a payload is spelled as in a signature. A parameter of any other type — a `double[]`, a
# `Complex[]`, a property table — is scratch the caller allocated, not storage a script value can
# still be holding, so it is left out of the provenance map rather than reported as a borrow.
PAYLOAD_TYPE_RE = re.compile(
    r"\bNumericBuffer\b"
    r"|\bJgsValue\b"
    r"|\bJgsStructArray\b"
    r"|\bJgsPackedComplex\b"
    r"|\bJgsObject\b"
    r"|Dictionary<\s*string\s*,\s*JgsValue\s*>"
    r"|\bSpan<double>"
)

ASSIGN_RE = re.compile(
    r"^(?:(?:[\w.<>\[\],? ]+?)\s+)?([A-Za-z_]\w*)\s*=\s*(?![=>])(.+)$")
FOREACH_RE = re.compile(r"^foreach\s*\(\s*(?:[\w.<>\[\],? ]+\s+)?([A-Za-z_]\w*)\s+in\s+(.+?)\s*$")

CATEGORIES = ["write", "adopt", "mutate", "dispose", "fresh", "read"]

# The categories the record holds. A read and a fresh payload cannot lose a write, and both change
# with every builtin written, so recording them would make the check fail on ordinary work.
RECORDED = {"write", "adopt", "mutate", "dispose", "unclear"}


@dataclass
class Site:
    path: str
    member: str
    category: str
    kind: str
    statement: str
    line: int
    ordinal: int = 0
    rule: str = ""
    note: str = ""

    def key(self) -> tuple[str, str, str, str, int]:
        return (self.path, self.member, self.kind, self.statement, self.ordinal)


def strip_code(text: str) -> str:
    """Blanks comments and string bodies so brackets inside them cannot fool the splitter."""
    out: list[str] = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                out.append(" ")
                i += 1
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            while i < n and not (text[i] == "*" and i + 1 < n and text[i + 1] == "/"):
                out.append("\n" if text[i] == "\n" else " ")
                i += 1
            out.append("  ")
            i += 2
            continue
        if c in "\"'":
            verbatim = c == '"' and i > 0 and text[i - 1] == "@"
            out.append(" ")
            i += 1
            while i < n:
                if text[i] == "\\" and not verbatim:
                    out.append("  ")
                    i += 2
                    continue
                if text[i] == c:
                    if verbatim and i + 1 < n and text[i + 1] == c:
                        out.append("  ")
                        i += 2
                        continue
                    out.append(" ")
                    i += 1
                    break
                out.append("\n" if text[i] == "\n" else " ")
                i += 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def statements(text: str) -> list[tuple[int, str]]:
    """Splits a file into logical statements, each with the line it starts on."""
    code = strip_code(text)
    found: list[tuple[int, str]] = []
    start, line, start_line, depth = 0, 1, 1, 0
    for i, c in enumerate(code):
        if c in "([":
            depth += 1
        elif c in ")]":
            depth -= 1
        elif c == "\n":
            line += 1
        elif c in ";{}" and depth <= 0:
            piece = code[start:i].strip()
            if piece:
                found.append((start_line, " ".join(piece.split())))
            start, start_line = i + 1, line
            continue
        if not code[start:i].strip():
            start_line = line
    piece = code[start:].strip()
    if piece:
        found.append((start_line, " ".join(piece.split())))
    return found


MEMBER_RE = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*"
    r"(?:public|private|internal|protected)\s+"
    r"(?:static\s+|sealed\s+|abstract\s+|override\s+|virtual\s+|unsafe\s+|partial\s+|readonly\s+|new\s+|async\s+)*"
    r"[\w<>,.\[\]?]+\s+(\w+)\s*[(<]"
)


def members(text: str) -> list[tuple[int, str, set[str]]]:
    """Each member's line, name and parameter names.

    A parameter is a value the caller still holds, so a mutator or a mint over one is a site the
    model has to justify — which is why parameters are seeded into the provenance map rather than
    left unknown.
    """
    found: list[tuple[int, str, set[str]]] = []
    lines = text.splitlines()
    for n, raw in enumerate(lines, start=1):
        m = MEMBER_RE.match(raw)
        if not m:
            continue
        head = " ".join(lines[n - 1:n + 6])
        open_paren = head.find("(", m.start(1))
        names: set[str] = set()
        if open_paren >= 0:
            for param in split_params(" ".join(call_args(head, open_paren))):
                words = re.findall(r"[A-Za-z_]\w*", param.split("=")[0])
                if words and PAYLOAD_TYPE_RE.search(param):
                    names.add(words[-1])
        found.append((n, m.group(1), names))
    return found


def member_at(marks: list[tuple[int, str, set[str]]], line: int) -> tuple[str, set[str]]:
    name, params = "<file>", set()
    for n, who, names in marks:
        if n <= line:
            name, params = who, names
        else:
            break
    return name, params


def split_params(params: str) -> list[str]:
    out, depth, current = [], 0, ""
    for c in params:
        if c in "<([":
            depth += 1
        elif c in ">)]":
            depth -= 1
        if c == "," and depth == 0:
            out.append(current.strip())
            current = ""
            continue
        current += c
    if current.strip():
        out.append(current.strip())
    return out


def read_sinks() -> dict[str, set[int]]:
    """Kernels that write into a buffer argument, read from their own signatures.

    Keyed by `Type.Name`, because the engine has verbs of its own that share a kernel's name —
    `JgsBroadcast.Map` and `PackedMath.Map` are not the same call, and only one of them writes
    into what it is handed.
    """
    sinks: dict[str, set[int]] = defaultdict(set)
    signature = re.compile(
        r"public\s+static\s+[\w<>\[\],. ?]+\s+(\w+)\s*(?:<[^>]*>)?\s*\(([^;{]*?)\)\s*(?:where[^{;]*)?[{;]", re.S)
    declaration = re.compile(r"(?:class|struct)\s+(\w+)")
    for root in (REPO / "src/JGraph.Numerics", REPO / "src/JGraph.Scripting"):
        for path in sorted(root.rglob("*.cs")):
            if "obj" in path.parts or "bin" in path.parts:
                continue
            text = strip_code(path.read_text(encoding="utf-8"))
            for m in signature.finditer(text):
                name, params = m.group(1), " ".join(m.group(2).split())
                owner = None
                for d in declaration.finditer(text, 0, m.start()):
                    owner = d.group(1)
                if owner is None:
                    continue
                for position, param in enumerate(split_params(params)):
                    words = param.replace("this ", "").replace("ref ", "").replace("out ", "").split()
                    if len(words) >= 2 and words[-1] in SINK_PARAM_NAMES and \
                            ("NumericBuffer" in param or "Span<double>" in param):
                        sinks[f"{owner}.{name}"].add(position)
    return sinks


def root_name(expr: str) -> str:
    m = re.match(r"\s*\(?\s*([A-Za-z_]\w*)", expr)
    return m.group(1) if m else ""


def provenance(expr: str, holders: dict[str, str], depth: int = 0,
               fresh: set[str] | None = None) -> str:
    """fresh, borrowed or unknown — where the payload (or wrapper) in `expr` came from."""
    expr = expr.strip()
    if not expr:
        return "unknown"

    # The gate is asked first: `target.WritableArray()` is the entry's own storage after M7 ran.
    if GATED_RE.search(expr):
        return "gated"

    # Freshness next: `v.AsCell.ToArray()` and `new JgsValue[] { v.AsCell[0] }` allocate here even
    # though an accessor appears inside them. A bare `v.AsCell` does not.
    if FRESH_RE.search(expr) or FRESH_WRAPPER_RE.search(expr):
        return "fresh"
    if fresh:
        call = re.match(r"\s*\(?\s*(?:[\w.]+\.)?(\w+)\s*\(", expr)
        if call and call.group(1) in fresh:
            return "fresh"
    if ACCESSOR_RE.search(expr):
        return "borrowed"
    if depth < 3:
        root = root_name(expr)
        if root in holders:
            # A parameter is the caller's: for ownership it reads exactly like a borrowed payload.
            return "borrowed" if holders[root] == "parameter" else holders[root]
    return "unknown"


def build_holders(body: list[tuple[int, str]], fresh: set[str] | None = None) -> dict[str, str]:
    """Where each local in this member got what it holds. Worst provenance wins."""
    holders: dict[str, str] = {}
    for _, statement in body:
        m = FOREACH_RE.match(statement) or ASSIGN_RE.match(statement)
        if not m:
            continue
        name, rhs = m.group(1), m.group(2)
        found = provenance(rhs, holders, fresh=fresh)
        if holders.get(name) == "borrowed":
            continue
        holders[name] = found
    return holders


def lvalue_of(statement: str) -> str:
    """The left side of a top-level assignment, or an empty string."""
    depth = 0
    for i, c in enumerate(statement):
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        elif depth == 0 and c == "=":
            if statement[i - 1:i] in "=!<>+-*/|&^%" or statement[i + 1:i + 2] == "=":
                continue
            return statement[:i].strip()
        elif depth == 0 and statement[i:i + 2] in ("++", "--") and i > 0:
            return statement[:i].strip()
    return ""


def call_args(statement: str, open_paren: int) -> list[str]:
    depth, i = 0, open_paren
    while i < len(statement):
        if statement[i] in "([":
            depth += 1
        elif statement[i] in ")]":
            depth -= 1
            if depth == 0:
                return split_params(statement[open_paren + 1:i])
        i += 1
    return []


def trim(statement: str) -> str:
    return statement if len(statement) <= 150 else statement[:147] + "..."


def parse(path: Path) -> tuple[str, dict[str, list[tuple[int, str]]], dict[str, set[str]]]:
    """A file's statements grouped by member, with each member's parameter names."""
    text = path.read_text(encoding="utf-8")
    marks = members(text)
    groups: dict[str, list[tuple[int, str]]] = defaultdict(list)
    parameters: dict[str, set[str]] = {}
    for line, statement in statements(text):
        member, names = member_at(marks, line)
        groups[member].append((line, statement))
        parameters.setdefault(member, set()).update(names)
    return path.relative_to(REPO).as_posix(), groups, parameters


def holders_of(body: list[tuple[int, str]], parameters: set[str],
               fresh: set[str] | None = None) -> dict[str, str]:
    holders = build_holders(body, fresh)
    for name in parameters:
        holders.setdefault(name, "parameter")
    return holders


RETURN_RE = re.compile(r"^return\s+(.+)$")
LAMBDA_RE = re.compile(r"=>\s*(.+)$")


def return_contracts(files: list[tuple[str, dict, dict]]) -> set[str]:
    """Methods every one of whose returns is a value minted here.

    A `Reshape` on what another method handed back is M4's question — whether that method's answer
    is the caller's own wrapper — and the answer is in the callee, not at the call. Two passes let
    a chain of such helpers settle; a name declared more than once anywhere is only counted fresh
    when every one of them is, since the scan matches calls by name.
    """
    fresh: set[str] = set()
    for _ in range(3):
        verdicts: dict[str, set[str]] = defaultdict(set)
        for _, groups, parameters in files:
            for member, body in groups.items():
                holders = holders_of(body, parameters.get(member, set()), fresh)
                for _, statement in body:
                    m = RETURN_RE.match(statement)
                    expression = m.group(1) if m else None
                    if expression is None and "=>" in statement and "JgsValue" in statement:
                        arrow = LAMBDA_RE.search(statement)
                        expression = arrow.group(1) if arrow else None
                    if not expression:
                        continue
                    verdicts[member].add(provenance(expression, holders, fresh=fresh))
        settled = {name for name, seen in verdicts.items() if seen == {"fresh"}} - set(GATED)
        if settled == fresh:
            break
        fresh = settled
    return fresh


def analyze(relative: str, groups: dict, parameters: dict, sinks: dict[str, set[int]],
            fresh_calls: set[str]) -> list[Site]:
    found: list[Site] = []
    for member, body in groups.items():
        holders = holders_of(body, parameters.get(member, set()), fresh_calls)
        for line, statement in body:
            for category, kind in classify(statement, holders, sinks, fresh_calls):
                found.append(Site(relative, member, category, kind, trim(statement), line))
    return found


def classify(statement: str, holders: dict[str, str], sinks: dict[str, set[int]],
             fresh_calls: set[str] | None = None) -> list[tuple[str, str]]:
    """Every ownership-relevant thing one statement does."""
    out: list[tuple[str, str]] = []

    # 1. An in-place store: the left side indexes into something.
    lvalue = lvalue_of(statement)
    if lvalue and "[" in lvalue:
        road = ACCESSOR_RE.search(lvalue)
        root = root_name(lvalue)
        rhs = statement[len(lvalue):].lstrip("= ").strip()
        if GATED_RE.search(lvalue):
            out.append(("write", "gated"))
        elif road:
            out.append(("write", road.group(1)))
        elif root in holders and holders[root] == "gated":
            out.append(("write", "gated"))
        elif root in holders and holders[root] in ("borrowed", "parameter"):
            out.append(("write", "local"))
        elif root in holders and holders[root] == "fresh":
            payload = None if FRESH_RE.search(rhs) else PAYLOAD_RE.search(rhs)
            out.append(("adopt", payload.group(0).lstrip(".").split("[")[0]) if payload
                       else ("fresh", "local"))

    # 2. A payload handed to a kernel at a destination position, or to a span filler.
    for m in re.finditer(r"(?<![\w.])(\w+)\.(\w+)\s*\(", statement):
        name = f"{m.group(1)}.{m.group(2)}"
        positions = sinks.get(name)
        if not positions:
            continue
        args = call_args(statement, m.end() - 1)
        for position in positions:
            if position >= len(args):
                continue
            road = ACCESSOR_RE.search(args[position])
            root = root_name(args[position])
            if GATED_RE.search(args[position]) or (root in holders and holders[root] == "gated"):
                out.append(("write", "gated"))
            elif road:
                out.append(("write", road.group(1)))
            elif root in holders and holders[root] in ("borrowed", "parameter"):
                out.append(("write", "local"))

    # 3. `source.CopyTo(target)` and the span fillers write their target.
    for m in re.finditer(r"\.(CopyTo|Fill|Clear)\(", statement):
        args = call_args(statement, m.end() - 1)
        target = args[0] if (args and m.group(1) == "CopyTo") else lvalue_before(statement, m.start())
        road = ACCESSOR_RE.search(target)
        root = root_name(target)
        if GATED_RE.search(target) or (root in holders and holders[root] == "gated"):
            out.append(("write", "gated"))
        elif road:
            out.append(("write", road.group(1)))
        elif root in holders and holders[root] in ("borrowed", "parameter"):
            out.append(("write", "local"))

    # 4. Disposal.
    for m in re.finditer(r"([\w.\[\]]+)\.Dispose\(\)", statement):
        owner = m.group(1)
        road = ACCESSOR_RE.search(owner)
        root = root_name(owner)
        if road:
            out.append(("dispose", road.group(1)))
        elif root in holders:
            out.append(("dispose" if holders[root] in ("borrowed", "parameter") else "fresh", "local"))

    # 5. A wrapper or payload class minted over a payload.
    for m in FACTORY_RE.finditer(statement):
        args = call_args(statement, m.end() - 1)
        if not args:
            continue
        found = provenance(args[0], holders, fresh=fresh_calls)
        out.append(("fresh" if found == "fresh" else
                    "adopt" if found == "borrowed" else "unclear", "JgsValue." + m.group(1)))
    for m in re.finditer(r"new\s+(JgsStructArray|JgsPackedComplex|JgsObject)\s*\(", statement):
        args = call_args(statement, m.end() - 1)
        if not args:
            continue
        found = provenance(args[0], holders, fresh=fresh_calls)
        out.append(("fresh" if found == "fresh" else
                    "adopt" if found == "borrowed" else "unclear", "new " + m.group(1)))

    # 6. M4's wrapper mutators.
    for m in MUTATOR_RE.finditer(statement):
        receiver = m.group(1)
        if receiver.endswith("."):
            continue
        found = holders.get(root_name(receiver), "unknown")
        if found == "parameter":
            found = "borrowed"
        if re.match(r"^\s*(return\s+)?" + re.escape(receiver) + r"\s*\.", statement) and found == "unknown":
            found = "unknown"
        out.append(("fresh" if found == "fresh" else
                    "mutate" if found == "borrowed" else "unclear", m.group(2)))

    # 7. The wrapper's own in-place setters. These are the payload writes M7 gates, so every call
    # of one on a wrapper the method did not mint is a site the model has to answer for.
    for m in SETTER_RE.finditer(statement):
        receiver = m.group(1)
        if receiver.endswith("."):
            continue
        found = holders.get(root_name(receiver), "unknown")
        out.append(("fresh" if found == "fresh" else "write", m.group(2)))

    # 7. Plain reads, so the denominator is honest.
    if not out:
        for m in ACCESSOR_RE.finditer(statement):
            out.append(("read", m.group(1)))
    return out


def lvalue_before(statement: str, at: int) -> str:
    """The receiver of a `.Fill(` / `.Clear(` call ending at `at`."""
    m = re.search(r"([\w.\[\]()]+)$", statement[:at])
    return m.group(1) if m else ""


def scan(sinks: dict[str, set[int]]) -> list[Site]:
    files = []
    for root in ROOTS:
        for path in sorted(root.rglob("*.cs")):
            if "obj" in path.parts or "bin" in path.parts:
                continue
            text = path.read_text(encoding="utf-8")
            if "JgsValue" not in text and "NumericBuffer" not in text:
                continue
            files.append(parse(path))

    fresh_calls = return_contracts(files)
    found: list[Site] = []
    for relative, groups, parameters in files:
        found.extend(analyze(relative, groups, parameters, sinks, fresh_calls))

    seen: Counter[tuple] = Counter()
    for site in found:
        if site.category == "read":
            continue
        key = site.key()[:4]
        seen[key] += 1
        site.ordinal = seen[key] - 1
    return found


def load_record() -> dict[tuple, Site]:
    if not RECORD.exists():
        return {}
    out = {}
    with RECORD.open(encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            site = Site(row["file"], row["member"], row["category"], row["kind"],
                        row["statement"], int(row["line"] or 0), int(row["ordinal"] or 0),
                        row.get("rule", ""), row.get("note", ""))
            out[site.key()] = site
    return out


def write_record(sites: list[Site], previous: dict[tuple, Site]) -> None:
    rows = sorted((s for s in sites if s.category in RECORDED),
                  key=lambda s: (s.path, s.line, s.kind, s.ordinal))
    with RECORD.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(["file", "member", "category", "kind", "line", "ordinal", "rule", "statement", "note"])
        for site in rows:
            kept = previous.get(site.key())
            writer.writerow([
                site.path, site.member, site.category, site.kind, site.line, site.ordinal,
                kept.rule if kept else site.rule, site.statement, kept.note if kept else site.note,
            ])


def summarize(sites: list[Site]) -> None:
    by_category: Counter[str] = Counter(s.category for s in sites)
    print("| category | sites |")
    print("|---|---:|")
    for name in CATEGORIES + ["unclear"]:
        if by_category.get(name):
            print(f"| {name} | {by_category[name]} |")
    print()
    interesting = [s for s in sites if s.category in ("write", "adopt", "mutate", "dispose", "unclear")]
    by_kind: Counter[tuple[str, str]] = Counter((s.category, s.kind) for s in interesting)
    if by_kind:
        print("| category | payload road | sites |")
        print("|---|---|---:|")
        for (category, kind), count in sorted(by_kind.items()):
            print(f"| {category} | {kind} | {count} |")
        print()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true", help="re-record the audit")
    parser.add_argument("--list", metavar="CATEGORY", help="print every site in one category")
    parser.add_argument("--files", action="store_true", help="group the listing by file")
    parser.add_argument("--sinks", action="store_true", help="print the kernels that write into an argument")
    args = parser.parse_args()

    sinks = read_sinks()
    if args.sinks:
        for name, positions in sorted(sinks.items()):
            print(f"  {name}({', '.join(str(p) for p in sorted(positions))})")
        print(f"{len(sinks)} kernel(s) write into an argument")
        print()

    sites = scan(sinks)
    summarize(sites)

    if args.list:
        chosen = [s for s in sites if s.category == args.list]
        if args.files:
            per: Counter[str] = Counter(s.path for s in chosen)
            for path, count in per.most_common():
                print(f"{count:5d}  {path}")
        else:
            for site in chosen:
                print(f"{site.path}:{site.line}  {site.member}  [{site.kind}]  {site.statement}")
        print()

    previous = load_record()
    if args.write:
        write_record(sites, previous)
        print(f"recorded {len([s for s in sites if s.category in RECORDED])} site(s) in {RECORD.name}")
        return 0

    live = {s.key(): s for s in sites if s.category in RECORDED}
    added = [k for k in live if k not in previous]
    gone = [k for k in previous if k not in live]
    changed = [k for k in live if k in previous and live[k].category != previous[k].category]
    if not (added or gone or changed):
        print(f"ownership audit OK ({len(live)} classified site(s), "
              f"{len([s for s in sites if s.category not in RECORDED])} read or fresh)")
        return 0

    for k in added:
        s = live[k]
        print(f"  + unclassified {s.category}: {s.path}:{s.line} {s.member} — {s.statement}")
    for k in gone:
        s = previous[k]
        print(f"  - recorded site is gone: {s.path} {s.member} — {s.statement}")
    for k in changed:
        print(f"  ! {previous[k].category} became {live[k].category}: {live[k].path}:{live[k].line} {live[k].member}")
    print(f"{len(added) + len(gone) + len(changed)} difference(s); re-run with --write once each is classified")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
