// example.csx — a C# script for JGraph (compiled with Roslyn scripting).
//
// How to run: open the figure window (dotnet run --project src/JGraph.Application),
// click "Script…", choose "C#" in the language selector, paste this in, and press Run (F5).
//
// The JG API is imported statically, so Plot/Title/Legend/... need no qualifier, and
// System.Math is in scope (Sin, Cos, Exp, ...). Host helpers: readcsv(path),
// print(value), show().

int n = 400;
var t = new double[n];
var signal = new double[n];
var carrier = new double[n];
for (int i = 0; i < n; i++)
{
    t[i] = i * (10.0 / (n - 1));
    double envelope = Exp(-0.35 * t[i]);
    signal[i] = envelope * Sin(3 * t[i]);
    carrier[i] = 0.5 * Cos(3 * t[i]);
}

double rms = Sqrt(signal.Average(v => v * v));
print($"signal RMS = {rms:F4}");

Plot(t, signal, "b-");
Hold(true);                 // keep the first series when adding the second
Plot(t, carrier, "r--");
Title("Damped oscillation (C#)");
XLabel("time (s)");
YLabel("amplitude");
Legend("damped sine", "carrier");
Grid(true);
XLim(0, 10);
show();

// --- Or load your own data ---
// A CSV/TSV/xlsx file becomes a typed Table; plot columns by name.
//   var data = readcsv("sample-measurement.csv");
//   Plot(data, "time", "voltage", "b-");   // a date/time column becomes a date axis
//   Hold(true);
//   Plot(data, "time", "current", "r-");
//   Title("Measurements");
//   Legend("voltage", "current");
//   show();
