# ADR 0215 — Language forms R2025b reads differently: open items 15, 17, 21, 23, 28, 30, 53 and 73

## Status

Accepted, 2026-10-08. The fourth batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored). Each item is a form of the language that the MATLAB dialect parsed or evaluated
differently from R2025b. Every answer was recorded from R2025b (probe `probe_b5` in the open-items
scratch, then the fixture `oi_language_forms`).

## Context

These forms are each small, but a ported script meets them early. Command syntax handed a string
where MATLAB hands a char vector. `@name` failed on its own line for a name nothing answered. An
unsuppressed `[a, b] = f()` printed nothing. `0x1F` did not parse. A list written as a statement
showed nothing. A function named `e` could not be called, because `e` was Euler's number.

## Decision

All of it is the MATLAB dialect's. JGS keeps `e`, its own literals and its own echo.

### Command syntax (item 15)

Each word of a command (`class abc`, `upper abc`) is a char vector, as R2025b passes it. The words
had been string scalars, so `class abc` answered `'string'`.

### A handle to a name nothing answers (items 17 and 73)

`@no_such_fn` makes a handle. The name is resolved at the call. A call with no arguments is
`MATLAB:UndefinedFunction` "Unrecognized function or variable 'no_such_fn'.", and a call with
arguments is "Undefined function 'no_such_fn' for input arguments of type 'double'.". Two such handles
to the same name are `isequal`, and `functions` reports type `simple` with an empty file. The handle's
target is an `IJgsUnanswered` stand-in (`UndefinedFunction` implements it). The stand-in compares by
name (`NamedHandle.SameAs`).

### The echo of a multiple assignment (item 21)

An unsuppressed `[a, b] = deal(1, 2)` shows each target in order. A `~` shows nothing. An indexed
target (`st.x`, `cc{2}`) shows its variable whole. A `[c{:}] = …` target is one echo of `c`, and that
target now fills every slot of `c`, where it had filled one.

### Hexadecimal and binary literals (item 23)

The lexer reads `0x`/`0X` and `0b`/`0B` literals, with the suffixes `u8`…`u64` and `s8`…`s64`. Without
a suffix the class is the smallest unsigned integer that holds the value (`0x1F` is `uint8`,
`0x1FFFF` is `uint32`). With an `s` suffix the bits are read as two's complement (`0x80s8` is -128).
The class rides on the `NumberLiteral` (`IntegerClass`). The loop compiler refuses a classed literal,
so it runs through the interpreter and keeps its class. Integer arithmetic follows from the class:
`-0x1F` saturates to `uint8` 0.

### An anonymous function called for nothing (item 28)

`a = @() max([1 2]); a()` asks the body for one output and echoes `ans`, as R2025b does. A body
that returns nothing (`@() disp(5)`) still answers nothing.

### A list written as a statement (item 30)

A bare `s.f` on a struct array, or `c{:}`, shows each value as `ans` and leaves `ans` holding the
last. A list given to an operator (`s.f + 1`) or to a function that takes one argument (`disp(s.f)`)
is `MATLAB:maxrhs` "Too many input arguments.".

### `e` (item 53)

The MATLAB dialect has no `e`. A bare `e` is `MATLAB:UndefinedFunction` "Unrecognized function or
variable 'e'.". A function, method or file named `e` is called, and `e(2)` with an `e.m` on the path
calls it. `e` is a builtin that refuses itself (`AutoCallsBare`), so every name on the path ranks
ahead of it. The shadowing warning (`JgsFunctionPath.WarnShadowing`) skips it.

## Measured

- `oi_language_forms`: 39 lines, all exact.

## Divergences

- A literal past 2^53 (`0xFFFFFFFFFFFFFFFFu64`, `0x20000000000001`) keeps its class and loses its low
  bits. JGraph holds integers in doubles (ADR 0069), and R2025b keeps every bit.

## Consequences

Scripts that use command syntax, late-bound handles, hexadecimal masks, a function named `e`, or
listing a struct field at the prompt now behave as they do in R2025b. A MATLAB script that used `e`
for Euler's number fails, as it does in MATLAB, and `exp(1)` is the spelling.
