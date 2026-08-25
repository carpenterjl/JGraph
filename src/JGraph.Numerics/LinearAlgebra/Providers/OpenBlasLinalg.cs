using JGraph.Numerics.LinearAlgebra.Native;

namespace JGraph.Numerics.LinearAlgebra;

/// <summary>
/// The OpenBLAS <see cref="DenseLinalg"/>. Construction is gated by <see cref="LinalgProvider"/>,
/// which only instantiates this after <see cref="OpenBlasLoader"/> reports a successful load.
/// Numeric note: BLAS multiplies every element — it does not skip zero factors the way the managed
/// saxpy kernel does — so <c>0·Inf</c> and <c>0·NaN</c> contribute NaN here exactly as they do in
/// MATLAB, where the managed kernel's skip silently drops them (recorded divergence, ADR 0088).
/// </summary>
public sealed class OpenBlasLinalg : DenseLinalg
{
    /// <inheritdoc />
    public override bool IsNative => true;

    /// <inheritdoc />
    public override string Description => OpenBlasLoader.Status.Description;

    /// <inheritdoc />
    public override unsafe void Gemm(bool transA, bool transB, int m, int n, int k,
        ReadOnlySpan<double> a, int lda, ReadOnlySpan<double> b, int ldb, Span<double> c, int ldc)
    {
        if (m == 0 || n == 0)
        {
            return;
        }

        if (k == 0)
        {
            // beta = 0 would clear C, but with nothing to multiply the empty spans cannot be
            // handed to the native call; the contract's "C is overwritten" is honored by hand.
            for (int col = 0; col < n; col++)
            {
                c.Slice(col * ldc, m).Clear();
            }

            return;
        }

        fixed (double* pa = a)
        fixed (double* pb = b)
        fixed (double* pc = c)
        {
            OpenBlasNative.Dgemm(OpenBlasNative.CblasColMajor,
                transA ? OpenBlasNative.CblasTrans : OpenBlasNative.CblasNoTrans,
                transB ? OpenBlasNative.CblasTrans : OpenBlasNative.CblasNoTrans,
                m, n, k, 1.0, pa, lda, pb, ldb, 0.0, pc, ldc);
        }
    }

    /// <inheritdoc />
    public override unsafe void Syrk(bool transposeFirst, int n, int k,
        ReadOnlySpan<double> a, int lda, Span<double> c, int ldc)
    {
        if (n == 0)
        {
            return;
        }

        if (k == 0)
        {
            for (int col = 0; col < n; col++)
            {
                c.Slice(col * ldc, n).Clear();
            }

            return;
        }

        fixed (double* pa = a)
        fixed (double* pc = c)
        {
            OpenBlasNative.Dsyrk(OpenBlasNative.CblasColMajor, OpenBlasNative.CblasLower,
                transposeFirst ? OpenBlasNative.CblasTrans : OpenBlasNative.CblasNoTrans,
                n, k, 1.0, pa, lda, 0.0, pc, ldc);
        }

        MirrorLowerTriangle(c, n, ldc);
    }

    /// <inheritdoc />
    public override unsafe int Getrf(int m, int n, Span<double> a, int lda, Span<int> ipiv)
    {
        if (m == 0 || n == 0)
        {
            return 0;
        }

        fixed (double* pa = a)
        fixed (int* pivots = ipiv)
        {
            return OpenBlasNative.Dgetrf(OpenBlasNative.LapackColMajor, m, n, pa, lda, pivots);
        }
    }

    /// <inheritdoc />
    public override unsafe void Getrs(bool transpose, int n, int nrhs,
        ReadOnlySpan<double> a, int lda, ReadOnlySpan<int> ipiv, Span<double> b, int ldb)
    {
        if (n == 0 || nrhs == 0)
        {
            return;
        }

        fixed (double* pa = a)
        fixed (int* pivots = ipiv)
        fixed (double* pb = b)
        {
            OpenBlasNative.Dgetrs(OpenBlasNative.LapackColMajor,
                transpose ? OpenBlasNative.CharTrans : OpenBlasNative.CharNoTrans,
                n, nrhs, pa, lda, pivots, pb, ldb);
        }
    }

    /// <inheritdoc />
    public override unsafe int Getri(int n, Span<double> a, int lda, ReadOnlySpan<int> ipiv)
    {
        if (n == 0)
        {
            return 0;
        }

        fixed (double* pa = a)
        fixed (int* pivots = ipiv)
        {
            return OpenBlasNative.Dgetri(OpenBlasNative.LapackColMajor, n, pa, lda, pivots);
        }
    }

    /// <inheritdoc />
    public override unsafe double Gecon(int n, ReadOnlySpan<double> a, int lda, double anorm)
    {
        if (n == 0)
        {
            return 0;
        }

        double rcond = 0;
        fixed (double* pa = a)
        {
            // LAPACK's estimator, which is what MATLAB's rcond reports — a lower bound on the true
            // reciprocal condition number, never the exact 1/κ the managed backend computes.
            OpenBlasNative.Dgecon(OpenBlasNative.LapackColMajor, OpenBlasNative.CharOneNorm,
                n, pa, lda, anorm, &rcond);
        }

        return rcond;
    }

    /// <inheritdoc />
    public override unsafe int Potrf(bool lower, int n, Span<double> a, int lda)
    {
        if (n == 0)
        {
            return 0;
        }

        fixed (double* pa = a)
        {
            return OpenBlasNative.Dpotrf(OpenBlasNative.LapackColMajor,
                lower ? OpenBlasNative.CharLower : OpenBlasNative.CharUpper, n, pa, lda);
        }
    }

    /// <inheritdoc />
    public override unsafe int Trtrs(bool lower, bool transpose, int n, int nrhs,
        ReadOnlySpan<double> a, int lda, Span<double> b, int ldb)
    {
        if (n == 0 || nrhs == 0)
        {
            return 0;
        }

        fixed (double* pa = a)
        fixed (double* pb = b)
        {
            return OpenBlasNative.Dtrtrs(OpenBlasNative.LapackColMajor,
                lower ? OpenBlasNative.CharLower : OpenBlasNative.CharUpper,
                transpose ? OpenBlasNative.CharTrans : OpenBlasNative.CharNoTrans,
                OpenBlasNative.CharNonUnit, n, nrhs, pa, lda, pb, ldb);
        }
    }

    /// <inheritdoc />
    public override unsafe int Gels(int m, int n, int nrhs, Span<double> a, int lda, Span<double> b, int ldb)
    {
        if (m == 0 || n == 0 || nrhs == 0)
        {
            return 0;
        }

        fixed (double* pa = a)
        fixed (double* pb = b)
        {
            return OpenBlasNative.Dgels(OpenBlasNative.LapackColMajor, OpenBlasNative.CharNoTrans,
                m, n, nrhs, pa, lda, pb, ldb);
        }
    }

    /// <inheritdoc />
    public override unsafe int Geqrf(int m, int n, Span<double> a, int lda, Span<double> tau)
    {
        if (m == 0 || n == 0)
        {
            return 0;
        }

        fixed (double* pa = a)
        fixed (double* pt = tau)
        {
            return OpenBlasNative.Dgeqrf(OpenBlasNative.LapackColMajor, m, n, pa, lda, pt);
        }
    }

    /// <inheritdoc />
    public override unsafe int Orgqr(int m, int n, int k, Span<double> a, int lda, ReadOnlySpan<double> tau)
    {
        if (m == 0 || n == 0)
        {
            return 0;
        }

        fixed (double* pa = a)
        fixed (double* pt = tau)
        {
            return OpenBlasNative.Dorgqr(OpenBlasNative.LapackColMajor, m, n, k, pa, lda, pt);
        }
    }

    /// <inheritdoc />
    public override unsafe int Ormqr(bool leftSide, bool transpose, int m, int n, int k,
        ReadOnlySpan<double> a, int lda, ReadOnlySpan<double> tau, Span<double> c, int ldc)
    {
        if (m == 0 || n == 0 || k == 0)
        {
            return 0;
        }

        fixed (double* pa = a)
        fixed (double* pt = tau)
        fixed (double* pc = c)
        {
            return OpenBlasNative.Dormqr(OpenBlasNative.LapackColMajor,
                leftSide ? OpenBlasNative.CharLeft : OpenBlasNative.CharRight,
                transpose ? OpenBlasNative.CharTrans : OpenBlasNative.CharNoTrans,
                m, n, k, pa, lda, pt, pc, ldc);
        }
    }

    /// <inheritdoc />
    public override unsafe int Geqp3(int m, int n, Span<double> a, int lda, Span<int> jpvt, Span<double> tau)
    {
        if (m == 0 || n == 0)
        {
            return 0;
        }

        // Every entry must be zero on the way in; a nonzero one would pin that column to the front.
        jpvt.Clear();
        fixed (double* pa = a)
        fixed (int* pj = jpvt)
        fixed (double* pt = tau)
        {
            return OpenBlasNative.Dgeqp3(OpenBlasNative.LapackColMajor, m, n, pa, lda, pj, pt);
        }
    }

    /// <inheritdoc />
    public override unsafe int Gesdd(SvdVectors job, int m, int n, Span<double> a, int lda,
        Span<double> s, Span<double> u, int ldu, Span<double> vt, int ldvt)
    {
        if (m == 0 || n == 0)
        {
            return 0;
        }

        // LAPACK never reads U or Vᵀ for a values-only job, but it is handed a real address all the
        // same: a null one is outside the Fortran contract even where it is never dereferenced.
        Span<double> uOut = u.IsEmpty ? stackalloc double[1] : u;
        Span<double> vtOut = vt.IsEmpty ? stackalloc double[1] : vt;
        fixed (double* pa = a)
        fixed (double* ps = s)
        fixed (double* pu = uOut)
        fixed (double* pvt = vtOut)
        {
            return OpenBlasNative.Dgesdd(OpenBlasNative.LapackColMajor, JobCharacter(job), m, n,
                pa, lda, ps, pu, Math.Max(ldu, 1), pvt, Math.Max(ldvt, 1));
        }
    }

    /// <inheritdoc />
    public override unsafe int Gesvd(SvdVectors job, int m, int n, Span<double> a, int lda,
        Span<double> s, Span<double> u, int ldu, Span<double> vt, int ldvt)
    {
        if (m == 0 || n == 0)
        {
            return 0;
        }

        Span<double> uOut = u.IsEmpty ? stackalloc double[1] : u;
        Span<double> vtOut = vt.IsEmpty ? stackalloc double[1] : vt;

        // The unconverged superdiagonal LAPACK would report through; nothing here reads it back,
        // but the array has to exist because a failing call writes to it.
        var superb = new double[Math.Max(Math.Min(m, n) - 1, 1)];
        byte character = JobCharacter(job);
        fixed (double* pa = a)
        fixed (double* ps = s)
        fixed (double* pu = uOut)
        fixed (double* pvt = vtOut)
        fixed (double* pb = superb)
        {
            return OpenBlasNative.Dgesvd(OpenBlasNative.LapackColMajor, character, character, m, n,
                pa, lda, ps, pu, Math.Max(ldu, 1), pvt, Math.Max(ldvt, 1), pb);
        }
    }

    /// <inheritdoc />
    public override unsafe int Syevd(bool vectors, bool lower, int n, Span<double> a, int lda, Span<double> w)
    {
        if (n == 0)
        {
            return 0;
        }

        fixed (double* pa = a)
        fixed (double* pw = w)
        {
            return OpenBlasNative.Dsyevd(OpenBlasNative.LapackColMajor,
                vectors ? OpenBlasNative.CharVectors : OpenBlasNative.CharNone,
                lower ? OpenBlasNative.CharLower : OpenBlasNative.CharUpper, n, pa, lda, pw);
        }
    }

    /// <inheritdoc />
    public override unsafe int Geev(bool vectors, int n, Span<double> a, int lda,
        Span<double> wr, Span<double> wi, Span<double> vr, int ldvr)
    {
        if (n == 0)
        {
            return 0;
        }

        double left = 0;
        Span<double> vrOut = vr.IsEmpty ? stackalloc double[1] : vr;
        fixed (double* pa = a)
        fixed (double* pwr = wr)
        fixed (double* pwi = wi)
        fixed (double* pvr = vrOut)
        {
            return OpenBlasNative.Dgeev(OpenBlasNative.LapackColMajor,
                OpenBlasNative.CharNone,
                vectors ? OpenBlasNative.CharVectors : OpenBlasNative.CharNone,
                n, pa, lda, pwr, pwi, &left, 1, pvr, Math.Max(ldvr, 1));
        }
    }

    /// <summary>The LAPACK <c>jobz</c> character for how much of the SVD's factors were asked for.</summary>
    private static byte JobCharacter(SvdVectors job) => job switch
    {
        SvdVectors.None => OpenBlasNative.CharNone,
        SvdVectors.Economy => OpenBlasNative.CharSome,
        _ => OpenBlasNative.CharAll,
    };
}
