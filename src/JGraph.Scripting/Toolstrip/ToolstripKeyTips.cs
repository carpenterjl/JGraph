namespace JGraph.Scripting.Toolstrip;

/// <summary>What a key tip leads to.</summary>
public enum ToolstripKeyTipScope
{
    /// <summary>A tab: typing it selects the tab and shows its controls' tips.</summary>
    Tab,

    /// <summary>A quick access bar button: typing it runs the button.</summary>
    QuickAccess,

    /// <summary>A control on the selected tab: typing it runs the control, or opens its popup.</summary>
    Control,
}

/// <summary>A key tip: the letters to type, and what they reach.</summary>
/// <param name="Tip">The letters (or digits, on the quick access bar).</param>
/// <param name="Target">The tab or control's tag.</param>
/// <param name="Scope">What the target is.</param>
public sealed record ToolstripKeyTip(string Tip, string Target, ToolstripKeyTipScope Scope);

/// <summary>Which tips are showing.</summary>
public enum ToolstripKeyTipMode
{
    /// <summary>None.</summary>
    Off,

    /// <summary>The tabs' and the quick access bar's.</summary>
    Tabs,

    /// <summary>The selected tab's controls'.</summary>
    Controls,
}

/// <summary>What a typed key did.</summary>
public enum ToolstripKeyTipMatch
{
    /// <summary>No shown tip starts with what has been typed; nothing changed.</summary>
    None,

    /// <summary>Some tips start with it: only those are shown now.</summary>
    Partial,

    /// <summary>A tip is complete: the host acts on it.</summary>
    Full,
}

/// <summary>
/// The toolstrip's key tips, as R2025b's <c>MW/uiframework/MnemonicsManager</c> runs them (App
/// Designer window plan, stage W1). Alt pressed and released alone shows the tabs' tips and the
/// quick access bar's numbers; typing a tab's tip selects the tab and shows its controls' tips;
/// typing a control's runs it. Tips may be longer than one letter (Step is "DD"): a key that only
/// begins some tips narrows the shown ones to those. Escape goes back a level, then hides them.
/// </summary>
/// <remarks>
/// The state machine only; the control shows the tips and acts on a match. R2025b's App Designer
/// gives tips to the RUN section alone (Run "R", Step "DD", Stop "Q") and none to its tabs, which
/// leaves even those unreachable; <see cref="Assign"/> gives every tab and control without one a
/// tip from its label, a JGraph addition recorded in the stage's ADR.
/// </remarks>
public sealed class ToolstripKeyTips
{
    private IReadOnlyList<ToolstripKeyTip> _tips = [];

    /// <summary>Which tips are showing.</summary>
    public ToolstripKeyTipMode Mode { get; private set; }

    /// <summary>What has been typed of a longer tip.</summary>
    public string Typed { get; private set; } = "";

    /// <summary>The tips showing: those that start with what has been typed.</summary>
    public IEnumerable<ToolstripKeyTip> Shown =>
        Mode == ToolstripKeyTipMode.Off ? [] : _tips.Where(t => t.Tip.StartsWith(Typed, StringComparison.Ordinal));

    /// <summary>Shows the tabs' and the quick access bar's tips.</summary>
    public void ShowTabs(IReadOnlyList<ToolstripKeyTip> tips) => Show(ToolstripKeyTipMode.Tabs, tips);

    /// <summary>Shows the selected tab's controls' tips.</summary>
    public void ShowControls(IReadOnlyList<ToolstripKeyTip> tips) => Show(ToolstripKeyTipMode.Controls, tips);

    /// <summary>Hides the tips.</summary>
    public void Hide() => Show(ToolstripKeyTipMode.Off, []);

    /// <summary>
    /// Takes a typed key. A tip equal to what has been typed with this key is a full match (the
    /// first such, in the order given) and is returned for the host to act on; the tips stay as they
    /// are until the host shows others or hides them.
    /// </summary>
    /// <param name="key">The key's character; letters are taken in upper case.</param>
    public (ToolstripKeyTipMatch Match, ToolstripKeyTip? Tip) Type(char key)
    {
        if (Mode == ToolstripKeyTipMode.Off)
        {
            return (ToolstripKeyTipMatch.None, null);
        }

        string typed = Typed + char.ToUpperInvariant(key);
        if (_tips.FirstOrDefault(t => t.Tip == typed) is { } full)
        {
            Typed = "";
            return (ToolstripKeyTipMatch.Full, full);
        }

        if (_tips.Any(t => t.Tip.StartsWith(typed, StringComparison.Ordinal)))
        {
            Typed = typed;
            return (ToolstripKeyTipMatch.Partial, null);
        }

        return (ToolstripKeyTipMatch.None, null);
    }

    /// <summary>
    /// Escape: forgets a part-typed tip first; otherwise the controls' tips go back to the tabs'
    /// (returns <see cref="ToolstripKeyTipMode.Tabs"/>, for the host to show them), and the tabs'
    /// are hidden.
    /// </summary>
    /// <returns>The mode the host should show next.</returns>
    public ToolstripKeyTipMode Back()
    {
        if (Typed.Length > 0)
        {
            Typed = "";
            return Mode;
        }

        if (Mode == ToolstripKeyTipMode.Controls)
        {
            return ToolstripKeyTipMode.Tabs;
        }

        Hide();
        return ToolstripKeyTipMode.Off;
    }

    /// <summary>
    /// Gives every entry a tip: its own when it has one; otherwise a letter of its label not yet
    /// taken - the first letters of its words, then its other letters, then any free letter - and,
    /// when there are more entries than free letters, two letters behind one letter kept for them.
    /// No tip given here begins another or is begun by one (a tip that began another would be
    /// complete before the longer one could be typed); tips given by the layout are kept as they
    /// are, repeats included (Run, Pause and Continue share "R" and only one of them shows).
    /// </summary>
    /// <param name="entries">Each entry's own tip, or null, and its label.</param>
    /// <returns>The tips, in the entries' order.</returns>
    public static IReadOnlyList<string> Assign(IReadOnlyList<(string? Tip, string? Label)> entries)
    {
        var tips = new string?[entries.Count];
        var used = new List<string>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Tip is { Length: > 0 } own)
            {
                tips[i] = own;
                used.Add(own);
            }
        }

        bool Free(string tip) => !used.Any(u => u.StartsWith(tip, StringComparison.Ordinal) || tip.StartsWith(u, StringComparison.Ordinal));
        int needed = tips.Count(t => t is null);
        List<char> freeLetters = [.. Enumerable.Range('A', 26).Select(c => (char)c).Where(c => Free(c.ToString()))];
        char? reserve = null;
        if (needed > freeLetters.Count && freeLetters.Count > 0)
        {
            reserve = freeLetters.Contains('Y') ? 'Y' : freeLetters[^1];
            used.Add(reserve.Value.ToString());
        }

        int singles = reserve is null ? freeLetters.Count : freeLetters.Count - 1;
        int overflow = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            if (tips[i] is not null)
            {
                continue;
            }

            string? tip = null;
            if (singles > 0)
            {
                string label = (entries[i].Label ?? "").ToUpperInvariant();
                IEnumerable<char> words = label.Split([' ', '\n', '-', '/', '.'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.FirstOrDefault(char.IsAsciiLetterUpper)).Where(c => c != default);
                tip = words.Concat(label.Where(char.IsAsciiLetterUpper)).Concat(freeLetters)
                    .Select(c => c.ToString()).FirstOrDefault(Free);
            }

            if (tip is null && reserve is not null)
            {
                tip = $"{reserve}{(char)('A' + overflow % 26)}";
                overflow++;
            }

            if (tip is not null)
            {
                singles -= tip.Length == 1 ? 1 : 0;
                tips[i] = tip;
                used.Add(tip);
            }
        }

        return [.. tips.Select(t => t ?? "")];
    }

    private void Show(ToolstripKeyTipMode mode, IReadOnlyList<ToolstripKeyTip> tips)
    {
        Mode = mode;
        _tips = tips;
        Typed = "";
    }
}
