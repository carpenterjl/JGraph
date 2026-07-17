# example.py — a Python script for JGraph (a real in-process CPython via pythonnet).
#
# How to run: open the figure window (dotnet run --project src/JGraph.Application),
# click "Script…", choose "Python" in the language selector, paste this in, and press
# Run (F5). The Python engine appears only when a CPython 3.x runtime is found (from the
# PYTHONNET_PYDLL environment variable or the python/py launcher).
#
# The JG API is the JGraph.Api.JG type. Host helpers: readcsv(path), show();
# print() writes to the console below. Python lists are converted to .NET arrays.
import math

n = 400
t = [i * (10.0 / (n - 1)) for i in range(n)]
signal = [math.exp(-0.35 * ti) * math.sin(3 * ti) for ti in t]
carrier = [0.5 * math.cos(3 * ti) for ti in t]

rms = math.sqrt(sum(v * v for v in signal) / len(signal))
print(f"signal RMS = {rms:.4f}")

JG.Plot(t, signal, "b-")
JG.Hold(True)                # keep the first series when adding the second
JG.Plot(t, carrier, "r--")
JG.Title("Damped oscillation (Python)")
JG.XLabel("time (s)")
JG.YLabel("amplitude")
JG.Legend("damped sine", "carrier")
JG.Grid(True)
JG.XLim(0, 10)
show()

# --- Or load your own data ---
# data = readcsv("sample-measurement.csv")
# JG.Plot(data, "time", "voltage", "b-")   # a date/time column becomes a date axis
# JG.Hold(True)
# JG.Plot(data, "time", "current", "r-")
# JG.Title("Measurements")
# JG.Legend("voltage", "current")
# show()
