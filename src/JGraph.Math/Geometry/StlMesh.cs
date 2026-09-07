using System.Globalization;
using System.Text;

namespace JGraph.Maths.Geometry;

/// <summary>A triangle mesh read from, or about to be written to, an STL file.</summary>
/// <param name="Points">The vertices, one per row, three coordinates across.</param>
/// <param name="Faces">The triangles, as zero-based indices into <paramref name="Points"/>.</param>
/// <param name="Normals">One outward normal per triangle, as the file recorded or computed it.</param>
/// <param name="Attributes">The two-byte attribute a binary file carries per triangle.</param>
/// <param name="SolidIndex">Which named solid of a text file each triangle came from, counting from one.</param>
/// <param name="Format">Whether the file was <c>"binary"</c> or <c>"text"</c>.</param>
public sealed record StlMesh(
    double[,] Points,
    int[,] Faces,
    double[,] Normals,
    ushort[] Attributes,
    double[] SolidIndex,
    string Format);

/// <summary>
/// Reading and writing STL, in both the format's shapes: the 80-byte-header binary one and the
/// <c>solid … facet normal … endsolid</c> text one.
/// </summary>
/// <remarks>
/// <para>
/// STL stores three loose corners per triangle and no vertex numbering at all, so reading one means
/// welding: coordinate triples that are bit-for-bit equal become one vertex, in the order they were
/// first seen. That is what turns a file's 36 loose corners into the 8 vertices of a cube, and it is
/// why a mesh written and read back has the vertex numbering the file implies rather than the one it
/// was written from.
/// </para>
/// <para>
/// A triangle is wound to agree with the normal the file recorded, which is what <c>stlread</c>
/// documents: where the stored normal points against the winding, the second and third corners are
/// swapped.
/// </para>
/// </remarks>
public static class StlIo
{
    /// <summary>Reads an STL file, working out for itself which of the two formats it is in.</summary>
    public static StlMesh Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0)
        {
            throw new InvalidDataException("The file is empty.");
        }

        return LooksBinary(bytes) ? ReadBinary(bytes) : ReadText(bytes);
    }

    /// <summary>Writes an STL file in the binary or the text shape.</summary>
    public static void Write(
        string path, double[,] points, int[,] faces, bool binary,
        ushort[]? attributes, double[]? solidIndex)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(faces);
        int count = faces.GetLength(0);
        var normals = new double[count, 3];
        for (int t = 0; t < count; t++)
        {
            (double nx, double ny, double nz) = FaceNormal(points, faces, t);
            normals[t, 0] = nx;
            normals[t, 1] = ny;
            normals[t, 2] = nz;
        }

        if (binary)
        {
            WriteBinary(path, points, faces, normals, attributes);
            return;
        }

        WriteText(path, points, faces, normals, solidIndex);
    }

    /// <summary>The unit normal of one triangle, wound the way its corners are given.</summary>
    public static (double X, double Y, double Z) FaceNormal(double[,] points, int[,] faces, int t)
    {
        double ax = points[faces[t, 0], 0], ay = points[faces[t, 0], 1], az = Depth(points, faces[t, 0]);
        double bx = points[faces[t, 1], 0] - ax, by = points[faces[t, 1], 1] - ay, bz = Depth(points, faces[t, 1]) - az;
        double cx = points[faces[t, 2], 0] - ax, cy = points[faces[t, 2], 1] - ay, cz = Depth(points, faces[t, 2]) - az;
        double nx = (by * cz) - (bz * cy);
        double ny = (bz * cx) - (bx * cz);
        double nz = (bx * cy) - (by * cx);
        double length = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        return length == 0 ? (0, 0, 0) : (nx / length, ny / length, nz / length);
    }

    private static double Depth(double[,] points, int row) =>
        points.GetLength(1) >= 3 ? points[row, 2] : 0;

    /// <summary>
    /// Whether the bytes are the binary shape. A text file starts with the word <c>solid</c>, but so
    /// do some binary ones, so the deciding test is the length the triangle count implies: a binary
    /// file is exactly 84 + 50·n bytes long.
    /// </summary>
    private static bool LooksBinary(byte[] bytes)
    {
        if (bytes.Length < 84)
        {
            return false;
        }

        uint count = BitConverter.ToUInt32(bytes, 80);
        return bytes.Length == 84L + (50L * count);
    }

    private static StlMesh ReadBinary(byte[] bytes)
    {
        uint count = BitConverter.ToUInt32(bytes, 80);
        var welder = new VertexWelder();
        var faces = new int[count, 3];
        var normals = new double[count, 3];
        var attributes = new ushort[count];
        var solids = new double[count];
        int at = 84;
        for (int t = 0; t < count; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                normals[t, k] = BitConverter.ToSingle(bytes, at + (4 * k));
            }

            for (int v = 0; v < 3; v++)
            {
                int off = at + 12 + (12 * v);
                faces[t, v] = welder.Weld(
                    BitConverter.ToSingle(bytes, off),
                    BitConverter.ToSingle(bytes, off + 4),
                    BitConverter.ToSingle(bytes, off + 8));
            }

            attributes[t] = BitConverter.ToUInt16(bytes, at + 48);
            solids[t] = 1;
            at += 50;
        }

        return Finish(welder, faces, normals, attributes, solids, "binary");
    }

    private static StlMesh ReadText(byte[] bytes)
    {
        string text = Encoding.UTF8.GetString(bytes);
        var welder = new VertexWelder();
        var faces = new List<int[]>();
        var normals = new List<double[]>();
        var solids = new List<double>();
        double[] normal = [0, 0, 0];
        var corners = new List<int>();
        int solid = 0;

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (words[0].ToLowerInvariant())
            {
                case "solid":
                    solid++;
                    break;

                case "facet":
                    normal = words.Length >= 5
                        ? [Number(words[^3]), Number(words[^2]), Number(words[^1])]
                        : [0, 0, 0];
                    corners.Clear();
                    break;

                case "vertex":
                    corners.Add(welder.Weld(Number(words[1]), Number(words[2]), Number(words[3])));
                    break;

                case "endfacet":
                    if (corners.Count == 3)
                    {
                        faces.Add([.. corners]);
                        normals.Add(normal);
                        solids.Add(Math.Max(solid, 1));
                    }

                    break;
            }
        }

        var faceArray = new int[faces.Count, 3];
        var normalArray = new double[faces.Count, 3];
        for (int t = 0; t < faces.Count; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                faceArray[t, k] = faces[t][k];
                normalArray[t, k] = normals[t][k];
            }
        }

        return Finish(welder, faceArray, normalArray, [], [.. solids], "text");
    }

    private static double Number(string word) =>
        double.Parse(word, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static StlMesh Finish(
        VertexWelder welder, int[,] faces, double[,] normals,
        ushort[] attributes, double[] solids, string format)
    {
        double[,] points = welder.Points();
        int count = faces.GetLength(0);
        if (points.GetLength(0) < 3)
        {
            throw new InvalidDataException("The file holds fewer than three distinct vertices.");
        }

        for (int t = 0; t < count; t++)
        {
            (double nx, double ny, double nz) = FaceNormal(points, faces, t);
            double along = (nx * normals[t, 0]) + (ny * normals[t, 1]) + (nz * normals[t, 2]);
            if (along < -0.1)
            {
                (faces[t, 1], faces[t, 2]) = (faces[t, 2], faces[t, 1]);
            }
        }

        return new StlMesh(points, faces, normals, attributes, solids, format);
    }

    private static void WriteBinary(
        string path, double[,] points, int[,] faces, double[,] normals, ushort[]? attributes)
    {
        int count = faces.GetLength(0);
        var bytes = new byte[84 + (50 * count)];
        BitConverter.GetBytes((uint)count).CopyTo(bytes, 80);
        int at = 84;
        for (int t = 0; t < count; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                BitConverter.GetBytes((float)normals[t, k]).CopyTo(bytes, at + (4 * k));
            }

            for (int v = 0; v < 3; v++)
            {
                int row = faces[t, v];
                int off = at + 12 + (12 * v);
                BitConverter.GetBytes((float)points[row, 0]).CopyTo(bytes, off);
                BitConverter.GetBytes((float)points[row, 1]).CopyTo(bytes, off + 4);
                BitConverter.GetBytes((float)Depth(points, row)).CopyTo(bytes, off + 8);
            }

            BitConverter.GetBytes(attributes is not null && t < attributes.Length ? attributes[t] : (ushort)0)
                .CopyTo(bytes, at + 48);
            at += 50;
        }

        File.WriteAllBytes(path, bytes);
    }

    private static void WriteText(
        string path, double[,] points, int[,] faces, double[,] normals, double[]? solidIndex)
    {
        var text = new StringBuilder();
        int count = faces.GetLength(0);
        double current = double.NaN;
        for (int t = 0; t < count; t++)
        {
            double solid = solidIndex is not null && t < solidIndex.Length ? solidIndex[t] : 1;
            if (solid != current)
            {
                if (!double.IsNaN(current))
                {
                    text.Append("endsolid\n");
                }

                text.Append("solid\n");
                current = solid;
            }

            text.Append(CultureInfo.InvariantCulture, $"facet normal {G(normals[t, 0])} {G(normals[t, 1])} {G(normals[t, 2])}\n");
            text.Append("  outer loop\n");
            for (int v = 0; v < 3; v++)
            {
                int row = faces[t, v];
                text.Append(CultureInfo.InvariantCulture,
                    $"    vertex {G(points[row, 0])} {G(points[row, 1])} {G(Depth(points, row))}\n");
            }

            text.Append("  endloop\n");
            text.Append("endfacet\n");
        }

        if (!double.IsNaN(current))
        {
            text.Append("endsolid\n");
        }

        File.WriteAllText(path, text.ToString());
    }

    /// <summary>
    /// A coordinate written the way the format's readers expect: single precision, because that is
    /// all the binary shape can hold and a text file that says more than it can round-trip is
    /// misleading.
    /// </summary>
    private static string G(double value) =>
        ((float)value).ToString("G9", CultureInfo.InvariantCulture);

    /// <summary>Turns loose corners into numbered vertices, keeping the order they first appear in.</summary>
    private sealed class VertexWelder
    {
        private readonly Dictionary<(double, double, double), int> _seen = [];
        private readonly List<(double X, double Y, double Z)> _points = [];

        public int Weld(double x, double y, double z)
        {
            var key = (x, y, z);
            if (_seen.TryGetValue(key, out int already))
            {
                return already;
            }

            _seen[key] = _points.Count;
            _points.Add((x, y, z));
            return _points.Count - 1;
        }

        public double[,] Points()
        {
            var array = new double[_points.Count, 3];
            for (int i = 0; i < _points.Count; i++)
            {
                array[i, 0] = _points[i].X;
                array[i, 1] = _points[i].Y;
                array[i, 2] = _points[i].Z;
            }

            return array;
        }
    }
}
