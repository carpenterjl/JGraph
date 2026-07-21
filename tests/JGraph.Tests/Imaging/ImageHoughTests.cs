using JGraph.Imaging;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>M24c: the Hough line transform — accumulator geometry, peak picking, and segment extraction.</summary>
public class ImageHoughTests
{
    private static ImageBuffer Blank(int h, int w) => new(h, w, 1);

    [Fact]
    public void Accumulate_SizesTheAxesFromTheImageDiagonal()
    {
        using ImageBuffer image = Blank(3, 4);
        (ImageBuffer accumulator, double[] theta, double[] rho) = HoughTransform.Accumulate(image);
        using (accumulator)
        {
            Assert.Equal(180, theta.Length);
            Assert.Equal(-90, theta[0]);
            Assert.Equal(89, theta[^1]);

            // ceil(sqrt(9 + 16)) = 5 → rho runs -5..5.
            Assert.Equal(11, rho.Length);
            Assert.Equal(-5, rho[0]);
            Assert.Equal(5, rho[^1]);
            Assert.Equal(rho.Length, accumulator.Height);
            Assert.Equal(theta.Length, accumulator.Width);
        }
    }

    [Fact]
    public void Peaks_FindAHorizontalLineAtTheExpectedRhoAndTheta()
    {
        // A horizontal run on row 4 (1-based y = 5). Theta spans -90..89, so a horizontal line is
        // theta = -90° with rho = -y, exactly as MATLAB reports it.
        using ImageBuffer image = Blank(9, 20);
        for (int c = 2; c < 18; c++)
        {
            image[4, c, 0] = 1.0;
        }

        (ImageBuffer accumulator, double[] theta, double[] rho) = HoughTransform.Accumulate(image);
        using (accumulator)
        {
            (int RhoIndex, int ThetaIndex)[] peaks = HoughTransform.Peaks(accumulator, count: 1);
            (int rhoIndex, int thetaIndex) = Assert.Single(peaks);

            Assert.Equal(-90, theta[thetaIndex]);
            Assert.Equal(-5, rho[rhoIndex]);
            Assert.Equal(16, accumulator[rhoIndex, thetaIndex, 0]); // every pixel voted for it
        }
    }

    [Fact]
    public void Peaks_SuppressTheNeighbourhoodSoTwoLinesAreFoundSeparately()
    {
        using ImageBuffer image = Blank(21, 21);
        for (int i = 0; i < 21; i++)
        {
            image[3, i, 0] = 1.0;  // horizontal
            image[i, 15, 0] = 1.0; // vertical
        }

        (ImageBuffer accumulator, double[] theta, _) = HoughTransform.Accumulate(image);
        using (accumulator)
        {
            (int RhoIndex, int ThetaIndex)[] peaks = HoughTransform.Peaks(accumulator, count: 2);
            Assert.Equal(2, peaks.Length);

            // Horizontal is theta ≈ -90, vertical theta ≈ 0. A short line spreads its votes over a
            // couple of adjacent angles and can tie, so this asserts separation, not an exact bin.
            var angles = peaks.Select(p => theta[p.ThetaIndex]).OrderBy(a => a).ToArray();
            Assert.InRange(angles[0], -90, -88);
            Assert.InRange(angles[1], -2, 2);
        }
    }

    [Fact]
    public void Lines_RecoversTheEndpointsOfASegment()
    {
        using ImageBuffer image = Blank(9, 30);
        for (int c = 4; c <= 24; c++)
        {
            image[4, c, 0] = 1.0;
        }

        (ImageBuffer accumulator, double[] theta, double[] rho) = HoughTransform.Accumulate(image);
        using (accumulator)
        {
            (int RhoIndex, int ThetaIndex)[] peaks = HoughTransform.Peaks(accumulator, count: 1);
            HoughTransform.LineSegment segment = Assert.Single(
                HoughTransform.Lines(image, theta, rho, peaks, fillGap: 5, minLength: 10));

            // 1-based endpoints: columns 4 and 24 → x = 5 and 25, both on row 4 → y = 5.
            Assert.Equal(5, Math.Min(segment.Point1X, segment.Point2X));
            Assert.Equal(25, Math.Max(segment.Point1X, segment.Point2X));
            Assert.Equal(5, segment.Point1Y);
            Assert.Equal(5, segment.Point2Y);
            Assert.Equal(-90, segment.Theta);
        }
    }

    [Fact]
    public void Lines_SplitOnGapsWiderThanFillGap()
    {
        // Two 11-pixel runs on the same row separated by a 9-pixel gap.
        using ImageBuffer image = Blank(9, 40);
        for (int c = 0; c <= 10; c++)
        {
            image[4, c, 0] = 1.0;
        }

        for (int c = 20; c <= 30; c++)
        {
            image[4, c, 0] = 1.0;
        }

        (ImageBuffer accumulator, double[] theta, double[] rho) = HoughTransform.Accumulate(image);
        using (accumulator)
        {
            (int RhoIndex, int ThetaIndex)[] peaks = HoughTransform.Peaks(accumulator, count: 1);

            // A small fillGap keeps them apart; a large one bridges the gap into one segment.
            Assert.Equal(2, HoughTransform.Lines(image, theta, rho, peaks, fillGap: 3, minLength: 5).Length);
            Assert.Single(HoughTransform.Lines(image, theta, rho, peaks, fillGap: 15, minLength: 5));
        }
    }

    [Fact]
    public void Lines_DropSegmentsShorterThanMinLength()
    {
        using ImageBuffer image = Blank(9, 20);
        for (int c = 0; c < 5; c++)
        {
            image[4, c, 0] = 1.0;
        }

        (ImageBuffer accumulator, double[] theta, double[] rho) = HoughTransform.Accumulate(image);
        using (accumulator)
        {
            (int RhoIndex, int ThetaIndex)[] peaks = HoughTransform.Peaks(accumulator, count: 1);
            Assert.Empty(HoughTransform.Lines(image, theta, rho, peaks, fillGap: 5, minLength: 40));
        }
    }
}
