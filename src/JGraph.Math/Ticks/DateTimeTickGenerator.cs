using System.Globalization;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Maths.Ticks;

/// <summary>
/// Generates ticks for a date/time axis whose values are OLE automation dates (see
/// <see cref="DateTimeAxis"/>). It chooses a natural calendar/clock step (seconds, minutes, hours,
/// days, months, or years) closest to the requested tick spacing, aligns ticks to that step's
/// boundaries, and formats labels at an appropriate resolution.
/// </summary>
public sealed class DateTimeTickGenerator : ITickGenerator
{
    public static readonly DateTimeTickGenerator Instance = new();

    private static readonly Candidate[] Candidates = BuildCandidates();

    public AxisScaleType ScaleType => AxisScaleType.DateTime;

    public TickSet Generate(DataRange range, int targetCount, string? labelFormat = null)
    {
        if (!range.IsValid || targetCount < 2)
        {
            return TickSet.Empty;
        }

        DateTime start = DateTimeAxis.FromValue(range.Min);
        DateTime end = DateTimeAxis.FromValue(range.Max);
        double idealDays = range.Length / targetCount;
        Candidate step = ChooseCandidate(idealDays);
        string format = labelFormat ?? DefaultFormat(step.Unit);

        var majors = new List<Tick>();
        DateTime tick = AlignDown(start, step);
        int guard = 0;
        while (tick <= end && guard++ < 100_000)
        {
            if (tick >= start)
            {
                double value = DateTimeAxis.ToValue(tick);
                majors.Add(new Tick(value, tick.ToString(format, CultureInfo.CurrentCulture)));
            }

            tick = Advance(tick, step);
        }

        return new TickSet(majors, Array.Empty<double>(), step.ApproxDays);
    }

    private enum StepUnit
    {
        Second,
        Minute,
        Hour,
        Day,
        Month,
        Year,
    }

    private readonly record struct Candidate(StepUnit Unit, int Amount, double ApproxDays);

    private static Candidate ChooseCandidate(double idealDays)
    {
        // Pick the candidate whose spacing is closest to the ideal on a log scale, so the tick count
        // lands near the target instead of always rounding up to a sparser step.
        Candidate best = Candidates[0];
        double bestScore = double.MaxValue;
        foreach (Candidate c in Candidates)
        {
            double score = System.Math.Abs(System.Math.Log(c.ApproxDays / idealDays));
            if (score < bestScore)
            {
                bestScore = score;
                best = c;
            }
        }

        return best;
    }

    private static DateTime AlignDown(DateTime t, Candidate step)
    {
        switch (step.Unit)
        {
            case StepUnit.Second:
            {
                var day = t.Date;
                int totalSeconds = (int)(t - day).TotalSeconds;
                int aligned = totalSeconds - (totalSeconds % step.Amount);
                return day.AddSeconds(aligned);
            }

            case StepUnit.Minute:
            {
                var day = t.Date;
                int totalMinutes = (int)(t - day).TotalMinutes;
                int aligned = totalMinutes - (totalMinutes % step.Amount);
                return day.AddMinutes(aligned);
            }

            case StepUnit.Hour:
            {
                var day = t.Date;
                int hour = t.Hour;
                int aligned = hour - (hour % step.Amount);
                return day.AddHours(aligned);
            }

            case StepUnit.Day:
                // Align to midnight of the day; multi-day steps advance from there.
                return t.Date;

            case StepUnit.Month:
            {
                int monthIndex = ((t.Year * 12) + (t.Month - 1));
                int aligned = monthIndex - (monthIndex % step.Amount);
                int year = aligned / 12;
                int month = (aligned % 12) + 1;
                return new DateTime(year, month, 1);
            }

            case StepUnit.Year:
            default:
            {
                int year = t.Year - (t.Year % step.Amount);
                if (year < 1)
                {
                    year = 1;
                }

                return new DateTime(year, 1, 1);
            }
        }
    }

    private static DateTime Advance(DateTime t, Candidate step) => step.Unit switch
    {
        StepUnit.Second => t.AddSeconds(step.Amount),
        StepUnit.Minute => t.AddMinutes(step.Amount),
        StepUnit.Hour => t.AddHours(step.Amount),
        StepUnit.Day => t.AddDays(step.Amount),
        StepUnit.Month => t.AddMonths(step.Amount),
        _ => t.AddYears(step.Amount),
    };

    private static string DefaultFormat(StepUnit unit) => unit switch
    {
        StepUnit.Second => "HH:mm:ss",
        StepUnit.Minute => "HH:mm",
        StepUnit.Hour => "HH:mm",
        StepUnit.Day => "MMM d",
        StepUnit.Month => "MMM yyyy",
        _ => "yyyy",
    };

    private static Candidate[] BuildCandidates()
    {
        const double secondDays = 1.0 / 86400.0;
        const double minuteDays = 1.0 / 1440.0;
        const double hourDays = 1.0 / 24.0;
        const double monthDays = 30.436875;
        const double yearDays = 365.25;

        return new[]
        {
            new Candidate(StepUnit.Second, 1, secondDays),
            new Candidate(StepUnit.Second, 2, 2 * secondDays),
            new Candidate(StepUnit.Second, 5, 5 * secondDays),
            new Candidate(StepUnit.Second, 10, 10 * secondDays),
            new Candidate(StepUnit.Second, 15, 15 * secondDays),
            new Candidate(StepUnit.Second, 30, 30 * secondDays),
            new Candidate(StepUnit.Minute, 1, minuteDays),
            new Candidate(StepUnit.Minute, 2, 2 * minuteDays),
            new Candidate(StepUnit.Minute, 5, 5 * minuteDays),
            new Candidate(StepUnit.Minute, 10, 10 * minuteDays),
            new Candidate(StepUnit.Minute, 15, 15 * minuteDays),
            new Candidate(StepUnit.Minute, 30, 30 * minuteDays),
            new Candidate(StepUnit.Hour, 1, hourDays),
            new Candidate(StepUnit.Hour, 2, 2 * hourDays),
            new Candidate(StepUnit.Hour, 3, 3 * hourDays),
            new Candidate(StepUnit.Hour, 6, 6 * hourDays),
            new Candidate(StepUnit.Hour, 12, 12 * hourDays),
            new Candidate(StepUnit.Day, 1, 1),
            new Candidate(StepUnit.Day, 2, 2),
            new Candidate(StepUnit.Day, 7, 7),
            new Candidate(StepUnit.Day, 14, 14),
            new Candidate(StepUnit.Month, 1, monthDays),
            new Candidate(StepUnit.Month, 3, 3 * monthDays),
            new Candidate(StepUnit.Month, 6, 6 * monthDays),
            new Candidate(StepUnit.Year, 1, yearDays),
            new Candidate(StepUnit.Year, 2, 2 * yearDays),
            new Candidate(StepUnit.Year, 5, 5 * yearDays),
            new Candidate(StepUnit.Year, 10, 10 * yearDays),
            new Candidate(StepUnit.Year, 20, 20 * yearDays),
            new Candidate(StepUnit.Year, 50, 50 * yearDays),
            new Candidate(StepUnit.Year, 100, 100 * yearDays),
        };
    }
}
