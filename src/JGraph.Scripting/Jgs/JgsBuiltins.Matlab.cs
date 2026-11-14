using System.Globalization;
using System.Linq;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The builtins a MATLAB script expects but JGS never needed, plus the multiple-output forms of the ones
/// it already had. They are registered in both dialects — a JGS script is welcome to call <c>strcmp</c>
/// — because a second, dialect-gated name table would be one more thing to drift.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// MATLAB functions JGraph knows about but does not implement. Naming them explicitly turns a
    /// baffling "not recognized" into an answer: this script needs something JGraph does not have.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> UnsupportedFunctions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["syms"] = "symbolic math",
        ["solve"] = "symbolic math",
        ["ode23"] = "stiffness-tuned ODE solvers — ode45 is implemented",
        ["fmincon"] = "constrained optimization (Optimization Toolbox)",
        ["lsqcurvefit"] = "nonlinear least squares (Optimization Toolbox)",
        ["parfeval"] = "parallel execution",
        ["gpuArray"] = "GPU arrays",
    };

    /// <summary>Whether <paramref name="name"/> is a MATLAB function JGraph deliberately does not have.</summary>
    internal static bool IsUnsupportedMatlabFunction(string name, out string what) =>
        UnsupportedFunctions.TryGetValue(name, out what!);

    /// <summary>Registers the MATLAB-facing builtins into <paramref name="env"/>.</summary>
    private static void RegisterMatlabBuiltins(
        JgsEnvironment env, JGraphScriptGlobals host, Random random, JgsRunningDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // The mustBe… family (M62). They are validators inside an arguments block and ordinary
        // builtins outside one, which is how MATLAB's are, so they register with everything else.
        RegisterValidators(Define);

        // R2025b's validateattributes, in its catalog's words (open item 309, ADR 0353). A string scalar is
        // its own class there - "" is a string, nonempty - so its arguments arrive as written.
        env.Builtins.Register("validateattributes", JgsValue.Function(new BuiltinFunction("validateattributes", ValidateAttributes)
        {
            KeepsStringArguments = true,
        }));

        // --- Numeric ----------------------------------------------------------------------------
        Define("rem", (args, line, col) =>
        {
            Arity("rem", args, 2, line, col);

            // rem keeps the sign of the dividend, where mod keeps the sign of the divisor.
            double divisor = Num("rem", args, 1, line, col);
            return MapNumeric("rem", args[0], x => ScalarRem(x, divisor), line, col);
        });

        Define("randn", (args, line, col) =>
        {
            ArityRange("randn", args, 0, 2, line, col);

            // randn(n), randn(r, c), and randn(size(x)) — the last is how a script matches an existing
            // vector's length, which is the common case in a measurement script.
            int count = args.Count switch
            {
                0 => 1,
                1 when args[0].Type == JgsType.Array => (int)ToDoubles("randn", args[0], line, col).Aggregate(1.0, static (a, b) => a * b),
                1 => Count("randn", args, 0, line, col),
                _ => Count("randn", args, 0, line, col) * Count("randn", args, 1, line, col),
            };

            var samples = new double[Math.Max(count, 0)];
            for (int i = 0; i < samples.Length; i++)
            {
                // Box-Muller: two uniforms in, one standard normal out.
                double u1 = 1.0 - random.NextDouble(); // in (0, 1], so Log is finite
                double u2 = random.NextDouble();
                samples[i] = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            }

            return Numbers(samples);
        });

        // Truncation towards zero, applied to both parts of a complex number as MATLAB's is (M81) —
        // the same widening floor, ceil and round took.
        MathX(Define, "fix", Math.Truncate, Always, static z => Componentwise(z, Math.Truncate));

        // repmat tiles in two dimensions: repmat(A, m, n) is m copies down by n across, and
        // repmat(A, m) is m by m. It predates shaped arrays (M40) and used to read only the last
        // count and lay the copies end to end, so repmat([1 2; 3 4], 2, 1) came back as a flat
        // four-element row instead of a 4-by-2.
        Define("repmat", (args, line, col) =>
        {
            // MATLAB code takes any number of counts and R2025b's checks (open item 127).
            if (JgsRunningDialect.ThreadIsMatlab)
            {
                return RepmatMatlab(args, line, col);
            }

            ArityRange("repmat", args, 2, 3, line, col);
            int down;
            int across;
            if (args.Count == 3)
            {
                down = Count("repmat", args, 1, line, col);
                across = Count("repmat", args, 2, line, col);
            }
            else
            {
                double[] counts = NumericVector("repmat", args, 1, line, col);
                if (counts.Length is not (1 or 2))
                {
                    throw new JgsRuntimeException(line, col,
                        "repmat takes a count, a [down across] pair, or two counts.");
                }

                down = (int)Math.Round(counts[0]);
                across = (int)Math.Round(counts[^1]);
            }

            // A negative count is no copies: repmat('a', -1, 1) is a 0-by-1 char (measured).
            down = Math.Max(0, down);
            across = Math.Max(0, across);

            JgsValue source = args[0];

            // A char row is one value here and 1-by-n characters in MATLAB, so tiling it as an element
            // built a grid of separate one-character pieces: repmat('ab', 1, 3) answered a 1-by-3
            // double where MATLAB answers the 1-by-6 char 'ababab'. repmat has always multiplied the
            // source's size, and the size of a char row is the size of its text.
            if (source.Type == JgsType.String)
            {
                return TiledCharRow(source.AsString, down, across);
            }

            // A string array does tile as elements — repmat("a", 1, 3) is three strings, not a longer
            // one — but the tag saying so lives on the wrapper, and the tiling mints a fresh one.
            if (source.IsStringArray)
            {
                return TiledStringArray(source, down, across);
            }

            // A cell tiles as elements too, and was falling through to the line below — which treats
            // anything that is not an array as one value and built a 1-by-2 double out of a 2-by-2
            // cell (M122). It is the same tiling as a string array's, differing only in what the
            // answer is wrapped in.
            if (source.Type == JgsType.Cell)
            {
                return TiledCell(source, down, across);
            }

            if (source.Type != JgsType.Array)
            {
                return JgsMatrix.BuildValues(down, across, (_, _) => source);
            }

            int rows = JgsMatrix.RowCount(source);
            int cols = JgsMatrix.ColCount(source);
            return JgsMatrix.BuildValues(
                rows * down, cols * across, (r, c) => JgsMatrix.At(source, r % rows, c % cols));
        });

        // --- Type predicates --------------------------------------------------------------------
        Define("isnumeric", (args, line, col) =>
        {
            Arity("isnumeric", args, 1, line, col);

            // Through the shared helper rather than a test of its own (M64). The two had drifted:
            // this one asked whether the elements were numbers, which a datetime's milliseconds are,
            // so isnumeric(datetime) answered true where class(datetime) said 'datetime'. That is the
            // same disagreement islogical and class were made to share a helper over. A logical is not
            // numeric (R2025b: isnumeric(true) is false; k14_mps_json's x_true), though arithmetic takes it.
            return JgsValue.Bool(IsNumericValue(args[0]) && !IsLogicalValue(args[0]));
        });

        Define("ischar", (args, line, col) =>
        {
            Arity("ischar", args, 1, line, col);

            // A char matrix is char too (M105) — it is the stack of char rows char('a', 'bcd') builds,
            // and asking only about the char row missed every one of them.
            return JgsValue.Bool(args[0].Type == JgsType.String || args[0].IsCharMatrix);
        });

        Define("islogical", (args, line, col) =>
        {
            Arity("islogical", args, 1, line, col);

            // A mask an imaging builtin produced — edge, imbinarize, bwareaopen — is tagged logical, so
            // the predicate every MATLAB masking example opens with answers true for it. class() reads
            // the same helper, so the two can no longer drift apart.
            return JgsValue.Bool(IsLogicalValue(args[0]));
        });

        Define("iscell", (args, line, col) =>
        {
            Arity("iscell", args, 1, line, col);
            return JgsValue.Bool(args[0].Type == JgsType.Cell);
        });

        Define("isstruct", (args, line, col) =>
        {
            Arity("isstruct", args, 1, line, col);
            return JgsValue.Bool(IsStructValue(args[0]));
        });

        // --- Strings ----------------------------------------------------------------------------
        // strcmp, strcmpi and strtrim are declared in JgsBuiltins.TextFamily.cs.
        Define("strsplit", (args, line, col) => SplitText(args, 1, line, col)[0]);
        Define("strjoin", (args, line, col) => JoinText(args, line, col));
        Define("num2str", (args, line, col) => NumberText(args, line, col));

        // num2str writes a number for a person to read; mat2str writes one for the language to read
        // back, which is why it keeps the brackets and the semicolons (M52 wave E).
        // Marked string-aware because the whole point of mat2str is to write text eval reads
        // back as the same value, and a string demoted to a char row on the way in cannot be told
        // from one that was written as a char row (M122).
        env.Builtins.Register("mat2str", JgsValue.Function(
            new BuiltinFunction("mat2str", (args, line, col) => MatrixText(args, line, col))
            { KeepsStringArguments = true }));
        Define("int2str", (args, line, col) => WholeNumberText(args, line, col));

        // str2double reads MATLAB's spelling of a number and nothing else — see NumberSpelledBy for
        // what that admits. A value that is not text is NaN, not an error: NaN is the answer the
        // function promises for "not a number", and a caller filtering with isnan relies on it.
        Define("str2double", (args, line, col) =>
        {
            Arity("str2double", args, 1, line, col);
            return NumbersSpelledBy(args[0]);
        });

        // --- Errors -----------------------------------------------------------------------------
        // error itself lives in JgsBuiltins.Errors.cs (M62), where the identifier and the MException
        // form are defined together. It is declared there rather than here because it is re-declared
        // over this registration, and two implementations of one name is one too many.
        // warning is three verbs under one name (measured R2025b): a message, raised through the same
        // reader error uses so 'pkg:id' before the format is an identifier; a state change,
        // warning('off' | 'on', id), which hands back the state it replaced when asked to; and a
        // question, warning('query', id), which prints the state as a statement and answers a
        // struct as an expression. The state lives on the host (JgsWarningState), and a warning
        // that is switched off is still what lastwarn reports.
        env.Builtins.Register("warning", JgsValue.Function(new BuiltinFunction("warning",
            (args, line, col) =>
            {
                JgsValue[] answers = Warning(env, host, dialect, args, 1, line, col);
                return answers.Length > 0 ? answers[0] : JgsValue.Null;
            })
        {
            MultiOutput = (args, wanted, line, col) => Warning(env, host, dialect, args, wanted, line, col),
            KnowsWhenDiscarded = true,
            BindsAnsAsStatement = false,
        }));

        Define("assert", (args, line, col) =>
        {
            ArityRange("assert", args, 1, 8, line, col);
            if (args[0].IsTruthy)
            {
                return JgsValue.Null;
            }

            throw new JgsRuntimeException(line, col,
                args.Count > 1
                    ? FormatMessage(env, host, dialect, "assert", args, 1, identified: false, line, col)
                    : "Assertion failed.");
        });

        // --- Cells and structs ------------------------------------------------------------------
        Define("cell", (args, line, col) =>
        {
            ArityRange("cell", args, 1, 2, line, col);
            if (args.Count == 1 && args[0].Type == JgsType.External)
            {
                return Net.NetBuiltinConversions.Cell(args[0], line, col); // a .NET array's elements (ADR 0174)
            }

            // cell(n) is an n-element row; cell(r, c) carries its grid shape (M41), so C{r, c}
            // addresses the element MATLAB means — column-major over the flat storage, like arrays.
            int rows = Count("cell", args, 0, line, col);
            int cols = args.Count == 2 ? Count("cell", args, 1, line, col) : rows;
            if (args.Count == 1)
            {
                (rows, cols) = (1, rows);
            }

            // Each element is R2025b's 0-by-0 double (open item 128: whos counts them as never set).
            var elements = new JgsValue[rows * cols];
            for (int i = 0; i < elements.Length; i++)
            {
                elements[i] = JgsRunningDialect.ThreadIsMatlab ? JgsEmpty.Zero() : JgsValue.Array(System.Array.Empty<JgsValue>());
            }

            JgsValue built = JgsValue.Cell(elements);
            built.Reshape(rows, cols);
            return built;
        });

        Define("struct", (args, line, col) =>
        {
            // struct([]) is the 0-by-0 struct with no fields (V6, #85): what a script starts an
            // accumulation from, and what isempty and a loop over numel treat as nothing.
            if (args.Count == 1 && args[0].Type == JgsType.Array && args[0].ArrayLength == 0 && !args[0].IsStringArray)
            {
                return JgsValue.StructArray(new JgsStructArray([]), 0, 0);
            }

            // struct(c) of a categorical is R2025b's own storage, with the warning any object gives
            // (open item 129, probe_129): the codes in the narrowest unsigned class that holds the
            // category count, 0 for an undefined element, and the category names as a column.
            // struct(on) of an OnOffSwitchState is its logical Data, with the same warning (probe_129b).
            if (args.Count == 1 && args[0].AsExternalOrNull() is OnOffSwitchStateValue states && JgsRunningDialect.ThreadIsMatlab)
            {
                Warn(host, "MATLAB:structOnObject",
                    "Calling STRUCT on an object prevents the object from hiding its implementation details and should " +
                    "thus be avoided. Use DISP or DISPLAY to see the visible public details of an object. See 'help " +
                    "struct' for more information.");
                return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal) { ["Data"] = states.Logical() });
            }

            if (args.Count == 1 && args[0].IsCategorical && JgsRunningDialect.ThreadIsMatlab)
            {
                Warn(host, "MATLAB:structOnObject",
                    "Calling STRUCT on an object prevents the object from hiding its implementation details and should " +
                    "thus be avoided. Use DISP or DISPLAY to see the visible public details of an object. See 'help " +
                    "struct' for more information.");
                return CategoricalStorage(args[0]);
            }

            // struct(obj) is a struct of the object's properties, each read as a dot would read it,
            // so a property with a get method is its answer (V6, #28, measured: the getters run in
            // declaration order); a Dependent property nothing can answer for is left out, and so is
            // one whose getter refuses (open item 29, measured). R2025b warns MATLAB:structOnObject
            // first, and answers an array of objects from its first, 1-by-1 (measured).
            JgsObject? instance = args.Count != 1 ? null
                : args[0].IsObjectArray ? (args[0].ArrayLength > 0 ? args[0].AsObjectArray.Elements[0] : null) // an array of value objects: its first (open item 65)
                : args[0].Type == JgsType.Object ? args[0].AsObject
                : args[0].Type == JgsType.Array && args[0].ArrayLength > 0 && args[0].ElementAt(0).Type == JgsType.Object
                    ? args[0].ElementAt(0).AsObject
                    : null;
            if (instance is not null)
            {
                if (JgsRunningDialect.ThreadIsMatlab)
                {
                    Warn(host, "MATLAB:structOnObject",
                        "Calling STRUCT on an object prevents the object from hiding its implementation details and should " +
                        "thus be avoided. Use DISP or DISPLAY to see the visible public details of an object. See 'help " +
                        "struct' for more information.");
                }

                var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                foreach (ClassProperty property in instance.Class.Properties)
                {
                    JgsValue? held;
                    try
                    {
                        held = instance.Class.DisplayValue(instance, property);
                    }
                    catch (JgsException)
                    {
                        continue;
                    }

                    if (held is not null)
                    {
                        fields[property.Spec.Name] = instance.Class.TryGetter(property.Spec.Name, out _) ? held : JgsValue.Share(held);
                    }
                }

                return JgsValue.Struct(fields);
            }

            if (args.Count % 2 != 0)
            {
                throw new JgsRuntimeException(line, col, "struct takes name/value pairs.");
            }

            return BuildStruct(args, line, col);
        });

        // feature('getpid'): this process's id as a double, the key matched in any case and further
        // arguments ignored (open item 27, measured). feature is undocumented and has hundreds of
        // keys, so another key is refused in JGraph's words rather than called "not found".
        Define("feature", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
            }

            if (IsTextScalar(args[0]) && TextOf(args[0]).Equals("getpid", StringComparison.OrdinalIgnoreCase))
            {
                return JgsValue.Number(Environment.ProcessId);
            }

            // feature('numcores'): the physical cores of every kind, the key in any case and a further
            // argument refused (R2025b, open item 169: 14 on a 6 P-core, 8 E-core machine). R2025b's
            // statement form also prints four or five lines of what it detected; JGraph's does not.
            if (IsTextScalar(args[0]) && TextOf(args[0]).Equals("numcores", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Count > 1)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:maxrhs", "Too many input arguments.");
                }

                return JgsValue.Number(JGraph.Numerics.LinearAlgebra.Native.ProcessorTopology.PhysicalCoreCount() ?? Environment.ProcessorCount);
            }

            throw new JgsRuntimeException(line, col, "JGraph:feature:unsupported",
                "JGraph answers feature('getpid') and feature('numcores') and no other feature key.");
        });

        Define("fieldnames", (args, line, col) =>
        {
            Arity("fieldnames", args, 1, line, col);

            // A column, as MATLAB's is (measured): fieldnames(s)' is the row a script compares with.
            // On an object it is the property names, Dependent ones included, and runs no getter (V6, #28).
            // A .NET object's are its properties and fields, as properties lists them (ADR 0174).
            IEnumerable<string> listed = args[0].Type == JgsType.Object
                ? args[0].ObjectClass.ListedProperties.Select(static p => p.Spec.Name)
                : args[0].AsExternalOrNull() is NetObject net
                    ? Net.NetDisplay.MemberNames(net)
                    : IsLibValue(args[0].AsExternalOrNull())
                    ? LibPropertyNames(args[0].AsExternal)
                    : IsDeviceValue(args[0].AsExternalOrNull())
                    ? DevicePropertyNames(args[0].AsExternal)
                    : StructOf("fieldnames", args[0], line, col).Keys;
            JgsValue names = JgsValue.Cell(listed.Select(JgsValue.Str).ToArray());
            names.Reshape(names.AsCell.Length, 1);
            return names;
        });

        Define("isfield", (args, line, col) =>
        {
            Arity("isfield", args, 2, line, col);
            if (args[0].Type != JgsType.Struct)
            {
                return JgsValue.Bool(false);
            }

            // A cell of names asks about each in turn, and the answer is a logical of the same shape.
            Dictionary<string, JgsValue> fields = args[0].AsStruct;
            if (args[1].Type == JgsType.Cell)
            {
                JgsValue[] names = args[1].AsCell;
                var flags = new JgsValue[names.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    flags[i] = JgsValue.Bool(names[i].Type == JgsType.String && fields.ContainsKey(names[i].AsString));
                }

                return JgsValue.Shaped(flags, args[1].Rows, args[1].Cols);
            }

            return JgsValue.Bool(fields.ContainsKey(Str("isfield", args, 1, line, col)));
        });

        Define("rmfield", (args, line, col) =>
        {
            Arity("rmfield", args, 2, line, col);
            if (args[0].Type != JgsType.Struct)
            {
                throw new JgsRuntimeException(line, col, "S must be a structure array.");
            }

            string[] doomed = FieldNameList("rmfield", args[1], line, col);
            if (JgsRunningDialect.ThreadIsMatlab)
            {
                doomed = RemovableFieldNames(args[0].AsStructArray.FieldNames, args[1], doomed, line, col);
            }

            return MapStructElements("rmfield", args[0], line, col, element =>
            {
                // A field that is not there is simply not removed: R2025b's rmfield answers the
                // struct unchanged (value-ownership plan, the order matrix's
                // w_dynfield_sub_shrink_rhs_shrink row, recorded from R2025b).

                // Rebuilt from the kept fields rather than copied and pruned: a Dictionary refills
                // a removed slot with the next field added, which put a later s.g ahead of the
                // fields that were there (V3b, the order matrix's dynfield rows).
                var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                foreach ((string name, JgsValue held) in element)
                {
                    if (System.Array.IndexOf(doomed, name) < 0)
                    {
                        fields[name] = held;
                    }
                }

                return fields;
            });
        });

        Define("orderfields", (args, line, col) =>
        {
            ArityRange("orderfields", args, 1, 2, line, col);

            // With no order given the fields sort by name, which is the whole point of the verb:
            // two structs built in different orders compare and display alike afterwards.
            string[] order = args.Count > 1
                ? FieldNameList("orderfields", args[1], line, col)
                : [.. StructOf("orderfields", args[0], line, col).Keys.OrderBy(n => n, StringComparer.Ordinal)];

            // An order of as many names as there are fields that is not a rearrangement of them is
            // R2025b's refusal in its words (open item 202, ADR 0351, measured).
            if (JgsRunningDialect.ThreadIsMatlab && args.Count > 1 && (args[1].Type == JgsType.Cell || args[1].IsStringArray)
                && args[0].Type == JgsType.Struct && args[0].AsStructArray.FieldNames is { } present && present.Length == order.Length
                && !present.OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(order.OrderBy(n => n, StringComparer.Ordinal), StringComparer.Ordinal))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:orderfields:CellStrMismatchFieldnames",
                    "Text in second input must match fieldnames of the struct.");
            }

            return MapStructElements("orderfields", args[0], line, col, element =>
            {
                var ordered = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
                foreach (string name in order)
                {
                    if (!element.TryGetValue(name, out JgsValue? held))
                    {
                        throw new JgsRuntimeException(line, col,
                            $"orderfields: this struct has no field '{name}'.");
                    }

                    ordered[name] = held;
                }

                if (ordered.Count != element.Count)
                {
                    throw new JgsRuntimeException(line, col,
                        "orderfields: the order must name every field exactly once.");
                }

                return ordered;
            });
        });

        Define("getfield", (args, line, col) =>
        {
            ArityRange("getfield", args, 2, 2, line, col);
            if (args[0].Type == JgsType.Object && !args[0].IsObjectArray)
            {
                // getfield(obj, 'p') reads the property, as R2025b's does (open item 116).
                string property = Str("getfield", args, 1, line, col);
                return args[0].AsObject.Fields.TryGetValue(property, out JgsValue? read)
                    ? read
                    : throw new JgsRuntimeException(line, col, "MATLAB:noSuchMethodOrField", $"Unrecognized method, property, or field '{property}' for class '{args[0].ObjectClass.Name}'.");
            }

            Dictionary<string, JgsValue> fields = StructOf("getfield", args[0], line, col);
            string name = Str("getfield", args, 1, line, col);

            // R2025b's getfield is f = s.(deblank(name)), so it reads a dynamic field's way: an absent
            // name is MATLAB:nonExistentField, and a present one no field may have is read, as a dot on
            // the variable reads it (open item 202, ADR 0351; Interpreter.ThroughParen).
            if (JgsRunningDialect.ThreadIsMatlab)
            {
                name = Deblanked(name);
                return fields.TryGetValue(name, out JgsValue? read)
                    ? read
                    : throw new JgsRuntimeException(line, col, "MATLAB:nonExistentField", $"Unrecognized field name \"{name}\".");
            }

            return fields.TryGetValue(name, out JgsValue? held)
                ? held
                : throw new JgsRuntimeException(line, col, $"getfield: this struct has no field '{name}'.");
        });

        Define("setfield", (args, line, col) =>
        {
            Arity("setfield", args, 3, line, col);

            // setfield keeps its string arguments, so the value stays a string (open item 201) and
            // the name may be one. In the MATLAB dialect it is R2025b's s.(deblank(name)) = v: the
            // name loses its trailing blanks and must be one a field can have (open item 202, ADR 0351).
            string name = IsStringScalar(args[1]) ? TextOf(args[1]) : Str("setfield", args, 1, line, col);
            if (JgsRunningDialect.ThreadIsMatlab)
            {
                name = Deblanked(name);
                if (!IsValidFieldName(name))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:AddField:InvalidFieldName", $"Invalid field name: '{name}'.");
                }
            }

            JgsValue held = args[2];

            // setfield answers with a changed copy and leaves its argument alone, which is what
            // makes it usable in an expression: s = setfield(s, 'a', 1). The new field is an entry
            // over the caller's value, so it takes a share (M2).
            return MapStructElements("setfield", args[0], line, col, element =>
                new Dictionary<string, JgsValue>(element, StringComparer.Ordinal) { [name] = JgsValue.Share(held) });
        });

        Define("num2cell", (args, line, col) =>
        {
            Arity("num2cell", args, 1, line, col);

            // A struct array's elements, one per cell, in its shape (stage K20: a metaclass's lists).
            if (args[0].Type == JgsType.Struct)
            {
                JgsValue[] elements = args[0].AsStructArray.Elements
                    .Select(fields => JgsValue.Struct(new Dictionary<string, JgsValue>(fields, StringComparer.Ordinal)))
                    .ToArray();
                JgsValue structs = JgsValue.Cell(elements);
                structs.Reshape(args[0].Rows, args[0].Cols);
                return structs;
            }

            // M2: over a cell the elements are the argument's own children, and each slot of the
            // answer is an entry, so it takes a share; over an array they are fresh numbers, for
            // which a share is the value itself. A 2-D argument keeps its shape (open item 116).
            JgsValue cells = JgsValue.Cell(Elements("num2cell", args[0], line, col).Select(JgsValue.Share).ToArray());
            if (args[0].IsNd && args[0].Type == JgsType.Object && cells.AsCell.Length == args[0].ArrayLength)
            {
                cells.ReshapeDims(args[0].Dims); // an array of objects with a third dimension (open item 121)
            }
            else if (args[0].IsCharMatrix || (args[0].Type == JgsType.String && !args[0].IsStringArray))
            {
                int[] shape = SizeDims(args[0]);
                if (shape.Length == 2 && cells.AsCell.Length == shape[0] * shape[1])
                {
                    cells.Reshape(shape[0], shape[1]);
                }
            }
            else if (args[0].Rows > 1 && args[0].Cols > 1 && cells.AsCell.Length == args[0].Rows * args[0].Cols)
            {
                cells.Reshape(args[0].Rows, args[0].Cols);
            }

            return cells;
        });

        Define("cell2mat", (args, line, col) =>
        {
            Arity("cell2mat", args, 1, line, col);
            if (args[0].Type != JgsType.Cell)
            {
                throw new JgsRuntimeException(line, col, $"cell2mat expects a cell array, but got a {args[0].TypeName}.");
            }

            // A cell of objects of one class joins into an array of them (open item 116).
            if (args[0].AsCell.Length > 0 && args[0].AsCell.All(static v => v.Type == JgsType.Object)
                && ObjectsAsArray("cell2mat", args[0].AsCell, args[0].Rows, args[0].Cols, line, col) is { } objects)
            {
                return objects;
            }

            var flat = new List<JgsValue>();
            foreach (JgsValue element in args[0].AsCell)
            {
                if (element.Type == JgsType.Array)
                {
                    for (int i = 0; i < element.ArrayLength; i++)
                    {
                        flat.Add(element.ElementAt(i));
                    }
                }
                else
                {
                    flat.Add(element);
                }
            }

            return JgsValue.Array(flat.ToArray());
        });

        // --- Applying functions -----------------------------------------------------------------
        //
        // feval is declared with the eval family: a name it is handed resolves through the
        // interpreter, the way the same name written as a call would.
        //
        // cellfun(..., 'UniformOutput', false) hands back a cell instead of an array — without it,
        // every result has to be a scalar, exactly as MATLAB insists.
        Define("cellfun", (args, line, col) => ApplyOverCells(env, args, 1, line, col)[0]);

        // --- Index arithmetic -------------------------------------------------------------------
        Define("sub2ind", (args, line, col) =>
        {
            Arity("sub2ind", args, 3, line, col);
            double[] shape = ToDoubles("sub2ind", args[0], line, col);
            if (shape.Length < 2)
            {
                throw new JgsRuntimeException(line, col, "sub2ind: the size must have at least two dimensions.");
            }

            int rows = (int)shape[0];
            int row = Count("sub2ind", args, 1, line, col) - dialect.IndexBase;
            int column = Count("sub2ind", args, 2, line, col) - dialect.IndexBase;
            return JgsValue.Number((column * rows) + row + dialect.IndexBase);
        });

        Define("ind2sub", (args, line, col) =>
        {
            Arity("ind2sub", args, 2, line, col);
            double[] shape = ToDoubles("ind2sub", args[0], line, col);
            if (shape.Length < 2)
            {
                throw new JgsRuntimeException(line, col, "ind2sub: the size must have at least two dimensions.");
            }

            int rows = (int)shape[0];
            int flat = Count("ind2sub", args, 1, line, col) - dialect.IndexBase;
            return JgsValue.Array([
                JgsValue.Number((flat % rows) + dialect.IndexBase),
                JgsValue.Number((flat / rows) + dialect.IndexBase),
            ]);
        });

        WrapFormatters(env, host, dialect);
        RegisterMultiOutputForms(env, dialect);
    }

    /// <summary>
    /// <c>repmat</c> of a char row: <paramref name="down"/> by <paramref name="across"/> copies of the
    /// text, answered as the one longer piece of text MATLAB answers rather than as a grid of pieces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The size arithmetic is repmat's own — the answer is (1 × down)-by-(n × across) — and the two
    /// empty corners fall straight out of it. An empty char row is 0-by-0 here exactly as it is in
    /// MATLAB, so repeating one is still nothing however large the counts; and a zero count leaves a
    /// dimension empty while the other keeps the size it was multiplied to, which is why
    /// <c>repmat('abc', 0, 2)</c> is 0-by-6 and not 0-by-0.
    /// </para>
    /// <para>
    /// A single row comes back as <see cref="JgsType.String"/> — the representation a char row has
    /// always had here — and anything taller as a real char matrix, which is the same collapse
    /// <see cref="WrapCharMatrix"/> performs for a value read out of one.
    /// </para>
    /// </remarks>
    private static JgsValue TiledCharRow(string text, int down, int across)
    {
        int height = (text.Length == 0 ? 0 : 1) * down;
        int width = text.Length * across;

        // Asked before the one-row case, which would answer '' for it (open item 31).
        if (height == 1 && width == 0)
        {
            return JgsValue.CharMatrix([string.Empty]); // repmat('a', 1, 0) is the 1-by-0 char (measured)
        }

        if (height == 1)
        {
            return JgsValue.Str(string.Concat(Enumerable.Repeat(text, across)));
        }

        if (height == 0 && width == 0)
        {
            return JgsValue.Str(string.Empty); // the 0-by-0 char row '' already is
        }

        var codes = new JgsValue[height * width];
        for (int c = 0; c < width; c++)
        {
            for (int r = 0; r < height; r++)
            {
                codes[(c * height) + r] = JgsValue.Number(text[c % text.Length]);
            }
        }

        return JgsValue.Shaped(codes, height, width).MarkCharMatrix();
    }

    /// <summary>
    /// <c>repmat</c> of a string array: the elements tile the way any other array's do, and the answer
    /// is marked a string array again because the tag lives on the wrapper the tiling replaced.
    /// </summary>
    private static JgsValue TiledStringArray(JgsValue source, int down, int across)
    {
        int rows = JgsMatrix.RowCount(source);
        int cols = JgsMatrix.ColCount(source);
        int height = rows * down;
        int width = cols * across;
        var elements = new JgsValue[height * width];
        for (int c = 0; c < width; c++)
        {
            for (int r = 0; r < height; r++)
            {
                elements[(c * height) + r] = JgsMatrix.At(source, r % rows, c % cols);
            }
        }

        return JgsValue.StringArray(elements, height, width);
    }

    /// <summary>A cell tiled down-by-across, in the cell it came in.</summary>
    private static JgsValue TiledCell(JgsValue source, int down, int across)
    {
        int rows = JgsMatrix.RowCount(source);
        int cols = JgsMatrix.ColCount(source);
        int height = rows * down;
        int width = cols * across;
        var elements = new JgsValue[height * width];
        for (int c = 0; c < width; c++)
        {
            for (int r = 0; r < height; r++)
            {
                // M2: each tile's slot is an entry over the source's child, so it takes a share —
                // `d = repmat({v}, 1, 2); d{1}(1) = 7` must leave d{2} and v alone.
                elements[(c * height) + r] = JgsValue.Share(JgsMatrix.At(source, r % rows, c % cols));
            }
        }

        JgsValue tiled = JgsValue.Cell(elements);
        tiled.Reshape(height, width);
        return tiled;
    }

    /// <summary>
    /// MATLAB's quotes do not decode escapes, but its formatting functions do: <c>fprintf('a\n')</c>
    /// prints a line break even though the literal holds a backslash and an 'n'. JGS decodes escapes in
    /// the literal itself, so only a MATLAB call needs this pass — and only on the format string.
    /// Which it is, is the calling code's dialect, read at the call (V11, ADR 0172): a <c>.m</c>
    /// script run from a JGS session prints its newline.
    /// </summary>
    /// <remarks>
    /// An escape MATLAB does not recognise ends the format string there rather than passing through:
    /// the warning goes out once for the call, and the shortened format is what <c>sprintf</c> then
    /// works from — which is also what makes the truncation repeat correctly, since MATLAB stops each
    /// pass over a cycling format at the same place (ADR 0148).
    /// </remarks>
    private static void WrapFormatters(JgsEnvironment env, JGraphScriptGlobals host, JgsRunningDialect dialect)
    {
        foreach (string name in new[] { "sprintf", "fprintf" })
        {
            if (!env.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function)
            {
                continue;
            }

            IJgsCallable inner = existing.AsCallable;
            env.Builtins.Register(name, JgsValue.Function(Wrapping(name, inner, (args, line, col) =>
            {
                if (!dialect.IsMatlab)
                {
                    return inner.Call(args, line, col); // JGS decoded its escapes in the literal
                }

                // The format is normally first; fprintf(fid, fmt, …) shifts it one slot right. It may
                // be a string scalar, now that the two keep their string arguments (open item 306).
                int at = args.Count > 0 && (args[0].Type == JgsType.String || IsStringScalar(args[0]))
                    ? 0
                    : args.Count > 1 && args[0].Type is JgsType.Number or JgsType.Bool
                        && (args[1].Type == JgsType.String || IsStringScalar(args[1]))
                        ? 1
                        : -1;
                if (at < 0)
                {
                    return inner.Call(args, line, col);
                }

                string format = JgsFormatEscapes.Decode(TextOf(args[at]), out JgsFormatEscapes.Fault? fault);
                if (fault is { } bad)
                {
                    Warn(env, host, bad.Message, line, col);
                }

                var unescaped = new JgsValue[args.Count];
                for (int i = 0; i < args.Count; i++)
                {
                    unescaped[i] = i == at ? JgsValue.Str(format) : args[i];
                }

                return inner.Call(unescaped, line, col);
            })));
        }
    }

    /// <summary>
    /// Wraps the builtins MATLAB scripts routinely call with two outputs — <c>[r, c] = size(x)</c>,
    /// <c>[v, i] = max(x)</c>, <c>[s, i] = sort(x)</c> — so that form works while the single-value form
    /// keeps behaving exactly as it did.
    /// </summary>
    private static void RegisterMultiOutputForms(JgsEnvironment env, JgsRunningDialect dialect)
    {
        // A guard runs before either road, one answer or several, so a refusal is the same for both.
        void Wrap(
            string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both, bool takesCount = false,
            Action<IReadOnlyList<JgsValue>, int, int>? guard = null)
        {
            if (!env.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function)
            {
                return;
            }

            IJgsCallable single = existing.AsCallable;
            Func<IReadOnlyList<JgsValue>, int, int, JgsValue> one = guard is null
                ? single.Call
                : (args, line, col) =>
                {
                    guard(args, line, col);
                    return single.Call(args, line, col);
                };
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> several = guard is null
                ? both
                : (args, wanted, line, col) =>
                {
                    guard(args, line, col);
                    return both(args, wanted, line, col);
                };
            env.Builtins.Register(name, JgsValue.Function(
                new BuiltinFunction(name, one) { MultiOutput = several, TakesOutputCount = takesCount }));
        }

        // deal exists only to feed several outputs at once, so it is declared where the several-output
        // forms are (M52 wave E). One value is handed to every output; several must match them.
        env.Builtins.Register("deal", JgsValue.Function(new BuiltinFunction(
            "deal",
            (args, line, col) => Dealt(args, 1, line, col)[0])
        {
            MultiOutput = Dealt,
            // V6: deal hands its arguments on as they are, so a string scalar stays a string
            // (it was read as text, and came out a char row).
            KeepsStringArguments = true,
        }));

        // An animated line's points come back one coordinate per output, and how many there are is
        // fixed by the line rather than by the call — so a flat line asked for three says so.
        Wrap("getpoints", (args, wanted, line, col) =>
        {
            JgsValue[] coordinates = GetPoints(args, line, col);
            return wanted <= coordinates.Length
                ? coordinates
                : throw new JgsRuntimeException(line, col,
                    $"getpoints: this line has {coordinates.Length} coordinates, not {wanted}.");
        });

        // A dimension that is not a number is R2025b's refusal, in its words (open item 129), asked for
        // one answer or several: size(lh.Source) over a column of listeners hands size a cell. A
        // matfile's size(m, 'v') names a variable.
        static void RefuseTextDimensions(IReadOnlyList<JgsValue> args, int line, int col)
        {
            if (JgsRunningDialect.ThreadIsMatlab && !(args.Count == 2 && IsMatFile(args[0]))
                && args.Skip(1).Any(static a => a.Type is not (JgsType.Number or JgsType.Bool or JgsType.Array) || a.IsStringArray || a.IsCharMatrix))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:size:invalidDim",
                    "Dimension argument must be a positive integer scalar or a vector of positive integers. When an empty dimension argument is specified, it must be a 0-by-0, 1-by-0, or 0-by-1 array.");
            }
        }

        Wrap("size", (args, wanted, line, col) =>
        {
            JgsValue result = SingleOf(env, "size", args, line, col);
            if (result.Type != JgsType.Array)
            {
                return [result];
            }

            if (args.Count > 1)
            {
                // The call already said which dimensions it wants, so each output is one of them
                // rather than a share of every dimension there is — no folding, no padding.
                var picked = new JgsValue[System.Math.Max(1, wanted)];
                for (int i = 0; i < picked.Length; i++)
                {
                    picked[i] = i < result.ArrayLength ? result.ElementAt(i) : JgsValue.Number(1);
                }

                return picked;
            }

            var dimensions = new double[result.ArrayLength];
            for (int i = 0; i < dimensions.Length; i++)
            {
                dimensions[i] = result.ElementAt(i).AsNumber;
            }

            // MATLAB pads missing dimensions with 1 and folds the ones past the last requested
            // output into it: [r, c] = size(rgb) reports c = width * channels.
            var outputs = new JgsValue[wanted];
            for (int i = 0; i < wanted; i++)
            {
                double dim = i < dimensions.Length ? dimensions[i] : 1;
                if (i == wanted - 1)
                {
                    for (int j = wanted; j < dimensions.Length; j++)
                    {
                        dim *= dimensions[j];
                    }
                }

                outputs[i] = JgsValue.Number(dim);
            }

            return outputs;
        }, guard: RefuseTextDimensions);

        Wrap("max", (args, _, line, col) => ExtremeWithIndex(env, "max", args, dialect, line, col));
        Wrap("min", (args, _, line, col) => ExtremeWithIndex(env, "min", args, dialect, line, col));

        Wrap("sort", (args, _, line, col) =>
        {
            JgsValue sorted = SingleOf(env, "sort", args, line, col);
            double[] original = ToDoubles("sort", args[0], line, col);
            double[] ordered = ToDoubles("sort", sorted, line, col);

            // The permutation: for each sorted position, where its value came from. Values already
            // taken are skipped so repeated values map to distinct sources.
            var used = new bool[original.Length];
            var order = new JgsValue[ordered.Length];
            for (int i = 0; i < ordered.Length; i++)
            {
                for (int j = 0; j < original.Length; j++)
                {
                    if (!used[j] && original[j].Equals(ordered[i]))
                    {
                        used[j] = true;
                        order[i] = JgsValue.Number(j + dialect.IndexBase);
                        break;
                    }
                }
            }

            return [sorted, JgsValue.Array(order)];
        });

        Wrap("find", (args, wanted, line, col) => FindSubscripts(args, dialect, wanted, line, col));

        // [C, ia, ic] = unique(...). Wrapped here rather than declared with its outputs because the
        // one-output form is registered with the base builtins, before this file runs.
        Wrap("unique", (args, wanted, line, col) => UniqueParts(args, dialect, wanted, line, col));

        // [C, matches] = strsplit(...) reports the delimiters it actually cut on, and
        // [a, b] = cellfun(...) asks each element's function for that many answers - none for a
        // statement (V9.1).
        Wrap("strsplit", (args, wanted, line, col) => SplitText(args, wanted, line, col));
        Wrap("cellfun", (args, wanted, line, col) => ApplyOverCells(env, args, wanted, line, col), takesCount: true);

        Wrap("ind2sub", (args, _, line, col) =>
        {
            JgsValue pair = SingleOf(env, "ind2sub", args, line, col);
            return [pair.ElementAt(0), pair.ElementAt(1)];
        });

        // meshgrid and ndgrid compute their whole set of grids as one array value; the wrapped form
        // peels that set apart for MATLAB's [X, Y, Z] = meshgrid(x, y, z). The one-output form is X
        // alone in MATLAB, while JGS keeps the set — that is what 'let [X, Y] = meshgrid(x, y)'
        // destructures. M59 made both names N-dimensional, so the peeling counts the grids rather
        // than assuming there are two of them.
        foreach (string name in (string[])["meshgrid", "ndgrid"])
        {
            if (!env.TryGet(name, out JgsValue grid) || grid.Type != JgsType.Function)
            {
                continue;
            }

            IJgsCallable setForm = grid.AsCallable;

            // The one-output form is decided by the calling code's dialect at the call (V11, ADR
            // 0172): X alone for MATLAB code, the whole set for JGS's destructuring.
            Func<IReadOnlyList<JgsValue>, int, int, JgsValue> single = (args, line, col) =>
            {
                JgsValue set = setForm.Call(args, line, col);
                return dialect.IsMatlab ? set.ElementAt(0) : set;
            };
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, single)
            {
                MultiOutput = (args, wanted, line, col) =>
                {
                    // One vector means the same vector once per output, which is how MATLAB reads
                    // [X, Y, Z] = meshgrid(v): the count of outputs is what says how many dimensions
                    // the box has, and there is nothing else in the call that could say it. Given two
                    // vectors or more the arguments say it themselves and asking for a third grid is
                    // the error it already was.
                    if (args.Count == 1 && wanted > 1)
                    {
                        var repeated = new JgsValue[wanted];
                        Array.Fill(repeated, args[0]);
                        args = repeated;
                    }

                    JgsValue set = setForm.Call(args, line, col);
                    int available = set.ArrayLength;
                    int answers = System.Math.Clamp(wanted, 1, available);
                    var grids = new JgsValue[answers];
                    for (int i = 0; i < answers; i++)
                    {
                        grids[i] = set.ElementAt(i);
                    }

                    return grids;
                },
            }));
        }
    }

    /// <summary>
    /// MATLAB's <c>[row, col] = find(x)</c> (optionally <c>[row, col, v]</c>): subscripts of the truthy
    /// elements, column-major over a matrix. JGraph vectors have no orientation and are treated as row
    /// vectors, so their row subscripts are all the dialect's first index.
    /// </summary>
    private static JgsValue[] FindSubscripts(
        IReadOnlyList<JgsValue> args, JgsDialect dialect, int outputs, int line, int col)
    {
        ArityRange("find", args, 1, dialect.IsMatlab ? 3 : 2, line, col);

        // A sparse matrix answers find from what it stores rather than from what it stands for, which
        // is the reason it is stored that way: a pattern with a thousand entries in a matrix of a
        // billion places should cost a thousand.
        if (args.Count > 0 && args[0].Type == JgsType.Sparse)
        {
            return SparseFind(args[0].AsSparse, outputs, dialect, line, col);
        }

        (int origin, int? wanted, bool fromEnd) = FindLimit("find", args, dialect, line, col);

        var rows = new List<JgsValue>();
        var cols = new List<JgsValue>();
        var values = new List<JgsValue>();
        JgsValue subject = args[0];

        if (JgsMatrix.IsMatrix(subject))
        {
            int height = JgsMatrix.RowCount(subject);
            int width = JgsMatrix.ColCount(subject);
            for (int c = 0; c < width; c++)
            {
                for (int r = 0; r < height; r++)
                {
                    JgsValue element = JgsMatrix.At(subject, r, c);
                    if (element.IsTruthy)
                    {
                        rows.Add(JgsValue.Number(r + origin));
                        cols.Add(JgsValue.Number(c + origin));
                        values.Add(element);
                    }
                }
            }
        }
        else
        {
            JgsValue[] elements = Arr("find", args, 0, line, col);
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i].IsTruthy)
                {
                    rows.Add(JgsValue.Number(origin));
                    cols.Add(JgsValue.Number(i + origin));
                    values.Add(elements[i]);
                }
            }
        }

        // All three lists were filled in step, so limiting them the same way keeps them in step.
        rows = Limited(rows, wanted, fromEnd);
        cols = Limited(cols, wanted, fromEnd);
        values = Limited(values, wanted, fromEnd);

        // Subscripts stand up the same way the single-output form's linear indices do: a row for a row
        // vector, a column for anything else. Without this the two forms of the same call disagreed
        // about shape, which stess_24 caught (M52).
        JgsValue Shaped(List<JgsValue> found) => FoundIndices(JgsValue.Array(found.ToArray()), subject);

        return outputs >= 3
            ? [Shaped(rows), Shaped(cols), Shaped(values)]
            : [Shaped(rows), Shaped(cols)];
    }

    private static JgsValue[] ExtremeWithIndex(
        JgsEnvironment env, string name, IReadOnlyList<JgsValue> args, JgsRunningDialect dialect, int line, int col)
    {
        JgsValue best = SingleOf(env, name, args, line, col);
        if (args.Count != 1 || args[0].Type != JgsType.Array)
        {
            return [best]; // the two-argument form is elementwise, and has no index to report
        }

        double[] values = ToDoubles(name, args[0], line, col);
        double target = best.AsNumber;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i].Equals(target))
            {
                return [best, JgsValue.Number(i + dialect.IndexBase)];
            }
        }

        return [best];
    }

    private static JgsValue SingleOf(
        JgsEnvironment env, string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        env.TryGet(name, out JgsValue value);
        return value is { Type: JgsType.Function } callable
            ? callable.AsCallable.Call(args, line, col)
            : throw new JgsRuntimeException(line, col, $"'{name}' is not available.");
    }

    private static bool AllOfType(JgsValue array, JgsType type)
    {
        if (array.IsPacked)
        {
            return (array.PackedKind == JgsPackedKind.Bool) == (type == JgsType.Bool);
        }

        for (int i = 0; i < array.ArrayLength; i++)
        {
            if (array.ElementAt(i).Type != type)
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<JgsValue> Elements(string name, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Cell)
        {
            return value.AsCell;
        }

        if (value.Type == JgsType.Object)
        {
            return ObjectElementValues(value); // num2cell over an array of objects (open item 116)
        }

        if (value.IsCharMatrix || (value.Type == JgsType.String && !value.IsStringArray))
        {
            // A char array, one character per element, a char matrix column by column (R2025b, measured 2026-11-07).
            string text = value.IsCharMatrix ? value.CharMatrixText() : value.AsString;
            return text.Select(static c => JgsValue.Str(c.ToString())).ToArray();
        }

        if (value.Type != JgsType.Array)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects an array or cell, but got a {value.TypeName}.");
        }

        var elements = new JgsValue[value.ArrayLength];
        for (int i = 0; i < elements.Length; i++)
        {
            elements[i] = value.ElementAt(i);
        }

        return elements;
    }

    /// <summary>
    /// <c>struct(...)</c>: name/value pairs, where a cell value spreads across the elements of a
    /// struct array (M65). <c>struct('a', {1, 2, 3})</c> is three elements, and <c>struct('a', {})</c>
    /// is an empty struct array that still has the field — the two forms a script uses to preallocate.
    /// </summary>
    private static JgsValue BuildStruct(IReadOnlyList<JgsValue> args, int line, int col)
    {
        var names = new string[args.Count / 2];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = StructNameArgument(args[i * 2], i * 2, line, col);
        }

        // The element count is the size of the cell values, which must agree; a non-cell value is
        // the same in every element, and all-scalar arguments make a 1-by-1.
        int count = 1;
        int rows = 1;
        int cols = 1;
        for (int i = 0; i < names.Length; i++)
        {
            if (args[(i * 2) + 1].Type != JgsType.Cell)
            {
                continue;
            }

            JgsValue cell = args[(i * 2) + 1];
            if (cell.AsCell.Length == 1) continue;
            if (count != 1 && (cell.AsCell.Length != count || cell.Rows != rows || cell.Cols != cols))
            {
                throw new JgsRuntimeException(line, col,
                    $"struct: '{names[i]}' has {cell.AsCell.Length} values but another field has {count}.");
            }

            count = cell.AsCell.Length;
            rows = cell.Rows;
            cols = cell.Cols;
        }

        var elements = new Dictionary<string, JgsValue>[count];
        for (int e = 0; e < count; e++)
        {
            var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            for (int i = 0; i < names.Length; i++)
            {
                JgsValue given = args[(i * 2) + 1];

                // M2: the field is an entry over the caller's value, so it takes a share of it.
                fields[names[i]] = JgsValue.Share(
                    given.Type == JgsType.Cell ? given.AsCell[given.AsCell.Length == 1 ? 0 : e] : given);
            }

            elements[e] = fields;
        }

        // An empty cell value gives its shape to the empty struct array: struct('f', cell(0, 3)) is
        // 0-by-3 (open item 98, measured).
        return JgsValue.StructArray(new JgsStructArray(elements, names), rows, cols);
    }

    /// <summary>
    /// A field name handed to <c>struct</c>, which keeps string arguments (open item 201): a char row
    /// or a string scalar. In the MATLAB dialect anything else, and an empty name, is R2025b's
    /// <c>NonStringFieldNames</c>, and a name no field may have its <c>InvalidFieldName</c> (open
    /// item 202, ADR 0351, measured); JGS keeps its own sentence and takes any text.
    /// </summary>
    private static string StructNameArgument(JgsValue value, int index, int line, int col)
    {
        bool matlab = JgsRunningDialect.ThreadIsMatlab;
        string? name = value.Type == JgsType.String ? value.AsString
            : IsStringScalar(value) && value.ElementAt(0).AsString != MissingSentinel ? value.ElementAt(0).AsString
            : IsEmptyCharRow(value) ? string.Empty
            : null;
        if (matlab && string.IsNullOrEmpty(name))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:Cell2Struct:NonStringFieldNames",
                "Field names must be non-empty character vectors or string scalars.");
        }

        if (name is null)
        {
            throw new JgsRuntimeException(line, col, $"struct expects argument {index + 1} to be a string, but got a {value.TypeName}.");
        }

        return matlab && !IsValidFieldName(name)
            ? throw new JgsRuntimeException(line, col, "MATLAB:Cell2Struct:InvalidFieldName", $"Invalid field name \"{name}\".")
            : name;
    }

    /// <summary>Text without its trailing whitespace and null characters, as <c>deblank</c> leaves it.</summary>
    private static string Deblanked(string text) => text.TrimEnd(' ', '\t', '\n', '\v', '\f', '\r', '\0');

    /// <summary>
    /// R2025b's refusal to read a field whose name no field may have, which only a struct built
    /// outside the language can hold (open item 202, ADR 0351: k10_feval risky 11, k16 in.struct.badname).
    /// </summary>
    internal static JgsRuntimeException InvalidFieldNameRead(string name, int line, int col) =>
        new(line, col, "MATLAB:AddField:InvalidFieldName", $"Invalid field name: '{name}'.");

    /// <summary>
    /// The names R2025b's <c>rmfield</c> removes, each without its trailing blanks, or its refusal of
    /// the first that names no field (open item 202, ADR 0351, from rmfield.m): one name is refused
    /// only when the struct has fields to miss, several are refused at the first absent one.
    /// </summary>
    private static string[] RemovableFieldNames(string[] present, JgsValue asked, string[] names, int line, int col)
    {
        bool one = asked.Type == JgsType.String || names.Length == 1;
        for (int i = 0; i < names.Length; i++)
        {
            string trimmed = Deblanked(names[i]);
            if (System.Array.IndexOf(present, trimmed) < 0 && (!one || present.Length > 0))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:rmfield:InvalidFieldname", $"A field named '{names[i]}' doesn't exist.");
            }

            names[i] = trimmed;
        }

        return names;
    }

    /// <summary>One field name, or a cell of them — the shape <c>rmfield</c> and friends accept.</summary>
    private static string[] FieldNameList(string name, JgsValue value, int line, int col)
    {
        if (value.IsStringArray) return value.BoxedElements().Select(v => v.AsString).ToArray();
        if (value.Type == JgsType.Cell)
        {
            var names = new string[value.AsCell.Length];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = value.AsCell[i].Type == JgsType.String
                    ? value.AsCell[i].AsString
                    : throw new JgsRuntimeException(line, col, $"{name} expects field names as text.");
            }

            return names;
        }

        return value.Type == JgsType.String
            ? [value.AsString]
            : throw new JgsRuntimeException(line, col, $"{name} expects a field name, but got a {value.TypeName}.");
    }

    /// <summary>
    /// Rebuilds a struct value element by element, keeping its shape. This is what makes the
    /// field-editing verbs array-aware in one place: before M65 they read the first element and
    /// answered with a lone struct, so <c>rmfield</c> on a struct array silently dropped every
    /// element but one.
    /// </summary>
    private static JgsValue MapStructElements(
        string name,
        JgsValue value,
        int line,
        int col,
        Func<Dictionary<string, JgsValue>, Dictionary<string, JgsValue>> edit)
    {
        if (value.Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col, $"{name} expects a struct, but got a {value.TypeName}.");
        }

        JgsStructArray payload = value.AsStructArray;
        var edited = new Dictionary<string, JgsValue>[payload.Length];
        for (int i = 0; i < edited.Length; i++)
        {
            // M2: the edit copies the element's fields into the answer's own dictionary, and each
            // field is an entry, so what it copies is a share per child — never the source's own
            // wrappers, which a later write through either struct would move for both.
            edited[i] = edit(JgsStructArray.SharedCopy(payload.Elements[i]));
        }

        string[] fields = edited.Length > 0 ? [.. edited[0].Keys] : payload.EmptyFields;
        return JgsValue.StructArray(new JgsStructArray(edited, fields), value.Rows, value.Cols);
    }

    private static Dictionary<string, JgsValue> StructOf(string name, JgsValue value, int line, int col)
    {
        // Every element of a struct array carries the same fields (M65), so fieldnames and isfield
        // answer for the array as readily as for one element — which is what MATLAB does and what a
        // script asking about a regionprops result needs.
        if (value.Type == JgsType.Struct)
        {
            return value.AsStruct;
        }

        throw new JgsRuntimeException(line, col, $"{name} expects a struct, but got a {value.TypeName}.");
    }


    /// <summary>Formats an <c>error</c>/<c>warning</c>/<c>assert</c> message, honouring a format string.</summary>
    /// <remarks>
    /// <para>
    /// <c>identified</c> says whether an identifier was read off the front of the call. It decides,
    /// together with whether any data follows, whether the message argument is a format at all — and a
    /// message that is not a format is used exactly as written, escapes and per cent signs and all.
    /// </para>
    /// <para>
    /// <c>error('a \n b')</c> keeps its backslash and its 'n' in R2025b, where <c>error('a \n b', 1)</c>
    /// and <c>error('my:id', 'a \n b')</c> both break the line: MATLAB reads the message as a format
    /// only when something follows it, or when an identifier came before it. Decoding it either way
    /// would put a line break in a message that a script wrote a literal backslash into, and would
    /// make the escape warning below fire on messages nobody asked to have formatted — including,
    /// recursively, the warning's own text, which names the offending character after a backslash.
    /// </para>
    /// </remarks>
    private static string FormatMessage(
        JgsEnvironment env, JGraphScriptGlobals host, JgsRunningDialect dialect, string name,
        IReadOnlyList<JgsValue> args, int start, bool identified, int line, int col)
    {
        string format = Str(name, args, start, line, col);
        bool hasData = args.Count > start + 1;
        bool isFormat = identified || hasData;

        // Only MATLAB leaves escapes to the format reader; a JGS literal arrived with its own already
        // decoded by the lexer, so decoding again here would read a backslash the script had asked to
        // keep.
        if (isFormat && dialect.IsMatlab)
        {
            format = JgsFormatEscapes.Decode(format, out JgsFormatEscapes.Fault? fault);
            if (fault is { } bad)
            {
                Warn(env, host, bad.Message, line, col);
            }
        }

        if (!hasData)
        {
            return format;
        }

        // These four read their data the way MATLAB's sprintf does — the format repeats until the
        // values are gone and stops where they run out, so error('a%d ', 1, 2) is 'a1 a2 ' and
        // error('a%d %d', 1) is 'a1 '. It matters here beyond the general parity: a format cut short
        // at a bad escape usually has no conversions left, and the strict reading refused the call
        // outright rather than dropping the values MATLAB drops.
        JgsValue[] data = args.Skip(start + 1).ToArray();
        return dialect.IsMatlab
            ? JgsSprintf.FormatMatlab(format, data)
            : JgsSprintf.Format(format, data);
    }

    // --- warning ------------------------------------------------------------------------------------

    /// <summary>
    /// The three forms of <c>warning</c>; see the registration. <paramref name="wanted"/> is zero for
    /// a statement, which is when a query prints and a state change says nothing.
    /// </summary>
    private static JgsValue[] Warning(
        JgsEnvironment env, JGraphScriptGlobals host, JgsRunningDialect dialect,
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        JgsWarningState state = host.Warnings;

        // warning alone is warning('query', 'all').
        if (args.Count == 0)
        {
            return WarningQuery(host, state, "all", wanted);
        }

        // warning(s) puts back what an earlier query or state change handed out.
        if (args[0].Type == JgsType.Struct)
        {
            Arity("warning", args, 1, line, col);

            // A struct without both fields is refused, a message struct among them (measured, open
            // item 306: warning(struct('message', 'm', 'identifier', 'a:b'))).
            if (dialect.IsMatlab && StructElements(args[0]).Any(static f => !f.ContainsKey("state") || !f.ContainsKey("identifier")))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:warning:missingStructureField",
                    "The input to WARNING must have fields named 'state' and 'identifier'.");
            }

            foreach (Dictionary<string, JgsValue> fields in StructElements(args[0]))
            {
                if (fields.TryGetValue("identifier", out JgsValue? id) && id.Type == JgsType.String
                    && fields.TryGetValue("state", out JgsValue? on) && on.Type == JgsType.String)
                {
                    // A struct a query handed out may carry the error state, and puts it back
                    // (open item 189, measured R2025b).
                    string word = on.AsString.ToLowerInvariant();
                    state.SetState(id.AsString, word == JgsWarningState.ErrorState && id.AsString != "all"
                        ? JgsWarningState.ErrorState
                        : word == "on" ? "on" : "off");
                }
            }

            return [];
        }

        string first = Str("warning", args, 0, line, col);
        string verb = first.ToLowerInvariant();

        // warning('error', id) is the third state (open item 189, measured R2025b): a warning under
        // id is raised as an error with id and the warning's message. warning('error') alone is
        // still a message whose text is "error", as R2025b's is.
        bool errorVerb = verb == JgsWarningState.ErrorState && args.Count > 1;
        if (verb is "on" or "off" or "query" || errorVerb)
        {
            if (errorVerb && args.Count > 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:maxrhs", "Too many input arguments.");
            }

            ArityRange("warning", args, 1, 2, line, col);
            if (args.Count == 2 && args[1].Type != JgsType.String)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:badopt", "Unknown command option.");
            }

            string identifier = args.Count == 2 ? args[1].AsString : "all";
            if (errorVerb && identifier == "all")
            {
                throw new JgsRuntimeException(line, col, "MATLAB:warning:cannotUseAllWithError",
                    "The 'all' message identifier cannot be used with the 'error' warning setting.");
            }

            // The two settings are switched on and off only; the error state is for identifiers.
            if (!IsWarningSetting(identifier) || (errorVerb && identifier is "backtrace" or "verbose"))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:warning:unknownSettingOrId",
                    $"Unknown setting or incorrect message identifier '{identifier}'.");
            }

            if (verb == "query")
            {
                return WarningQuery(host, state, identifier, wanted);
            }

            // The answer is the state being replaced, which is why it is read before the change.
            JgsValue previous = identifier == "all" ? WarningTable(state) : WarningEntry(identifier, state.StateOf(identifier));
            state.SetState(identifier, verb);
            return wanted > 0 ? [previous] : [];
        }

        // An identifier alone, with no message after it, is refused (measured R2025b, open item 306):
        // warning('pkg:id') is an error, where warning('pkg:') and warning('a:1b') are messages.
        if (dialect.IsMatlab)
        {
            RefuseLoneIdentifier("warning", args, line, col);
        }

        // A message. The identifier rule is error's own: at least one colon, no whitespace, no
        // conversion, and something after it to be the format.
        bool hasIdentifier = args.Count > 1 && IsErrorIdentifier(first);
        string message = FormatMessage(env, host, dialect, "warning", args, hasIdentifier ? 1 : 0, hasIdentifier, line, col);
        string under = hasIdentifier ? first : string.Empty;

        // An identifier in the error state raises the warning as an error under its own identifier,
        // and lastwarn is left as it was (measured R2025b, open item 189). Its report is headed
        // "Error using warning", as R2025b's getReport heads it (open item 306).
        if (state.IsError(under))
        {
            throw new JgsRuntimeException(line, col, under, message) { UsingName = "warning" };
        }

        state.Record(under, message);
        if (state.IsOn(under))
        {
            host.WriteWarning("Warning: " + message);
        }

        return [];
    }

    /// <summary>
    /// R2025b's refusal of an identifier given with no message (measured, open item 306):
    /// <c>warning('pkg:id')</c> and <c>error('pkg:id')</c> are <c>MATLAB:&lt;verb&gt;:missingMessageArgument</c>.
    /// An identifier here is two or more parts between colons, each a letter then letters, digits or
    /// underscores; anything else alone is the message (<c>'pkg:'</c>, <c>'a:1b'</c>, <c>'a-b:c'</c>).
    /// An identifier followed by something other than text is <c>badMessageArgument</c> (measured for
    /// <c>warning</c>, so asked for by <paramref name="messageType"/>).
    /// </summary>
    private static void RefuseLoneIdentifier(string verb, IReadOnlyList<JgsValue> args, int line, int col, bool messageType = true)
    {
        if (args.Count == 1 && IsTextScalar(args[0]) && IsMessageIdentifier(TextOf(args[0])))
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:missingMessageArgument",
                "If you specify a message identifier argument, you must specify the message text argument.");
        }

        if (messageType && args.Count > 1 && IsTextScalar(args[0]) && IsErrorIdentifier(TextOf(args[0])) && !IsTextScalar(args[1]))
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{verb}:badMessageArgument",
                "A message identifier must be followed by a message that is a character vector or string scalar.");
        }
    }

    /// <summary>Whether <paramref name="text"/> is a message identifier by R2025b's stricter rule for a lone argument.</summary>
    private static bool IsMessageIdentifier(string text)
    {
        string[] parts = text.Split(':');
        return parts.Length >= 2 && parts.All(static part =>
            part.Length > 0 && char.IsAsciiLetter(part[0]) && part.All(static c => char.IsAsciiLetterOrDigit(c) || c == '_'));
    }

    /// <summary>
    /// A name <c>warning</c> can switch: <c>'all'</c>, the two settings, or a message identifier,
    /// which has a colon somewhere inside it and no whitespace.
    /// </summary>
    private static bool IsWarningSetting(string identifier) =>
        identifier is "all" or "backtrace" or "verbose"
        || (identifier.Contains(':', StringComparison.Ordinal)
            && !identifier.Any(char.IsWhiteSpace)
            && !identifier.StartsWith(':')
            && !identifier.EndsWith(':'));

    /// <summary>
    /// <c>warning('query', id)</c>: the state as a struct, or, as a statement, the sentence MATLAB
    /// prints — one line for an identifier, the table of everything not at the default for 'all'.
    /// </summary>
    private static JgsValue[] WarningQuery(JGraphScriptGlobals host, JgsWarningState state, string identifier, int wanted)
    {
        if (wanted > 0)
        {
            return [identifier == "all" ? WarningTable(state) : WarningEntry(identifier, state.StateOf(identifier))];
        }

        if (identifier != "all")
        {
            host.print($"The state of warning '{identifier}' is '{state.StateOf(identifier)}'.");
            return [];
        }

        // R2025b's layout (measured, open item 306): no colon after the heading, the column heads not
        // indented and the rows by two, a blank line after the rows; the two settings are not
        // identifiers and are not listed; and with nothing to list, one line saying so.
        string defaultState = state.DefaultOn ? "on" : "off";
        var rows = state.Explicit().Where(e => e.State != defaultState && e.Identifier is not ("backtrace" or "verbose")).ToList();
        if (rows.Count == 0)
        {
            host.print($"All warnings have the state '{defaultState}'.");
            return [];
        }

        host.print($"The default warning state is '{defaultState}'. Warnings not set to the default are");
        host.print(string.Empty);
        host.print("State  Warning Identifier");
        host.print(string.Empty);
        foreach ((string id, string setting) in rows)
        {
            host.print($"  {setting,5}  {id}");
        }

        host.print(string.Empty);
        return [];
    }

    /// <summary>One row of the state table: <c>identifier</c> and <c>state</c> ("on", "off" or "error").</summary>
    private static JgsValue WarningEntry(string identifier, string setting) =>
        JgsValue.Struct(WarningFields(identifier, setting));

    private static Dictionary<string, JgsValue> WarningFields(string identifier, string setting) =>
        new(StringComparer.Ordinal)
        {
            ["identifier"] = JgsValue.Str(identifier),
            ["state"] = JgsValue.Str(setting),
        };

    /// <summary>The whole table as a column of structs: the default first, then every explicit setting.</summary>
    private static JgsValue WarningTable(JgsWarningState state)
    {
        var rows = new List<Dictionary<string, JgsValue>> { WarningFields("all", state.DefaultOn ? "on" : "off") };
        foreach ((string id, string setting) in state.Explicit())
        {
            rows.Add(WarningFields(id, setting));
        }

        return JgsValue.StructArray(new JgsStructArray([.. rows]), rows.Count, 1);
    }

    /// <summary>The elements of a struct or struct array, each as its field dictionary.</summary>
    private static IEnumerable<Dictionary<string, JgsValue>> StructElements(JgsValue value)
    {
        if (value.Type != JgsType.Struct)
        {
            yield break;
        }

        if (value.IsStructArray)
        {
            foreach (Dictionary<string, JgsValue> element in value.AsStructArray.Elements)
            {
                yield return element;
            }

            yield break;
        }

        yield return value.AsStruct;
    }

}
