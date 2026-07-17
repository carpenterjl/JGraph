using JGraph.Core.Data;
using JGraph.Core.Drawing;
using JGraph.Core.Primitives;
using JGraph.Serialization.Dto;

namespace JGraph.Serialization.Mapping;

/// <summary>Shared conversions between primitive/style model values and their document DTOs.</summary>
internal static class DtoConvert
{
    public static PointDto ToDto(Point2D p) => new(p.X, p.Y);

    public static Point2D ToPoint(PointDto d) => new(d.X, d.Y);

    public static RangeDto ToDto(DataRange r) => new(r.Min, r.Max);

    public static DataRange ToRange(RangeDto d) => new(d.Min, d.Max);

    public static RectDto ToDto(Rect2D r) => new(r.X, r.Y, r.Width, r.Height);

    public static Rect2D ToRect(RectDto d) => new(d.X, d.Y, d.Width, d.Height);

    public static SizeDto ToDto(Size2D s) => new(s.Width, s.Height);

    public static Size2D ToSize(SizeDto d) => new(d.Width, d.Height);

    public static LineStyleDto ToDto(LineStyle s) => new(s.Color, s.Width, s.Dash, s.Cap, s.Join);

    public static LineStyle ToLineStyle(LineStyleDto d) => new(d.Color, d.Width, d.Dash, d.Cap, d.Join);

    public static TextStyleDto ToDto(TextStyle s) => new(s.Color, s.FontSize, s.FontFamily, s.Bold, s.Italic);

    public static TextStyle ToTextStyle(TextStyleDto d) => new(d.Color, d.FontSize, d.FontFamily, d.Bold, d.Italic);

    public static SeriesDto ToDto(IDataSeries data)
    {
        int n = data.Count;
        var xs = new double[n];
        var ys = new double[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = data.GetX(i);
            ys[i] = data.GetY(i);
        }

        return new SeriesDto(xs, ys);
    }

    public static ArrayDataSeries ToSeries(SeriesDto d) => new(d.Xs, d.Ys);

    public static ColormapDto ToDto(Colormap c) => new(c.Name, c.Stops.ToArray());

    public static Colormap ToColormap(ColormapDto d) =>
        d.Stops.Length >= 2 ? new Colormap(string.IsNullOrEmpty(d.Name) ? "Custom" : d.Name, d.Stops) : Colormap.Viridis;
}
