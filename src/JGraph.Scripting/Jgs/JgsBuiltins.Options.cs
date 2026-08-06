namespace JGraph.Scripting.Jgs;

/// <summary>
/// Argument parsing for builtins that take options. MATLAB functions take a handful of positional
/// arguments followed by a free-order tail of bare option words (<c>'replicate'</c>, <c>'stable'</c>,
/// <c>'omitnan'</c>) and <c>'Name', value</c> pairs (<c>'Sensitivity', 0.6</c>,
/// <c>'Endpoints', 'shrink'</c>). One declared spec per builtin replaces what would otherwise be a
/// hand-rolled tail per function, and — because the spec knows every legal word — an unrecognized
/// option can name the alternatives instead of just refusing.
/// </summary>
/// <remarks>
/// This machinery arrived with the image-processing surface (M46) and was written against
/// <see cref="JgsValue"/> alone, so it never had anything to do with pictures. M52 moved it here
/// under domain-neutral names, because the base-language builtins need exactly the same parsing —
/// and the ones that hand-rolled it were the ones silently ignoring options they did not know.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>One builtin's option surface.</summary>
    /// <param name="Builtin">The name to put in diagnostics.</param>
    /// <param name="Flags">Bare words accepted anywhere in the option tail.</param>
    /// <param name="Names">Names that consume the argument after them.</param>
    /// <param name="AllowNumericFlag">Whether a bare number may appear in the tail (imfilter's pad value).</param>
    /// <param name="StringPositionals">
    /// How many leading positional slots may legitimately hold a string — imwrite's path, for one.
    /// Past that a string is read as an option word, so a misspelling like <c>'adaptiv'</c> is reported
    /// against the list of real options instead of being swallowed as data and failing later with a
    /// message about the wrong thing.
    /// </param>
    internal sealed record OptionSpec(
        string Builtin,
        string[] Flags,
        string[] Names,
        bool AllowNumericFlag = false,
        int StringPositionals = 0)
    {
        /// <summary>
        /// Splits <paramref name="args"/> into positional arguments and options. The option tail starts
        /// at the first string that matches a declared flag or name, which means a string that is
        /// genuinely positional (a file path, a method word consumed positionally) has to come before
        /// any option — exactly MATLAB's own rule.
        /// </summary>
        /// <param name="args">The call's arguments.</param>
        /// <param name="positionalMax">How many leading arguments may be positional.</param>
        /// <param name="line">Source line, for diagnostics.</param>
        /// <param name="col">Source column, for diagnostics.</param>
        public ParsedArgs Parse(IReadOnlyList<JgsValue> args, int positionalMax, int line, int col)
        {
            var positional = new List<JgsValue>();
            var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var named = new Dictionary<string, JgsValue>(StringComparer.OrdinalIgnoreCase);
            JgsValue? numericFlag = null;

            int i = 0;
            while (i < args.Count && positional.Count < positionalMax && !StartsOptions(args[i], positional.Count))
            {
                positional.Add(args[i]);
                i++;
            }

            while (i < args.Count)
            {
                JgsValue arg = args[i];
                if (arg.Type == JgsType.String)
                {
                    string word = arg.AsString;
                    if (MatchName(word) is { } canonical)
                    {
                        if (i + 1 >= args.Count)
                        {
                            throw new JgsRuntimeException(line, col,
                                $"{Builtin}: '{canonical}' needs a value after it.");
                        }

                        named[canonical] = args[i + 1];
                        i += 2;
                        continue;
                    }

                    if (MatchFlag(word) is { } flag)
                    {
                        flags.Add(flag);
                        i++;
                        continue;
                    }

                    throw new JgsRuntimeException(line, col,
                        $"{Builtin}: unknown option '{word}'{Alternatives()}.");
                }

                if (AllowNumericFlag && arg.Type == JgsType.Number && numericFlag is null)
                {
                    numericFlag = arg;
                    i++;
                    continue;
                }

                throw new JgsRuntimeException(line, col,
                    $"{Builtin}: unexpected argument {i + 1}; options come after the data" +
                    $"{Alternatives()}.");
            }

            return new ParsedArgs(Builtin, positional, flags, named, numericFlag, line, col);
        }

        private bool StartsOptions(JgsValue value, int slot) =>
            value.Type == JgsType.String && slot >= StringPositionals;

        // Case-insensitive but never partial: MATLAB tolerates unambiguous abbreviations, and copying
        // that would mean a later option could silently change what an existing script's abbreviation
        // resolves to. Spelling an option in full is a fixed target.
        private string? MatchName(string word) => Find(Names, word);

        private string? MatchFlag(string word) => Find(Flags, word);

        private static string? Find(string[] candidates, string word)
        {
            foreach (string candidate in candidates)
            {
                if (string.Equals(candidate, word, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        private string Alternatives()
        {
            var all = new List<string>();
            foreach (string flag in Flags)
            {
                all.Add($"'{flag}'");
            }

            foreach (string name in Names)
            {
                all.Add($"'{name}'");
            }

            return all.Count == 0 ? string.Empty : $" (options: {string.Join(", ", all)})";
        }
    }

    /// <summary>The result of <see cref="OptionSpec.Parse"/>: positional arguments plus looked-up options.</summary>
    internal sealed class ParsedArgs(
        string builtin,
        IReadOnlyList<JgsValue> positional,
        HashSet<string> flags,
        Dictionary<string, JgsValue> named,
        JgsValue? numericFlag,
        int line,
        int col)
    {
        /// <summary>The leading arguments, before any option word.</summary>
        public IReadOnlyList<JgsValue> Positional => positional;

        /// <summary>A bare number in the option tail, when the spec allows one.</summary>
        public JgsValue? NumericFlag => numericFlag;

        /// <summary>Whether a bare option word was given.</summary>
        public bool Has(string flag) => flags.Contains(flag);

        /// <summary>The first of a mutually exclusive set that was given, or <paramref name="fallback"/>.</summary>
        public string OneOf(string fallback, params string[] choices)
        {
            string? found = null;
            foreach (string choice in choices)
            {
                if (!flags.Contains(choice))
                {
                    continue;
                }

                if (found is not null)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{builtin}: '{found}' and '{choice}' cannot both be given.");
                }

                found = choice;
            }

            return found ?? fallback;
        }

        /// <summary>The raw value of a name-value option, or null when it was not given.</summary>
        public JgsValue? Named(string name) => named.TryGetValue(name, out JgsValue? value) ? value : null;

        /// <summary>A numeric name-value option, or <paramref name="fallback"/>.</summary>
        public double Scalar(string name, double fallback)
        {
            if (Named(name) is not { } value)
            {
                return fallback;
            }

            if (value.Type != JgsType.Number)
            {
                throw new JgsRuntimeException(line, col, $"{builtin}: '{name}' takes a number.");
            }

            return value.AsNumber;
        }

        /// <summary>A logical name-value option, or <paramref name="fallback"/>.</summary>
        public bool Flag(string name, bool fallback)
        {
            if (Named(name) is not { } value)
            {
                return fallback;
            }

            return value.Type switch
            {
                JgsType.Bool => value.AsBool,
                JgsType.Number => value.AsNumber != 0,
                _ => throw new JgsRuntimeException(line, col, $"{builtin}: '{name}' takes true or false."),
            };
        }

        /// <summary>A string name-value option, or null.</summary>
        public string? Text(string name)
        {
            if (Named(name) is not { } value)
            {
                return null;
            }

            if (value.Type != JgsType.String)
            {
                throw new JgsRuntimeException(line, col, $"{builtin}: '{name}' takes a word.");
            }

            return value.AsString;
        }

        /// <summary>
        /// A whole-number name-value option, or <paramref name="fallback"/>. Separate from
        /// <see cref="Scalar"/> because a count that arrives as 2.5 is a mistake worth naming rather
        /// than a number to round quietly.
        /// </summary>
        public int Whole(string name, int fallback)
        {
            if (Named(name) is not { } value)
            {
                return fallback;
            }

            if (value.Type != JgsType.Number)
            {
                throw new JgsRuntimeException(line, col, $"{builtin}: '{name}' takes a whole number.");
            }

            double number = value.AsNumber;
            if (number != Math.Floor(number) || !double.IsFinite(number))
            {
                throw new JgsRuntimeException(line, col,
                    $"{builtin}: '{name}' takes a whole number, but got {number}.");
            }

            return (int)number;
        }

        /// <summary>
        /// A name-value option whose value must be one of a fixed set of words, or
        /// <paramref name="fallback"/>. The diagnostic lists the accepted spellings, which is the whole
        /// point: an option word that is merely ignored is how a script silently does the wrong thing.
        /// </summary>
        public string Word(string name, string fallback, params string[] allowed)
        {
            if (Text(name) is not { } word)
            {
                return fallback;
            }

            foreach (string candidate in allowed)
            {
                if (string.Equals(candidate, word, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            throw new JgsRuntimeException(line, col,
                $"{builtin}: '{name}' does not take '{word}' (expected one of {Quoted(allowed)}).");
        }

        /// <summary>
        /// A numeric-vector name-value option, or null. A single number counts as a one-element
        /// vector — <c>'FilterSize', 5</c> and <c>'FilterSize', [5 5]</c> are both ordinary MATLAB.
        /// </summary>
        public double[]? Vector(string name) =>
            Named(name) is { } value ? NumericVector(builtin, value, line, col) : null;

        /// <summary>
        /// A window-size option: one number means a square window, a pair means (rows, cols). Null when
        /// the option was not given.
        /// </summary>
        public (int Height, int Width)? Window(string name)
        {
            double[]? size = Vector(name);
            if (size is null)
            {
                return null;
            }

            return size.Length switch
            {
                1 => (Positive(size[0]), Positive(size[0])),
                2 => (Positive(size[0]), Positive(size[1])),
                _ => throw new JgsRuntimeException(line, col,
                    $"{builtin}: '{name}' takes a size or a [rows, cols] pair."),
            };
        }

        private static string Quoted(string[] words)
        {
            var all = new List<string>(words.Length);
            foreach (string word in words)
            {
                all.Add($"'{word}'");
            }

            return string.Join(", ", all);
        }

        private int Positive(double value)
        {
            int rounded = (int)Math.Round(value);
            if (rounded < 1)
            {
                throw new JgsRuntimeException(line, col, $"{builtin}: sizes must be positive whole numbers.");
            }

            return rounded;
        }
    }

    /// <summary>
    /// Reads a number or an array of numbers as a vector. MATLAB writes a one-element vector as a bare
    /// scalar — <c>imgaussfilt(I, 2)</c>, <c>edge(I, 'canny', 0.3)</c>, <c>medfilt2(I, 3)</c> — so
    /// every option that accepts "one value or a pair" has to come through here rather than assuming
    /// an array arrived.
    /// </summary>
    private static double[] NumericVector(string name, JgsValue value, int line, int col) =>
        value.Type == JgsType.Number ? [value.AsNumber] : ToDoubles(name, value, line, col);
}
