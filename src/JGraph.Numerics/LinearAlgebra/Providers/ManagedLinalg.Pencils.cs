using System.Numerics;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The pencil and Schur half of the managed backend (M91). These wrap the hand-rolled kernels that
/// have always answered — the Francis iteration, the QZ iteration, the block-exchange reorder —
/// behind the provider contract, marshalling between the contract's flat column-major spans and
/// the kernels' rectangles. The marshalling is O(n²) against the kernels' O(n³); the native path
/// is where the arithmetic itself gets cheaper.
/// </summary>
public sealed partial class ManagedLinalg
{
    /// <inheritdoc />
    public override int Ggev(bool vectors, int n, Span<double> a, int lda, Span<double> b, int ldb,
        Span<double> alphar, Span<double> alphai, Span<double> beta, Span<double> vr, int ldvr)
    {
        if (n == 0)
        {
            return 0;
        }

        if (!vectors)
        {
            // Values through the QZ iteration, which is the only managed route that can answer an
            // eigenvalue at infinity — a singular B never reaches a division there.
            GeneralizedSchur? qz = TryFactorPencil(Rect(a, lda, n), Rect(b, ldb, n));
            if (qz is null)
            {
                return 1;
            }

            for (int i = 0; i < n; i++)
            {
                alphar[i] = qz.Alpha[i].Real;
                alphai[i] = qz.Alpha[i].Imaginary;
                beta[i] = qz.Beta[i];
            }

            return 0;
        }

        // Vectors through B⁻¹·A, the route the front has always taken for a finite pencil: the
        // reduced matrix's eigenvectors are the pencil's exactly, and the general eigensolver
        // already knows how to pack them.
        var factor = new double[(long)n * n];
        for (int c = 0; c < n; c++)
        {
            b.Slice(c * ldb, n).CopyTo(factor.AsSpan(c * n, n));
        }

        var pivots = new int[n];
        if (Getrf(n, n, factor, n, pivots) != 0)
        {
            return 1;
        }

        var reduced = new double[(long)n * n];
        for (int c = 0; c < n; c++)
        {
            a.Slice(c * lda, n).CopyTo(reduced.AsSpan(c * n, n));
        }

        Getrs(transpose: false, n, n, factor, n, pivots, reduced, n);
        int info = Geev(vectors: true, n, reduced, n, alphar, alphai, vr, ldvr);
        if (info != 0)
        {
            return info;
        }

        beta[..n].Fill(1.0);
        NormalizePackedVectors(n, alphai, vr, ldvr);
        return 0;
    }

    /// <inheritdoc />
    public override int Sygvd(bool vectors, bool lower, int n, Span<double> a, int lda,
        Span<double> b, int ldb, Span<double> w)
    {
        if (n == 0)
        {
            return 0;
        }

        int definite = Potrf(lower, n, b, ldb);
        if (definite != 0)
        {
            return n + definite;
        }

        // Mirror A to a full symmetric copy first, so the two triangular solves can treat it as a
        // plain square matrix whichever triangle the caller stored.
        var c = new double[(long)n * n];
        for (int col = 0; col < n; col++)
        {
            for (int row = 0; row < n; row++)
            {
                bool stored = lower ? row >= col : row <= col;
                c[(col * n) + row] = stored ? a[(col * lda) + row] : a[(row * lda) + col];
            }
        }

        // C = L⁻¹·A·L⁻ᵀ (or R⁻ᵀ·A·R⁻¹): one triangular solve, a transpose, and the same solve again.
        _ = Trtrs(lower, transpose: !lower, n, n, b, ldb, c, n);
        TransposeInPlace(c, n);
        _ = Trtrs(lower, transpose: !lower, n, n, b, ldb, c, n);

        // Symmetry is exact in theory and to rounding in practice; averaging keeps the symmetric
        // eigensolver's contract honest against the dust.
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double mean = (c[(j * n) + i] + c[(i * n) + j]) / 2;
                c[(j * n) + i] = mean;
                c[(i * n) + j] = mean;
            }
        }

        int info = Syevd(vectors, lower: true, n, c, n, w);
        if (info != 0)
        {
            return info;
        }

        if (vectors)
        {
            // Carry the vectors back through the factor: z = L⁻ᵀ·y (or R⁻¹·y), which lands them
            // already scaled so Zᵀ·B·Z is the identity — the contract's normalization for free.
            _ = Trtrs(lower, transpose: lower, n, n, b, ldb, c, n);
        }

        for (int col = 0; col < n; col++)
        {
            c.AsSpan(col * n, n).CopyTo(a.Slice(col * lda, n));
        }

        return 0;
    }

    /// <inheritdoc />
    public override int Gees(bool vectors, int n, Span<double> a, int lda,
        Span<double> wr, Span<double> wi, Span<double> vs, int ldvs)
    {
        if (n == 0)
        {
            return 0;
        }

        Schur schur = Schur.FactorManaged(Rect(a, lda, n));
        WriteRect(schur.T, a, lda);
        if (vectors)
        {
            WriteRect(schur.U, vs, ldvs);
        }

        Complex[] values = schur.Eigenvalues;
        for (int i = 0; i < n; i++)
        {
            wr[i] = values[i].Real;
            wi[i] = values[i].Imaginary;
        }

        return 0;
    }

    /// <inheritdoc />
    public override int Gges(bool vectors, int n, Span<double> a, int lda, Span<double> b, int ldb,
        Span<double> alphar, Span<double> alphai, Span<double> beta,
        Span<double> vsl, int ldvsl, Span<double> vsr, int ldvsr)
    {
        if (n == 0)
        {
            return 0;
        }

        GeneralizedSchur? qz = TryFactorPencil(Rect(a, lda, n), Rect(b, ldb, n));
        if (qz is null)
        {
            return 1;
        }

        WriteRect(qz.AA, a, lda);
        WriteRect(qz.BB, b, ldb);
        if (vectors)
        {
            // The kernel's convention is Q·A·Z = AA; the contract's is A = VSL·AA·VSRᵀ. The left
            // factors are each other's transposes and the right ones coincide.
            WriteRectTransposed(qz.Q, vsl, ldvsl);
            WriteRect(qz.Z, vsr, ldvsr);
        }

        for (int i = 0; i < n; i++)
        {
            alphar[i] = qz.Alpha[i].Real;
            alphai[i] = qz.Alpha[i].Imaginary;
            beta[i] = qz.Beta[i];
        }

        return 0;
    }

    /// <inheritdoc />
    public override int Trsen(ReadOnlySpan<bool> select, int n, Span<double> t, int ldt,
        Span<double> q, int ldq, Span<double> wr, Span<double> wi)
    {
        if (n == 0)
        {
            return 0;
        }

        Schur reordered = Schur.ReorderManaged(Rect(t, ldt, n), Rect(q, ldq, n), select.ToArray());
        WriteRect(reordered.T, t, ldt);
        WriteRect(reordered.U, q, ldq);

        Complex[] values = reordered.Eigenvalues;
        for (int i = 0; i < n; i++)
        {
            wr[i] = values[i].Real;
            wi[i] = values[i].Imaginary;
        }

        return 0;
    }

    /// <summary>The managed QZ, with its singular-pencil refusal turned into a null for the contract's info code.</summary>
    private static GeneralizedSchur? TryFactorPencil(double[,] a, double[,] b)
    {
        try
        {
            return GeneralizedSchur.FactorManaged(a, b);
        }
        catch (ArgumentException)
        {
            // "This pencil is singular — every number is an eigenvalue of it": the one failure the
            // kernel reports by throwing, and the contract reports by info.
            return null;
        }
    }

    /// <summary>
    /// LAPACK <c>dggev</c>'s eigenvector scaling — the largest component's |re| + |im| becomes 1 —
    /// applied over the packed columns the general eigensolver produced, so the two backends hand
    /// the front the same convention.
    /// </summary>
    private static void NormalizePackedVectors(int n, ReadOnlySpan<double> wi, Span<double> vr, int ldvr)
    {
        for (int j = 0; j < n;)
        {
            bool pair = j + 1 < n && wi[j] > 0 && wi[j + 1] < 0;
            double largest = 0;
            for (int r = 0; r < n; r++)
            {
                double size = Math.Abs(vr[(j * ldvr) + r])
                    + (pair ? Math.Abs(vr[((j + 1) * ldvr) + r]) : 0);
                largest = Math.Max(largest, size);
            }

            if (largest > 0 && largest != 1)
            {
                for (int r = 0; r < n; r++)
                {
                    vr[(j * ldvr) + r] /= largest;
                    if (pair)
                    {
                        vr[((j + 1) * ldvr) + r] /= largest;
                    }
                }
            }

            j += pair ? 2 : 1;
        }
    }

    private static double[,] Rect(ReadOnlySpan<double> a, int lda, int n)
    {
        var rect = new double[n, n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                rect[r, c] = a[(c * lda) + r];
            }
        }

        return rect;
    }

    private static void WriteRect(double[,] source, Span<double> destination, int ld)
    {
        int n = source.GetLength(0);
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                destination[(c * ld) + r] = source[r, c];
            }
        }
    }

    private static void WriteRectTransposed(double[,] source, Span<double> destination, int ld)
    {
        int n = source.GetLength(0);
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                destination[(c * ld) + r] = source[c, r];
            }
        }
    }

    private static void TransposeInPlace(double[] a, int n)
    {
        for (int c = 0; c < n; c++)
        {
            for (int r = c + 1; r < n; r++)
            {
                (a[(c * n) + r], a[(r * n) + c]) = (a[(r * n) + c], a[(c * n) + r]);
            }
        }
    }
}
