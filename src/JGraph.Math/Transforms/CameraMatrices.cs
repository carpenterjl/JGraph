using JGraph.Core.Primitives;

namespace JGraph.Maths.Transforms;

/// <summary>
/// The 4-by-4 homogeneous matrices MATLAB's <c>viewmtx</c> and <c>makehgtform</c> hand back. Pure
/// arithmetic with no camera and no axes behind it: these two functions answer with a matrix a
/// script does its own geometry with, so they are the same numbers whatever this build draws.
/// </summary>
/// <remarks>
/// Row-major, and the same convention MATLAB documents: a point is the column <c>[x y z 1]'</c> and
/// a transform is applied by multiplying it from the left, so composing several is a product read
/// right to left. <see cref="Projection3D"/> is the renderer's camera and shares the rotation rows
/// with <see cref="ViewMatrix(double, double)"/> — deliberately, since a script that projects points
/// by hand with <c>viewmtx</c> should land where the figure drew them.
/// </remarks>
public static class CameraMatrices
{
    /// <summary>The 4-by-4 identity — <c>makehgtform</c> with nothing to do.</summary>
    public static double[,] Identity()
    {
        var m = new double[4, 4];
        for (int i = 0; i < 4; i++)
        {
            m[i, i] = 1;
        }

        return m;
    }

    /// <summary>
    /// MATLAB's orthographic <c>viewmtx(az, el)</c>: the rotation that takes data coordinates to
    /// screen-right, screen-up, and depth toward the viewer, for a camera at that azimuth and
    /// elevation in degrees.
    /// </summary>
    public static double[,] ViewMatrix(double azimuthDegrees, double elevationDegrees)
    {
        double az = azimuthDegrees * System.Math.PI / 180.0;
        double el = elevationDegrees * System.Math.PI / 180.0;
        double sa = System.Math.Sin(az), ca = System.Math.Cos(az);
        double se = System.Math.Sin(el), ce = System.Math.Cos(el);

        var m = new double[4, 4];
        m[0, 0] = ca;
        m[0, 1] = sa;
        m[1, 0] = -se * sa;
        m[1, 1] = se * ca;
        m[1, 2] = ce;
        m[2, 0] = ce * sa;
        m[2, 1] = -ce * ca;
        m[2, 2] = se;
        m[3, 3] = 1;
        return m;
    }

    /// <summary>
    /// MATLAB's perspective <c>viewmtx(az, el, phi, target)</c>, built the way the documentation
    /// describes it: the orthographic rotation about the target, then a division by depth for a
    /// viewpoint <c>1 / tan(phi / 2)</c> away. A <paramref name="phiDegrees"/> of zero is the
    /// orthographic matrix exactly, so the two forms meet.
    /// </summary>
    public static double[,] ViewMatrix(double azimuthDegrees, double elevationDegrees, double phiDegrees, Vector3D target)
    {
        double[,] rotation = ViewMatrix(azimuthDegrees, elevationDegrees);
        double[,] about = Multiply(rotation, Translate(-target.X, -target.Y, -target.Z));
        if (phiDegrees == 0)
        {
            return about;
        }

        // The viewpoint's distance from the target, from the angle the whole box is meant to
        // subtend: a narrow angle is a far camera and barely converges, a wide one is close and
        // foreshortens hard.
        double distance = 1 / System.Math.Tan(phiDegrees * System.Math.PI / 360.0);
        double[,] perspective = Identity();
        perspective[3, 2] = -1 / distance;
        return Multiply(perspective, about);
    }

    /// <summary>A pure translation.</summary>
    public static double[,] Translate(double x, double y, double z)
    {
        double[,] m = Identity();
        m[0, 3] = x;
        m[1, 3] = y;
        m[2, 3] = z;
        return m;
    }

    /// <summary>A per-axis scaling.</summary>
    public static double[,] Scale(double x, double y, double z)
    {
        var m = new double[4, 4];
        m[0, 0] = x;
        m[1, 1] = y;
        m[2, 2] = z;
        m[3, 3] = 1;
        return m;
    }

    /// <summary>
    /// A right-handed rotation of <paramref name="radians"/> about an arbitrary axis through the
    /// origin — Rodrigues' formula, which the three coordinate-axis rotations are special cases of
    /// and so are built from rather than written out again.
    /// </summary>
    public static double[,] RotateAbout(Vector3D axis, double radians)
    {
        double length = System.Math.Sqrt((axis.X * axis.X) + (axis.Y * axis.Y) + (axis.Z * axis.Z));
        if (length < 1e-300)
        {
            return Identity();
        }

        double x = axis.X / length, y = axis.Y / length, z = axis.Z / length;
        double c = System.Math.Cos(radians), s = System.Math.Sin(radians), t = 1 - c;

        double[,] m = Identity();
        m[0, 0] = (t * x * x) + c;
        m[0, 1] = (t * x * y) - (s * z);
        m[0, 2] = (t * x * z) + (s * y);
        m[1, 0] = (t * x * y) + (s * z);
        m[1, 1] = (t * y * y) + c;
        m[1, 2] = (t * y * z) - (s * x);
        m[2, 0] = (t * x * z) - (s * y);
        m[2, 1] = (t * y * z) + (s * x);
        m[2, 2] = (t * z * z) + c;
        return m;
    }

    /// <summary>The 4-by-4 product <c>a * b</c>.</summary>
    public static double[,] Multiply(double[,] a, double[,] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var m = new double[4, 4];
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                double sum = 0;
                for (int k = 0; k < 4; k++)
                {
                    sum += a[r, k] * b[k, c];
                }

                m[r, c] = sum;
            }
        }

        return m;
    }
}
