using System.Text;

namespace JGraph.Deploy;

/// <summary>
/// The Excel add-in's Visual Basic module (<c>mcc -b</c>, <c>&lt;name&gt;.bas</c>; compiler and SDK plan,
/// stage K17e), transcribed from R2025b's generated modules (k17_excel: bas.file.k17bas.bas,
/// v3456.file.k17v3456.bas, pfile.file.k17p.bas, mcc_default.file.k17m.bas, mcc_twofiles.bas):
/// the module variables, <c>InitModule</c> creating the COM utility library's <c>MWUtil</c> and
/// giving it Excel's <c>Application</c>, one worksheet <c>Function</c> per exported function with
/// every input an <c>Optional Variant</c> (<c>varargin</c> as <c>V0</c>, <c>V1</c>, ... up to 29
/// arguments in all, packed by the <c>pack</c> function written last), the component created by
/// its versioned ProgID on first use, the call with <c>nargout</c> and the output variables first
/// (no <c>nargout</c> for a function without outputs, which answers <c>Empty</c>), the first output
/// answered and the <c>Array(...)</c> form shown in comments for several, and every error answered
/// as <c>"Error in " &amp; Err.Source &amp; ": " &amp; Err.Description</c>. Lines longer than 89
/// characters (the signature and the <c>pack</c> call) are wrapped at a space with <c>  _</c>, as
/// R2025b wraps them. Two things differ from R2025b's text: the utility library's ProgID is JGraph's
/// own (<c>JGraphComUtil.MWUtil25.2</c>; decision KO10), and the objects are created with no server
/// name where R2025b's module writes <c>CreateObject(progid, "localhost")</c>. That form is a DCOM
/// activation, which reads the machine's registrations alone, so an add-in registered for the
/// current user (what mcc, <c>_install.bat</c> and regsvr32 give without administrator rights) answers
/// every cell "Error in VBAProject: Class not registered on local machine" (measured 2026-11-07 in
/// Excel 16 over R2025b's own add-in: k17_cells; <c>docs/MATLAB_Behavioral_Fixes.md</c>). Without the
/// server name the component is created in Excel's process from either hive. CR LF line ends, a
/// line end after the last line.
/// </summary>
public static class ExcelVisualBasic
{
    /// <summary>How many arguments a worksheet function takes in all: the fixed inputs and then <c>V0</c>, <c>V1</c>, ... for <c>varargin</c>.</summary>
    public const int MaxArguments = 29;

    /// <summary>The longest line written before it is wrapped.</summary>
    public const int LineWidth = 89;

    /// <summary>The COM utility library's versioned ProgID the module creates (<c>JGraphComUtil.MWUtil25.2</c>).</summary>
    public static string UtilityProgId => ExcelComGuids.UtilityVersionedProgId("MWUtil");

    /// <summary>The module for <paramref name="options"/>' add-in.</summary>
    public static string Module(ExcelAddInOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string progId = options.AddInName + "." + options.ClassName + "." + ExcelAddIn.ProgIdSuffix(options.Version);
        return Module(options.ClassName, progId, UtilityProgId, options.Functions);
    }

    /// <summary>The module: the class variable named <paramref name="className"/>, the component created by <paramref name="progId"/>, the utility by <paramref name="utilityProgId"/>.</summary>
    public static string Module(string className, string progId, string utilityProgId, IReadOnlyList<ExcelFunction> functions)
    {
        ArgumentNullException.ThrowIfNull(className);
        ArgumentNullException.ThrowIfNull(progId);
        ArgumentNullException.ThrowIfNull(utilityProgId);
        ArgumentNullException.ThrowIfNull(functions);
        var lines = new List<string>
        {
            "Dim MCLUtil As Object",
            "Dim bModuleInitialized As Boolean",
            "Dim " + className + " As Object ",
            "",
            "Private Sub InitModule()",
            "    If Not bModuleInitialized Then",
            "        On Error GoTo Handle_Error",
            "        If MCLUtil Is Nothing Then",
            "            Set MCLUtil = CreateObject(\"" + utilityProgId + "\")",
            "        End If",
            "        Call MCLUtil.MWInitApplication(Application)",
            "        ",
            "        bModuleInitialized = True",
            "        Exit Sub",
            "Handle_Error:",
            "        bModuleInitialized = False",
            "    End If",
            "End Sub",
            "",
        };
        bool anyVarargin = false;
        foreach (ExcelFunction f in functions)
        {
            bool hasVarargin = f.Inputs.Count > 0 && f.Inputs[^1] == "varargin";
            anyVarargin |= hasVarargin;
            Function(lines, f, hasVarargin, className, progId);
        }

        if (anyVarargin)
        {
            lines.AddRange(Pack);
        }

        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>Writes <c>&lt;name&gt;.bas</c> into <paramref name="folder"/> and answers its path.</summary>
    public static string Write(string folder, ExcelAddInOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string path = Path.Combine(folder, options.AddInName + ".bas");
        File.WriteAllText(path, Module(options), new UTF8Encoding(false));
        return path;
    }

    /// <summary>
    /// Wraps <paramref name="text"/> as R2025b's writer does: words are taken while the line stays
    /// within <see cref="LineWidth"/> characters (its leading spaces counted), a broken line ends
    /// with <c>  _</c>, and each continuation line starts with <paramref name="indent"/> spaces.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string text, int indent)
    {
        ArgumentNullException.ThrowIfNull(text);
        string lead = text[..(text.Length - text.TrimStart(' ').Length)];
        string[] words = text.TrimStart(' ').Split(' ');
        var lines = new List<string>();
        string line = string.Empty;
        foreach (string word in words)
        {
            if (line.Length == 0)
            {
                line = lead + word;
                lead = new string(' ', indent);
                continue;
            }

            string candidate = line + " " + word;
            if (candidate.Length > LineWidth)
            {
                lines.Add(line + "  _");
                line = lead + word;
            }
            else
            {
                line = candidate;
            }
        }

        lines.Add(line);
        return lines;
    }

    private static void Function(List<string> lines, ExcelFunction f, bool hasVarargin, string className, string progId)
    {
        List<string> inputs = hasVarargin ? [.. f.Inputs.Take(f.Inputs.Count - 1)] : [.. f.Inputs];
        int variadic = hasVarargin ? MaxArguments - inputs.Count : 0;
        List<string> parameters = [.. inputs.Select(static p => "Optional " + p + " As Variant")];
        for (int k = 0; k < variadic; k++)
        {
            parameters.Add("Optional V" + k + " As Variant");
        }

        lines.AddRange(Wrap("Function " + f.Name + "(" + string.Join(", ", parameters) + ") As Variant", 4));
        lines.Add("");
        if (f.Outputs.Count > 0)
        {
            lines.Add("    Dim " + string.Join(", ", f.Outputs) + " As Variant");
        }

        if (hasVarargin)
        {
            lines.Add("    Dim varargin As Variant");
        }

        if (f.Outputs.Count > 0 || hasVarargin)
        {
            lines.Add("");
        }

        lines.Add("    On Error GoTo Handle_Error");
        lines.Add("");
        lines.Add("    Call InitModule()");
        lines.Add("");
        lines.Add("    If " + className + " Is Nothing Then");
        lines.Add("        Set " + className + " = CreateObject(\"" + progId + "\")");
        lines.Add("    End If");
        if (hasVarargin)
        {
            string packed = string.Join(", ", Enumerable.Range(0, variadic).Select(static k => "V" + k));
            List<string> pack = [.. Wrap("    varargin = pack(" + packed + ")", 8)];
            pack[^1] += "    ";
            lines.AddRange(pack);
        }

        lines.Add("");
        List<string> arguments = [];
        if (f.Outputs.Count > 0)
        {
            arguments.Add(f.Outputs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            arguments.AddRange(f.Outputs);
        }

        arguments.AddRange(inputs);
        if (hasVarargin)
        {
            arguments.Add("varargin");
        }

        string call = "Call " + className + "." + f.Name + "(" + string.Join(", ", arguments) + ")";
        if (f.Outputs.Count > 1)
        {
            lines.Add("    ' By default, MATLAB Excel Add-in returns just one output for MATLAB functions ");
            lines.Add("    ' that have more than one output.    ");
            lines.Add("");
        }

        lines.Add("    " + call);
        lines.Add("    " + f.Name + " = " + (f.Outputs.Count > 0 ? f.Outputs[0] : "Empty"));
        lines.Add("");
        if (f.Outputs.Count > 1)
        {
            lines.Add("    ' If you want to use all the return values, you can use the following ");
            lines.Add("    ' code as a guideline. To use this form, call the function from Excel cell,");
            lines.Add("    ' highlight the cells which will receive the outputs and hit Ctrl+Shift+Enter.");
            lines.Add("    '");
            lines.Add("    ' " + call);
            lines.Add("    ' " + f.Name + " = Array(" + string.Join(", ", f.Outputs) + ")");
            lines.Add("");
        }

        lines.Add("    Exit Function");
        lines.Add("Handle_Error:");
        lines.Add("    " + f.Name + " = \"Error in \" & Err.Source & \": \" & Err.Description");
        lines.Add("End Function");
        lines.Add("");
    }

    /// <summary>The <c>pack</c> function R2025b writes after the last worksheet function when any takes <c>varargin</c>: the arguments given, Empty and missing ones left out, as a zero-based array or Empty.</summary>
    private static readonly string[] Pack =
    [
        "Function pack(ParamArray inputs() As Variant) As Variant",
        "    Dim i As Integer",
        "    Dim c As New Collection",
        "    Dim result() As Variant",
        "    ",
        "    For i = LBound(inputs) To UBound(inputs)",
        "        If Not IsMissing(inputs(i)) And Not IsEmpty(inputs(i)) Then",
        "            c.Add inputs(i)",
        "        End If",
        "    Next",
        "    ",
        "    'Convert collection to array",
        "    If c.Count > 0 Then",
        "        ReDim result(0 To (c.Count - 1))",
        "        For i = 1 To c.Count",
        "            result(i - 1) = c.Item(i)",
        "        Next",
        "        pack = result",
        "    Else",
        "        pack = Empty",
        "    End If",
        "",
        "End Function",
    ];
}
