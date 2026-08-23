using JGraph.Api;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M80: the pieces <c>tiledlayout</c> and <c>nexttile</c> need now that a layout is an object a
/// script can name — the two handle peels, and the arithmetic that turns an argument into a tile
/// number.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>
    /// The properties <c>tiledlayout(…, Name, Value)</c> takes. The list is what tells a leading
    /// word that is an option name from one that is <c>'flow'</c>.
    /// </summary>
    private static readonly string[] TiledLayoutOptionNames =
    [
        "TileSpacing", "Padding", "TileIndexing", "Title", "Subtitle", "XLabel", "YLabel",
        "Visible", "Tag", "UserData",
    ];

    /// <summary>
    /// Splits a leading container off <c>tiledlayout</c>'s arguments.
    /// <para>
    /// This cannot be the ordinary figure peel, and the reason is worth stating: a figure's handle is
    /// its number, so in <c>tiledlayout(3, 3)</c> the first 3 <em>is</em> a handle to figure 3 as soon
    /// as a script has opened three figures — and the ordinary peel would take the grid's row count
    /// as its parent. What tells the two apart is the shape of the rest of the call rather than the
    /// first argument: a parent is followed by two numbers, or by the one word <c>'flow'</c>.
    /// </para>
    /// </summary>
    private static (FigureModel Figure, IReadOnlyList<JgsValue> Remaining) PeelLayoutParent(
        IReadOnlyList<JgsValue> args)
    {
        bool parented = args.Count switch
        {
            >= 3 => args[1].Type != JgsType.String && args[2].Type != JgsType.String,
            2 => args[1].Type == JgsType.String,
            _ => false,
        };

        if (parented
            && JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? entry)
            && entry.Target is FigureModel figure)
        {
            return (figure, [.. args.Skip(1)]);
        }

        return (JG.CurrentFigure, args);
    }

    /// <summary>Splits a leading layout handle off, which is how <c>nexttile(t, …)</c> names one.</summary>
    private static (TiledLayoutModel? Layout, IReadOnlyList<JgsValue> Remaining) PeelLayout(
        IReadOnlyList<JgsValue> args)
    {
        if (args.Count == 0
            || !JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? entry)
            || entry.Target is not TiledLayoutModel layout)
        {
            return (null, args);
        }

        return (layout, [.. args.Skip(1)]);
    }

    /// <summary>
    /// The current figure's layout, making a one-tile flowing one if it has none. <c>nexttile</c>
    /// without a <c>tiledlayout</c> before it is legal MATLAB and means exactly that.
    /// </summary>
    private static TiledLayoutModel CurrentLayout()
    {
        FigureModel figure = JG.CurrentFigure;
        if (figure.TiledLayout is { } existing)
        {
            return existing;
        }

        var layout = new TiledLayoutModel { Flow = true };
        figure.TiledLayout = layout;
        return layout;
    }

    /// <summary>A positive whole number, refused by name when the argument is not one.</summary>
    internal static int WholeNumber(string what, double given, int line, int col)
    {
        if (given < 1 || !double.IsFinite(given) || System.Math.Abs(given - System.Math.Round(given)) > 1e-9)
        {
            throw new JgsRuntimeException(line, col,
                $"{what} is a positive whole number, but got {given:G6}.");
        }

        return (int)System.Math.Round(given);
    }
}
