// SPDX-License-Identifier: MIT
// Copyright (c) JGraph contributors

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Indexed writes into a <c>datetime</c> or a <c>duration</c> (V6, ADR 0167, #128, #129): what
/// <c>d(k) = rhs</c> stores when the target is a time. A time value is a tagged array of
/// milliseconds, so the ordinary array roads do the writing - the growth, the deletion, the mask
/// and the count rules are theirs - and this file only settles what the right-hand side is worth
/// in milliseconds, and refuses what R2025b refuses, in its words.
/// </summary>
/// <remarks>
/// Every rule here was measured in R2025b (<c>datetime_forms</c>). Into a datetime: another
/// datetime goes in as it is - a one-element one fills every selected slot - unless one side has
/// a time zone and the other has not; text (a char row, a string, a string array, a cell of
/// text) is read as a date, first through the target's own <c>Format</c> and then through the
/// shapes <c>datetime(text)</c> recognizes, where <c>'NaT'</c>, <c>''</c> and the missing string
/// are <c>NaT</c>, and a wall-clock reading goes into a zoned target as the instant it names
/// there; a number of any class is refused with the message that names the component
/// properties; everything else (a logical, a duration, a char matrix, a cell holding a datetime,
/// a struct) is "Right hand side of an assignment must be a datetime array or text representing
/// dates and times.". Into a duration: another duration as it is, a number as a count of days,
/// text in a timer format (<c>hh:mm:ss</c>, <c>dd:hh:mm:ss</c>, <c>mm:ss</c>); everything else is
/// refused in one sentence. A datetime written into a double array is "not convertible", and
/// into a char row is the count mismatch R2025b answers.
/// </remarks>
internal sealed partial class Interpreter
{
    private const string NotADatetimeRhs =
        "Right hand side of an assignment must be a datetime array or text representing dates and times.";

    private const string NumericIntoDatetime =
        "You cannot assign numeric values to a datetime array. Assign to the array's Year, Month, Day, Hour, "
        + "Minute, or Second properties, or use datetime(x,'ConvertFrom',...) to create datetime values for assignment.";

    private const string NotADurationRhs =
        "Right hand side of an assignment must be a duration array, numeric array representing days, or text "
        + "representing durations in timer formats (e.g. 'hh:mm:ss').";

    private const string ZoneMismatch =
        "Cannot combine or compare a datetime array with a time zone with one without a time zone.";

    /// <summary>
    /// The milliseconds a right-hand side is worth in a time target: one bare number when it
    /// stands for one moment (so a mask or a range selection is filled with it, as any scalar
    /// fills one), a plain numeric array in its own shape otherwise (so the count rules apply),
    /// or the time value itself when it is one of the target's kind with more than one element.
    /// </summary>
    private static JgsValue IntoTimeArray(JgsValue target, JgsValue rhs, Node at)
    {
        JgsTimeTag tag = target.TimeTag!;
        if (rhs.TimeTag is { } given)
        {
            if (given.Kind != tag.Kind)
            {
                throw new JgsRuntimeException(at.Line, at.Column, target.IsDatetime ? NotADatetimeRhs : NotADurationRhs);
            }

            if (target.IsDatetime && (tag.TimeZone is { Length: > 0 }) != (given.TimeZone is { Length: > 0 }))
            {
                throw new JgsRuntimeException(at.Line, at.Column, ZoneMismatch);
            }

            return rhs.ArrayLength == 1 ? JgsValue.Number(rhs.ElementAt(0).AsNumber) : rhs;
        }

        return target.IsDatetime ? IntoDatetime(tag, rhs, at) : IntoDuration(rhs, at);
    }

    private static JgsValue IntoDatetime(JgsTimeTag tag, JgsValue rhs, Node at)
    {
        if (TextPieces(rhs) is { } texts)
        {
            TimeZoneInfo? zone = JgsTime.ZoneOf(tag);
            var values = new double[texts.Length];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = DatetimeOfText(texts[i], tag.Format, zone, at);
            }

            return Shaped(values, rhs);
        }

        if (IsPlainNumeric(rhs))
        {
            throw new JgsRuntimeException(at.Line, at.Column, NumericIntoDatetime);
        }

        throw new JgsRuntimeException(at.Line, at.Column, NotADatetimeRhs);
    }

    private static JgsValue IntoDuration(JgsValue rhs, Node at)
    {
        if (TextPieces(rhs) is { } texts)
        {
            var values = new double[texts.Length];
            for (int i = 0; i < values.Length; i++)
            {
                if (!JgsTime.TryParseDuration(texts[i], out values[i]))
                {
                    throw new JgsRuntimeException(at.Line, at.Column, NotADurationRhs);
                }
            }

            return Shaped(values, rhs);
        }

        if (IsPlainNumeric(rhs))
        {
            // A number written into a duration is a count of days (measured: u(2) = 5 is 120 hours).
            if (rhs.Type != JgsType.Array)
            {
                return JgsValue.Number(rhs.AsNumber * JgsTime.MsPerDay);
            }

            var values = new double[rhs.ArrayLength];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = rhs.ElementAt(i).AsNumber * JgsTime.MsPerDay;
            }

            return Shaped(values, rhs);
        }

        throw new JgsRuntimeException(at.Line, at.Column, NotADurationRhs);
    }

    /// <summary>
    /// The pieces of text a right-hand side is, or null: a char row is one, a string array its
    /// elements, a cell of char rows and string scalars its cells. A char matrix is not text
    /// here (R2025b refuses it, rows matching or not), and neither is a cell holding anything else.
    /// </summary>
    private static string[]? TextPieces(JgsValue rhs)
    {
        if (rhs.Type == JgsType.String)
        {
            return [rhs.AsString];
        }

        if (rhs.IsStringArray)
        {
            return Array.ConvertAll(rhs.BoxedElements(), static e => e.AsString);
        }

        if (rhs.Type != JgsType.Cell)
        {
            return null;
        }

        JgsValue[] pieces = rhs.AsCell;
        var texts = new string[pieces.Length];
        for (int i = 0; i < pieces.Length; i++)
        {
            if (!JgsBuiltins.IsTextScalar(pieces[i]))
            {
                return null;
            }

            texts[i] = JgsBuiltins.TextOf(pieces[i]);
        }

        return texts;
    }

    /// <summary>A number, a complex, or a numeric array of any class that is not text, logical or a time.</summary>
    private static bool IsPlainNumeric(JgsValue rhs) =>
        rhs.Type is JgsType.Number or JgsType.Complex
        || (rhs.Type == JgsType.Array && !rhs.IsStringArray && !rhs.IsCharMatrix && !rhs.IsTime
            && !JgsBuiltins.IsLogicalValue(rhs) && !JgsMatrix.IsNested(rhs));

    /// <summary>
    /// One text as the datetime it names: <c>NaT</c>, the empty text and the missing string are
    /// <c>NaT</c>; otherwise the target's own <c>Format</c> is tried first, then the shapes
    /// <c>datetime(text)</c> recognizes; a wall-clock reading into a zoned target is the instant
    /// it names in that zone.
    /// </summary>
    private static double DatetimeOfText(string text, string format, TimeZoneInfo? zone, Node at)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0 || JgsBuiltins.IsMissingText(text))
        {
            return JgsTime.NotATime;
        }

        if (!JgsTime.TryParse(trimmed, format, out double ms) && !JgsTime.TryParse(trimmed, null, out ms))
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Unable to convert the text '{text}' to a datetime value because its format was not recognized.");
        }

        return double.IsNaN(ms) || zone is null ? ms : JgsTime.ToUtc(ms, zone);
    }

    /// <summary>One value as a bare number, several as a plain array in the right-hand side's own shape.</summary>
    private static JgsValue Shaped(double[] values, JgsValue model)
    {
        if (values.Length == 1)
        {
            return JgsValue.Number(values[0]);
        }

        int[] dims = model.Type == JgsType.Cell ? [model.Rows, model.Cols] : model.Dims;
        return JgsMatrix.FromColumnMajorDims(values, dims);
    }

    /// <summary>
    /// Keeps a datetime's default display in step with what it holds after a write (measured:
    /// writing a moment with a time of day into a date-only array shows the time for every
    /// element, and an array left holding only <c>NaT</c> shows the full format). Only the two
    /// default formats move; a format a script set stays as it was set.
    /// </summary>
    private static void RefreshDatetimeFormat(JgsValue target, JgsValue written)
    {
        if (target.TimeTag is not { Kind: JgsTimeKind.Datetime } tag
            || tag.Format is not (JgsTime.DateOnlyFormat or JgsTime.DefaultDatetimeFormat))
        {
            return;
        }

        // What went in decides whether the whole array has to be read again: a time of day
        // written into a date-only array settles it at once, a moment on midnight written into a
        // full-format array may let the display fall back to the date, and a NaT may leave nothing
        // but NaT behind.
        bool anyTimeOfDay = false;
        bool anyMoment = false;
        int count = written.Type == JgsType.Array ? written.ArrayLength : 1;
        for (int i = 0; i < count; i++)
        {
            double ms = (written.Type == JgsType.Array ? written.ElementAt(i) : written).AsNumber;
            if (double.IsNaN(ms))
            {
                continue;
            }

            anyMoment = true;
            if (JgsTime.WallClock(ms, tag).TimeOfDay != TimeSpan.Zero)
            {
                anyTimeOfDay = true;
                break;
            }
        }

        if (tag.Format == JgsTime.DateOnlyFormat && anyMoment && !anyTimeOfDay)
        {
            return; // a whole day among whole days
        }

        if (tag.Format == JgsTime.DateOnlyFormat && anyTimeOfDay)
        {
            target.MarkTime(tag with { Format = JgsTime.DefaultDatetimeFormat });
            return;
        }

        if (tag.Format == JgsTime.DefaultDatetimeFormat && anyTimeOfDay)
        {
            return; // a time of day among times of day
        }

        JgsTimeTag refreshed = JgsTime.DatetimeTag(JgsBuiltins.TimeMs(target), tag.TimeZone);
        if (refreshed.Format != tag.Format)
        {
            target.MarkTime(refreshed);
        }
    }
}
