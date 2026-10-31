using System.Text.RegularExpressions;

namespace JGraph.Scripting.Designer;

/// <summary>
/// Convert's rules (App Designer window plan W8c, <c>appdesigner/util/AppDataCopyUtil</c>, <c>PositionUtil.clampToParent</c>), free of a document: the new app's name, how the
/// figure's width is shared out among the regions, which region a component goes to, where it stands in it.
/// </summary>
public static partial class DesignConversion
{
    /// <summary>What a converted app's name ends in (<c>"_converted"</c>).</summary>
    public const string Suffix = "_converted";

    /// <summary>What Save Copy As's title says (<c>saveCopyAsDialogTitle</c>).</summary>
    public const string SaveCopyTitle = "Save Copy As";

    /// <summary>What goes on an exported file's name (<c>ExportModelCommand</c>).</summary>
    public const string ExportSuffix = "_exported";

    /// <summary>A copy over the app's own file (<c>copyOverAppError</c> for an app).</summary>
    public const string CopyOverApp = "Cannot copy over existing app";

    /// <summary>What the first step of its tutorial says (<c>responsify_newAppCreated</c>).</summary>
    public const string Converted = "Your app has been converted to use auto-reflowing behaviors.";

    /// <summary>What its tutorial says of a figure's own callback (<c>responsify_sizeChanged</c>).</summary>
    public const string SizeChangedDetached = "Your original app had a SizeChangedFcn callback on the Figure. This callback has been disconnected to allow automatic resizing behaviors.";

    /// <summary>
    /// The new app's name (<c>_createAppName</c>): the name with what an earlier conversion left taken off, the suffix on it, and a number from 2 when an open app has that.
    /// </summary>
    public static string ConvertedName(string appName, IEnumerable<string> openApps)
    {
        ArgumentNullException.ThrowIfNull(appName);
        ArgumentNullException.ThrowIfNull(openApps);
        string stem = EarlierConversion().Replace(appName, string.Empty, 1) + Suffix;
        var open = new HashSet<string>(openApps, StringComparer.Ordinal);
        string name = stem;
        for (int n = 2; open.Contains(name); n++)
        {
            name = stem + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return name;
    }

    /// <summary>Where each region ends as a share of the figure's width (<c>getRegionPanelDimensionRatios</c>, from the template's boxes): 0, each region's right edge, 1.</summary>
    public static IReadOnlyList<double> Ratios(int panels) => panels switch
    {
        2 => [0, 221d / 640, 1],
        3 => [0, 221d / 860, 641d / 860, 1],
        _ => throw new ArgumentOutOfRangeException(nameof(panels), panels, "A responsive app has 2 or 3 panels."),
    };

    /// <summary>
    /// Each region's left edge and width for a figure of <paramref name="width"/> (<c>resizeRegionPanels</c>): rounded shares of it less a pixel, one after another from 1, what
    /// is left over going to the last.
    /// </summary>
    public static IReadOnlyList<(double X, double Width)> RegionBoxes(IReadOnlyList<double> ratios, double width)
    {
        ArgumentNullException.ThrowIfNull(ratios);
        var boxes = new List<(double X, double Width)>();
        double x = 1;
        double used = 0;
        for (int i = 1; i < ratios.Count; i++)
        {
            double w = Math.Floor(width * (ratios[i] - ratios[i - 1]) - 1 + 0.5);
            boxes.Add((x, w));
            x += w;
            used += w;
        }

        double left = width - used;
        if (left != 0)
        {
            boxes[^1] = (boxes[^1].X, boxes[^1].Width + left);
        }

        return boxes;
    }

    /// <summary>
    /// The regions a component's middle falls in (<c>getComponentsWithinBounds</c>): both ends count. R2025b puts a component right on a boundary in both regions, so that it
    /// is made twice; the caller takes the first.
    /// </summary>
    public static bool InRegion(double x, double width, double figureWidth, double low, double high)
    {
        double left = x / figureWidth;
        double right = (x + width) / figureWidth;
        double middle = left + (right - left) * 0.5;
        return middle >= low && middle <= high;
    }

    /// <summary>A box brought inside a parent of the size given (<c>PositionUtil.clampToParent</c>).</summary>
    public static (double X, double Y, double Width, double Height) Clamp(double x, double y, double width, double height, double parentWidth, double parentHeight)
    {
        static double Within(double value, double low) => value < low ? low : value;
        (x, width) = One(x, width, parentWidth);
        (y, height) = One(y, height, parentHeight);
        return (x, y, width, height);

        static (double At, double Size) One(double at, double size, double parent)
        {
            if (at < 1)
            {
                at = 1;
                if (size > parent)
                {
                    size = parent - 1;
                }
            }

            if (at + size > parent)
            {
                at = Within(at - (at + size - parent), 1);
                if (size > parent)
                {
                    size = Within(size + (parent - 1 - size), 1);
                }
            }

            return (at, size);
        }
    }

    [GeneratedRegex(@"(_converted)(\d*)\b")]
    private static partial Regex EarlierConversion();
}

/// <summary>What Convert made: the new app, and the figure callback it disconnected, if any (<c>createCallbacks</c>'s answer).</summary>
/// <param name="Document">The new app, not saved.</param>
/// <param name="Detached">The name of the figure's own <c>SizeChangedFcn</c> callback, which stands unbound in it; null when it had none.</param>
public sealed record DesignConverted(DesignDocument Document, string? Detached);
