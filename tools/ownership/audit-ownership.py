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
    r"|JgsStructArray\.SharedCopy\("
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

CATEGORIES = ["write", "adopt", "mutate", "dispose", "store", "handback", "fresh", "read"]

# The categories the record holds. A read and a fresh payload cannot lose a write, and both change
# with every builtin written, so recording them would make the check fail on ordinary work.
RECORDED = {"write", "adopt", "mutate", "dispose", "store", "handback", "unclear"}

# The C# list of builtins whose answer the interpreter adopts at a binding instead of sharing (V2.2,
# M2). Every name on it must be one this audit sees return only minted wrappers.
MINTING_SOURCE = REPO / "src/JGraph.Scripting/Jgs/JgsBuiltins.Minting.cs"


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
            # Each parameter on its own: joining them first and splitting again (as V1.1 did)
            # collapsed the list to one string and seeded only the last name.
            for param in call_args(head, open_paren):
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


ANNOTATED_RE = re.compile(
    r"//\s*audit:\s*mints[^\n]*\n(?:\s*///[^\n]*\n)*\s*(?:\[[^\]]*\]\s*)*"
    r"(?:public|private|internal|protected)[^\n(]*?\s(\w+)\s*\(")

# Members a comment `// audit: mints` above the declaration vouches for: every return is minted,
# in a shape the scan cannot settle on its own (a switch expression, a recursion). The comment is
# the assertion, read by a person, and the ADR lists them.
ANNOTATED: set[str] = set()


def parse(path: Path) -> tuple[str, dict[str, list[tuple[int, str]]], dict[str, set[str]]]:
    """A file's statements grouped by member, with each member's parameter names."""
    text = path.read_text(encoding="utf-8")
    ANNOTATED.update(ANNOTATED_RE.findall(text))
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
    # A builtin's body is a lambda over `args`, which no member signature declares: the arguments
    # are the caller's wrappers, so the name is seeded as a parameter everywhere (V2.1).
    holders.setdefault("args", "parameter")
    return holders


# ---------------------------------------------------------------------------------------------
# V2.1: wrappers, not payloads. M2 says every entry holds a wrapper of its own, so a road that
# stores a wrapper it did not just compute — an argument, an element read out of someone's cell or
# struct, a local holding one — into a container it is building is a site the model has to answer
# for (`store`), and a builtin that hands such a wrapper back is one the interpreter may not adopt
# at a binding (`handback`). Only the top-level shape of an expression counts: `Foo(args[0])` is a
# call, whose answer is Foo's business and is settled per helper in two passes.
# ---------------------------------------------------------------------------------------------

SHARE_WRAPPER_RE = re.compile(r"^(JgsValue\.Share|[\w.]*CopyForBinding|CopyContainer|[\w.]*RetainedForEntry|Hold)\(")
MINT_WRAPPER_RE = re.compile(r"^(new\s|JgsValue\.\w+\(|JgsMatrix\.\w+\(|JgsEmpty\.)")
ARRAY_TYPE_RE = re.compile(r"^(JgsValue\[\]|List<JgsValue>|IReadOnlyList<JgsValue>|IList<JgsValue>)$")
DICT_TYPE_RE = re.compile(
    r"^(Dictionary<string, JgsValue>|IReadOnlyDictionary<string, JgsValue>|IDictionary<string, JgsValue>)$")
DECL_RE = re.compile(
    r"^(?:(?:readonly|static|private|public|internal|const)\s+)*"
    r"([A-Za-z_][\w<>,\[\] ]*?)\s+([A-Za-z_]\w*)\s*=\s*(?![=>])(.+)$")
WRAPPER_FOREACH_RE = re.compile(
    r"^foreach\s*\(\s*(?:\(([^)]*)\)|([\w<>\[\],]+)\s+([A-Za-z_]\w*))\s+in\s+(.+?)\s*$")
# LINQ and array verbs that hand the same wrappers on in a new container.
PASS_THROUGH_RE = re.compile(
    r"(\.(ToArray|ToList|Clone|Reverse|Skip|Take|Where|Concat|OrderBy|OrderByDescending|Distinct)\([^()]*\))+$")
ELEMENT_ACCESSOR_RE = re.compile(r"\.(AsCell|AsArray|BoxedElements\(\))$")
FIELD_ACCESSOR_RE = re.compile(r"\.(AsStruct|Fields)$|\.Elements\s*\[[^\]]*\]$")


def unparen(expression: str) -> str:
    expression = expression.strip()
    while expression.startswith("(") and expression.endswith(")"):
        depth, whole = 0, True
        for i, c in enumerate(expression):
            if c == "(":
                depth += 1
            elif c == ")":
                depth -= 1
                if depth == 0 and i != len(expression) - 1:
                    whole = False
                    break
        if not whole:
            break
        expression = expression[1:-1].strip()
    return expression


def split_top(expression: str, separators: tuple[str, ...]) -> list[str]:
    """Splits at the separators that sit outside every bracket."""
    out, depth, current, i = [], 0, "", 0
    while i < len(expression):
        c = expression[i]
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        if depth == 0:
            hit = next((s for s in separators if expression.startswith(s, i)), None)
            if hit is not None:
                out.append(current)
                current = ""
                i += len(hit)
                continue
        current += c
        i += 1
    out.append(current)
    return [o.strip() for o in out]


def indexed_base(expression: str) -> str | None:
    """`base` of a top-level `base[...]`, with the brackets balanced."""
    expression = expression.rstrip()
    if not expression.endswith("]"):
        return None
    depth = 0
    for i in range(len(expression) - 1, -1, -1):
        c = expression[i]
        if c == "]":
            depth += 1
        elif c == "[":
            depth -= 1
            if depth == 0:
                base = expression[:i].rstrip()
                return base if base and not base.endswith(("(", ",")) else None
    return None


class WrapperScope:
    """Where each local of a member got the wrapper, wrapper array or field dictionary it holds."""

    def __init__(self, borrowing: set[str], fresh: set[str]) -> None:
        self.wrappers: dict[str, str] = {}
        self.arrays: dict[str, str] = {}
        self.dicts: dict[str, str] = {}
        self.borrowing = borrowing
        self.fresh = fresh

    def array_of(self, expression: str) -> str | None:
        expression = unparen(expression)
        if ".Select(JgsValue.Share)" in expression or "ConvertAll(" in expression and "JgsValue.Share" in expression:
            return "shared"  # every element passes through Share on its way in
        stripped = PASS_THROUGH_RE.sub("", expression)
        if stripped != expression:
            inner = self.array_of(stripped)
            return inner
        if expression.startswith("[.. ") or expression.startswith("[..") and expression.endswith("]"):
            inner = self.array_of(expression[3:-1] if expression.startswith("[.. ") else expression[3:-1])
            return inner
        if ELEMENT_ACCESSOR_RE.search(expression) or re.match(r"^[\w.]+\.(AsStruct|Fields)\.Values$", expression):
            return "borrowed"
        if re.match(r"^(args|arguments)$", expression):
            return "borrowed"
        m = re.match(r"^(?:[\w.]+\.)?(\w+)\s*\(", expression)
        if m:
            return "borrowed" if m.group(1) in self.borrowing else None
        m = re.match(r"^([A-Za-z_]\w*)$", expression)
        if m:
            return self.arrays.get(m.group(1))
        return None

    def dict_of(self, expression: str) -> str | None:
        expression = unparen(expression)
        if FIELD_ACCESSOR_RE.search(expression):
            return "borrowed"
        m = re.match(r"^([A-Za-z_]\w*)$", expression)
        if m:
            return self.dicts.get(m.group(1))
        m = re.match(r"^new\s+Dictionary<string, JgsValue>\s*\((.*)\)$", expression)
        if m:
            source = split_top(m.group(1), (",",))[0]
            return "copy" if self.dict_of(source) == "borrowed" else "fresh"
        return None

    def wrapper(self, expression: str) -> str:
        """borrowed, shared, fresh or unknown: the provenance of a wrapper-valued expression."""
        expression = unparen(expression)
        if not expression:
            return "unknown"
        if "?" in expression and not expression.startswith("?"):
            branches = split_top(expression, ("??",))
            if len(branches) == 1:
                branches = split_top(expression, ("?", ":"))
                branches = branches[1:] if len(branches) >= 3 else [expression]
            if len(branches) > 1:
                verdicts = {self.wrapper(b) for b in branches}
                return "borrowed" if "borrowed" in verdicts else ("fresh" if verdicts == {"fresh"} else "unknown")
        cast = re.match(r"^\((JgsValue(?:\[\])?|[\w<>,]+)\)\s*(.+)$", expression)
        if cast and "(" not in cast.group(1):
            return self.wrapper(cast.group(2))
        if SHARE_WRAPPER_RE.match(expression):
            return "shared"
        if MINT_WRAPPER_RE.match(expression):
            return "fresh"
        if re.match(r"^(args|arguments)\s*\[", expression):
            return "borrowed"
        base = indexed_base(expression)
        if base is not None:
            if self.array_of(base) == "borrowed" or self.dict_of(base) == "borrowed":
                return "borrowed"
            if ELEMENT_ACCESSOR_RE.search(base) or FIELD_ACCESSOR_RE.search(base):
                return "borrowed"
            # `Helper(args)[0]`: the element of an array a helper built is that helper's business,
            # and an array is never "fresh" for its elements — only its storage.
            return "unknown"
        if re.match(r"^JgsMatrix\.At\(", expression):
            return "borrowed"
        m = re.match(r"^([A-Za-z_]\w*)$", expression)
        if m:
            return self.wrappers.get(m.group(1), "unknown")
        m = re.match(r"^(?:[\w.]+\.)?(\w+)\s*\(", expression)
        if m:
            if m.group(1) in self.borrowing:
                return "borrowed"
            return "fresh" if m.group(1) in self.fresh else "unknown"
        if re.match(r"^[\w.]+\.(Value|Key)$", expression):
            root = expression.split(".")[0]
            return self.wrappers.get(root, "unknown")
        return "unknown"

    def learn(self, statement: str) -> None:
        m = WRAPPER_FOREACH_RE.match(statement)
        if m:
            source = m.group(4)
            if m.group(1):
                names = [p.split()[-1] for p in split_params(m.group(1))]
                if len(names) == 2 and self.dict_of(source) == "borrowed":
                    self.wrappers[names[1]] = "borrowed"
            elif m.group(2) in ("JgsValue", "var"):
                if self.array_of(source) == "borrowed" or self.dict_of(source) == "borrowed" \
                        or (source.endswith(".Values") and self.dict_of(source[:-7]) == "borrowed"):
                    self.wrappers[m.group(3)] = "borrowed"
            elif m.group(2) == "KeyValuePair<string,JgsValue>" or m.group(2) == "KeyValuePair<string, JgsValue>":
                if self.dict_of(source) == "borrowed":
                    self.wrappers[m.group(3)] = "borrowed"
            return
        m = DECL_RE.match(statement)
        if not m:
            m = re.match(r"^([A-Za-z_]\w*)\s*=\s*(?![=>])(.+)$", statement)
            if m and m.group(1) in self.wrappers and self.wrappers[m.group(1)] != "borrowed":
                self.wrappers[m.group(1)] = self.wrapper(m.group(2))
            return
        typ, name, rhs = m.group(1).strip(), m.group(2), m.group(3)
        if typ == "JgsValue" or (typ == "var" and self.wrapper(rhs) in ("borrowed", "shared")):
            if self.wrappers.get(name) != "borrowed":
                self.wrappers[name] = self.wrapper(rhs)
        elif ARRAY_TYPE_RE.match(typ) or (typ == "var" and self.array_of(rhs)):
            self.arrays[name] = self.array_of(rhs) or ("fresh" if FRESH_RE.search(rhs) else "unknown")
        elif DICT_TYPE_RE.match(typ) or (typ == "var" and self.dict_of(rhs)):
            self.dicts[name] = self.dict_of(rhs) or ("fresh" if FRESH_RE.search(rhs) else "unknown")


def classify_stores(statement: str, scope: WrapperScope) -> list[tuple[str, str]]:
    """Every place this statement stores a borrowed wrapper into a container being built."""
    out: list[tuple[str, str]] = []
    lvalue = lvalue_of(statement)
    if lvalue and lvalue.endswith("]"):
        rhs = statement[len(lvalue):].lstrip("= ").strip()
        base = indexed_base(lvalue)
        if lvalue.startswith("["):
            if scope.wrapper(rhs) == "borrowed":
                out.append(("store", "init"))
        elif base is not None:
            payload_write = GATED_RE.search(base) or ACCESSOR_RE.search(base) \
                or scope.array_of(base) == "borrowed" or scope.dict_of(base) == "borrowed"
            if scope.wrapper(rhs) == "borrowed":
                # A borrowed wrapper stored into someone's payload through the gate is still a
                # store: the slot written is an entry, and it must not hold another entry's wrapper.
                out.append(("store", "gated" if payload_write else "slot"))
    for m in re.finditer(r"\.(Add|AddRange|SetSlot)\(", statement):
        args = call_args(statement, m.end() - 1)
        if not args:
            continue
        value = args[-1] if m.group(1) == "SetSlot" else args[0]
        if m.group(1) == "AddRange":
            if scope.array_of(value) == "borrowed":
                out.append(("store", "copy"))
        elif scope.wrapper(value) == "borrowed":
            out.append(("store", "setslot" if m.group(1) == "SetSlot" else "add"))
    for m in FACTORY_RE.finditer(statement):
        args = call_args(statement, m.end() - 1)
        if not args:
            continue
        first = args[0].strip()
        if first.startswith("[") and first.endswith("]"):
            for element in split_params(first[1:-1]):
                element = element.strip()
                if element.startswith(".."):
                    if scope.array_of(element[2:]) == "borrowed":
                        out.append(("store", "copy"))
                elif element and scope.wrapper(element) == "borrowed":
                    out.append(("store", "collection"))
        elif scope.array_of(first) == "borrowed" and PASS_THROUGH_RE.search(first):
            out.append(("store", "copy"))
    for m in re.finditer(r"new\s+Dictionary<string, JgsValue>\s*\(", statement):
        args = call_args(statement, m.end() - 1)
        if args and scope.dict_of(args[0]) == "borrowed":
            out.append(("store", "copy"))
    # Initializer entries inside a `new … { [key] = value, … }` that the splitter kept whole.
    for m in re.finditer(r"\{\s*(\[[^\]]+\]\s*=\s*[^,}]+(?:,\s*\[[^\]]+\]\s*=\s*[^,}]+)*)\s*\}", statement):
        for entry in split_top(m.group(1), (",",)):
            value = entry.split("=", 1)[1].strip() if "=" in entry else ""
            if value and scope.wrapper(value) == "borrowed":
                out.append(("store", "init"))
    return out


def borrowing_contracts(files: list[tuple[str, dict, dict]], fresh: set[str]) -> set[str]:
    """Members that hand back a borrowed wrapper on some return, settled in passes."""
    borrowing: set[str] = set()
    for _ in range(3):
        found: set[str] = set()
        for _, groups, parameters in files:
            for member, body in groups.items():
                scope = WrapperScope(borrowing, fresh)
                for name in parameters.get(member, set()) | {"args"}:
                    scope.wrappers.setdefault(name, "borrowed")
                for _, statement in body:
                    scope.learn(statement)
                    m = RETURN_RE.match(statement)
                    # A member handing back a borrowed wrapper, or an array holding borrowed
                    # wrappers (`return [.. args]`), makes every call of it a borrow.
                    if m and (scope.wrapper(m.group(1)) == "borrowed" or scope.array_of(m.group(1)) == "borrowed"):
                        found.add(member)
        if found == borrowing:
            break
        borrowing = found
    return borrowing


REGISTRATION_RE = re.compile(r"(?:Define\w*|Register|BuiltinFunction)\(\s*\"([\w.]+)\"\s*,")
HELPER_REGISTRATION_RE = re.compile(r"\b(\w+)\(\s*Define\w*\s*,\s*\"([\w.]+)\"")
LAMBDA_HEAD_RE = re.compile(r"^\s*\(?\s*\w*\s*\(?\s*(?:args|arguments)\s*,[^)]*\)\s*=>\s*(.*)$", re.S)


def registrations(raw: str):
    """Each builtin registered by a literal name in this file, with its lambda body (comments and
    string bodies blanked, so the name is read from the raw text and the body from the stripped)."""
    code = strip_code(raw)
    seen_direct: set[int] = set()
    for m in REGISTRATION_RE.finditer(raw):
        open_paren = code.index("(", m.start())
        depth, k = 0, open_paren
        while k < len(code):
            c = code[k]
            if c in "([{":
                depth += 1
            elif c in ")]}":
                depth -= 1
                if depth == 0:
                    break
            k += 1
        body = code[m.end():k]
        if "new BuiltinFunction(" in body:
            continue  # the inner BuiltinFunction("name", …) match carries the single-output body
        yield m.group(1), body, raw.count("\n", 0, m.start()) + 1, None
    for m in HELPER_REGISTRATION_RE.finditer(raw):
        yield m.group(2), "", raw.count("\n", 0, m.start()) + 1, m.group(1)


def analyze_builtins(files: list[Path], fresh: set[str], borrowing: set[str],
                     helper_verdicts: dict[str, set[str]]) -> tuple[list[Site], dict[str, set[str]]]:
    """Every builtin's return provenances, and a site for each return of a borrowed wrapper."""
    verdicts: dict[str, set[str]] = defaultdict(set)
    sites: list[Site] = []
    for path in files:
        raw = path.read_text(encoding="utf-8")
        relative = path.relative_to(REPO).as_posix()
        for name, body, line, helper in registrations(raw):
            if helper is not None:
                verdicts[name] |= helper_verdicts.get(helper, {"unknown"})
                continue
            scope = WrapperScope(borrowing, fresh)
            scope.wrappers["args"] = "borrowed"
            flat = " ".join(body.split())
            head = LAMBDA_HEAD_RE.match(flat)
            if head and not head.group(1).startswith("{"):
                expression = head.group(1)
                while expression.count(")") > expression.count("(") and expression.endswith(")"):
                    expression = expression[:-1].rstrip()
                verdict = scope.wrapper(expression)
                verdicts[name].add(verdict)
                if verdict == "borrowed":
                    sites.append(Site(relative, f"builtin:{name}", "handback", "lambda", trim(expression), line))
                continue
            for _, statement in statements(body):
                scope.learn(statement)
                m = RETURN_RE.match(statement)
                if not m:
                    continue
                verdict = scope.wrapper(m.group(1))
                verdicts[name].add(verdict)
                if verdict == "borrowed":
                    sites.append(Site(relative, f"builtin:{name}", "handback", "return", trim(statement), line))
            if not verdicts[name]:
                verdicts[name].add("none")
    return sites, verdicts


def helper_return_verdicts(files: list[tuple[str, dict, dict]], fresh: set[str],
                           borrowing: set[str]) -> dict[str, set[str]]:
    """For a registrar helper (`MathX(Define, "sin", …)`), what the lambdas inside it return."""
    verdicts: dict[str, set[str]] = defaultdict(set)
    for _, groups, parameters in files:
        for member, body in groups.items():
            scope = WrapperScope(borrowing, fresh)
            for name in parameters.get(member, set()) | {"args"}:
                scope.wrappers.setdefault(name, "borrowed")
            for _, statement in body:
                scope.learn(statement)
                m = RETURN_RE.match(statement)
                expression = m.group(1) if m else None
                if expression is None and "=>" in statement and "args" in statement:
                    arrow = LAMBDA_RE.search(statement)
                    expression = arrow.group(1) if arrow else None
                    if expression:
                        while expression.count(")") > expression.count("(") and expression.endswith(")"):
                            expression = expression[:-1].rstrip()
                if expression:
                    verdicts[member].add(scope.wrapper(expression))
    return verdicts


def minting_names() -> list[str]:
    if not MINTING_SOURCE.exists():
        return []
    text = strip_code(MINTING_SOURCE.read_text(encoding="utf-8"))
    raw = MINTING_SOURCE.read_text(encoding="utf-8")
    start = raw.find("MintingBuiltins")
    return re.findall(r"\"([\w.]+)\"", raw[start:]) if start >= 0 else []


def check_minting(verdicts: dict[str, set[str]]) -> list[str]:
    """Every name the interpreter adopts must be one every return of which is minted here."""
    problems = []
    for name in minting_names():
        seen = verdicts.get(name)
        if seen is None:
            problems.append(f"{name}: not a builtin this audit can see (registered without a literal name?)")
        elif seen != {"fresh"}:
            problems.append(f"{name}: returns {', '.join(sorted(seen))} — only an all-fresh builtin may be adopted")
    return problems


# ---------------------------------------------------------------------------------------------
# V3.1: which builtins can run script code. M5's whole-call scope holds every argument of such a
# builtin as a counted share for the length of the call, so the list the interpreter reads
# (`JgsBuiltins.ScriptRunningBuiltins`) must name every builtin whose body reaches a script entry
# point — a callable's `Call`, the interpreter's evaluators, a callback drain — directly or through
# any helper, and nothing else. The call graph is by member name, so an overload or a name two
# classes share over-approximates: a builtin it wrongly flags pays a share, never a lost write.
# ---------------------------------------------------------------------------------------------

SCRIPTING_ROOT = REPO / "src/JGraph.Scripting"
SCOPES_SOURCE = REPO / "src/JGraph.Scripting/Jgs/JgsBuiltins.Scopes.cs"

# A statement that hands control to script code: a callable invoked (directly, or as a callback
# asked for nothing through JgsCallbacks.Invoke - V9), source text or a syntax tree evaluated, a
# paused session resumed, queued callbacks run.
SCRIPT_ENTRY_RE = re.compile(
    r"\.(Call|CallMultiple|CallDiscarded|CallAsStatement|CallAsCallback)\("
    r"|\bJgsCallbacks\.Invoke\("
    r"|\b(EvaluateSource|EvaluateSourceIn|EvaluateInContext|EvaluateForOutputsInContext|EvaluateForOutputsIn"
    r"|EvaluateIn|ExecuteFunctionBody|RunScriptFile|RunWhilePaused)\("
    r"|\binterpreter\.Evaluate\w*\("
    r"|\.Drain\("
)
# A call the graph follows: an unqualified `Name(` (the member's own class, its partials, a static
# import), or one qualified by the two receivers the engine's own members are reached through —
# the builtins class and the interpreter. A call on any other receiver (`list.Add(`,
# `builder.Build(`) is a library call or a model object's, and following it by name alone joined
# every builtin to every other through `Add` and `Build`; the members that do reach script through
# such a receiver are the entry points themselves, which SCRIPT_ENTRY_RE names directly.
CALLEE_RE = re.compile(
    r"(?<![\w.])(?<!new )([A-Z]\w*)\s*(?:<[^<>()]*>)?\("
    r"|\.([A-Z]\w*)\s*(?:<[^<>()]*>)?\("
)
# The receivers whose members the graph follows by name: the engine's own classes, reached as a
# type or through the one instance a builtin holds.
OWN_RECEIVER_RE = re.compile(
    r"\b(?:JgsBuiltins|Interpreter|interpreter|_interpreter|this|JgsCallbackDispatcher\.Current\??"
    r"|dispatcher\??|_dispatcher\??)\s*$")

# Receiver calls by these names are the base library's (collections, text, tasks, spans) or are
# spelled the same by a dozen model types, and following them by name joined every builtin to
# every other. A member of the engine's own with one of these names is still reached by an
# unqualified call from its own class.
LIBRARY_NAMES = {
    "Add", "AddRange", "Clear", "Build", "Remove", "RemoveAt", "RemoveAll", "Insert", "Contains",
    "ContainsKey", "TryGetValue", "TryGet", "TryAdd", "Get", "Set", "GetValueOrDefault", "Parse",
    "TryParse", "Format", "ToString", "Append", "AppendLine", "Dispose", "Invoke", "Equals",
    "CompareTo", "Run", "Start", "Stop", "Wait", "Select", "SelectMany", "Where", "Any", "All",
    "First", "FirstOrDefault", "Last", "LastOrDefault", "Count", "Sum", "Max", "Min", "Sort",
    "OrderBy", "Reverse", "Split", "Join", "Replace", "Trim", "TrimEnd", "TrimStart", "Substring",
    "IndexOf", "LastIndexOf", "StartsWith", "EndsWith", "Concat", "CopyTo", "Fill", "Slice",
    "AsSpan", "AsMemory", "GetValue", "SetValue", "Push", "Pop", "Peek", "Enqueue", "Dequeue",
    "TryDequeue", "Write", "WriteLine", "Read", "ReadLine", "ReadToEnd", "Close", "Open", "Flush",
    "Register", "Lookup", "Resolve", "Value", "Values", "Keys", "ToArray", "ToList", "ToDictionary",
    "GetEnumerator", "MoveNext", "Distinct", "Zip", "Skip", "Take", "Aggregate", "Cast", "OfType",
    "Exists", "Find", "FindIndex", "ConvertAll", "ForEach", "TrueForAll", "SequenceEqual", "Length",
    "Create", "Clone", "Copy", "Reset", "Update", "Apply", "Execute", "Handle", "Process", "Load",
    "Save", "Delete", "Draw", "Render", "Refresh", "Show", "Hide", "Enter", "Exit", "Push", "Emit",
}

# Members whose road to script code is a user class's method chosen because an argument is an
# object — an operator or function overload. M5 holds every argument of a call whose arguments
# include an object for the whole call whatever the builtin, so these are the dynamic rule's, and
# cutting them keeps `sin`, `plus` and the rest of the numeric surface off the list. `Construct`
# is a class's constructor, which binds its arguments as parameters — counted shares (M2) — before
# its body runs, exactly as a user function does, so it needs no whole-call scope either.
OVERLOAD_DISPATCH = {
    "TryUnaryOverload", "TryOperatorOverload", "TryObjectDisplay", "Construct",
}


FUNCTION_HEAD_RE = re.compile(
    r"(?:^|[\s(])(?:[\w<>\[\],.?]+\s+)+([A-Za-z_]\w*)\s*(?:<[\w\s,]*>)?\s*\((.*)\)\s*(?:where\s[^{]*)?$", re.S)
EXPRESSION_BODIED_RE = re.compile(
    r"^(?:[\w<>\[\],.?]+\s+)+([A-Za-z_]\w*)\s*(?:<[\w\s,]*>)?\s*\([^=]*\)\s*=>")
NOT_A_FUNCTION = {"if", "for", "foreach", "while", "switch", "using", "lock", "catch", "fixed", "return",
                  "new", "else", "when", "throw", "await", "nameof", "typeof", "sizeof", "default", "is", "in"}


# Functions whose only road to script code is a `.Call` on another builtin they looked up by name
# — not a callable an argument supplied — and whose targets run none: `warning` (every numeric
# warning is printed through it), a text verb's legacy JGS spelling, a reduction's own inner
# implementation, `CallBuiltin`'s named lookup. Each is a claim about the target, checked by hand
# when the forward is written: a forward to a builtin that does run script must not be listed.
FORWARDS_TO_BUILTIN = {
    "Warn": "warning",
    "WarnIfRankDeficient": "warning",
    "TextSearched": "the legacy JGS contains/startsWith/endsWith/count",
    "IsMissingOf": "the legacy JGS ismissing",
    "SplitText2": "the legacy JGS split",
    "Reduce": "the reduction's own inner builtin (sum, prod, mean, …)",
    "CallBuiltin": "a builtin named by the caller in C# (min, max, …)",
}


def callees(statement: str, local: dict[str, str] | None = None) -> set[str]:
    """The function names a statement calls, as the script-reach graph follows them; a call of
    one of this file's local functions names its file-scoped node."""
    found = set()
    for m in CALLEE_RE.finditer(statement):
        if m.group(1):
            name = m.group(1)
            found.add(local.get(name, name) if local else name)
        elif m.group(2) not in LIBRARY_NAMES and OWN_RECEIVER_RE.search(statement[:m.start()]):
            found.add(m.group(2))
    return found


def function_bodies(raw: str, label: str = "") -> tuple[dict[str, list[str]], dict[str, str]]:
    """Every method and local function in a file, with the statements inside its braces, and the
    file's local functions by name.

    A local function (`JgsValue CloseFigures(…) { … }` inside a registrar) is a node of its own:
    attributing its body to the member around it hid `close`'s `CloseRequestFcn` road behind a
    name nothing called. It is scoped to its file (`JgsBuiltins.cs::CloseFigures`), because a
    dozen files each have an `At` or a `Sample` of their own and joining them by name joined
    `datetime` to a quadrature's integrand. A lambda is not a node; its statements belong to the
    function it is in. A control statement's head (`if (Foo()) {`) is a statement of its function.
    """
    code = strip_code(raw)
    bodies: dict[str, list[str]] = defaultdict(list)
    local: dict[str, str] = {}
    stack: list[tuple[str, int]] = []
    depth, start = 0, 0
    paren = 0
    for i, c in enumerate(code):
        if c in "([":
            paren += 1
        elif c in ")]":
            paren -= 1
        elif c == "{" and paren <= 0:
            head = " ".join(code[start:i].split())
            m = FUNCTION_HEAD_RE.search(head)
            depth += 1
            if m and m.group(1) not in NOT_A_FUNCTION and "=>" not in head and "=" not in head.split("(")[0]:
                name = m.group(1)
                if stack:  # declared inside a function: a local function, scoped to the file
                    name = local.setdefault(m.group(1), f"{label}::{m.group(1)}")
                stack.append((name, depth))
            elif head and stack:
                bodies[stack[-1][0]].append(head)
            start = i + 1
        elif c == "}" and paren <= 0:
            piece = " ".join(code[start:i].split())
            if piece and stack:
                bodies[stack[-1][0]].append(piece)
            if stack and stack[-1][1] == depth:
                stack.pop()
            depth -= 1
            start = i + 1
        elif c == ";" and paren <= 0:
            piece = " ".join(code[start:i].split())
            if piece:
                m = EXPRESSION_BODIED_RE.match(piece)
                if m and m.group(1) not in NOT_A_FUNCTION:
                    name = m.group(1)
                    if stack:
                        name = local.setdefault(m.group(1), f"{label}::{m.group(1)}")
                    bodies[name].append(piece)
                elif stack:
                    bodies[stack[-1][0]].append(piece)
            start = i + 1
    return bodies, local


LOCALS: dict[str, dict[str, str]] = {}


def script_reach(files: list[tuple[str, dict, dict]]) -> set[str]:
    """Every function name whose body, or the body of any function it calls, reaches a script entry."""
    calls: dict[str, set[str]] = defaultdict(set)
    reach: set[str] = set()
    for path in sorted(SCRIPTING_ROOT.rglob("*.cs")):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        bodies, local = function_bodies(path.read_text(encoding="utf-8"), path.name)
        LOCALS[path.name] = local
        for member, body in bodies.items():
            if member in OVERLOAD_DISPATCH or member.split("::")[-1] in FORWARDS_TO_BUILTIN:
                continue
            for statement in body:
                if SCRIPT_ENTRY_RE.search(statement):
                    reach.add(member)
                calls[member] |= callees(statement, local)
    while True:
        grown = {member for member, callees in calls.items() if member not in reach and callees & reach}
        if not grown:
            return reach
        reach |= grown


def script_running_builtins(reach: set[str]) -> dict[str, int]:
    """Every builtin registered by a literal name whose body reaches script code, with its line."""
    found: dict[str, int] = {}
    for path in sorted(SCRIPTING_ROOT.rglob("*.cs")):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        raw = path.read_text(encoding="utf-8")
        for name, body, line, helper in registrations(raw):
            if helper is not None:
                runs = helper in reach
            else:
                local = LOCALS.get(path.name, {})
                runs = any(SCRIPT_ENTRY_RE.search(s) or callees(s, local) & reach for _, s in statements(body))
            if runs:
                found.setdefault(name, line)
    return found


def registered_names() -> set[str]:
    names: set[str] = set()
    for path in sorted(SCRIPTING_ROOT.rglob("*.cs")):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        names.update(name for name, _, _, _ in registrations(path.read_text(encoding="utf-8")))
    return names


def listed_script_runners() -> list[str]:
    if not SCOPES_SOURCE.exists():
        return []
    raw = SCOPES_SOURCE.read_text(encoding="utf-8")
    start = raw.find("ScriptRunningBuiltins")
    return re.findall(r"\"([\w.]+)\"", raw[start:]) if start >= 0 else []


ASSERTED_RE = re.compile(r"//\s*audit: runs no script:\s*([\w. ]+?)\s+—")


def asserted_script_free() -> set[str]:
    """Builtins the graph flags that JgsBuiltins.Scopes.cs asserts, each with its reason, run no
    script — a name two helpers share, or a forward to another builtin. Like `// audit: mints`, a
    spelled-out claim the audit believes and a reviewer can read."""
    if not SCOPES_SOURCE.exists():
        return set()
    names: set[str] = set()
    for m in ASSERTED_RE.finditer(SCOPES_SOURCE.read_text(encoding="utf-8")):
        names.update(m.group(1).split())
    return names


def check_script_runners(computed: dict[str, int]) -> list[str]:
    """The list must be exactly the builtins this audit sees reach script code, less those
    asserted script-free; an assertion about a builtin the graph no longer flags is stale."""
    listed = set(listed_script_runners())
    asserted = asserted_script_free()
    flagged = set(computed)
    problems = [f"{name}: reaches script code but is neither on ScriptRunningBuiltins nor asserted script-free"
                for name in sorted(flagged - listed - asserted)]
    known = registered_names()
    for name in sorted(listed - flagged):
        problems.append(f"{name}: on ScriptRunningBuiltins but "
                        + ("reaches no script entry" if name in known else "not a builtin this audit can see"))
    for name in sorted(asserted - flagged):
        problems.append(f"{name}: asserted script-free but the graph no longer flags it (stale assertion)")
    for name in sorted(asserted & listed):
        problems.append(f"{name}: both listed and asserted script-free")
    return problems


RETURN_RE = re.compile(r"^return\s+(.+)$")
LAMBDA_RE = re.compile(r"=>\s*(.+)$")


def return_contracts(files: list[tuple[str, dict, dict]]) -> set[str]:
    """Methods every one of whose returns is a value minted here.

    A `Reshape` on what another method handed back is M4's question — whether that method's answer
    is the caller's own wrapper — and the answer is in the callee, not at the call. Two passes let
    a chain of such helpers settle; a name declared more than once anywhere is only counted fresh
    when every one of them is, since the scan matches calls by name.
    """
    fresh: set[str] = set(ANNOTATED)
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
        settled = ({name for name, seen in verdicts.items() if seen == {"fresh"}} - set(GATED)) | ANNOTATED
        if settled == fresh:
            break
        fresh = settled
    return fresh


def analyze(relative: str, groups: dict, parameters: dict, sinks: dict[str, set[int]],
            fresh_calls: set[str], borrowing: set[str]) -> list[Site]:
    found: list[Site] = []
    for member, body in groups.items():
        holders = holders_of(body, parameters.get(member, set()), fresh_calls)
        scope = WrapperScope(borrowing, fresh_calls)
        for name in parameters.get(member, set()) | {"args"}:
            scope.wrappers.setdefault(name, "borrowed")
        for line, statement in body:
            scope.learn(statement)
            for category, kind in classify(statement, holders, sinks, fresh_calls):
                found.append(Site(relative, member, category, kind, trim(statement), line))
            for category, kind in classify_stores(statement, scope):
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


SCRIPT_FILES: list[tuple[str, dict, dict]] = []


def scan(sinks: dict[str, set[int]]) -> tuple[list[Site], dict[str, set[str]]]:
    files = []
    paths = []
    for root in ROOTS:
        for path in sorted(root.rglob("*.cs")):
            if "obj" in path.parts or "bin" in path.parts:
                continue
            text = path.read_text(encoding="utf-8")
            if "JgsValue" not in text and "NumericBuffer" not in text:
                continue
            files.append(parse(path))
            paths.append(path)

    SCRIPT_FILES.extend(parse(path) for path in sorted(SCRIPTING_ROOT.rglob("*.cs"))
                        if "obj" not in path.parts and "bin" not in path.parts)
    fresh_calls = return_contracts(files)
    borrowing = borrowing_contracts(files, fresh_calls)
    found: list[Site] = []
    for relative, groups, parameters in files:
        found.extend(analyze(relative, groups, parameters, sinks, fresh_calls, borrowing))

    helpers = helper_return_verdicts(files, fresh_calls, borrowing)
    handbacks, verdicts = analyze_builtins(paths, fresh_calls, borrowing, helpers)
    found.extend(handbacks)

    seen: Counter[tuple] = Counter()
    for site in found:
        if site.category == "read":
            continue
        key = site.key()[:4]
        seen[key] += 1
        site.ordinal = seen[key] - 1
    return found, verdicts


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
    parser.add_argument("--builtins", action="store_true",
                        help="print every builtin whose returns are all minted (the candidates for adoption)")
    parser.add_argument("--runners", action="store_true",
                        help="print every builtin whose body reaches script code (ScriptRunningBuiltins)")
    args = parser.parse_args()

    sinks = read_sinks()
    if args.sinks:
        for name, positions in sorted(sinks.items()):
            print(f"  {name}({', '.join(str(p) for p in sorted(positions))})")
        print(f"{len(sinks)} kernel(s) write into an argument")
        print()

    sites, verdicts = scan(sinks)
    summarize(sites)

    if args.builtins:
        minted = sorted(name for name, seen in verdicts.items() if seen == {"fresh"})
        print(f"{len(minted)} builtin(s) return only minted wrappers, of {len(verdicts)} seen:")
        print("  " + " ".join(minted))
        print()

    minting_problems = check_minting(verdicts)
    if minting_problems:
        print(f"{len(minting_problems)} minting problem(s) in {MINTING_SOURCE.name}:")
        for p in minting_problems:
            print("  -", p)
        return 1
    print(f"minting builtins OK ({len(minting_names())} adopted at a binding, each returning only minted wrappers)")

    runners = script_running_builtins(script_reach(SCRIPT_FILES))
    if args.runners:
        print(f"{len(runners)} builtin(s) reach script code:")
        print("  " + " ".join(sorted(runners)))
        print()
    runner_problems = check_script_runners(runners)
    if runner_problems:
        print(f"{len(runner_problems)} script-running problem(s) in {SCOPES_SOURCE.name}:")
        for p in runner_problems:
            print("  -", p)
        return 1
    print(f"script-running builtins OK ({len(listed_script_runners())} hold their arguments for the whole call, "
          f"{len(asserted_script_free())} flagged by name and asserted script-free)")

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
