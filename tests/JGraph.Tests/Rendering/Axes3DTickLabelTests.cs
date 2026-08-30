using System;
using System.Collections.Generic;
using System.Linq;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Rendering;

/// <summary>
/// Where a 3D axes puts its tick labels. The three rulers do not merely run alongside the box, they
/// meet: the x and y rulers share the near bottom corner, and the vertical edge the z ruler rides is
/// the foot of one of them. A label pushed out radially from the floor centre lands on the same pixel
/// for both rulers at a shared corner, which is how "-1.5" came to be printed through "-3" and "3"
/// through "-3". These tests pin the placement by the only thing that matters about it — the boxes
/// the labels occupy must not intersect.
/// </summary>
public class Axes3DTickLabelTests
{
    /// <summary>The reported figure's shape: a surface over [-3,3]x[-3,3] seen from view(45,30).</summary>
    private static FigureModel Figure(double azimuth = 45, double elevation = 30)
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();

        const int n = 21;
        var x = new double[n];
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = -3 + (6.0 * i / (n - 1));
            y[i] = x[i];
        }

        var z = new double[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                double radius = Math.Sqrt((x[c] * x[c]) + (y[r] * y[r])) + double.Epsilon;
                z[r, c] = Math.Sin(radius) / radius;
            }
        }

        axes.AddSurface(x, y, z);
        axes.Azimuth = azimuth;
        axes.Elevation = elevation;
        axes.PrimaryXAxis.AutoScale = false;
        axes.PrimaryXAxis.Range = new DataRange(-3, 3);
        axes.PrimaryYAxis.AutoScale = false;
        axes.PrimaryYAxis.Range = new DataRange(-3, 3);
        axes.ZAxis.AutoScale = false;
        axes.ZAxis.Range = new DataRange(-1.5, 1.5);
        return figure;
    }

    private static RecordingRenderContext Render(FigureModel figure)
    {
        var context = new RecordingRenderContext(new Size2D(640, 480));
        new FigureRenderer().Render(figure, context);
        return context;
    }

    /// <summary>
    /// Resolves each recorded string to the rectangle it covers, using the same text metric the
    /// renderer measured with. Anchor plus alignment is the whole placement; either alone is not.
    /// </summary>
    private static List<(string Text, Rect2D Box)> LabelBoxes(RecordingRenderContext context)
    {
        var boxes = new List<(string, Rect2D)>();
        for (int i = 0; i < context.Texts.Count; i++)
        {
            string text = context.Texts[i];
            Size2D size = context.MeasureText(text, context.TextStyles[i]);
            Point2D at = context.TextPositions[i];
            (HorizontalAlignment h, VerticalAlignment v) = context.TextAlignments[i];

            double left = h switch
            {
                HorizontalAlignment.Left => at.X,
                HorizontalAlignment.Right => at.X - size.Width,
                _ => at.X - (size.Width / 2),
            };
            double top = v switch
            {
                VerticalAlignment.Top => at.Y,
                VerticalAlignment.Bottom => at.Y - size.Height,
                VerticalAlignment.Baseline => at.Y - size.Height,
                _ => at.Y - (size.Height / 2),
            };

            boxes.Add((text, new Rect2D(left, top, size.Width, size.Height)));
        }

        return boxes;
    }

    private static bool Overlaps(Rect2D a, Rect2D b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width &&
        a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    /// <summary>
    /// The bug as reported: at view(45,30) over [-3,3] with z on [-1.5,1.5] every ruler's extreme
    /// tick lands on a corner another ruler also labels, and the labels were drawn on top of each
    /// other. No two of them may share a pixel.
    /// </summary>
    [Fact]
    public void TickLabels_AtSharedBoxCorners_DoNotOverlap()
    {
        RecordingRenderContext context = Render(Figure());
        List<(string Text, Rect2D Box)> boxes = LabelBoxes(context);

        // The reported pairs by name, so a regression says which corner came apart rather than only
        // that some pair did: "-1.5" is the z ruler's foot, "-3"/"3" the two floor rulers' ends.
        Assert.Contains(boxes, b => b.Text == "-1.5");
        Assert.Equal(2, boxes.Count(b => b.Text == "-3"));
        Assert.Equal(2, boxes.Count(b => b.Text == "3"));

        for (int i = 0; i < boxes.Count; i++)
        {
            for (int j = i + 1; j < boxes.Count; j++)
            {
                Assert.False(
                    Overlaps(boxes[i].Box, boxes[j].Box),
                    $"'{boxes[i].Text}' at {boxes[i].Box} overlaps '{boxes[j].Text}' at {boxes[j].Box}");
            }
        }
    }

    /// <summary>
    /// The corner is not special-cased, so the separation must survive turning the box. Each quarter
    /// turn hands the near corner to a different pair of ruler ends and moves the vertical edge the
    /// z ruler rides; all four have to come out clean.
    /// </summary>
    [Theory]
    [InlineData(45)]
    [InlineData(135)]
    [InlineData(-45)]
    [InlineData(-135)]
    public void TickLabels_DoNotOverlap_AtAnyQuarterTurn(double azimuth)
    {
        List<(string Text, Rect2D Box)> boxes = LabelBoxes(Render(Figure(azimuth)));

        for (int i = 0; i < boxes.Count; i++)
        {
            for (int j = i + 1; j < boxes.Count; j++)
            {
                Assert.False(
                    Overlaps(boxes[i].Box, boxes[j].Box),
                    $"az={azimuth}: '{boxes[i].Text}' overlaps '{boxes[j].Text}'");
            }
        }
    }

    /// <summary>
    /// Why the corners come apart: a floor label is pushed along the partner axis, so the x labels
    /// leave the box in the direction y grows outward and the y labels in the direction x does. Those
    /// two directions differ, which is the entire separation — a radial push from the floor centre
    /// makes them equal at the shared corner and the labels coincide exactly.
    /// </summary>
    [Fact]
    public void FloorLabels_LeaveTheBox_AlongTheirPartnerAxis()
    {
        List<(string Text, Rect2D Box)> boxes = LabelBoxes(Render(Figure()));

        // At view(45,30) the near corner carries x = 3 and y = -3. The x label goes down and left,
        // the y label down and right: same side of the box, opposite sides of the corner.
        Rect2D xEnd = boxes.Where(b => b.Text == "3").OrderByDescending(b => b.Box.Y).First().Box;
        Rect2D yEnd = boxes.Where(b => b.Text == "-3").OrderByDescending(b => b.Box.Y).First().Box;

        Assert.True(
            xEnd.X + xEnd.Width <= yEnd.X,
            $"the two corner labels must be side by side, got x-end {xEnd} and y-end {yEnd}");
        Assert.True(
            Math.Abs(xEnd.Y - yEnd.Y) < 4,
            "both sit below the corner, so they share a row");
    }

    /// <summary>
    /// The z ruler's foot and the floor ruler that ends at the same corner: one is pushed left off
    /// the vertical edge, the other down off the floor edge, so they stack instead of colliding.
    /// </summary>
    [Fact]
    public void ZRulerFoot_ClearsTheFloorLabel_AtTheSameCorner()
    {
        List<(string Text, Rect2D Box)> boxes = LabelBoxes(Render(Figure()));

        Rect2D zFoot = boxes.Single(b => b.Text == "-1.5").Box;
        // The floor label nearest the z ruler's foot is the one that used to be printed through it.
        Rect2D nearest = boxes
            .Where(b => b.Text != "-1.5")
            .OrderBy(b => Math.Abs(b.Box.X - zFoot.X) + Math.Abs(b.Box.Y - zFoot.Y))
            .First().Box;

        Assert.False(Overlaps(zFoot, nearest), $"z foot {zFoot} overlaps floor label {nearest}");
        Assert.True(zFoot.Y + zFoot.Height <= nearest.Y, "the z label sits above the floor label");
    }
}
