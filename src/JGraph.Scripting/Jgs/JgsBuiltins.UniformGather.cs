namespace JGraph.Scripting.Jgs;

/// <summary>
/// The kinds a uniform <c>arrayfun</c> or <c>cellfun</c> collects in R2025b beyond numbers, text,
/// categoricals and objects (open item 324, ADR 0371, probes p6d, p6d2, p6d3): a 1-by-1 cell apiece
/// is a cell of what they hold, a 1-by-1 struct apiece a struct array (the same field names in the
/// same order), a datetime, a duration or a calendarDuration apiece one array of them. A function handle is
/// <c>UnimplementedOutputArrayType</c>, a containers.Map or a table <c>SubsasgnProblem</c>, and an
/// answer of one of these kinds beside an answer of another class <c>MismatchInOutputTypes</c>.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Whether an answer is one of the kinds this file decides for (MATLAB dialect).</summary>
    private static bool IsGatheredKind(JgsValue value) =>
        value.Type is JgsType.Cell or JgsType.Function or JgsType.Table
        || (value.Type == JgsType.Struct && value.TimeTag is null && (value.ClassName is null || IsKeyedCollection(value)))
        || value.IsDatetime || value.IsDuration || IsCalendarDuration(value);

    /// <summary>
    /// Holds one uniform answer to R2025b's rule against the first answer of its output, in the MATLAB
    /// dialect, when either is of a gathered kind; anything else is left to the existing checks.
    /// <paramref name="index"/> and <paramref name="output"/> are 1-based, as the messages count.
    /// </summary>
    internal static void CheckGatheredAnswer(string verb, JgsValue first, JgsValue answer, int index, int output, int line, int col)
    {
        if (!JgsRunningDialect.ThreadIsMatlab || (!IsGatheredKind(first) && !IsGatheredKind(answer)))
        {
            return;
        }

        int count = answer.Type switch
        {
            JgsType.Cell => answer.AsCell.Length,
            JgsType.Struct when !IsKeyedCollection(answer) && (answer.TimeTag is null || IsCalendarDuration(answer)) => answer.AsStructArray.Length,
            JgsType.Function or JgsType.Table or JgsType.Struct or JgsType.Object => 1,
            _ => SizeDims(answer).Aggregate(1, static (a, b) => a * b),
        };

        if (count != 1)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:NotAScalarOutput",
                $"Non-scalar in Uniform output, at index {index}, output {output}.\nSet 'UniformOutput' to false.");
        }

        string firstClass = ClassOf(first, JgsDialect.Matlab);
        string answerClass = ClassOf(answer, JgsDialect.Matlab);
        if (!string.Equals(firstClass, answerClass, StringComparison.Ordinal))
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:MismatchInOutputTypes",
                $"Mismatch in type of outputs, at index {index}, output {output} ({firstClass} versus {answerClass}).\nSet 'UniformOutput' to false.");
        }

        if (answer.Type == JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:UnimplementedOutputArrayType",
                "function_handle output type is not supported. Set 'UniformOutput' to false.");
        }

        if (answer.Type == JgsType.Table || IsKeyedCollection(answer))
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:SubsasgnProblem",
                $"Unable to assign into output array {output} at index {index}. Set 'UniformOutput' to false.");
        }

        if (answer.Type == JgsType.Struct && !ReferenceEquals(first, answer))
        {
            string[] had = first.AsStructArray.FieldNames;
            string[] has = answer.AsStructArray.FieldNames;
            if (had.Length != has.Length)
            {
                throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:structFieldNumberMismatch",
                    $"At index {index}, output {output}, the number of struct fields does not match previous number of fields. Set 'UniformOutput' to false.");
            }

            for (int f = 0; f < had.Length; f++)
            {
                if (!string.Equals(had[f], has[f], StringComparison.Ordinal))
                {
                    throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:structFieldNameMismatch",
                        $"At index {index}, output {output}, the name of struct field '{has[f]}' does not match previous name '{had[f]}'. Set 'UniformOutput' to false.");
                }
            }
        }
    }

    /// <summary>
    /// The checked answers of one output as one row of their kind - a cell, a struct array, a
    /// datetime or duration array - or null when the first is not a cell, a plain struct or a time.
    /// The caller gives it the walked array's shape.
    /// </summary>
    internal static JgsValue? GatheredAnswers(JgsValue[] answers)
    {
        if (!JgsRunningDialect.ThreadIsMatlab || answers.Length == 0)
        {
            return null;
        }

        JgsValue first = answers[0];
        if (first.Type == JgsType.Cell)
        {
            return JgsValue.Cell(Array.ConvertAll(answers, static a => JgsValue.Share(a.AsCell[0])));
        }

        if (first.Type == JgsType.Struct && first.ClassName is null && first.TimeTag is null)
        {
            var elements = new Dictionary<string, JgsValue>[answers.Length];
            for (int i = 0; i < answers.Length; i++)
            {
                elements[i] = answers[i].AsStructArray.Elements[0];
                JgsHolders.Share(elements[i]); // M2: the gathered array is a second holder of each element
            }

            return JgsValue.StructArray(new JgsStructArray(elements, first.AsStructArray.FieldNames), 1, answers.Length);
        }

        if (IsCalendarDuration(first))
        {
            var elements = new Dictionary<string, JgsValue>[answers.Length];
            for (int i = 0; i < answers.Length; i++)
            {
                elements[i] = answers[i].AsStructArray.Elements[0];
                JgsHolders.Share(elements[i]); // M2, as a struct array's
            }

            return CalendarValue(elements, JgsValue.Number(0));
        }

        if (first.IsDatetime || first.IsDuration)
        {
            var numbers = Array.ConvertAll(answers, static a => JgsValue.Number(a.ElementAt(0).AsNumber));
            return JgsMatrix.FromElements(numbers, 1, numbers.Length).WithTimeTag(first.TimeTag!);
        }

        return null;
    }
}
