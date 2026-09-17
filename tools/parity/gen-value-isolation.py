#!/usr/bin/env python3
"""Generates the value-isolation matrix fixtures of the value-ownership plan (V0).

    python tools/parity/gen-value-isolation.py [--out tests/JGraph.Tests/MatlabParity/fixtures] [--ceiling 2000]

Appendix A's cases are hand-written (value_isolation_<area>.m); this writes the matrix around them,
one fixture per axis of the ownership model, so the surface is complete by construction rather
than hand-picked:

    value_isolation_gen_entry_<n>   M2/M3: every payload kind stored in every entry kind, then
                                    written through the original and through the entry, by every
                                    write form the payload takes; sizes 5 and 5000
    value_isolation_gen_scope_<n>   M5: every scope kind holding every payload kind while a call
                                    writes the global it came from; sizes 5 and 5000
    value_isolation_gen_overlap     M10: the right-hand side, a subscript, or both are the target's
                                    own storage, with and without growth, promotion and demotion,
                                    for every element type and target kind
    value_isolation_gen_refusal     M14: a refused write leaves the target and its aliases whole,
                                    for every write form, failure kind and target kind
    value_isolation_gen_multi       M11: every output form of a multiple assignment whose earlier
                                    target aliases a later output
    value_isolation_gen_order       M15/M16: the order of right-hand side, subscripts and end by
                                    target shape, with each part growing, shrinking, rebinding or
                                    clearing the target
    value_isolation_gen_builtins    M8: every value-transforming builtin with its result discarded,
                                    kept and failing midway, and the handle-type controls

Every case is one local function printing one exact line; both engines run the same show()
helper, so a value's class, size and contents compare as text. A fixture's .owners sidecar gives
each axis a default owning stage and names the exceptions; the stamp mode records the baselines.
The script prints the line count per fixture and fails above the ceiling, which keeps each fixture
inside the parity slice's budget.

Excluded on purpose: growth past an allocation limit (an unbounded allocation attempt is not a
safe fixture on either engine), and the persistent-slot and nested-workspace entry kinds, which
the hand-written fixtures hold (#17, #18, #24, #36) because they need a function of their own.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]

PROLOGUE = """function run_case(name, fn)
global vlog_text
vlog_text = '';
try
    fprintf('CHK|%s|%s|exact\\n', name, clean(fn()));
catch err
    fprintf('CHK|%s|ERR %s|exact\\n', name, clean(err.message));
end
end

function s = clean(s)
if isnumeric(s) || islogical(s)
    s = mat2str(s);
end
s = char(s);
s = strrep(s, char(13), '');
s = strrep(s, char(10), ' ');
s = strrep(s, '|', '/');
end

function s = show(x)
% class, size and contents as text; a large array by its first two elements, its last and its sum.
sz = mat2str(size(x));
if isa(x, 'dictionary')
    ks = keys(x);
    parts = cell(1, numel(ks));
    for k = 1:numel(ks)
        parts{k} = sprintf('%s=%s', show(ks(k)), show(x(ks(k))));
    end
    s = sprintf('dictionary %d {%s}', numEntries(x), strjoin(parts, ' '));
elseif isa(x, 'ValueBox')
    s = sprintf('ValueBox p=%s', show(x.p));
elseif isstruct(x)
    if isscalar(x)
        fn = fieldnames(x);
        parts = cell(1, numel(fn));
        for k = 1:numel(fn)
            parts{k} = sprintf('%s=%s', fn{k}, show(x.(fn{k})));
        end
        s = sprintf('struct %s {%s}', sz, strjoin(parts, ' '));
    else
        fn = strjoin(fieldnames(x)', ',');
        if numel(x) <= 8
            parts = arrayfun(@(e) show_fields(e), x, 'UniformOutput', false);
            s = sprintf('struct %s [%s] {%s}', sz, fn, strjoin(parts(:)', ' '));
        else
            s = sprintf('struct %s [%s] {%s %s .. %s}', sz, fn, show_fields(x(1)), show_fields(x(2)), show_fields(x(end)));
        end
    end
elseif iscell(x)
    if numel(x) <= 8
        parts = cellfun(@show, x, 'UniformOutput', false);
        s = sprintf('cell %s {%s}', sz, strjoin(parts(:)', ' '));
    else
        s = sprintf('cell %s {%s %s .. %s}', sz, show(x{1}), show(x{2}), show(x{end}));
    end
elseif isstring(x)
    if numel(x) <= 8
        s = sprintf('string %s {%s}', sz, strjoin(cellstr(x(:)'), ' '));
    else
        s = sprintf('string %s {%s %s .. %s}', sz, x(1), x(2), x(end));
    end
elseif ischar(x)
    if numel(x) <= 16
        s = sprintf('char %s {%s}', sz, x(:)');
    else
        s = sprintf('char %s {%s%s..%s}', sz, x(1), x(2), x(end));
    end
elseif islogical(x)
    if numel(x) <= 8
        s = sprintf('logical %s %s', sz, mat2str(x));
    else
        d = double(x(:));
        s = sprintf('logical %s [%s %s .. %s] sum=%s', sz, mat2str(d(1)), mat2str(d(2)), mat2str(d(end)), mat2str(sum(d)));
    end
elseif isnumeric(x) && ~isreal(x)
    % real and imaginary planes apart: mat2str's spelling of a complex array is its own line (#61)
    if numel(x) <= 8
        s = sprintf('%s %s re=%s im=%s', class(x), sz, mat2str(double(real(x))), mat2str(double(imag(x))));
    else
        d = double(real(x(:))); e = double(imag(x(:)));
        s = sprintf('%s %s re=[%s %s .. %s] sum=%s im=[%s %s .. %s] sum=%s', class(x), sz, ...
            mat2str(d(1)), mat2str(d(2)), mat2str(d(end)), mat2str(sum(d)), ...
            mat2str(e(1)), mat2str(e(2)), mat2str(e(end)), mat2str(sum(e)));
    end
elseif isnumeric(x)
    if numel(x) <= 8
        s = sprintf('%s %s %s', class(x), sz, mat2str(double(x)));
    else
        d = double(x(:));
        s = sprintf('%s %s [%s %s .. %s] sum=%s', class(x), sz, mat2str(d(1)), mat2str(d(2)), mat2str(d(end)), mat2str(sum(d)));
    end
else
    s = sprintf('%s %s', class(x), sz);
end
end

function s = show_fields(e)
fn = fieldnames(e);
parts = cell(1, numel(fn));
for k = 1:numel(fn)
    parts{k} = show(e.(fn{k}));
end
s = strjoin(parts, ',');
end
"""

# ---------------------------------------------------------------------------------------------
# Payload kinds: how to make one of size n, and the write forms it takes (applied to a path T).
# Each form is a list of statement templates with {T} for the target path and {n} for the size.
# ---------------------------------------------------------------------------------------------

def make_payload(kind: str, n: int) -> str:
    return {
        "real": f"v = (1:{n}) * 1;",
        "complex": f"v = (1:{n}) + 1i;",
        "single": f"v = single(1:{n});",
        "int32": f"v = int32(1:{n});",
        "logical": f"v = mod(1:{n}, 2) == 0;",
        "char": f"t0 = repmat('ab', 1, {n}); v = t0(1:{n});",
        "string": f"v = \"s\" + (1:{n});",
        "cell": f"v = num2cell(1:{n});",
        "structarr": f"v = struct('f', num2cell(1:{n}));",
        "struct": f"v = struct('f', 1:{n});",
        "object": f"v = ValueBox(); v.p = 1:{n};",
        "dict": f"v = dictionary(1:{n}, 1:{n});",
    }[kind]


NUMERIC = ["real", "complex", "single", "int32"]
PAYLOADS = NUMERIC + ["logical", "char", "string", "cell", "structarr", "struct", "object", "dict"]

FORMS: dict[str, dict[str, list[str]]] = {
    **{k: {
        "elem": ["{T}(2) = 9;"],
        "elem2d": ["{T}(1, 2) = 9;"],
        "mask": ["m = false(size({T})); m(2) = true; {T}(m) = 9;"],
        "colon": ["{T}(:) = 9;"],
        "grow": ["{T}(end + 1) = 9;"],
        "delete": ["{T}(2) = [];"],
        "loop": ["for k = 1:2, {T}(k) = 9; end"],
    } for k in NUMERIC},
    "logical": {
        "elem": ["{T}(2) = false;"],
        "mask": ["m = false(size({T})); m(2) = true; {T}(m) = false;"],
        "colon": ["{T}(:) = false;"],
        "grow": ["{T}(end + 1) = true;"],
        "delete": ["{T}(2) = [];"],
        "numify": ["{T}(2) = 9;"],
        "loop": ["for k = 1:2, {T}(k) = false; end"],
    },
    "char": {
        "elem": ["{T}(2) = 'z';"],
        "colon": ["{T}(:) = 'z';"],
        "grow": ["{T}(end + 1) = 'z';"],
        "delete": ["{T}(2) = [];"],
    },
    "string": {
        "elem": ["{T}(2) = \"z\";"],
        "grow": ["{T}(end + 1) = \"z\";"],
        "delete": ["{T}(2) = [];"],
        "bracechar": ["{T}{{2}}(1) = 'Q';"],
    },
    "cell": {
        "brace": ["{T}{{2}} = 9;"],
        "bracein": ["{T}{{2}}(1) = 9;"],
        "paren": ["{T}(2) = {{9}};"],
        "delete": ["{T}(2) = [];"],
        "grow": ["{T}{{end + 1}} = 9;"],
    },
    "structarr": {
        "elemfield": ["{T}(2).f = 9;"],
        "elemfieldin": ["{T}(2).f(1) = 9;"],
        "delete": ["{T}(2) = [];"],
        "grow": ["{T}(end + 1).f = 9;"],
        "newfield": ["{T}(2).g = 9;"],
    },
    "struct": {
        "field": ["{T}.f = 9;"],
        "fieldin": ["{T}.f(2) = 9;"],
        "newfield": ["{T}.g = 9;"],
    },
    "object": {
        "prop": ["{T}.p = 9;"],
        "propin": ["{T}.p(2) = 9;"],
    },
    "dict": {
        "key": ["{T}(2) = 9;"],
        "newkey": ["{T}({n} + 1) = 9;"],
        "remove": ["{T}(2) = [];"],
    },
}
FORMS["real"]["complexify"] = ["{T}(2) = 1i;"]
FORMS["real"]["charify"] = ["{T}(2) = 'a';"]
FORMS["single"]["complexify"] = ["{T}(2) = 1i;"]


def form_code(kind: str, form: str, path: str, n: int) -> str:
    return " ".join(FORMS[kind][form]).replace("{T}", path).replace("{n}", str(n)).replace("{{", "{").replace("}}", "}")


# ---------------------------------------------------------------------------------------------
# Entry kinds: how v is stored, how the entry is read back, and the path a write goes through.
# ---------------------------------------------------------------------------------------------

ENTRIES: dict[str, tuple[str, str, str | None]] = {
    "local": ("w = v;", "w", "w"),
    "global": ("global gE_{id}; gE_{id} = v;", "gE_{id}", "gE_{id}"),
    "cellslot": ("c = {v};", "c{1}", "c{1}"),
    "cellassign": ("c = cell(1, 1); c{1} = v;", "c{1}", "c{1}"),
    "field": ("s = struct(); s.f = v;", "s.f", "s.f"),
    "objprop": ("o = ValueBox(); o.p = v;", "o.p", "o.p"),
    "anon": ("f = @() v;", "f()", None),
    "map": ("m0 = containers.Map(); m0('k') = v;", "m0('k')", None),
}


def gen_entry(n: int) -> tuple[str, list[str], str]:
    """The entry matrix: (fixture text, case names, owners text)."""
    calls: list[str] = []
    funcs: list[str] = []
    ident = 0
    for kind in PAYLOADS:
        for entry, (store, read, path) in ENTRIES.items():
            for form in FORMS[kind]:
                ident += 1
                store_ = store.replace("{id}", str(ident))
                read_ = read.replace("{id}", str(ident))
                # (a) write the original after storing it; does the entry see the write?
                name = f"e_{kind}_{entry}_{form}_orig"
                calls.append(name)
                funcs.append(
                    f"function s = {name}()\n{make_payload(kind, n)}\n{store_}\n{form_code(kind, form, 'v', n)}\n"
                    f"s = sprintf('v=%s entry=%s', show(v), show({read_}));\nend\n")
                # (b) write through the entry; does the original see the write?
                if path is not None:
                    path_ = path.replace("{id}", str(ident))
                    name = f"e_{kind}_{entry}_{form}_entry"
                    calls.append(name)
                    funcs.append(
                        f"function s = {name}()\n{make_payload(kind, n)}\n{store_}\n{form_code(kind, form, path_, n)}\n"
                        f"s = sprintf('v=%s entry=%s', show(v), show({read_}));\nend\n")
        # (c) a parameter: the callee writes what it was handed and returns it.
        for form in FORMS[kind]:
            name = f"e_{kind}_param_{form}"
            calls.append(name)
            funcs.append(
                f"function s = {name}()\n{make_payload(kind, n)}\nw = apply_in_callee(v, '{form}:{kind}');\n"
                f"s = sprintf('v=%s entry=%s', show(v), show(w));\nend\n")

    # One callee for every form, switching on form:kind (the same form name means different code
    # for different payloads: elem on a logical writes false); the write lands on the parameter.
    callee = ["function x = apply_in_callee(x, form)", "switch form"]
    for kind in PAYLOADS:
        for form in FORMS[kind]:
            callee.append(f"    case '{form}:{kind}'\n        {form_code(kind, form, 'x', n)}")
    callee.append("end\nend\n")
    header = (
        f"% value_isolation_gen_entry_{n}.m -- GENERATED by tools/parity/gen-value-isolation.py; do not edit.\n"
        f"% The entry matrix of the value-ownership plan (rules M2 and M3) at size {n}: every payload kind\n"
        f"% stored in every entry kind, then written through the original (_orig), through the entry\n"
        f"% (_entry) and inside a callee (param), by every write form the payload takes; each line shows\n"
        f"% the original and the entry afterwards.\n\n")
    body = "".join(f"run_case('{c}', @{c});\n" for c in calls) + "\n" + PROLOGUE + "\n" + "\n".join(callee) + "\n" + "\n".join(funcs)
    return header + body, calls, owners(calls, "V2", ENTRY_OWNER_RULES)


# Owner rules: (case-name prefix, stage). A generated line that fails is stamped pending the first
# rule its name matches, else the axis's default; a stage's commit that finds a line it does not
# own re-owns it here and regenerates, so the sidecars and this table never drift apart.
ENTRY_OWNER_RULES: list[tuple[str, str]] = [
    ("e_dict_", "V6"),                       # dictionary forms the probes found missing (#136, #149)
    # growth, deletion and char writes through a container path are V6's refusals (#63, #90, #91)
    ("e_*_cellslot_grow_entry", "V6"), ("e_*_cellslot_delete_entry", "V6"),
    ("e_*_cellassign_grow_entry", "V6"), ("e_*_cellassign_delete_entry", "V6"),
    ("e_*_field_grow_entry", "V6"), ("e_*_field_delete_entry", "V6"),
    ("e_*_objprop_grow_entry", "V6"), ("e_*_objprop_delete_entry", "V6"),
    ("e_string_*_bracechar_", "V6"),
    # roads through a value object's property that are refused outright today -- a char row, a
    # brace then paren, a struct array's element field, a struct's field -- are V6's
    ("e_char_objprop_", "V6"), ("e_cell_objprop_bracein_", "V6"),
    ("e_structarr_objprop_elemfieldin_", "V6"), ("e_struct_objprop_*_entry", "V6"),
    # writing a numeric into a logical array (`v(2) = 9`, through any entry kind or none) converts
    # the whole array to double where R2025b keeps it logical: a class-coercion defect of the
    # write road, not an aliasing one, so it is V6's "every rebuild keeps what the value is"
    # (#52, #53, and appendix A #157 re-owned by V2's audit), whichever entry the array sits in
    ("e_logical_*_numify_", "V6"), ("e_logical_param_numify", "V6"),
    # a char row written through a container path (`c{1}(2) = 'z'`, `s.f(:) = 'z'`), and a string
    # array's element written through braces inside a callee, are V6's refused roads (#90, #91)
    ("e_char_*_elem_entry", "V6"), ("e_char_*_colon_entry", "V6"), ("e_string_param_bracechar", "V6"),
    # an indexed write into a scalar reached through a container path (`c{2}(1) = 9` where c{2}
    # is one number, `s(2).f(1) = 9` where f is) is refused today ("Cannot assign by index into a
    # number") where R2025b writes: V6's container_path_writes (#82, #83's level-by-level rule)
    ("e_cell_*_bracein_", "V6"), ("e_cell_param_bracein", "V6"),
    ("e_structarr_*_elemfieldin_", "V6"), ("e_structarr_param_elemfieldin", "V6"),
    # a value object's property written through a global is V4 (#16); through a cell slot or a
    # struct field it is refused or replaced by a struct today, which is V6's write-back rule for
    # a value held in a container (#137–#139's shape for value classes)
    ("e_object_global_prop_entry", "V4"),
    ("e_object_cellslot_prop_entry", "V6"), ("e_object_cellassign_prop_entry", "V6"),
    ("e_object_field_prop_entry", "V6"),
    # any other write through a value object's property lands in the instance's fields in place
    # (Interpreter.Objects.cs:179): V1's object detach (M3)
    ("e_*_objprop_*_entry", "V1"),
]


def owners(calls: list[str], default: str, rules: list[tuple[str, str]]) -> str:
    """The sidecar: the axis's default, then every case a rule moves to another stage. A rule is a
    case-name pattern with * standing for one name segment (the payload or entry kind)."""
    import fnmatch
    lines = [f"*\t{default}"]
    for name in calls:
        for pattern, stage in rules:
            matched = name.startswith(pattern) if "*" not in pattern else fnmatch.fnmatchcase(name, pattern + "*")
            if matched:
                # A rule claims the line and stops the search. One naming the axis default keeps the
                # line at the default (no sidecar entry) while shielding it from a broader rule below.
                if stage != default:
                    lines.append(f"{name}\t{stage}")
                break
    return "\n".join(lines) + "\n"


# ---------------------------------------------------------------------------------------------
# Scope kinds: a global of each payload kind held while a call writes it.
# ---------------------------------------------------------------------------------------------

def first_write(kind: str, path: str) -> str:
    """The write a scope's callee makes into the global: the payload's third element becomes 9 (the
    third, so that a loop source shared with the global would show it on a later iteration)."""
    return {
        "real": f"{path}(3) = 9;", "complex": f"{path}(3) = 9;", "single": f"{path}(3) = 9;",
        "int32": f"{path}(3) = 9;", "logical": f"{path}(3) = true;", "char": f"{path}(3) = 'z';",
        "string": f"{path}(3) = \"z\";", "cell": f"{path}{{3}} = 9;", "structarr": f"{path}(3).f = 9;",
        "struct": f"{path}.f(3) = 9;", "object": f"{path}.p(3) = 9;", "dict": f"{path}(3) = 9;",
    }[kind]


SCOPES: dict[str, tuple[str, list[str]]] = {
    # name: (expression template using G for the global and B() for the writing call, payload kinds)
    "binary": ("r = G + B();", NUMERIC + ["logical"]),
    "bracket": ("r = [G, B()];", NUMERIC + ["logical", "string"]),
    "cell_literal": ("r = {G, B()}; r = r{1};", PAYLOADS),
    "builtin_arg": ("[r, ~] = deal(G, B());", PAYLOADS),
    "user_arg": ("r = pass_first(G, B());", PAYLOADS),
    "method_arg": ("rd = ValueReader(); r = rd.keep(G, B());", PAYLOADS),
    "handle_arg": ("h = @pass_first; r = h(G, B());", PAYLOADS),
    "feval_arg": ("r = feval(@pass_first, G, B());", PAYLOADS),
    "wholecall_cellfun": ("r = cellfun(@(x) bump_then(x, BH), {G}, 'UniformOutput', false); r = r{1};", PAYLOADS),
    "loop_source": ("r = {}; for e = G, r{end + 1} = e; B(); end", NUMERIC + ["logical", "char", "string", "cell", "structarr"]),
    "index_target": ("r = G(BI());", NUMERIC + ["logical", "char", "string", "cell", "structarr"]),
}


def gen_scope(n: int) -> tuple[str, list[str], str]:
    calls: list[str] = []
    funcs: list[str] = []
    for kind in PAYLOADS:
        g = f"gS_{kind}"
        # the writer: writes the global's third element and answers 0; the index writer answers 3
        funcs.append(f"function z = bump_{kind}()\nglobal {g}\n{first_write(kind, g)}\nz = 0;\nend\n")
        funcs.append(f"function k = bumpi_{kind}()\nglobal {g}\n{first_write(kind, g)}\nk = 3;\nend\n")
        for scope, (template, kinds) in SCOPES.items():
            if kind not in kinds:
                continue
            name = f"s_{scope}_{kind}"
            calls.append(name)
            expr = template.replace("G", g).replace("BI()", f"bumpi_{kind}()").replace("BH", f"@bump_{kind}").replace("B()", f"bump_{kind}()")
            funcs.append(
                f"function s = {name}()\nglobal {g}\n{make_payload(kind, n)}\n{g} = v;\n{expr}\n"
                f"s = sprintf('held=%s global=%s', show(r), show({g}));\nend\n")
    helpers = (
        "function a = pass_first(a, ~)\nend\n\n"
        "function r = bump_then(x, b)\nb();\nr = x;\nend\n")
    header = (
        f"% value_isolation_gen_scope_{n}.m -- GENERATED by tools/parity/gen-value-isolation.py; do not edit.\n"
        f"% The scope matrix of the value-ownership plan (rule M5) at size {n}: a global of every payload\n"
        f"% kind held by every scope kind -- an operand, a literal, a builtin's, a user function's, a\n"
        f"% method's, a handle's and feval's argument, a whole-call builtin, a loop's source, an index\n"
        f"% target -- while a call writes the global's first element; each line shows what the scope\n"
        f"% held and the global afterwards.\n\n")
    body = "".join(f"run_case('{c}', @{c});\n" for c in calls) + "\n" + PROLOGUE + "\n" + helpers + "\n" + "\n".join(funcs)
    return header + body, calls, owners(calls, "V3", SCOPE_OWNER_RULES)


SCOPE_OWNER_RULES: list[tuple[str, str]] = [
    ("s_index_target_dict", "V6"),
]


# ---------------------------------------------------------------------------------------------
# Overlap (M10): the right-hand side, a subscript, or both are the target's own storage.
# ---------------------------------------------------------------------------------------------

OVERLAP_TYPES: dict[str, str] = {
    "double": "[3 1 2]",
    "single": "single([3 1 2])",
    "int8": "int8([3 1 2])",
    "logical": "[true false true]",
    "char": "'cab'",
    "string": "[\"c\" \"a\" \"b\"]",
    "complex": "[3+1i 1 2]",
    "cell": "{3, 'a', 2}",
    "structarr": "struct('f', {3, 1, 2})",
}

OVERLAP_CASES: dict[str, tuple[str, list[str]]] = {
    # name: (write with {T} the target, element types it applies to)
    "permute_rhs_same": ("{T}([2 3 1]) = {T};", list(OVERLAP_TYPES)),
    "permute_rhs_fresh": ("{T}([2 3 1]) = {T}([1 2 3]);", list(OVERLAP_TYPES)),
    "shift_rhs_fresh": ("{T}(2:3) = {T}(1:2);", list(OVERLAP_TYPES)),
    "grow_rhs_same": ("{T}(4:6) = {T};", list(OVERLAP_TYPES)),
    "grow_end_rhs_same": ("{T}(end + 1:end + 3) = {T};", list(OVERLAP_TYPES)),
    "sub_same": ("{T}({T}) = 9;", ["double", "single", "int8"]),
    "sub_same_mask": ("{T}({T}) = false;", ["logical"]),
    "both_same": ("{T}({T}) = {T};", ["double", "single", "int8"]),
    "sub_same_promote": ("{T}({T}) = 1i;", ["double", "single"]),
    "sub_same_demote": ("{T}({T}) = 'a';", ["double"]),
    "both_same_grow": ("{T}({T} + 3) = {T};", ["double", "single", "int8"]),
}

OVERLAP_TARGETS: dict[str, tuple[str, str, str]] = {
    # name: (setup with {V} the value, the target path, the read path)
    "variable": ("x = {V};", "x", "x"),
    "field": ("st = struct(); st.v = {V};", "st.v", "st.v"),
    "cellelem": ("c = {{V}};", "c{1}", "c{1}"),
    "global": ("global gO_{id}; gO_{id} = {V};", "gO_{id}", "gO_{id}"),
    "persistent": ("persistent p; p = {V};", "p", "p"),
}


def gen_overlap() -> tuple[str, list[str], str]:
    calls: list[str] = []
    funcs: list[str] = []
    ident = 0
    for case, (write, types) in OVERLAP_CASES.items():
        for etype in types:
            for target, (setup, path, read) in OVERLAP_TARGETS.items():
                ident += 1
                name = f"o_{case}_{etype}_{target}"
                calls.append(name)
                setup_ = setup.replace("{V}", OVERLAP_TYPES[etype]).replace("{id}", str(ident))
                path_ = path.replace("{id}", str(ident))
                read_ = read.replace("{id}", str(ident))
                funcs.append(
                    f"function s = {name}()\n{setup_}\n{write.replace('{T}', path_)}\ns = show({read_});\nend\n")
    header = (
        "% value_isolation_gen_overlap.m -- GENERATED by tools/parity/gen-value-isolation.py; do not edit.\n"
        "% The overlap matrix of the value-ownership plan (rule M10): an indexed write whose right-hand\n"
        "% side, subscript, or both are the target's own storage -- permuted, shifted, grown from itself,\n"
        "% promoted to complex or demoted to char through a self-subscript -- for every element type and\n"
        "% every target kind (a variable, a field, a cell element, a global, a persistent).\n\n")
    body = "".join(f"run_case('{c}', @{c});\n" for c in calls) + "\n" + PROLOGUE + "\n" + "\n".join(funcs)
    return header + body, calls, owners(calls, "V3", OVERLAP_OWNER_RULES)


OVERLAP_OWNER_RULES: list[tuple[str, str]] = [
    # a char row written through a field or a cell element is V6's refusal (#60, #92), and so is
    # growth through either (#90) and a struct array's element write through either
    ("o_*_char_field", "V6"), ("o_*_char_cellelem", "V6"),
    ("o_grow_*_field", "V6"), ("o_grow_*_cellelem", "V6"),
    ("o_both_same_grow_*_field", "V6"), ("o_both_same_grow_*_cellelem", "V6"),
    ("o_*_structarr_field", "V6"), ("o_*_structarr_cellelem", "V6"),
]


# ---------------------------------------------------------------------------------------------
# Refusal (M14): a refused write leaves the target and its aliases whole.
# ---------------------------------------------------------------------------------------------

REFUSAL_FORMS: dict[str, tuple[str, dict[str, str]]] = {
    # form: (value, {failure kind: write with {T}}); every write must be refused by MATLAB
    "range": ("[1 2]", {"count": "{T}(4:5) = [7 8 9];", "subscript": "{T}(0) = 7;", "convert": "{T}(2) = {5};"}),
    "range2d": ("[1 2; 3 4]", {"count": "{T}(3:4, 1) = [7 8 9];", "subscript": "{T}(0, 1) = 7;", "convert": "{T}(1, 2) = {5};"}),
    "mask": ("[1 2 3]", {"count": "{T}(logical([1 0 1])) = [7 8 9];", "convert": "{T}(logical([1 0 1])) = {5};"}),
    "cellparen": ("{1, 2}", {"count": "{T}(4:5) = {7, 8, 9};", "subscript": "{T}(0) = {7};", "convert": "{T}(4:5) = 7;"}),
    "cellbrace": ("{1, 2}", {"count": "[{T}{4:5}] = deal(7, 8, 9);", "subscript": "{T}{0} = 7;", "multi": "{T}{[1 2]} = 7;"}),
    "structelem": ("struct('a', {1, 2})", {"count": "{T}(4:6) = {T}(1:2);", "subscript": "{T}(0) = {T}(1);", "convert": "{T}(3) = 7;"}),
    "stringelem": ("[\"a\" \"b\"]", {"count": "{T}(4:5) = [\"x\" \"y\" \"z\"];", "subscript": "{T}(0) = \"x\";", "convert": "{T}(2) = {5};"}),
}

REFUSAL_TARGETS: dict[str, tuple[str, str, list[str]]] = {
    # target: (setup with {V}, the path, the reads printed afterwards)
    "variable": ("x = {V};", "x", ["x"]),
    "alias": ("x = {V}; y = x;", "x", ["x", "y"]),
    "field": ("st = struct(); st.v = {V}; t = st;", "st.v", ["st.v", "t.v"]),
    "cellelem": ("c = {{V}}; d = c;", "c{1}", ["c{1}", "d{1}"]),
    "global": ("global gR_{id}; gR_{id} = {V}; y = gR_{id};", "gR_{id}", ["gR_{id}", "y"]),
}


def gen_refusal() -> tuple[str, list[str], str]:
    calls: list[str] = []
    funcs: list[str] = []
    ident = 0
    for form, (value, failures) in REFUSAL_FORMS.items():
        for failure, write in failures.items():
            for target, (setup, path, reads) in REFUSAL_TARGETS.items():
                ident += 1
                name = f"r_{form}_{failure}_{target}"
                calls.append(name)
                setup_ = setup.replace("{V}", value).replace("{id}", str(ident))
                path_ = path.replace("{id}", str(ident))
                reads_ = [r.replace("{id}", str(ident)) for r in reads]
                shows = " ".join(f"{r}=%s" for r in reads_)
                args = ", ".join(f"show({r})" for r in reads_)
                funcs.append(
                    f"function s = {name}()\n{setup_}\nrefused = 0;\ntry\n    {write.replace('{T}', path_)}\ncatch\n    refused = 1;\nend\n"
                    f"s = sprintf('refused=%d {shows}', refused, {args});\nend\n")
    header = (
        "% value_isolation_gen_refusal.m -- GENERATED by tools/parity/gen-value-isolation.py; do not edit.\n"
        "% The refusal matrix of the value-ownership plan (rule M14): every write form failing on a count\n"
        "% mismatch, a bad subscript or an unconvertible value, on a variable, an alias, a field, a cell\n"
        "% element and a global; each line shows whether the write was refused and what the target and\n"
        "% its aliases hold afterwards. Growth past an allocation limit is left out on purpose.\n\n")
    body = "".join(f"run_case('{c}', @{c});\n" for c in calls) + "\n" + PROLOGUE + "\n" + "\n".join(funcs)
    return header + body, calls, owners(calls, "V3", [])


# ---------------------------------------------------------------------------------------------
# Multiple assignment (M11): an earlier target aliases a later output.
# ---------------------------------------------------------------------------------------------

MULTI_PAYLOADS: dict[str, tuple[str, str, str]] = {
    # kind: (value, the element write used as the earlier target on v, what 7 becomes)
    "real": ("[1 2 3]", "v(1)", "7"),
    "cell": ("{1, 2, 3}", "v{1}", "7"),
    "string": ("[\"a\" \"b\" \"c\"]", "v(1)", "\"z\""),
    "structarr": ("struct('f', {1, 2, 3})", "v(1).f", "7"),
}

MULTI_FORMS: dict[str, str] = {
    # form: statement with {L} the earlier target, {V} the value name, {S} the seven
    "deal": "[{L}, b] = deal({S}, v);",
    "named": "[{L}, b] = two_out({S}, v);",
    "varargout": "[{L}, b] = va_out({S}, v);",
    "cslist": "C = {{{S}, v}}; [{L}, b] = C{{:}};",
    "fieldlist": "st = struct('f', {{{S}, v}}); [{L}, b] = st.f;",
    "three": "[{L}, b, c] = deal({S}, v, v);",
}


def gen_multi() -> tuple[str, list[str], str]:
    calls: list[str] = []
    funcs: list[str] = []
    for kind, (value, left, seven) in MULTI_PAYLOADS.items():
        for form, stmt in MULTI_FORMS.items():
            name = f"m_{form}_{kind}"
            calls.append(name)
            body = stmt.replace("{L}", left).replace("{S}", seven).replace("{{", "{").replace("}}", "}")
            show_c = " c=%s" if form == "three" else ""
            arg_c = ", show(c)" if form == "three" else ""
            funcs.append(
                f"function s = {name}()\nv = {value};\n{body}\ns = sprintf('v=%s b=%s{show_c}', show(v), show(b){arg_c});\nend\n")
        # the later output aliases the earlier target through a container too
        name = f"m_deal_container_{kind}"
        calls.append(name)
        funcs.append(
            f"function s = {name}()\nv = {value};\nc = {{v}};\n[c{{1}}, b] = deal({seven}, c{{1}});\n"
            f"s = sprintf('c1=%s b=%s', show(c{{1}}), show(b));\nend\n")
    helpers = (
        "function [a, b] = two_out(a, b)\nend\n\n"
        "function varargout = va_out(varargin)\nvarargout = varargin;\nend\n")
    header = (
        "% value_isolation_gen_multi.m -- GENERATED by tools/parity/gen-value-isolation.py; do not edit.\n"
        "% The multiple-assignment matrix of the value-ownership plan (rule M11): every output form --\n"
        "% deal, named outputs, varargout, a cell's and a struct array's comma list, three outputs --\n"
        "% whose first target is an element of the value a later output is bound to, for a numeric, a\n"
        "% cell, a string and a struct array, and through a cell element as the target.\n\n")
    body = "".join(f"run_case('{c}', @{c});\n" for c in calls) + "\n" + PROLOGUE + "\n" + helpers + "\n" + "\n".join(funcs)
    # deal of a string scalar into a cell element lands as a char: a form defect, not an order one
    return header + body, calls, owners(calls, "V3", [("m_deal_container_string", "V6")])


# ---------------------------------------------------------------------------------------------
# Order (M15/M16): right-hand side, subscripts and end by target shape, each part with a side effect.
# ---------------------------------------------------------------------------------------------

ORDER_SHAPES: dict[str, tuple[str, str, str]] = {
    # shape: (setup of the global target, the write with {I} the subscript and {R} the right-hand side, the read)
    "paren": ("gT = [1 2 3];", "gT({I}) = {R};", "gT"),
    "paren2d": ("gT = [1 2 3; 4 5 6];", "gT(1, {I}) = {R};", "gT"),
    "brace": ("gT = {1, 2, 3};", "gT{{{I}}} = {R};", "gT"),
    "field_paren": ("gT = struct('f', [1 2 3]);", "gT.f({I}) = {R};", "gT.f"),
    "brace_paren": ("gT = {[1 2 3]};", "gT{{1}}({I}) = {R};", "gT{1}"),
    "paren_field": ("gT = struct('f', {1, 2, 3});", "gT({I}).f = {R};", "gT"),
    "field_field_paren": ("gT = struct('a', struct('b', [1 2 3]));", "gT.a.b({I}) = {R};", "gT.a.b"),
    "dynfield": ("gT = struct('f', 1);", "gT.({N}) = {R};", "gT"),
}

ORDER_EFFECTS = ["plain", "grow", "shrink", "rebind", "clear"]


def order_effect_code(shape: str, effect: str) -> str:
    """The side effect a subscript or right-hand side function has on the global target gT."""
    if effect == "plain":
        return ""
    if effect == "clear":
        return "clear global gT"
    grows = {
        "paren": "gT(end + 1) = 50;", "paren2d": "gT(:, end + 1) = [50; 60];", "brace": "gT{end + 1} = 50;",
        "field_paren": "gT.f(end + 1) = 50;", "brace_paren": "gT{1}(end + 1) = 50;",
        "paren_field": "gT(end + 1).f = 50;", "field_field_paren": "gT.a.b(end + 1) = 50;",
        "dynfield": "gT.h = 50;",
    }
    shrinks = {
        "paren": "gT(end) = [];", "paren2d": "gT(:, end) = [];", "brace": "gT(end) = [];",
        "field_paren": "gT.f(end) = [];", "brace_paren": "gT{1}(end) = [];",
        "paren_field": "gT(end) = [];", "field_field_paren": "gT.a.b(end) = [];",
        "dynfield": "gT = rmfield(gT, 'f');",
    }
    rebinds = {
        "paren": "gT = [7 7 7 7];", "paren2d": "gT = [7 7 7 7; 7 7 7 7];", "brace": "gT = {7, 7, 7, 7};",
        "field_paren": "gT = struct('f', [7 7 7 7]);", "brace_paren": "gT = {[7 7 7 7]};",
        "paren_field": "gT = struct('f', {7, 7, 7, 7});", "field_field_paren": "gT = struct('a', struct('b', [7 7 7 7]));",
        "dynfield": "gT = struct('f', 7);",
    }
    return {"grow": grows, "shrink": shrinks, "rebind": rebinds}[effect][shape]


def gen_order() -> tuple[str, list[str], str]:
    calls: list[str] = []
    funcs: list[str] = []
    # per shape and effect: a subscript function (answers 2, or 'g' for a dynamic name) and a
    # right-hand-side function (answers 9), each logging and applying the effect to gT
    for shape in ORDER_SHAPES:
        for effect in ORDER_EFFECTS:
            code = order_effect_code(shape, effect)
            ans = "'g'" if shape == "dynfield" else "2"
            funcs.append(f"function k = idx_{shape}_{effect}()\nglobal gT\nvlog('idx');\n{code}\nk = {ans};\nend\n")
            funcs.append(f"function v = rhs_{shape}_{effect}()\nglobal gT\nvlog('rhs');\n{code}\nv = 9;\nend\n")
    for shape, (setup, write, read) in ORDER_SHAPES.items():
        for sub_effect in ORDER_EFFECTS:
            for rhs_effect in ORDER_EFFECTS:
                name = f"w_{shape}_sub_{sub_effect}_rhs_{rhs_effect}"
                calls.append(name)
                w = write.replace("{I}", f"idx_{shape}_{sub_effect}()").replace("{N}", f"idx_{shape}_{sub_effect}()").replace("{R}", f"rhs_{shape}_{rhs_effect}()")
                w = w.replace("{{", "{").replace("}}", "}")
                funcs.append(
                    f"function s = {name}()\nglobal gT\n{setup}\n{w}\ns = logged(show({read}));\nend\n")
        # end taken against the target as it is at that moment, with the right-hand side acting
        if shape != "dynfield":
            for rhs_effect in ORDER_EFFECTS:
                if rhs_effect == "plain":
                    continue
                name = f"w_{shape}_end_rhs_{rhs_effect}"
                calls.append(name)
                w = write.replace("{I}", "end").replace("{R}", f"rhs_{shape}_{rhs_effect}()").replace("{{", "{").replace("}}", "}")
                funcs.append(
                    f"function s = {name}()\nglobal gT\n{setup}\n{w}\ns = logged(show({read}));\nend\n")
    helpers = (
        "function s = logged(v)\nglobal vlog_text\ns = sprintf('%s / %s', vlog_text, v);\nend\n")
    header = (
        "% value_isolation_gen_order.m -- GENERATED by tools/parity/gen-value-isolation.py; do not edit.\n"
        "% The order matrix of the value-ownership plan (rules M15 and M16): for every target shape -- a\n"
        "% paren index on a variable, a 2-D one, a brace, a field then paren, a brace then paren, a paren\n"
        "% then field, two fields then paren, a dynamic field -- a subscript function and a right-hand-side\n"
        "% function that each log their call and grow, shrink, rebind or clear the global target, in every\n"
        "% combination, plus end against a right-hand side that acts; each line shows the call log and the\n"
        "% target afterwards.\n\n")
    body = "".join(f"run_case('{c}', @{c});\n" for c in calls) + "\n" + PROLOGUE + "\n" + helpers + "\n" + "\n".join(funcs)
    return header + body, calls, owners(calls, "V3", [])


# ---------------------------------------------------------------------------------------------
# Builtins (M8): value-transforming builtins never mutate their argument.
# ---------------------------------------------------------------------------------------------

BUILTIN_CASES: dict[str, tuple[str, str, str, str]] = {
    # name: (setup, the call as an expression, a failing call, the read of the source)
    "insert": ("d = dictionary(1, 10);", "insert(d, 1, 20)", "insert(d, [1 2], 20)", "d"),
    "remove": ("d = dictionary([1 2], [10 20]);", "remove(d, 2)", "remove(d, {1})", "d"),
    "setfield": ("st = struct('f', 1, 'g', 2);", "setfield(st, 'f', 7)", "setfield(st, 3, 7)", "st"),
    "rmfield": ("st = struct('f', 1, 'g', 2);", "rmfield(st, 'g')", "rmfield(st, 'zz')", "st"),
    "orderfields": ("st = struct('g', 2, 'f', 1);", "orderfields(st)", "orderfields(st, {'f'})", "st"),
    "horzcat": ("st = struct('f', 1);", "[st, st]", "[st, struct('q', 1)]", "st"),
    "sort_cell": ("c = {'b', 'a'};", "sort(c)", "sort(c, 3)", "c"),
    "strrep_cell": ("c = {'aa', 'ba'};", "strrep(c, 'a', 'x')", "strrep(c, 'a')", "c"),
    "unique": ("x = [3 1 2 1];", "unique(x)", "unique(x, 'nosuch')", "x"),
    "num2cell": ("x = [1 2 3];", "num2cell(x)", "num2cell(x, 3)", "x"),
}

HANDLE_CONTROLS: dict[str, tuple[str, str, str]] = {
    # name: (setup, the mutating call, the read afterwards) -- these MUST mutate, as in MATLAB
    "map_remove": ("m = containers.Map({'a', 'b'}, {1, 2});", "remove(m, 'b');", "m.Count"),
    "handle_method": ("h = HandleHolder(); h.data = [1 2 3];", "h.bump();", "h.data"),
    "map_assign": ("m = containers.Map(); m('k') = 1;", "m('k') = 2;", "m('k')"),
}


def gen_builtins() -> tuple[str, list[str], str]:
    calls: list[str] = []
    funcs: list[str] = []
    for name, (setup, call, failing, read) in BUILTIN_CASES.items():
        for mode in ("discarded", "kept", "failing"):
            case = f"b_{name}_{mode}"
            calls.append(case)
            if mode == "discarded":
                stmt = f"{call};"
            elif mode == "kept":
                stmt = f"r = {call};"
            else:
                stmt = f"try\n    r = {failing};\ncatch\nend"
            funcs.append(f"function s = {case}()\n{setup}\n{stmt}\ns = show({read});\nend\n")
    for name, (setup, call, read) in HANDLE_CONTROLS.items():
        case = f"b_control_{name}"
        calls.append(case)
        funcs.append(f"function s = {case}()\n{setup}\n{call}\ns = show({read});\nend\n")
    header = (
        "% value_isolation_gen_builtins.m -- GENERATED by tools/parity/gen-value-isolation.py; do not edit.\n"
        "% The builtin matrix of the value-ownership plan (rule M8): every value-transforming builtin with\n"
        "% its result discarded, kept and failing midway, showing the source afterwards; and the handle-type\n"
        "% controls (a containers.Map verb, a handle method) that must still mutate.\n\n")
    body = "".join(f"run_case('{c}', @{c});\n" for c in calls) + "\n" + PROLOGUE + "\n" + "\n".join(funcs)
    # Count is a uint64 in MATLAB and a double here: a class of the answer, not a mutation
    return header + body, calls, owners(calls, "V3", [("b_control_map_remove", "V6")])


# ---------------------------------------------------------------------------------------------

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=REPO / "tests/JGraph.Tests/MatlabParity/fixtures")
    parser.add_argument("--ceiling", type=int, default=2000)
    args = parser.parse_args()

    fixtures: dict[str, tuple[str, list[str], str]] = {}
    for n in (5, 5000):
        fixtures[f"value_isolation_gen_entry_{n}"] = gen_entry(n)
        fixtures[f"value_isolation_gen_scope_{n}"] = gen_scope(n)
    fixtures["value_isolation_gen_overlap"] = gen_overlap()
    fixtures["value_isolation_gen_refusal"] = gen_refusal()
    fixtures["value_isolation_gen_multi"] = gen_multi()
    fixtures["value_isolation_gen_order"] = gen_order()
    fixtures["value_isolation_gen_builtins"] = gen_builtins()

    over = False
    total = 0
    for name, (text, calls, owners) in fixtures.items():
        if len(set(calls)) != len(calls):
            dupes = sorted({c for c in calls if calls.count(c) > 1})
            print(f"{name}: duplicate case names {dupes[:5]}")
            return 1
        (args.out / f"{name}.m").write_text(text.replace("\r\n", "\n").replace("\n", "\r\n"), encoding="ascii", newline="")
        (args.out / f"{name}.owners").write_text(
            f"# GENERATED by tools/parity/gen-value-isolation.py: the axis's default owning stage.\r\n{owners}".replace("\n", "\r\n").replace("\r\r", "\r"),
            encoding="ascii", newline="")
        flag = "" if len(calls) <= args.ceiling else "  OVER THE CEILING"
        over = over or len(calls) > args.ceiling
        total += len(calls)
        print(f"  {name:<36} {len(calls):5d} lines{flag}")
    print(f"  {'total':<36} {total:5d} lines (ceiling {args.ceiling} per fixture)")
    return 1 if over else 0


if __name__ == "__main__":
    sys.exit(main())
