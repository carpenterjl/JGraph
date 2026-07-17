# ADR 0011: Tabular data model and import

- Status: Accepted
- Date: 2026-07-16

## Context

Until now every plot was fed raw `double[]` arrays; there was no way to load data from a file. Milestone
10 adds data import (the first step toward the larger data-and-scripting feature): read CSV/TSV, Excel
`.xlsx`, and pasted clipboard tables, and turn columns into plots — both programmatically and through a
GUI wizard. This needs a tabular data model, robust parsers, and a UI that maps columns onto plot types.

## Decision

1. **A new `Core`-only leaf project, `JGraph.Data`.** It references `JGraph.Core` only (like
   `JGraph.Signal` and `JGraph.Plugins`) and is BCL-only — no NuGet packages. It holds the table model,
   all readers, type inference, and the UI-free import-wizard state model. New one-way dependency edges:
   `Objects → Data`, `Api → Data`, and `Application → Data`.

2. **An immutable, column-oriented `Table` — not a `GraphObject`.** A table is a data *source* like
   `ArrayDataSeries`, not part of the plot tree; only the plots built from it enter the model. Columns
   are typed (`NumberColumn`, `DateTimeColumn`, `TextColumn`), with **NaN = missing** for numbers (the
   renderer already gaps on non-finite values), **OLE automation dates** for date columns (the existing
   `DateTimeAxis` convention, so a date column plots straight onto a date axis), and a first-appearance
   **category list** for text columns (which plugs into `AxisModel.UseCategories`). A column pair becomes
   a plot series through `TableSeries`, sharing the column's backing array with zero copy for number and
   date columns.

3. **Deterministic auto-detection with explicit overrides.** `DelimitedTextReader` detects the delimiter
   (scoring `, ; \t |` by field-count consistency), the header row (a label sitting over a
   numeric/date column body), and the number culture (invariant vs. comma-decimal, the latter only when
   the delimiter is not a comma). Each is overridable through `ImportOptions`. Numbers are tried before
   dates during type inference so integer columns never become dates. Parsing is RFC 4180-aware (quoted
   fields, doubled-quote escapes, embedded delimiters/newlines). Recoverable issues (ragged rows, error
   cells) become `ImportResult.Warnings`; only hard failures throw `ImportException`.

4. **A hand-rolled xlsx reader over the BCL.** `XlsxReader` opens the workbook ZIP with
   `System.IO.Compression` and parses its parts with `System.Xml.Linq`, reading shared/inline strings,
   numbers, booleans, and date-formatted numbers (recognised from the cell's number format). It uses each
   cell's cached value — **no formula evaluation, no styling beyond date detection, no merged cells**.
   Date cells are rendered as ISO-8601 strings so the shared type-inference path recognises them, keeping
   the xlsx and delimited paths unified. Excel 1900 serials are treated as OLE automation dates, which is
   exact for dates on or after 1900-03-01 (the Feb-1900 leap-year quirk is documented, not corrected).
   This keeps the zero-dependency discipline; the named fallback, had it proven infeasible, was ClosedXML.

5. **The import wizard's logic lives in a UI-free model.** `ImportWizardModel` (in `JGraph.Data`) owns
   source loading, re-parsing on option changes, the column→role mapping, the rules for which plot kinds
   a mapping allows, and whether a plot can be built. `TablePlotBuilder` (in `JGraph.Objects`) turns a
   `TablePlotSpec` into plots. Both are net8.0 and fully unit-tested. The WPF pieces —
   `ImportWizardWindow` and `DataImportService` in `JGraph.Application` — are a thin view and a dialog
   host, mirroring the existing Export/Open/Save services; nothing was added to `JGraph.Controls`.

## Consequences

- Data can be loaded without touching the plot types: `Table.ReadCsv`/`ReadXlsx`, `JG.ReadTable` +
  table-aware `JG.Plot(table, "x", "y")`/`axes.AddLine(table, "x", "y")`, and the **Import Data…** wizard
  all funnel through the same `Table` and the same `TablePlotBuilder`. Scripts (a later milestone) will
  reuse this surface unchanged.
- The `Objects → Data` edge is a new, permanent, one-way dependency: "objects" now means plots plus the
  fluent API over both arrays and tables. This is deliberate and recorded here.
- Readers materialise the whole table in memory (consistent with `ArrayDataSeries`); streaming/chunked
  import for very large files is out of scope.
- The xlsx subset is intentionally small. Formulas surface as their cached values, unusual number formats
  may not be recognised as dates, and styling/merges are ignored — acceptable for importing data, and
  extendable later without changing the public surface.
