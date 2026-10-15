namespace JGraph.Scripting.Jgs;

/// <summary>
/// R2025b's identifiers and words when a verb meets a value it does not take (open item 123): an
/// object of a script's class, where the verb was not overloaded by the class, and a categorical
/// handed to a text verb. Each refusal was measured (probe_123); a verb that answers in this build
/// keeps answering, and JGS code keeps its own sentences.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>What R2025b raises when a verb's first argument is an object of a script's class.</summary>
    private static readonly Dictionary<string, (string Id, string Words)> ObjectRefusals = BuildObjectRefusals();

    /// <summary>What R2025b raises when a text verb's subject is a categorical.</summary>
    private static readonly Dictionary<string, (string Id, string Words)> CategoricalTextRefusals = new(StringComparer.Ordinal)
    {
        ["regexp"] = ("MATLAB:REGEXP:invalidInputs", "The 'STRING' input must be either a char row vector, a cell array of char row vectors, or a string array."),
        ["regexpi"] = ("MATLAB:REGEXP:invalidInputs", "The 'STRING' input must be either a char row vector, a cell array of char row vectors, or a string array."),
        ["regexprep"] = ("MATLAB:REGEXP:invalidInputs", "The 'STRING' input must be either a char row vector, a cell array of char row vectors, or a string array."),
        ["strjoin"] = ("MATLAB:strjoin:InvalidCellType", "First input must be a string array or cell array of character vectors."),
        ["strsplit"] = ("MATLAB:strsplit:InvalidStringType", "First input must be either a character vector or a string scalar."),
        ["strtrim"] = ("MATLAB:strtrim:InputClass", "Input should be a string, character array, or a cell array of character arrays."),
        ["str2num"] = ("MATLAB:str2num:InvalidArgument", "Input must be a character vector or string scalar."),
        ["datestr"] = ("MATLAB:datestr:ConvertToDateNumber", "Unable to convert input to specified date string. Failed to convert input to date numbers."),
        ["fieldnames"] = ("MATLAB:categorical:UndefinedFunction", "Undefined function 'fieldnames' for input arguments of type 'categorical'."),
        ["int2str"] = ("MATLAB:math:mustBeNumericCharOrLogical", "Invalid data type. Argument must be numeric, char, or logical."),
        ["setfield"] = ("MATLAB:categorical:FieldAssignmentNotAllowed", "Attempt to assign field of non-structure array."),
        ["rmfield"] = ("MATLAB:rmfield:Arg1NotStructArray", "S must be a structure array."),
        ["num2str"] = ("MATLAB:num2str:nonNumericInput", "Input to num2str must be numeric."),
        ["mat2str"] = ("MATLAB:mat2str:NumericInput", "Input matrix must be a numeric array, character array, or string array."),
    };

    private static Dictionary<string, (string Id, string Words)> BuildObjectRefusals()
    {
        var table = new Dictionary<string, (string Id, string Words)>(StringComparer.Ordinal);
        foreach (string target in (string[])["double", "single", "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64", "logical", "char", "string", "cell", "cellstr"])
        {
            table[target] = ("MATLAB:invalidConversion", "Conversion to " + target + " from {0} is not possible.");
        }

        const string numericCharLogical = "Invalid data type. Argument must be numeric, char, or logical.";
        foreach (string verb in (string[])["int2str", "nnz", "abs", "isnan", "diff", "floor"])
        {
            table[verb] = ("MATLAB:math:mustBeNumericCharOrLogical", numericCharLogical);
        }

        foreach (string verb in (string[])["sum", "prod", "any", "all"])
        {
            table[verb] = ($"MATLAB:{verb}:wrongInput", "Invalid data type. First argument must be numeric or logical.");
        }

        foreach (string verb in (string[])["mean", "std", "var"])
        {
            table[verb] = ("MATLAB:sum:wrongInput", "Invalid data type. First argument must be numeric or logical.");
        }

        table["max"] = ("MATLAB:max:wrongInput", "First input array is an invalid data type.");
        table["min"] = ("MATLAB:min:wrongInput", "First input array is an invalid data type.");
        table["cumsum"] = ("MATLAB:cumsum:wrongInput", "Invalid data type. First input argument must be numeric or logical.");
        table["cumprod"] = ("MATLAB:cumprod:wrongInput", "Invalid data type. First input argument must be numeric or logical.");
        table["median"] = ("MATLAB:UndefinedFunction", "Undefined function 'sort' for input arguments of type '{0}'.");
        table["mode"] = ("MATLAB:UndefinedFunction", "Undefined function 'sort' for input arguments of type '{0}'.");
        table["find"] = ("MATLAB:UndefinedFunction", "Undefined function 'find' for input arguments of type '{0}'.");
        table["round"] = ("MATLAB:round:FirstArg", "First argument must be a numeric, logical, or char array.");
        table["sqrt"] = ("MATLAB:math:mustBeFloat", "Invalid data type. Argument must be double or single.");
        table["norm"] = ("MATLAB:norm:inputType", "First argument must be single or double.");
        table["num2str"] = ("MATLAB:num2str:nonNumericInput", "Input to num2str must be numeric.");
        table["mat2str"] = ("MATLAB:mat2str:NumericInput", "Input matrix must be a numeric array, character array, or string array.");
        table["rmfield"] = ("MATLAB:rmfield:Arg1NotStructArray", "S must be a structure array.");
        table["orderfields"] = ("MATLAB:orderfields:arg1NotStruct", "First argument must be a struct.");
        return table;
    }

    /// <summary>
    /// Wraps each verb in the two tables, and <c>sprintf</c>, <c>fprintf</c>, <c>setfield</c>,
    /// <c>isgraphics</c>, <c>cellfun</c>, <c>sort</c>, <c>class</c> and <c>max</c>/<c>min</c>, so
    /// MATLAB code meets R2025b's refusal. Registered after every other wrapper.
    /// </summary>
    internal static void RegisterR2025bRefusals(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        void Guard(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue?> check)
        {
            if (!env.Builtins.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function)
            {
                return;
            }

            IJgsCallable inner = existing.AsCallable;
            JgsValue[] Both(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
            {
                if (JgsRunningDialect.ThreadIsMatlab && check(args, line, col) is { } answered)
                {
                    return [answered];
                }

                // A verb that takes the output count (cellfun asked for none) is asked as it was.
                int asked = inner is BuiltinFunction { TakesOutputCount: true } ? wanted : Math.Max(wanted, 1);
                return inner is IJgsMultiCallable multi ? multi.CallMultiple(args, asked, line, col) : [inner.Call(args, line, col)];
            }

            var wrapper = new BuiltinFunction(name, (args, line, col) => Both(args, 1, line, col)[0])
            {
                MultiOutput = Both,
                BindsAnsAsStatement = inner is not BuiltinFunction { BindsAnsAsStatement: false },
                AutoCallsBare = inner is BuiltinFunction { AutoCallsBare: true },
                KnowsWhenDiscarded = inner is BuiltinFunction { KnowsWhenDiscarded: true },
                TakesOutputCount = inner is BuiltinFunction { TakesOutputCount: true },
            };
            if (inner is BuiltinFunction { KeepsStringArguments: true })
            {
                wrapper.KeepsStringArguments = true;
            }

            env.Builtins.Register(name, JgsValue.Function(wrapper));
        }

        foreach (string verb in ObjectRefusals.Keys.Union(CategoricalTextRefusals.Keys))
        {
            string name = verb;
            Guard(name, (args, line, col) =>
            {
                if (args.Count == 0)
                {
                    return null;
                }

                if (args[0].Type == JgsType.Object && ObjectRefusals.TryGetValue(name, out (string Id, string Words) refusal))
                {
                    throw new JgsRuntimeException(line, col, refusal.Id, string.Format(System.Globalization.CultureInfo.InvariantCulture, refusal.Words, args[0].ObjectClass.Name));
                }

                if (args[0].IsCategorical && CategoricalTextRefusals.TryGetValue(name, out refusal))
                {
                    throw new JgsRuntimeException(line, col, refusal.Id, refusal.Words);
                }

                return null;
            });
        }

        // A conversion in a format meets the object as the type it converts to (probe_123).
        foreach (string printer in (string[])["sprintf", "fprintf"])
        {
            Guard(printer, (args, line, col) =>
            {
                int formatAt = printer == "fprintf" && args.Count > 0 && args[0].Type == JgsType.Number ? 1 : 0;
                if (args.Count <= formatAt + 1 || args[formatAt].Type != JgsType.String)
                {
                    return null;
                }

                JgsValue? subject = args.Skip(formatAt + 1).FirstOrDefault(static a => a.Type == JgsType.Object);
                if (subject is null)
                {
                    return null;
                }

                string target = FirstConversionTarget(args[formatAt].AsString);
                throw new JgsRuntimeException(line, col, "MATLAB:invalidConversion",
                    $"Conversion to {target} from {subject.ObjectClass.Name} is not possible.");
            });
        }

        // setfield writes an object's property as obj.(name) = value would, on a copy of a value
        // object; a property the class lacks is R2025b's noPublicFieldForClass.
        Guard("setfield", (args, line, col) =>
        {
            if (args.Count != 3 || args[0].Type != JgsType.Object || args[0].IsObjectArray || args[1].Type != JgsType.String)
            {
                return null;
            }

            JgsValue target = args[0].ObjectClass.IsHandle ? args[0] : JgsValue.Share(args[0]);
            interpreter.WriteProperty(target, args[1].AsString, args[2], line, col);
            return target;
        });

        // isgraphics of an array of objects is false for each (probe_123).
        Guard("isgraphics", (args, line, col) =>
        {
            // A custom component is a graphics object, and one object asks its own road.
            if (args.Count == 0 || !args[0].IsObjectArray || args[0].ObjectClass.IsComponentContainer)
            {
                return null;
            }

            int count = args[0].ArrayLength;

            var flags = new JgsValue[count];
            Array.Fill(flags, JgsValue.False);
            return JgsMatrix.FromElements(flags, args[0].Rows, args[0].Cols);
        });

        // strrep over a categorical warns and hands the categorical back unchanged (probe_123).
        Guard("strrep", (args, line, col) =>
        {
            if (args.Count != 3 || !args.Any(static a => a.IsCategorical))
            {
                return null;
            }

            Warn(host, "MATLAB:strrep:InvalidInputType", "Inputs must be character vectors, cell arrays of character vectors, or string arrays.");
            return args[0];
        });

        Guard("cellfun", (args, line, col) =>
            args.Count >= 2 && args[1].IsCategorical
                ? throw new JgsRuntimeException(line, col, "MATLAB:cellfun:NotACell", "Input #2 expected to be a cell array, was categorical instead.")
                : null);

        // sort of a cell is a sort of names, and anything else in it is R2025b's refusal.
        Guard("sort", (args, line, col) =>
            args.Count >= 1 && args[0].Type == JgsType.Cell && args[0].AsCell.Any(static e => e.Type != JgsType.String || e.IsStringArray)
                ? throw new JgsRuntimeException(line, col, "MATLAB:sort:InputType", "Input argument must be a cell array of character vectors.")
                : null);

        // class(a.p) hands class several values, which outside a constructor is R2025b's refusal.
        Guard("class", (args, line, col) =>
            args.Count > 1
                ? throw new JgsRuntimeException(line, col, "MATLAB:dispatcher:invalidClassContext", "When called from outside a class constructor, the CLASS function must be called with one input.")
                : null);

        // isspace and isletter ask about characters (open item 129, probe_129): anything that is not
        // text answers false - one false for a categorical, a table or a time value, whose builtin
        // size is one object, and a false a place for numbers, cells, structs and arrays of objects.
        foreach (string characterClass in (string[])["isspace", "isletter"])
        {
            Guard(characterClass, (args, line, col) =>
            {
                if (args.Count != 1 || args[0].Type == JgsType.String || args[0].IsCharMatrix || args[0].IsStringArray)
                {
                    return null;
                }

                JgsValue subject = args[0];
                if (subject.IsCategorical || subject.IsTime || subject.Type is JgsType.Table or JgsType.Function or JgsType.External)
                {
                    return JgsValue.False;
                }

                int[] dims = SizeDims(subject);
                int count = dims.Aggregate(1, static (p, n) => p * n);
                var flags = new JgsValue[count];
                Array.Fill(flags, JgsValue.False);
                return count == 1 ? JgsValue.False : JgsMatrix.FromElementsDims(flags, dims);
            });
        }

        Guard("isstrprop", (args, line, col) =>
            args.Count >= 1 && args[0].IsCategorical
                ? throw new JgsRuntimeException(line, col, "MATLAB:isstrprop:UnexpectedType", "Unexpected data type.")
                : null);

        // max(a, b, dim) names a dimension R2025b does not take for two arrays.
        foreach (string extreme in (string[])["max", "min"])
        {
            Guard(extreme, (args, line, col) =>
                args.Count == 3 && !(args[1].Type == JgsType.Array && args[1].ArrayLength == 0) && args[2].Type != JgsType.String
                    ? throw new JgsRuntimeException(line, col, $"MATLAB:{extreme}:caseNotSupported", "Specifying the operating dimension is not supported for two input arrays.")
                    : null);
        }
    }

    /// <summary>The type the first conversion of a format turns its value into, in R2025b's words.</summary>
    private static string FirstConversionTarget(string format)
    {
        for (int i = 0; i < format.Length - 1; i++)
        {
            if (format[i] != '%')
            {
                continue;
            }

            int j = i + 1;
            if (format[j] == '%')
            {
                i = j;
                continue;
            }

            while (j < format.Length && "+-# 0123456789.*".Contains(format[j]))
            {
                j++;
            }

            if (j < format.Length)
            {
                return format[j] switch
                {
                    'd' or 'i' or 'u' or 'x' or 'X' or 'o' => "int64",
                    's' or 'c' => "text",
                    _ => "double",
                };
            }
        }

        return "double";
    }
}
