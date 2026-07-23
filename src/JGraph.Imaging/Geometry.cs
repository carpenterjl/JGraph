namespace JGraph.Imaging;

/// <summary>Geometric image operations: resize, rotate, crop.</summary>
public static class Geometry
{
    /// <summary>Resampling method for <see cref="Resize"/> and <see cref="Rotate"/>.</summary>
    public enum Interpolation
    {
        /// <summary>Nearest-neighbour (crisp, blocky).</summary>
        Nearest,

        /// <summary>Bilinear (smooth).</summary>
        Bilinear,
    }

    /// <summary>
    /// Resizes an image to <paramref name="newHeight"/>×<paramref name="newWidth"/> (MATLAB <c>imresize</c>).
    /// Bilinear uses align-corners mapping, so corner pixels are preserved exactly.
    /// </summary>
    public static ImageBuffer Resize(ImageBuffer image, int newHeight, int newWidth, Interpolation method = Interpolation.Bilinear)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newWidth);

        var result = new ImageBuffer(newHeight, newWidth, image.Channels);
        double rowScale = newHeight == 1 ? 0 : (image.Height - 1) / (double)(newHeight - 1);
        double colScale = newWidth == 1 ? 0 : (image.Width - 1) / (double)(newWidth - 1);

        for (int r = 0; r < newHeight; r++)
        {
            double srcR = r * rowScale;
            for (int c = 0; c < newWidth; c++)
            {
                double srcC = c * colScale;
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    result[r, c, ch] = method == Interpolation.Nearest
                        ? image[(int)Math.Round(srcR), (int)Math.Round(srcC), ch]
                        : Bilinear(image, srcR, srcC, ch);
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// Rotates an image counter-clockwise by <paramref name="degrees"/> about its centre (MATLAB <c>imrotate</c>).
    /// With <paramref name="loose"/> the output grows to fit the whole rotated image; otherwise it keeps the
    /// input size ('crop'). Pixels outside the source are filled with 0.
    /// </summary>
    public static ImageBuffer Rotate(ImageBuffer image, double degrees, Interpolation method = Interpolation.Bilinear, bool loose = true)
    {
        ArgumentNullException.ThrowIfNull(image);
        double radians = degrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);

        int outHeight = image.Height;
        int outWidth = image.Width;
        if (loose)
        {
            // Subtract a tiny epsilon so an exact integer extent (e.g. 2.0 + fp noise) doesn't ceil up.
            outWidth = (int)Math.Ceiling((Math.Abs(image.Width * cos)) + (Math.Abs(image.Height * sin)) - 1e-9);
            outHeight = (int)Math.Ceiling((Math.Abs(image.Width * sin)) + (Math.Abs(image.Height * cos)) - 1e-9);
            outWidth = Math.Max(1, outWidth);
            outHeight = Math.Max(1, outHeight);
        }

        double srcCenterX = (image.Width - 1) / 2.0;
        double srcCenterY = (image.Height - 1) / 2.0;
        double dstCenterX = (outWidth - 1) / 2.0;
        double dstCenterY = (outHeight - 1) / 2.0;

        var result = new ImageBuffer(outHeight, outWidth, image.Channels);
        for (int r = 0; r < outHeight; r++)
        {
            double dy = r - dstCenterY;
            for (int c = 0; c < outWidth; c++)
            {
                double dx = c - dstCenterX;
                // Inverse rotation (clockwise by the same angle) back into source coordinates.
                double srcX = srcCenterX + (dx * cos) + (dy * sin);
                double srcY = srcCenterY - (dx * sin) + (dy * cos);
                if (srcX < -0.5 || srcX > image.Width - 0.5 || srcY < -0.5 || srcY > image.Height - 0.5)
                {
                    continue; // outside source → stays 0
                }

                for (int ch = 0; ch < image.Channels; ch++)
                {
                    result[r, c, ch] = method == Interpolation.Nearest
                        ? image[Clamp((int)Math.Round(srcY), image.Height), Clamp((int)Math.Round(srcX), image.Width), ch]
                        : Bilinear(image, srcY, srcX, ch);
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// Crops a rectangle from an image: <paramref name="x"/> is the 0-based left column,
    /// <paramref name="y"/> the 0-based top row, and width/height are in pixels. The rect is clamped
    /// to the image bounds.
    /// </summary>
    public static ImageBuffer Crop(ImageBuffer image, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(image);
        int col0 = Math.Clamp(x, 0, image.Width - 1);
        int row0 = Math.Clamp(y, 0, image.Height - 1);
        int col1 = Math.Clamp(col0 + width - 1, col0, image.Width - 1);
        int row1 = Math.Clamp(row0 + height - 1, row0, image.Height - 1);

        int outWidth = col1 - col0 + 1;
        int outHeight = row1 - row0 + 1;
        var result = new ImageBuffer(outHeight, outWidth, image.Channels);
        for (int r = 0; r < outHeight; r++)
        {
            for (int c = 0; c < outWidth; c++)
            {
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    result[r, c, ch] = image[row0 + r, col0 + c, ch];
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    private static double Bilinear(ImageBuffer image, double srcR, double srcC, int channel)
    {
        int r0 = Clamp((int)Math.Floor(srcR), image.Height);
        int c0 = Clamp((int)Math.Floor(srcC), image.Width);
        int r1 = Clamp(r0 + 1, image.Height);
        int c1 = Clamp(c0 + 1, image.Width);
        double fr = srcR - Math.Floor(srcR);
        double fc = srcC - Math.Floor(srcC);

        double top = (image[r0, c0, channel] * (1 - fc)) + (image[r0, c1, channel] * fc);
        double bottom = (image[r1, c0, channel] * (1 - fc)) + (image[r1, c1, channel] * fc);
        return (top * (1 - fr)) + (bottom * fr);
    }

    private static int Clamp(int index, int length) => Math.Clamp(index, 0, length - 1);
}
