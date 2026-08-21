using System.Text;

namespace JGraph.Core.Drawing;

/// <summary>Which markup a text run is written in (MATLAB's <c>Interpreter</c> property).</summary>
public enum TextInterpreter
{
    /// <summary>MATLAB's TeX subset — the default there and here.</summary>
    Tex,

    /// <summary>No markup: the characters are the text, backslashes and all.</summary>
    None,

    /// <summary>
    /// LaTeX. Read here as the TeX subset with the surrounding math delimiters dropped, which covers
    /// the symbol and script markup the two share and is what almost every axis label uses it for.
    /// </summary>
    Latex,
}

/// <summary>
/// MATLAB's TeX markup, rendered into the characters a single text run can draw (M72).
/// </summary>
/// <remarks>
/// <para>
/// Before this, <c>title('x\cdot e^{-r^2}')</c> printed its own backslashes and braces: there was no
/// interpreter behind a text object at all, so every symbol a scientific label wants — sigma, plus or
/// minus, a superscript — had to be spelled out in words.
/// </para>
/// <para>
/// The translation is to Unicode rather than to a laid-out box of glyphs. That is a deliberate limit
/// and it is what makes the feature reachable from every text in the figure at once: a title, a tick
/// label, a legend entry and a text object are all one run of characters drawn by one call, and a run
/// of characters is exactly what this produces. Superscripts and subscripts therefore work for the
/// characters Unicode has them for — the digits, the signs, the parentheses and a good part of the
/// Latin alphabet — and fall back to the plain characters where it does not, rather than failing.
/// </para>
/// <para>
/// Divergence: the font and colour commands (<c>\bf</c>, <c>\it</c>, <c>\rm</c>, <c>\color</c>,
/// <c>\fontname</c>, <c>\fontsize</c>) are read and dropped, because a run drawn in one call has one
/// style; and <c>\frac</c>, <c>\int</c> with limits and the other built-up constructions are not
/// stacked. Everything they contain is still shown.
/// </para>
/// </remarks>
public static class TexMarkup
{
    /// <summary>
    /// The text as it should be drawn. A run with no backslash, caret or underscore in it is handed
    /// straight back, which is nearly every run in a figure and costs one scan.
    /// </summary>
    public static string Render(string? text, TextInterpreter interpreter)
    {
        if (string.IsNullOrEmpty(text) || interpreter == TextInterpreter.None)
        {
            return text ?? string.Empty;
        }

        if (text.AsSpan().IndexOfAny('\\', '^') < 0 && !text.Contains('_') && !text.Contains('{'))
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        int i = 0;

        // LaTeX wraps its maths in dollars; the markup inside is the part the two languages share.
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '$' && interpreter == TextInterpreter.Latex)
            {
                i++;
                continue;
            }

            switch (c)
            {
                case '\\':
                    i = Command(text, i, sb);
                    continue;
                case '^':
                    i = Script(text, i + 1, sb, Superscripts);
                    continue;
                case '_':
                    i = Script(text, i + 1, sb, Subscripts);
                    continue;

                // A group that is not the argument of anything contributes its contents. MATLAB's
                // braces are grouping, never characters.
                case '{' or '}':
                    i++;
                    continue;
                default:
                    sb.Append(c);
                    i++;
                    continue;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Reads one backslash command starting at <paramref name="at"/> and appends what it stands for,
    /// answering the index just past it.
    /// </summary>
    private static int Command(string text, int at, StringBuilder sb)
    {
        int start = at + 1;
        if (start >= text.Length)
        {
            sb.Append('\\');
            return text.Length;
        }

        // An escaped character stands for itself: \\ is a backslash, \{ is a brace.
        char first = text[start];
        if (!char.IsLetter(first))
        {
            sb.Append(first);
            return start + 1;
        }

        int end = start;
        while (end < text.Length && char.IsLetter(text[end]))
        {
            end++;
        }

        string name = text[start..end];

        // One space after a command name is the space that ended the name, not a space in the text:
        // 'a \\pm b' is a plus-or-minus between two letters, not one with a gap after it. Every TeX
        // there has ever been reads it that way and MATLAB's interpreter does too.
        int after = end < text.Length && text[end] == ' ' ? end + 1 : end;

        if (Symbols.TryGetValue(name, out string? symbol))
        {
            sb.Append(symbol);
            return after;
        }

        // The font and colour commands whose braced argument is a setting rather than text: what is
        // inside \\fontname{Courier} names a font and must not be shown.
        if (Settings.Contains(name))
        {
            return end < text.Length && text[end] == '{' ? SkipGroup(text, end) : after;
        }

        // \\bf and its kin take no argument at all; a brace after one is ordinary grouping, and what
        // is inside it is text that has to survive even though one run cannot bolden part of itself.
        if (Styles.Contains(name))
        {
            return after;
        }

        // Anything unrecognised is shown as written, which is what MATLAB does with a command it does
        // not know and is far more useful than dropping the label's content on the floor.
        sb.Append('\\').Append(name);
        return end;
    }

    /// <summary>
    /// Reads the argument of a <c>^</c> or <c>_</c> — a group or a single character — and appends it
    /// through <paramref name="table"/>, or plainly where a character has no raised form.
    /// </summary>
    private static int Script(string text, int at, StringBuilder sb, IReadOnlyDictionary<char, char> table)
    {
        if (at >= text.Length)
        {
            return at;
        }

        int end;
        string body;
        if (text[at] == '{')
        {
            end = SkipGroup(text, at);
            body = Render(text[(at + 1)..System.Math.Max(at + 1, end - 1)], TextInterpreter.Tex);
        }
        else if (text[at] == '\\')
        {
            // A bare command after a caret is its whole argument: e^\alpha raises the alpha.
            var inner = new StringBuilder();
            end = Command(text, at, inner);
            body = inner.ToString();
        }
        else
        {
            end = at + 1;
            body = text[at].ToString();
        }

        foreach (char c in body)
        {
            sb.Append(table.TryGetValue(c, out char raised) ? raised : c);
        }

        return end;
    }

    /// <summary>The index just past the group starting at the brace at <paramref name="open"/>.</summary>
    private static int SkipGroup(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}' && --depth == 0)
            {
                return i + 1;
            }
        }

        return text.Length;
    }

    /// <summary>
    /// The commands that switch a style on for what follows, which one run cannot honour part-way
    /// through. They take no argument, so a brace after one is grouping and its contents are text.
    /// </summary>
    private static readonly HashSet<string> Styles = new(StringComparer.Ordinal)
    {
        "bf", "it", "sl", "rm", "left", "right", "displaystyle", "textstyle",
        "mathrm", "mathbf", "mathit", "text",
    };

    /// <summary>The commands whose braced argument names a setting rather than being text to show.</summary>
    private static readonly HashSet<string> Settings = new(StringComparer.Ordinal)
    {
        "fontname", "fontsize", "color",
    };

    private static readonly Dictionary<char, char> Superscripts = new()
    {
        ['0'] = '⁰', ['1'] = '¹', ['2'] = '²', ['3'] = '³', ['4'] = '⁴',
        ['5'] = '⁵', ['6'] = '⁶', ['7'] = '⁷', ['8'] = '⁸', ['9'] = '⁹',
        ['+'] = '⁺', ['-'] = '⁻', ['='] = '⁼', ['('] = '⁽', [')'] = '⁾',
        ['n'] = 'ⁿ', ['i'] = 'ⁱ', ['a'] = 'ᵃ', ['b'] = 'ᵇ', ['c'] = 'ᶜ',
        ['d'] = 'ᵈ', ['e'] = 'ᵉ', ['f'] = 'ᶠ', ['g'] = 'ᵍ', ['h'] = 'ʰ',
        ['j'] = 'ʲ', ['k'] = 'ᵏ', ['l'] = 'ˡ', ['m'] = 'ᵐ', ['o'] = 'ᵒ',
        ['p'] = 'ᵖ', ['r'] = 'ʳ', ['s'] = 'ˢ', ['t'] = 'ᵗ', ['u'] = 'ᵘ',
        ['v'] = 'ᵛ', ['w'] = 'ʷ', ['x'] = 'ˣ', ['y'] = 'ʸ', ['z'] = 'ᶻ',
    };

    private static readonly Dictionary<char, char> Subscripts = new()
    {
        ['0'] = '₀', ['1'] = '₁', ['2'] = '₂', ['3'] = '₃', ['4'] = '₄',
        ['5'] = '₅', ['6'] = '₆', ['7'] = '₇', ['8'] = '₈', ['9'] = '₉',
        ['+'] = '₊', ['-'] = '₋', ['='] = '₌', ['('] = '₍', [')'] = '₎',
        ['a'] = 'ₐ', ['e'] = 'ₑ', ['h'] = 'ₕ', ['i'] = 'ᵢ', ['j'] = 'ⱼ',
        ['k'] = 'ₖ', ['l'] = 'ₗ', ['m'] = 'ₘ', ['n'] = 'ₙ', ['o'] = 'ₒ',
        ['p'] = 'ₚ', ['r'] = 'ᵣ', ['s'] = 'ₛ', ['t'] = 'ₜ', ['u'] = 'ᵤ',
        ['v'] = 'ᵥ', ['x'] = 'ₓ',
    };

    /// <summary>MATLAB's documented TeX character set, by the name a script writes.</summary>
    private static readonly Dictionary<string, string> Symbols = new(StringComparer.Ordinal)
    {
        // Lower-case Greek.
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ",
        ["epsilon"] = "ε", ["zeta"] = "ζ", ["eta"] = "η", ["theta"] = "θ",
        ["vartheta"] = "ϑ", ["iota"] = "ι", ["kappa"] = "κ", ["lambda"] = "λ",
        ["mu"] = "μ", ["nu"] = "ν", ["xi"] = "ξ", ["pi"] = "π",
        ["rho"] = "ρ", ["sigma"] = "σ", ["varsigma"] = "ς", ["tau"] = "τ",
        ["upsilon"] = "υ", ["phi"] = "φ", ["chi"] = "χ", ["psi"] = "ψ",
        ["omega"] = "ω",

        // Upper-case Greek.
        ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ", ["Lambda"] = "Λ",
        ["Xi"] = "Ξ", ["Pi"] = "Π", ["Sigma"] = "Σ", ["Upsilon"] = "Υ",
        ["Phi"] = "Φ", ["Psi"] = "Ψ", ["Omega"] = "Ω",

        // Arithmetic and relations.
        ["pm"] = "±", ["mp"] = "∓", ["times"] = "×", ["div"] = "÷",
        ["cdot"] = "·", ["ast"] = "∗", ["star"] = "⋆", ["circ"] = "∘",
        ["bullet"] = "•", ["leq"] = "≤", ["geq"] = "≥", ["neq"] = "≠",
        ["equiv"] = "≡", ["approx"] = "≈", ["cong"] = "≅", ["sim"] = "∼",
        ["propto"] = "∝", ["ll"] = "≪", ["gg"] = "≫", ["perp"] = "⊥",
        ["mid"] = "∣", ["parallel"] = "∥", ["prime"] = "′", ["surd"] = "√",
        ["infty"] = "∞", ["partial"] = "∂", ["nabla"] = "∇", ["int"] = "∫",
        ["oint"] = "∮", ["sum"] = "∑", ["prod"] = "∏", ["sqrt"] = "√",
        ["angle"] = "∠", ["degree"] = "°", ["neg"] = "¬", ["wedge"] = "∧",
        ["vee"] = "∨", ["oplus"] = "⊕", ["ominus"] = "⊖", ["otimes"] = "⊗",
        ["oslash"] = "⊘",

        // Sets and logic.
        ["in"] = "∈", ["notin"] = "∉", ["ni"] = "∋", ["subset"] = "⊂",
        ["supset"] = "⊃", ["subseteq"] = "⊆", ["supseteq"] = "⊇",
        ["cup"] = "∪", ["cap"] = "∩", ["emptyset"] = "∅", ["forall"] = "∀",
        ["exists"] = "∃", ["aleph"] = "ℵ", ["Re"] = "ℜ", ["Im"] = "ℑ",
        ["wp"] = "℘",

        // Arrows.
        ["leftarrow"] = "←", ["uparrow"] = "↑", ["rightarrow"] = "→",
        ["downarrow"] = "↓", ["leftrightarrow"] = "↔", ["updownarrow"] = "↕",
        ["Leftarrow"] = "⇐", ["Rightarrow"] = "⇒", ["Leftrightarrow"] = "⇔",
        ["leftharpoonup"] = "↼", ["rightharpoonup"] = "⇀",

        // Dots and spacing.
        ["ldots"] = "…", ["cdots"] = "⋯", ["vdots"] = "⋮", ["ddots"] = "⋱",
        ["quad"] = " ", ["qquad"] = "  ",

        // Cards and the two loose letters MATLAB documents.
        ["clubsuit"] = "♣", ["diamondsuit"] = "♦", ["heartsuit"] = "♥",
        ["spadesuit"] = "♠", ["copyright"] = "©",
    };
}
