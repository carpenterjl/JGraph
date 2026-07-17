using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace JGraph.Controls.Inspector;

/// <summary>
/// Converts a "#RRGGBB"/"#AARRGGBB" hex string (the inspector's engine-independent color exchange
/// format) into a WPF brush for swatch display. Null or invalid input yields a transparent brush.
/// </summary>
public sealed class HexBrushConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && Core.Drawing.Color.TryParse(hex, out Core.Drawing.Color color))
        {
            var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        return Brushes.Transparent;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
