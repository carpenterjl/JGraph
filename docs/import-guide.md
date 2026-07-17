# Importing and graphing data — a walkthrough

This guide walks through getting a data file onto a graph in JGraph three ways: the **Import Data…**
wizard in the figure window, a **script** in the in-app editor, and the **API** from your own code. It
uses [`examples/sample-measurement.csv`](../examples/sample-measurement.csv) — 200 rows of
`time,voltage,current` with ISO-8601 timestamps — as the running example.

---

## 1. The Import Data… wizard (GUI)

Launch the figure window and click **Import Data…** on the toolbar:

```sh
dotnet run --project src/JGraph.Application
```

The wizard is two pages: **Source & preview**, then **Columns & plot**. **Next** advances, **Back**
returns, **Cancel** closes without changing your figure.

### Page 1 — Source & preview

Choose where the data comes from:

- **Browse…** — pick a `.csv`, `.tsv`, `.txt`, or `.xlsx` file.
- **Paste from Clipboard** — import a table you copied from a spreadsheet or another app. The button is
  enabled only when the clipboard actually looks like a table (has rows and columns).

Once a source is loaded, a **preview grid** shows the first 100 rows. Each column header reads
`Name (Type)` — for example `time (DateTime)`, `voltage (Number)` — so you can confirm the reader
detected each column's type correctly before plotting.

The reader **auto-detects** everything by default, but four controls let you override it when the guess
is wrong:

| Control | Options | What it does |
| --- | --- | --- |
| **Delimiter** | Auto, Comma, Semicolon, Tab, Pipe | How fields are separated. *Auto* scores the candidates and picks the most consistent one. |
| **Header** | Auto, Yes, No | Whether the first row holds column names. *Auto* treats the first row as a header when it looks like labels over data. |
| **Decimal** | Auto, Point (.), Comma (,) | The decimal separator for numbers (e.g. `1,5` vs `1.5`). *Auto* picks whichever parses more numeric cells. |
| **Skip rows** | a number | Drops that many lines from the top before parsing (for files with a title banner or notes). |

For an **Excel `.xlsx`** file, a **Worksheet** selector appears; pick which sheet to read.

Any non-fatal issues (for example a ragged row that was padded) appear as amber **warnings** under the
preview; a red **error** message appears if the file can't be parsed at all. Fix the source or adjust the
options above, and the preview updates.

For `sample-measurement.csv` every default is correct: comma-delimited, a header row, and a `.` decimal —
so you can go straight to **Next**.

### Page 2 — Columns & plot

Map columns onto a plot:

- **X column** — the horizontal axis. Choose a column, or `(row index)` to plot against 0, 1, 2, …. A
  date/time column here produces a **date axis** with calendar ticks; a text column produces a
  **category axis**.
- **Y columns** — tick one or more numeric columns. Each becomes its own series, and a **legend**
  appears automatically when you pick more than one.
- **Plot type** — the list shows only the kinds that fit your selection (see the table below).
- **Error column** — shown only for an **Error bar** plot; pick the column holding the error magnitudes.
- **Bins** — shown only for a **Histogram**; the number of bins.
- **Target** — **New figure** opens the result in a fresh figure, or **Current axes (append)** overlays
  it on what you already have (MATLAB "hold on" semantics).

| Plot type | Needs |
| --- | --- |
| Line, Scatter, Stem | One or more Y columns; any X (number, date/time, category, or row index). |
| Bar | Exactly one Y column; X may be a category, number, or row index. |
| Histogram | One or more numeric Y columns; ignores X. |
| Error bar | One Y column, one error column, and a non-text X. |

For the sample file, choose **X = time**, tick **voltage** and **current**, leave **Plot type** on
**Line**, and click **Import**. You get a two-series line graph on a date/time axis with a legend — ready
to pan, zoom, edit, theme, and export like any other figure.

---

## 2. The same thing from a script

The figure window's **Script…** editor runs the same import and plotting through code. Reading a table is
one call — `readcsv(path)` — and you plot columns by name. Pick a language in the selector and press
**Run (F5)**. Relative paths resolve against the app's working directory, so keep the CSV alongside the
app or use an absolute path.

**JGS** (the built-in language) — see [`examples/example.jgs`](../examples/example.jgs):

```
let data = readcsv("sample-measurement.csv")
plot(data, "time", "voltage", "b-")
hold(true)
plot(data, "time", "current", "r-")
title("Measurements")
legend("voltage", "current")
show()
```

**C#** — see [`examples/example.csx`](../examples/example.csx):

```csharp
var data = readcsv("sample-measurement.csv");
Plot(data, "time", "voltage", "b-");
Plot(data, "time", "current", "r-");
Legend("voltage", "current");
show();
```

**Python** — see [`examples/example.py`](../examples/example.py):

```python
data = readcsv("sample-measurement.csv")
JG.Plot(data, "time", "voltage", "b-")
JG.Plot(data, "time", "current", "r-")
JG.Legend("voltage", "current")
show()
```

`readxlsx(path)` reads an Excel workbook, and `readtable(path)` picks the reader by file extension.

---

## 3. The same thing from the API

From your own C#/.NET code (no figure window needed), the entry points are `Table.ReadCsv` /
`Table.ReadXlsx` (or `JG.ReadTable`), and the table-aware plot helpers:

```csharp
Table table = Table.ReadCsv("examples/sample-measurement.csv");

var axes = new FigureModel().AddAxes();
axes.AddLine(table, "time", "voltage");   // a date/time column → a date axis, automatically
axes.AddLine(table, "time", "current");

// …or the MATLAB-flavored facade:
JG.Plot(table, "time", "current", "r--");
```

Pass an `ImportOptions` to override the delimiter, header, culture, sheet, or skipped rows — the same
overrides the wizard exposes. See [architecture.md](architecture.md) for the data-import design and
[adr/0011-tabular-data-and-import.md](adr/0011-tabular-data-and-import.md) for the reader's detection
rules and limits.
