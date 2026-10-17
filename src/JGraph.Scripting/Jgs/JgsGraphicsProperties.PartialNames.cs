using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The start of a property name R2025b's <c>get</c>, <c>set</c>, <c>findobj</c> and the creating
/// functions take in place of the whole name (open item 86; probe_86 swept every start of every
/// name of 23 kinds of object, 22,000 answers). The rule read off the sweep: the names of the
/// object's R2025b class that begin with the text are ranked, a listed name that can be set above
/// a listed one that cannot or a <c>…Mode</c> twin, those above the hidden names; the start means
/// the one name in the best rank that has any, and is refused when that rank holds two. So
/// <c>'Pos'</c> is <c>Position</c> although <c>PositionConstraint</c> begins the same (it is one of
/// the handful of names R2025b never abbreviates), <c>'XLi'</c> is <c>XLim</c> over <c>XLimMode</c>,
/// <c>'YA'</c> is <c>YAxisLocation</c> over the read-only <c>YAxis</c>, <c>'Ta'</c> is <c>Tag</c>
/// over the hidden <c>TargetManager</c>, and <c>'Tit'</c>, <c>'Col'</c> on an axes and <c>'Mark'</c>
/// on a line are refused. A start that only hidden names begin is refused here where R2025b answers
/// one of them (divergence). The dot, <c>isprop</c> and <c>findobj</c>'s <c>-property</c> take whole
/// names only, as R2025b's do; a struct given to <c>set</c> takes starts as the pair form does.
/// </summary>
internal static partial class JgsGraphicsProperties
{
    /// <summary>
    /// The names R2025b's <c>get</c> and <c>set</c> never resolve a start to, measured: each stands
    /// beside a hidden older spelling of the same setting, which is where the start would go.
    /// </summary>
    private static readonly HashSet<string> NeverAbbreviated = new(StringComparer.OrdinalIgnoreCase)
    {
        "PositionConstraint", "XLimitMethod", "YLimitMethod", "ZLimitMethod", "RLimitMethod", "ThetaLimitMethod",
    };

    /// <summary>
    /// The whole name <paramref name="typed"/> is the start of for this object in a MATLAB-dialect
    /// script, or false when it is none, or more than one of equal rank. A whole name is never
    /// shortened to another; the caller has already looked the text up as one.
    /// </summary>
    internal static bool TryPartialName(GraphObject target, string typed, out string name)
    {
        name = typed;
        if (!JgsRunningDialect.ThreadIsMatlab || typed.Length == 0)
        {
            return false;
        }

        if (R2025bClassesOf(target) is not { } classes)
        {
            // A uicontrol, panel or menu, whose R2025b names were not recorded, takes the one listed
            // name the text begins, as the app components do (U1).
            if (!SpeaksAsComponent(target))
            {
                return false;
            }

            string[] starts = [.. NamesOf(target).Where(known => known.StartsWith(typed, StringComparison.OrdinalIgnoreCase))];
            if (starts.Length != 1 || !TryFind(target, starts[0], out GraphicsProperty only))
            {
                return false;
            }

            name = only.Name;
            return true;
        }

        string? listed = null;
        string? other = null;
        int listedCount = 0;
        int otherCount = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string cls in classes)
        {
            if (!R2025bNames.ByClass.TryGetValue(cls, out Dictionary<string, bool>? names))
            {
                continue;
            }

            foreach ((string candidate, bool hidden) in names)
            {
                if (hidden || !candidate.StartsWith(typed, StringComparison.OrdinalIgnoreCase)
                    || NeverAbbreviated.Contains(candidate) || !seen.Add(candidate))
                {
                    continue;
                }

                // A Mode twin ranks with the names that cannot be set: 'XLi' is XLim, not XLimMode.
                bool modeTwin = candidate.EndsWith("Mode", StringComparison.Ordinal)
                    && names.ContainsKey(candidate[..^4]);
                if (modeTwin || R2025bNames.ReadOnly(cls, candidate))
                {
                    otherCount++;
                    other = candidate;
                }
                else
                {
                    listedCount++;
                    listed = candidate;
                }
            }
        }

        string? found = listedCount == 1 ? listed : listedCount == 0 && otherCount == 1 ? other : null;
        if (found is null || !TryFind(target, found, out GraphicsProperty property))
        {
            return false;
        }

        name = property.Name;
        return true;
    }

    /// <summary>
    /// The name a <c>get</c>, <c>set</c> or <c>findobj</c> argument means: itself when the object
    /// answers to it whole, else the one name it is the start of, else itself for the refusal.
    /// </summary>
    internal static string WholeName(GraphObject target, string typed) =>
        TryFind(target, typed, out _) || !TryPartialName(target, typed, out string whole) ? typed : whole;
}
