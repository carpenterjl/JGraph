using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>sendEventToHTMLSource</c> (app-building plan, U9b): an event for a <c>uihtml</c>'s page, sent
/// at the next drain point. R2025b holds it as a method of <c>matlab.ui.control.HTML</c>, so what it
/// refuses it refuses as a method does (probe <c>u9b_bridge</c>).
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterUiHtmlBuiltins(JgsEnvironment env)
    {
        env.Builtins.Register("sendEventToHTMLSource", JgsValue.Function(new BuiltinFunction("sendEventToHTMLSource",
            (args, line, col) => SendEventToHtmlSource(args, line, col))
        {
            BindsAnsAsStatement = false,
            KeepsStringArguments = true,

            // A method, so no row in the measured nargout table: it gives nothing, and asking for an
            // output is refused as a file's call is (probe u9b_bridge).
            MatlabOutputCount = 0,
            IsMatlabFile = true,
        }));
    }

    private static JgsValue SendEventToHtmlSource(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction", "Unrecognized function or variable 'sendEventToHTMLSource'.");
        }

        JgsValue target = args[0];
        if (target.Type == JgsType.Array && target.ArrayLength > 1 && !target.IsStringArray && !target.IsCharMatrix
            && Array.TrueForAll(target.BoxedElements(), static e => JgsHandleRegistry.TryGet(e, out JgsHandleEntry? each) && each.Target is UiHtmlModel))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:index:expected_one_output_for_assignment",
                $"Assigning to {target.ArrayLength} elements using a simple assignment statement is not supported. Consider using comma-separated list assignment.");
        }

        if (target.Type != JgsType.Number || !JgsHandleRegistry.TryGet(target, out JgsHandleEntry? entry) || entry.Target is not UiHtmlModel)
        {
            string type = target.Type == JgsType.Number && JgsHandleRegistry.TryGet(target, out JgsHandleEntry? other)
                ? JgsGraphicsClasses.ClassOf(other.Target)
                : ClassOf(target, JgsDialect.Matlab);
            throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction",
                $"Undefined function 'sendEventToHTMLSource' for input arguments of type '{type}'.");
        }

        var html = (UiHtmlModel)JgsHandleRegistry.Require(target, line, col).Target;
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Invalid argument list. Function requires 1 more input(s).");
        }

        if (args.Count > 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        JgsUiHtml.Send(html, EventNameOf(args[1], line, col), args.Count == 3 ? args[2] : JgsMatrix.FromColumnMajor([], 0, 0)); // no data reaches the page as [] (probe u9b_more)
        return JgsValue.Null;
    }

    /// <summary>An event's name: text of any of MATLAB's three kinds, laid end to end with commas when there are several.</summary>
    private static string EventNameOf(JgsValue name, int line, int col)
    {
        if (name.IsCharMatrix)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validation:IncompatibleSize", "Invalid argument at position 2. Value must be a vector.");
        }

        if (name.Type == JgsType.String)
        {
            return name.AsString;
        }

        if (name.IsStringArray)
        {
            return string.Join(",", Array.ConvertAll(name.BoxedElements(), static e => e.AsString));
        }

        if (name.Type == JgsType.Cell && Array.TrueForAll(name.AsCell, static e => e.Type == JgsType.String))
        {
            return string.Join(",", Array.ConvertAll(name.AsCell, static e => e.AsString));
        }

        throw new JgsRuntimeException(line, col, "MATLAB:validators:mustBeText",
            "Invalid argument at position 2. Value must be a character vector, string array, or cell array of character vectors.");
    }
}
