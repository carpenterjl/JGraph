using JGraph.Imaging;

namespace JGraph.Scripting.Jgs;

internal static partial class JgsBuiltins
{
    private static ImageBuffer PadExport(ImageBuffer source, int width, int height, int padding)
    {
        int left = source.Width - 1, right = 0, top = source.Height - 1, bottom = 0;
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            bool ink = false;
            for (int c = 0; c < source.Channels; c++)
                ink |= Math.Abs(source[y, x, c] - source[0, 0, c]) > 1.0 / 255;
            if (ink)
            {
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        if (right < left || bottom < top)
        {
            left = top = 0;
            right = source.Width - 1;
            bottom = source.Height - 1;
        }
        using var cropped = new ImageBuffer(bottom - top + 1, right - left + 1, source.Channels) { Class = source.Class };
        for (int y = 0; y < cropped.Height; y++)
        for (int x = 0; x < cropped.Width; x++)
        for (int c = 0; c < source.Channels; c++)
            cropped[y, x, c] = source[y + top, x + left, c];
        using var resized = Geometry.Resize(cropped, height - 2 * padding, width - 2 * padding);
        var result = new ImageBuffer(height, width, source.Channels) { Class = source.Class };
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        for (int c = 0; c < source.Channels; c++)
            result[y, x, c] = x >= padding && x < width - padding && y >= padding && y < height - padding ? resized[y - padding, x - padding, c] : source[0, 0, c];
        return result;
    }
    private static ImageBuffer CropReadRegion(ImageBuffer source, JgsValue region, int line, int col)
    {
        if (region.Type != JgsType.Cell || region.AsCell.Length != 2)
            throw new JgsRuntimeException(line, col, "PixelRegion requires row and column ranges in a cell.");
        int[] Range(JgsValue value, int length)
        {
            double[] a = ToDoubles("PixelRegion", value, line, col);
            if (a.Length is not (2 or 3))
                throw new JgsRuntimeException(line, col, "PixelRegion ranges are [start stop] or [start step stop].");
            double first = a[0], step = a.Length == 3 ? a[1] : 1, last = a[^1];
            if (!double.IsFinite(first) || !double.IsFinite(step) || first < 1 || step < 1 || first != Math.Floor(first) || step != Math.Floor(step) || last != Math.Floor(last) || last > length || last < first)
                throw new JgsRuntimeException(line, col, "Invalid PixelRegion range.");
            return Enumerable.Range(0, (int)((last - first) / step) + 1).Select(i => (int)(first + i * step) - 1).ToArray();
        }
        int[] rows = Range(region.AsCell[0], source.Height), cols = Range(region.AsCell[1], source.Width);
        var result = new ImageBuffer(rows.Length, cols.Length, source.Channels) { Class = source.Class };
        for (int r = 0; r < rows.Length; r++)
        for (int c = 0; c < cols.Length; c++)
        for (int ch = 0; ch < source.Channels; ch++)
            result.Pixels[(r * cols.Length + c) * source.Channels + ch] = source.Pixels[(rows[r] * source.Width + cols[c]) * source.Channels + ch];
        return result;
    }

    private static JgsValue TileImages(IReadOnlyList<JgsValue> args, int line, int col)
    {
        var spec = new OptionSpec("imtile", [], ["GridSize", "BorderSize", "BackgroundColor", "ThumbnailSize"]);
        ParsedArgs options = spec.Parse(args, 1, line, col);
        if (args.Count == 0 || args[0].Type != JgsType.Cell)
            throw new JgsRuntimeException(line, col, "imtile currently expects a cell of images.");
        var images = new List<ImgArg>();
        try
        {
            foreach (var value in args[0].AsCell)
                images.Add(ImgLike("imtile", [value], 0, line, col));
            if (images.Count == 0)
                return JgsValue.Array([]);
            int h = images.Max(i => i.Buffer.Height), w = images.Max(i => i.Buffer.Width);
            if (options.Vector("ThumbnailSize") is { } thumbnail)
            {
                if (thumbnail.Length != 2 || thumbnail.Any(d => d < 1 || !double.IsFinite(d)))
                    throw new JgsRuntimeException(line, col, "ThumbnailSize needs two positive dimensions.");
                h = (int)thumbnail[0];
                w = (int)thumbnail[1];
            }
            double[] grid = options.Vector("GridSize") ?? [Math.Ceiling(Math.Sqrt(images.Count)), double.NaN];
            if (grid.Length != 2)
                throw new JgsRuntimeException(line, col, "GridSize needs two dimensions.");
            int nr = double.IsNaN(grid[0]) ? (int)Math.Ceiling(images.Count / grid[1]) : (int)grid[0];
            int nc = double.IsNaN(grid[1]) ? (int)Math.Ceiling(images.Count / (double)nr) : (int)grid[1];
            if (nr < 1 || nc < 1 || nr * nc < images.Count)
                throw new JgsRuntimeException(line, col, "GridSize cannot hold the images.");
            double[] border = options.Vector("BorderSize") ?? [0, 0];
            int by = (int)border[0], bx = (int)border[^1];
            if (by < 0 || bx < 0)
                throw new JgsRuntimeException(line, col, "BorderSize must be nonnegative.");
            var result = new ImageBuffer(nr * (h + 2 * by), nc * (w + 2 * bx), 3) { Class = images[0].Buffer.Class };
            if (options.Named("BackgroundColor") is { } background)
            {
                var color = OptionColor(background, line, col, "imtile");
                for (int p = 0; p < result.Height * result.Width; p++)
                {
                    result.Pixels[p * 3] = color.R / 255.0;
                    result.Pixels[p * 3 + 1] = color.G / 255.0;
                    result.Pixels[p * 3 + 2] = color.B / 255.0;
                }
            }
            for (int i = 0; i < images.Count; i++)
            {
                var original = images[i].Buffer;
                double ratio = Math.Min(1, Math.Min(h / (double)original.Height, w / (double)original.Width));
                using var scaled = ratio < 1 ? Geometry.Resize(original, Math.Max(1, (int)Math.Round(original.Height * ratio)), Math.Max(1, (int)Math.Round(original.Width * ratio))) : null;
                var input = scaled ?? original;
                int y = i / nc * (h + 2 * by) + by + (h - input.Height) / 2, x = i % nc * (w + 2 * bx) + bx + (w - input.Width) / 2;
                for (int r = 0; r < input.Height; r++)
                for (int c = 0; c < input.Width; c++)
                for (int ch = 0; ch < 3; ch++)
                    result.Pixels[((y + r) * result.Width + x + c) * 3 + ch] = input.Pixels[(r * input.Width + c) * input.Channels + (input.Channels == 1 ? 0 : ch)];
            }
            return JgsValue.Image(result);
        }
        finally { foreach (var image in images) image.Dispose(); }
    }
}
