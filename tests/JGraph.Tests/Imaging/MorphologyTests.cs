using JGraph.Imaging;
using Xunit;

namespace JGraph.Tests.Imaging;

/// <summary>
/// M46 wave F: structuring elements, the reconstruction family, the binary neighbourhood operations,
/// and the three distance transforms.
/// </summary>
public sealed class MorphologyTests
{
    // --- Structuring elements ----------------------------------------------------------------

    [Fact]
    public void Disk_IsTheExactEuclideanDisk()
    {
        StructuringElement disk = StructuringElement.Disk(2);
        Assert.Equal(5, disk.Rows);
        Assert.Equal(5, disk.Cols);

        // Radius 2 keeps only what is genuinely within 2: the four axis tips reach, but (1, 2) at
        // √5 does not, so the shape is a plus with filled shoulders — thirteen offsets.
        Assert.False(disk.Member(0, 0));
        Assert.False(disk.Member(0, 4));
        Assert.True(disk.Member(0, 2));
        Assert.True(disk.Member(2, 0));
        Assert.True(disk.Member(1, 1));
        Assert.False(disk.Member(1, 4));
        Assert.Equal(13, disk.MemberCount);
    }

    [Fact]
    public void Diamond_IsTheCityBlockBall()
    {
        StructuringElement diamond = StructuringElement.Diamond(2);
        Assert.Equal(13, diamond.MemberCount);
        Assert.True(diamond.Member(0, 2));
        Assert.False(diamond.Member(0, 1));
    }

    [Fact]
    public void Octagon_CutsTheCornersOffTheSquare()
    {
        StructuringElement octagon = StructuringElement.Octagon(3);
        Assert.Equal(7, octagon.Rows);

        // The cut is |x| + |y| <= 4, so a corner at (3, 3) is out and (3, 1) is in.
        Assert.False(octagon.Member(0, 0));
        Assert.True(octagon.Member(0, 2));
        Assert.True(octagon.Member(3, 0));
    }

    [Fact]
    public void Octagon_RefusesARadiusThatIsNotAMultipleOfThree() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => StructuringElement.Octagon(4));

    [Fact]
    public void Line_AtZeroDegreesIsARow()
    {
        StructuringElement line = StructuringElement.Line(5, 0);
        Assert.Equal(1, line.Rows);
        Assert.Equal(5, line.Cols);
        Assert.Equal(5, line.MemberCount);
    }

    [Fact]
    public void Line_AtNinetyDegreesIsAColumn()
    {
        StructuringElement line = StructuringElement.Line(5, 90);
        Assert.Equal(5, line.Rows);
        Assert.Equal(1, line.Cols);
    }

    [Fact]
    public void Line_AtFortyFiveDegreesRunsCornerToCorner()
    {
        StructuringElement line = StructuringElement.Line(5, 45);
        Assert.Equal(line.Rows, line.Cols);

        // Rows grow downward and the angle is measured upward, so a +45° line goes bottom-left to
        // top-right: the anti-diagonal, not the main one.
        Assert.True(line.Member(line.Rows - 1, 0));
        Assert.True(line.Member(0, line.Cols - 1));
        Assert.False(line.Member(0, 0));
    }

    [Fact]
    public void Sphere_IsThreeDimensional()
    {
        StructuringElement sphere = StructuringElement.Sphere(1);
        Assert.True(sphere.Is3D);
        Assert.Equal(3, sphere.Pages);

        // The six face neighbours and the centre; the twelve edges and eight corners are all past 1.
        Assert.Equal(7, sphere.MemberCount);
    }

    [Fact]
    public void Ball_RisesToItsHeightAtTheCentre()
    {
        StructuringElement ball = StructuringElement.Ball(3, 2);
        Assert.False(ball.IsFlat);
        Assert.Equal(2.0, ball.HeightAt(3, 3), 12);
        Assert.Equal(0.0, ball.HeightAt(3, 0), 12);
        Assert.Equal(2 * Math.Sqrt(1 - (1.0 / 9.0)), ball.HeightAt(3, 2), 12);
    }

    [Fact]
    public void OriginOfAnEvenSizedElement_SitsJustBeforeTheMiddle()
    {
        // MATLAB puts the origin at floor((n + 1) / 2), which for n = 4 is the second entry.
        StructuringElement rectangle = StructuringElement.Rectangle(4, 4);
        Assert.Equal(1, rectangle.OriginRow);
        Assert.Equal(1, rectangle.OriginCol);
    }

    // --- Erosion and dilation ----------------------------------------------------------------

    [Fact]
    public void Dilation_ReflectsTheElement()
    {
        // Dilating a single pixel lays the element down on it, offsets and all: an element whose
        // members sit at 0 and +1 spreads the dot rightward. Reading the neighbourhood without
        // reflecting first would gather from the right instead, and spread it left.
        using var dot = new ImageBuffer(5, 5, 1);
        dot[2, 2, 0] = 1.0;

        StructuringElement rightward = StructuringElement.Arbitrary(new[,] { { 0.0, 1.0, 1.0 } });
        using ImageBuffer dilated = Morphology.Dilate(dot, rightward);

        Assert.Equal(0.0, dilated[2, 1, 0]);
        Assert.Equal(1.0, dilated[2, 2, 0]);
        Assert.Equal(1.0, dilated[2, 3, 0]);
        Assert.Equal(0.0, dilated[2, 4, 0]);

        // The duality carries the reflection with it — A ⊕ B is the complement of Aᶜ ⊖ B̂ — which is
        // exactly why dilation reflects and erosion does not.
        using ImageBuffer complement = PointOps.Complement(dot);
        using ImageBuffer eroded = Morphology.Erode(complement, rightward.Reflect());
        using ImageBuffer dual = PointOps.Complement(eroded);
        Assert.Equal(1.0, dual[2, 3, 0]);
        Assert.Equal(0.0, dual[2, 1, 0]);
    }

    [Fact]
    public void ErodeThenDilate_ByALineIsTheDualOfDilateThenErodeOnTheComplement()
    {
        using ImageBuffer shape = Blobs();
        StructuringElement line = StructuringElement.Line(5, 30);

        using ImageBuffer opened = Morphology.Open(shape, line);
        using ImageBuffer complement = PointOps.Complement(shape);
        using ImageBuffer closedComplement = Morphology.Close(complement, line);
        using ImageBuffer dual = PointOps.Complement(closedComplement);

        for (int r = 0; r < shape.Height; r++)
        {
            for (int c = 0; c < shape.Width; c++)
            {
                Assert.Equal(dual[r, c, 0], opened[r, c, 0], 12);
            }
        }
    }

    [Fact]
    public void TopHat_OfAFlatFieldIsZero()
    {
        using var flat = new ImageBuffer(12, 12, 1);
        flat.Pixels.Fill(0.4);
        using ImageBuffer hat = Morphology.TopHat(flat, StructuringElement.Square(3));
        foreach (double value in hat.Pixels)
        {
            Assert.Equal(0.0, value, 12);
        }
    }

    [Fact]
    public void TopHat_KeepsASpeckTheElementCannotHold()
    {
        using var picture = new ImageBuffer(15, 15, 1);
        picture.Pixels.Fill(0.2);
        picture[7, 7, 0] = 0.9;

        using ImageBuffer hat = Morphology.TopHat(picture, StructuringElement.Square(3));
        Assert.Equal(0.7, hat[7, 7, 0], 12);
        Assert.Equal(0.0, hat[0, 0, 0], 12);
    }

    [Fact]
    public void BottomHat_KeepsADarkPitTheElementCannotHold()
    {
        using var picture = new ImageBuffer(15, 15, 1);
        picture.Pixels.Fill(0.6);
        picture[7, 7, 0] = 0.1;

        using ImageBuffer hat = Morphology.BottomHat(picture, StructuringElement.Square(3));
        Assert.Equal(0.5, hat[7, 7, 0], 12);
    }

    [Fact]
    public void NonFlatDilation_AddsTheElementHeight()
    {
        using var picture = new ImageBuffer(7, 7, 1);
        picture[3, 3, 0] = 0.5;

        StructuringElement ball = StructuringElement.Ball(2, 0.2);
        using ImageBuffer dilated = Morphology.Dilate(picture, ball);

        // The peak keeps the pixel plus the element's own peak; a step out drops by the ellipsoid.
        Assert.Equal(0.7, dilated[3, 3, 0], 12);
        Assert.Equal(0.5 + (0.2 * Math.Sqrt(1 - 0.25)), dilated[3, 4, 0], 12);
    }

    [Fact]
    public void HitMiss_FindsTheUpperLeftCornerOfASquare()
    {
        using var picture = new ImageBuffer(9, 9, 1);
        for (int r = 3; r < 7; r++)
        {
            for (int c = 3; c < 7; c++)
            {
                picture[r, c, 0] = 1.0;
            }
        }

        // Foreground at the pixel and to its right and below; background above and to the left.
        StructuringElement hits = StructuringElement.Arbitrary(new[,]
        {
            { 0.0, 0.0, 0.0 },
            { 0.0, 1.0, 1.0 },
            { 0.0, 1.0, 1.0 },
        });
        StructuringElement misses = StructuringElement.Arbitrary(new[,]
        {
            { 1.0, 1.0, 1.0 },
            { 1.0, 0.0, 0.0 },
            { 1.0, 0.0, 0.0 },
        });

        using ImageBuffer corners = Morphology.HitMiss(picture, hits, misses);
        Assert.Equal(1.0, corners[3, 3, 0]);
        Assert.Equal(0.0, corners[3, 6, 0]);
        Assert.Equal(0.0, corners[5, 5, 0]);
    }

    // --- Reconstruction ----------------------------------------------------------------------

    [Fact]
    public void Reconstruct_KeepsOnlyTheComponentsTheMarkerTouches()
    {
        using ImageBuffer mask = Blobs();
        using var marker = new ImageBuffer(mask.Height, mask.Width, 1);
        marker[3, 3, 0] = 1.0;

        using ImageBuffer kept = MorphologicalReconstruction.Reconstruct(marker, mask, 8);

        // The blob around (3, 3) survives whole; the one at the other end is gone.
        Assert.Equal(1.0, kept[4, 4, 0]);
        Assert.Equal(1.0, kept[2, 2, 0]);
        Assert.Equal(0.0, kept[12, 12, 0]);
        Assert.Equal(1.0, mask[12, 12, 0]);
    }

    [Fact]
    public void Reconstruct_IsIdempotent()
    {
        using ImageBuffer mask = Blobs();
        using var marker = new ImageBuffer(mask.Height, mask.Width, 1);
        marker[3, 3, 0] = 1.0;

        using ImageBuffer once = MorphologicalReconstruction.Reconstruct(marker, mask, 8);
        using ImageBuffer twice = MorphologicalReconstruction.Reconstruct(once, mask, 8);
        for (int i = 0; i < once.Pixels.Length; i++)
        {
            Assert.Equal(once.Pixels[i], twice.Pixels[i], 12);
        }
    }

    [Fact]
    public void Reconstruct_CarriesAGrayLevelAlongAThinPath()
    {
        // A single scan cannot walk a spiral; this is the test that the queue phase runs.
        using var mask = new ImageBuffer(9, 9, 1);
        for (int c = 0; c < 9; c++)
        {
            mask[1, c, 0] = 0.8;
        }

        for (int r = 1; r < 8; r++)
        {
            mask[r, 8, 0] = 0.8;
        }

        for (int c = 0; c < 9; c++)
        {
            mask[7, c, 0] = 0.8;
        }

        using var marker = new ImageBuffer(9, 9, 1);
        marker[7, 0, 0] = 0.8;

        using ImageBuffer grown = MorphologicalReconstruction.Reconstruct(marker, mask, 4);
        Assert.Equal(0.8, grown[1, 0, 0], 12);
    }

    [Fact]
    public void FillHoles_ClosesAnEnclosedBackground()
    {
        using var ring = new ImageBuffer(11, 11, 1);
        for (int r = 2; r < 9; r++)
        {
            for (int c = 2; c < 9; c++)
            {
                bool edge = r == 2 || r == 8 || c == 2 || c == 8;
                ring[r, c, 0] = edge ? 1.0 : 0.0;
            }
        }

        using ImageBuffer filled = MorphologicalReconstruction.FillHoles(ring);
        Assert.Equal(1.0, filled[5, 5, 0]);
        Assert.Equal(0.0, filled[0, 0, 0]);
    }

    [Fact]
    public void FillHoles_OnGrayscaleRaisesABasinToItsRim()
    {
        using var bowl = new ImageBuffer(11, 11, 1);
        bowl.Pixels.Fill(0.2);
        for (int r = 3; r < 8; r++)
        {
            for (int c = 3; c < 8; c++)
            {
                bowl[r, c, 0] = 0.7;
            }
        }

        bowl[5, 5, 0] = 0.3;

        using ImageBuffer filled = MorphologicalReconstruction.FillHoles(bowl);
        Assert.Equal(0.7, filled[5, 5, 0], 12);
        Assert.Equal(0.2, filled[0, 0, 0], 12);
    }

    [Fact]
    public void FillFrom_FillsOnlyTheRegionHoldingTheSeed()
    {
        using var walls = new ImageBuffer(9, 9, 1);
        for (int r = 0; r < 9; r++)
        {
            walls[r, 4, 0] = 1.0;
        }

        using ImageBuffer filled = MorphologicalReconstruction.FillFrom(walls, [(4, 1)]);
        Assert.Equal(1.0, filled[0, 0, 0]);
        Assert.Equal(1.0, filled[8, 3, 0]);
        Assert.Equal(0.0, filled[4, 6, 0]);
    }

    [Fact]
    public void ClearBorder_DropsWhatTouchesTheEdgeAndKeepsWhatDoesNot()
    {
        using var picture = new ImageBuffer(11, 11, 1);
        picture[0, 5, 0] = 1.0;
        picture[1, 5, 0] = 1.0;
        picture[5, 5, 0] = 1.0;
        picture[5, 6, 0] = 1.0;

        using ImageBuffer cleared = MorphologicalReconstruction.ClearBorder(picture, 8);
        Assert.Equal(0.0, cleared[0, 5, 0]);
        Assert.Equal(0.0, cleared[1, 5, 0]);
        Assert.Equal(1.0, cleared[5, 5, 0]);
    }

    [Fact]
    public void HMax_FlattensAShallowPeakAndKeepsATallOne()
    {
        using var picture = new ImageBuffer(11, 11, 1);
        picture.Pixels.Fill(0.2);
        picture[3, 3, 0] = 0.25;
        picture[7, 7, 0] = 0.6;

        using ImageBuffer suppressed = MorphologicalReconstruction.HMax(picture, 0.1);
        Assert.Equal(0.2, suppressed[3, 3, 0], 12);
        Assert.Equal(0.5, suppressed[7, 7, 0], 12);
    }

    [Fact]
    public void RegionalMax_FindsThePlateauAndNothingBeside()
    {
        using var picture = new ImageBuffer(9, 9, 1);
        picture.Pixels.Fill(0.1);
        picture[4, 4, 0] = 0.8;
        picture[4, 5, 0] = 0.8;
        picture[3, 4, 0] = 0.5;

        using ImageBuffer peaks = MorphologicalReconstruction.RegionalMax(picture);
        Assert.Equal(1.0, peaks[4, 4, 0]);
        Assert.Equal(1.0, peaks[4, 5, 0]);
        Assert.Equal(0.0, peaks[3, 4, 0]);
        Assert.Equal(0.0, peaks[0, 0, 0]);
    }

    [Fact]
    public void RegionalMax_OfAFlatFieldIsEverywhere()
    {
        using var flat = new ImageBuffer(6, 6, 1);
        flat.Pixels.Fill(0.3);
        using ImageBuffer peaks = MorphologicalReconstruction.RegionalMax(flat);
        foreach (double value in peaks.Pixels)
        {
            Assert.Equal(1.0, value);
        }
    }

    [Fact]
    public void ExtendedMax_KeepsOnlyPeaksThatRiseByH()
    {
        using var picture = new ImageBuffer(13, 13, 1);
        picture.Pixels.Fill(0.2);
        picture[3, 3, 0] = 0.25;
        picture[9, 9, 0] = 0.7;

        using ImageBuffer significant = MorphologicalReconstruction.ExtendedMax(picture, 0.2);
        Assert.Equal(0.0, significant[3, 3, 0]);
        Assert.Equal(1.0, significant[9, 9, 0]);
    }

    [Fact]
    public void ImposeMin_PutsTheMinimaWhereTheMarkerIsAndNowhereElse()
    {
        using var picture = new ImageBuffer(13, 13, 1);
        picture.Pixels.Fill(0.5);
        picture[3, 3, 0] = 0.1;
        picture[9, 9, 0] = 0.2;

        using var marker = new ImageBuffer(13, 13, 1);
        marker[6, 6, 0] = 1.0;

        using ImageBuffer imposed = MorphologicalReconstruction.ImposeMin(picture, marker);
        using ImageBuffer minima = MorphologicalReconstruction.RegionalMin(imposed);

        Assert.Equal(1.0, minima[6, 6, 0]);
        Assert.Equal(0.0, minima[3, 3, 0]);
        Assert.Equal(0.0, minima[9, 9, 0]);
        Assert.Equal(0.0, imposed[6, 6, 0], 12);
    }

    // --- The neighbourhood table -------------------------------------------------------------

    [Fact]
    public void MakeLut_UsesMatlabsColumnMajorWeighting()
    {
        // Entry 2 is bit 1 set, which is row 1 of column 0 — the pixel above the centre in the
        // window as MATLAB numbers it, and the one to the left of centre once the window is read.
        double[] table = BinaryMorphology.MakeLut(w => w[1, 0] ? 1.0 : 0.0, 3);
        Assert.Equal(512, table.Length);
        Assert.Equal(1.0, table[2]);
        Assert.Equal(0.0, table[1]);
        Assert.Equal(1.0, table[3]);
    }

    [Fact]
    public void ApplyLut_WithAnAlwaysTrueTableFillsTheImage()
    {
        double[] table = BinaryMorphology.MakeLut(_ => 1.0, 3);
        using var picture = new ImageBuffer(5, 5, 1);
        using ImageBuffer result = BinaryMorphology.ApplyLut(picture, table);
        foreach (double value in result.Pixels)
        {
            Assert.Equal(1.0, value);
        }
    }

    [Fact]
    public void ApplyLut_ReproducesDilationByTheSquare()
    {
        // "Any neighbour set" is exactly what a 3×3 dilation asks, so the table and the morphology
        // have to agree — which is the check that the index and the window line up.
        double[] table = BinaryMorphology.MakeLut(
            w =>
            {
                foreach (bool cell in w)
                {
                    if (cell)
                    {
                        return 1.0;
                    }
                }

                return 0.0;
            },
            3);

        using ImageBuffer picture = Blobs();
        using ImageBuffer viaTable = BinaryMorphology.ApplyLut(picture, table);
        using ImageBuffer viaMorphology = Morphology.Dilate(picture, StructuringElement.Square(3));
        for (int i = 0; i < viaTable.Pixels.Length; i++)
        {
            Assert.Equal(viaMorphology.Pixels[i], viaTable.Pixels[i]);
        }
    }

    [Fact]
    public void Perimeter_OfASolidSquareIsItsOutline()
    {
        using var square = new ImageBuffer(9, 9, 1);
        for (int r = 2; r < 7; r++)
        {
            for (int c = 2; c < 7; c++)
            {
                square[r, c, 0] = 1.0;
            }
        }

        using ImageBuffer edge = BinaryMorphology.Perimeter(square);
        Assert.Equal(1.0, edge[2, 2, 0]);
        Assert.Equal(1.0, edge[2, 4, 0]);
        Assert.Equal(0.0, edge[4, 4, 0]);
    }

    // --- bwmorph ------------------------------------------------------------------------------

    [Fact]
    public void Clean_RemovesAnIsolatedPixelAndSparesAPair()
    {
        using var picture = new ImageBuffer(9, 9, 1);
        picture[2, 2, 0] = 1.0;
        picture[6, 6, 0] = 1.0;
        picture[6, 7, 0] = 1.0;

        using ImageBuffer cleaned = BinaryMorphology.Morph(picture, "clean");
        Assert.Equal(0.0, cleaned[2, 2, 0]);
        Assert.Equal(1.0, cleaned[6, 6, 0]);
    }

    [Fact]
    public void Fill_ClosesASinglePixelHole()
    {
        using var picture = new ImageBuffer(7, 7, 1);
        for (int r = 2; r < 5; r++)
        {
            for (int c = 2; c < 5; c++)
            {
                picture[r, c, 0] = 1.0;
            }
        }

        picture[3, 3, 0] = 0.0;

        using ImageBuffer filled = BinaryMorphology.Morph(picture, "fill");
        Assert.Equal(1.0, filled[3, 3, 0]);
    }

    [Fact]
    public void Remove_LeavesTheOutlineOfASolidBlock()
    {
        using var picture = new ImageBuffer(9, 9, 1);
        for (int r = 2; r < 7; r++)
        {
            for (int c = 2; c < 7; c++)
            {
                picture[r, c, 0] = 1.0;
            }
        }

        using ImageBuffer outline = BinaryMorphology.Morph(picture, "remove");
        Assert.Equal(0.0, outline[4, 4, 0]);
        Assert.Equal(1.0, outline[2, 4, 0]);
    }

    [Fact]
    public void Majority_FillsAPixelSurroundedByForeground()
    {
        using var picture = new ImageBuffer(7, 7, 1);
        for (int r = 2; r < 5; r++)
        {
            for (int c = 2; c < 5; c++)
            {
                picture[r, c, 0] = 1.0;
            }
        }

        picture[3, 3, 0] = 0.0;

        using ImageBuffer voted = BinaryMorphology.Morph(picture, "majority");
        Assert.Equal(1.0, voted[3, 3, 0]);
        Assert.Equal(0.0, voted[0, 0, 0]);
    }

    [Fact]
    public void Spur_TakesOffTheFreeEndOfALine()
    {
        using var line = new ImageBuffer(9, 9, 1);
        for (int c = 2; c < 7; c++)
        {
            line[4, c, 0] = 1.0;
        }

        using ImageBuffer trimmed = BinaryMorphology.Morph(line, "spur");
        Assert.Equal(0.0, trimmed[4, 2, 0]);
        Assert.Equal(0.0, trimmed[4, 6, 0]);
        Assert.Equal(1.0, trimmed[4, 4, 0]);
    }

    [Fact]
    public void Endpoints_KeepsOnlyTheTwoEndsOfALine()
    {
        using var line = new ImageBuffer(9, 9, 1);
        for (int c = 2; c < 7; c++)
        {
            line[4, c, 0] = 1.0;
        }

        using ImageBuffer ends = BinaryMorphology.Morph(line, "endpoints");
        Assert.Equal(1.0, ends[4, 2, 0]);
        Assert.Equal(1.0, ends[4, 6, 0]);
        Assert.Equal(0.0, ends[4, 4, 0]);
    }

    [Fact]
    public void Branchpoints_FindsTheJunctionOfAThreeArmedShape()
    {
        using var shape = new ImageBuffer(11, 11, 1);
        for (int c = 2; c < 9; c++)
        {
            shape[5, c, 0] = 1.0;
        }

        for (int r = 2; r < 5; r++)
        {
            shape[r, 5, 0] = 1.0;
        }

        using ImageBuffer junctions = BinaryMorphology.Morph(shape, "branchpoints");
        Assert.Equal(1.0, junctions[5, 5, 0]);
        Assert.Equal(0.0, junctions[5, 2, 0]);
    }

    [Fact]
    public void Skel_ReducesAThickBarToASingleStroke()
    {
        using var bar = new ImageBuffer(15, 21, 1);
        for (int r = 6; r < 11; r++)
        {
            for (int c = 3; c < 18; c++)
            {
                bar[r, c, 0] = 1.0;
            }
        }

        using ImageBuffer skeleton = BinaryMorphology.Morph(bar, "skel", int.MaxValue);

        // Every column the bar covered still has something in it, and no column has five.
        for (int c = 6; c < 15; c++)
        {
            int set = 0;
            for (int r = 0; r < 15; r++)
            {
                if (skeleton[r, c, 0] != 0)
                {
                    set++;
                }
            }

            Assert.InRange(set, 1, 2);
        }
    }

    [Fact]
    public void Thin_PreservesConnectivityOfARing()
    {
        using var ring = new ImageBuffer(17, 17, 1);
        for (int r = 0; r < 17; r++)
        {
            for (int c = 0; c < 17; c++)
            {
                double dr = r - 8;
                double dc = c - 8;
                double distance = Math.Sqrt((dr * dr) + (dc * dc));
                ring[r, c, 0] = distance is >= 4 and <= 6.5 ? 1.0 : 0.0;
            }
        }

        using ImageBuffer thinned = BinaryMorphology.Morph(ring, "thin", int.MaxValue);

        // The hole must survive: a ring thinned into a disc would mean the rule broke topology.
        Assert.Equal(0.0, thinned[8, 8, 0]);
        int count = 0;
        foreach (double value in thinned.Pixels)
        {
            if (value != 0)
            {
                count++;
            }
        }

        Assert.InRange(count, 20, 50);
    }

    [Fact]
    public void UltimateErode_MarksTheCentreOfADisc()
    {
        using var disc = new ImageBuffer(21, 21, 1);
        for (int r = 0; r < 21; r++)
        {
            for (int c = 0; c < 21; c++)
            {
                double dr = r - 10;
                double dc = c - 10;
                disc[r, c, 0] = (dr * dr) + (dc * dc) <= 49 ? 1.0 : 0.0;
            }
        }

        using ImageBuffer seeds = BinaryMorphology.UltimateErode(disc);
        Assert.Equal(1.0, seeds[10, 10, 0]);
        Assert.Equal(0.0, seeds[10, 15, 0]);
    }

    [Fact]
    public void Skeleton_PrunesShortBranches()
    {
        using var shape = new ImageBuffer(15, 25, 1);
        for (int c = 3; c < 22; c++)
        {
            shape[7, c, 0] = 1.0;
        }

        // A two-pixel spur hanging off the middle.
        shape[6, 12, 0] = 1.0;
        shape[5, 12, 0] = 1.0;

        using ImageBuffer pruned = BinaryMorphology.Skeleton(shape, 4);
        Assert.Equal(0.0, pruned[5, 12, 0]);
        Assert.Equal(1.0, pruned[7, 12, 0]);
    }

    [Fact]
    public void IsConnectivity_AcceptsTheNamedValuesAndRefusesTheRest()
    {
        Assert.True(BinaryMorphology.IsConnectivity(new[,] { { 4.0 } }));
        Assert.True(BinaryMorphology.IsConnectivity(new[,] { { 26.0 } }));
        Assert.False(BinaryMorphology.IsConnectivity(new[,] { { 5.0 } }));
        Assert.True(BinaryMorphology.IsConnectivity(BinaryMorphology.ConnectivityDefinition(true)));
        Assert.False(BinaryMorphology.IsConnectivity(new[,] { { 1.0, 1.0 }, { 1.0, 1.0 } }));
    }

    // --- Distance ------------------------------------------------------------------------------

    [Fact]
    public void Bwdist_FromOneSeedIsTheHypotenuseGrid()
    {
        using var picture = new ImageBuffer(21, 21, 1);
        picture[10, 10, 0] = 1.0;

        (double[] distance, int[] nearest) = DistanceTransforms.Transform(picture);
        for (int r = 0; r < 21; r++)
        {
            for (int c = 0; c < 21; c++)
            {
                double expected = Math.Sqrt(((r - 10.0) * (r - 10.0)) + ((c - 10.0) * (c - 10.0)));
                Assert.Equal(expected, distance[(r * 21) + c], 10);
                Assert.Equal((10 * 21) + 10, nearest[(r * 21) + c]);
            }
        }
    }

    [Fact]
    public void Bwdist_WithTwoSeedsGivesTheNearerOne()
    {
        using var picture = new ImageBuffer(1, 11, 1);
        picture[0, 0, 0] = 1.0;
        picture[0, 10, 0] = 1.0;

        (double[] distance, int[] nearest) = DistanceTransforms.Transform(picture);
        Assert.Equal(3.0, distance[3], 12);
        Assert.Equal(0, nearest[3]);
        Assert.Equal(2.0, distance[8], 12);
        Assert.Equal(10, nearest[8]);
    }

    [Fact]
    public void Bwdist_OfAnEmptyImageIsInfinite()
    {
        using var picture = new ImageBuffer(5, 5, 1);
        (double[] distance, int[] nearest) = DistanceTransforms.Transform(picture);
        Assert.All(distance, value => Assert.True(double.IsPositiveInfinity(value)));
        Assert.All(nearest, index => Assert.Equal(-1, index));
    }

    [Theory]
    [InlineData(DistanceTransforms.Metric.CityBlock, 7.0)]
    [InlineData(DistanceTransforms.Metric.Chessboard, 4.0)]
    public void Bwdist_ChamferMetricsMeasureTheirOwnWay(DistanceTransforms.Metric metric, double expected)
    {
        using var picture = new ImageBuffer(11, 11, 1);
        picture[5, 5, 0] = 1.0;

        (double[] distance, _) = DistanceTransforms.Transform(picture, metric);

        // Three across and four down: seven steps along the axes, four king moves.
        Assert.Equal(expected, distance[(9 * 11) + 8], 10);
    }

    [Fact]
    public void QuasiEuclidean_IsCloseToButNotTheExactDistance()
    {
        using var picture = new ImageBuffer(11, 11, 1);
        picture[5, 5, 0] = 1.0;

        (double[] approximate, _) = DistanceTransforms.Transform(
            picture, DistanceTransforms.Metric.QuasiEuclidean);
        (double[] exact, _) = DistanceTransforms.Transform(picture);

        int index = (9 * 11) + 8;
        Assert.Equal((3 * Math.Sqrt(2)) + 1, approximate[index], 10);
        Assert.True(approximate[index] > exact[index]);
    }

    [Fact]
    public void Geodesic_GoesRoundTheWallRatherThanThroughIt()
    {
        using var corridor = new ImageBuffer(9, 9, 1);
        corridor.Pixels.Fill(1.0);
        for (int r = 0; r < 8; r++)
        {
            corridor[r, 4, 0] = 0.0;
        }

        double[] distance = DistanceTransforms.Geodesic(
            corridor, [(0, 0)], DistanceTransforms.Metric.CityBlock);

        // Straight across would be six; the only way round is eight rows down, six across, eight
        // back up.
        Assert.Equal(22.0, distance[(0 * 9) + 6], 10);
        Assert.True(double.IsPositiveInfinity(distance[(3 * 9) + 4]));
    }

    [Fact]
    public void GrayDist_PrefersTheDarkValley()
    {
        using var picture = new ImageBuffer(5, 9, 1);
        picture.Pixels.Fill(1.0);
        for (int c = 0; c < 9; c++)
        {
            picture[3, c, 0] = 0.0;
        }

        double[] distance = DistanceTransforms.GrayWeighted(
            picture, [(3, 0)], DistanceTransforms.Metric.CityBlock);

        // Along the valley every step joins two zeros, so the whole run is free.
        Assert.Equal(0.0, distance[(3 * 9) + 8], 12);
        Assert.True(distance[(0 * 9) + 8] > 0.5);
    }

    /// <summary>Two square blobs, one at each end of a 16×16 picture.</summary>
    private static ImageBuffer Blobs()
    {
        var picture = new ImageBuffer(16, 16, 1);
        for (int r = 2; r < 6; r++)
        {
            for (int c = 2; c < 6; c++)
            {
                picture[r, c, 0] = 1.0;
            }
        }

        for (int r = 11; r < 14; r++)
        {
            for (int c = 11; c < 14; c++)
            {
                picture[r, c, 0] = 1.0;
            }
        }

        return picture;
    }
}
