using System.Numerics;
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
    public override unsafe bool TryGehrd(int n, Span<double> a, int lda, Span<double> tau)
    {
        if (n <= 2)
        {
            // A matrix of order two is already upper Hessenberg and has no reflector to store, which
            // LAPACK expresses by leaving tau empty rather than by refusing.
            tau.Clear();
            return true;
        }

        fixed (double* pa = a)
        fixed (double* pt = tau)
        {
            return OpenBlasNative.Dgehrd(OpenBlasNative.LapackColMajor, n, 1, n, pa, lda, pt) == 0;
        }
    }

    /// <inheritdoc />
    public override unsafe bool TryOrghr(int n, Span<double> a, int lda, ReadOnlySpan<double> tau)
    {
        if (n <= 2)
        {
            for (int c = 0; c < n; c++)
            {
                for (int r = 0; r < n; r++)
                {
                    a[r + (c * lda)] = r == c ? 1 : 0;
                }
            }

            return true;
        }

        fixed (double* pa = a)
        fixed (double* pt = tau)
        {
            return OpenBlasNative.Dorghr(OpenBlasNative.LapackColMajor, n, 1, n, pa, lda, pt) == 0;
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

    /// <inheritdoc />
    public override unsafe int Ggev(bool vectors, int n, Span<double> a, int lda, Span<double> b, int ldb,
        Span<double> alphar, Span<double> alphai, Span<double> beta, Span<double> vr, int ldvr)
    {
        if (n == 0)
        {
            return 0;
        }

        double left = 0;
        Span<double> vrOut = vr.IsEmpty ? stackalloc double[1] : vr;
        fixed (double* pa = a)
        fixed (double* pb = b)
        fixed (double* par = alphar)
        fixed (double* pai = alphai)
        fixed (double* pbe = beta)
        fixed (double* pvr = vrOut)
        {
            return OpenBlasNative.Dggev(OpenBlasNative.LapackColMajor,
                OpenBlasNative.CharNone,
                vectors ? OpenBlasNative.CharVectors : OpenBlasNative.CharNone,
                n, pa, lda, pb, ldb, par, pai, pbe, &left, 1, pvr, Math.Max(ldvr, 1));
        }
    }

    /// <inheritdoc />
    public override unsafe int Sygvd(bool vectors, bool lower, int n, Span<double> a, int lda,
        Span<double> b, int ldb, Span<double> w)
    {
        if (n == 0)
        {
            return 0;
        }

        fixed (double* pa = a)
        fixed (double* pb = b)
        fixed (double* pw = w)
        {
            return OpenBlasNative.Dsygvd(OpenBlasNative.LapackColMajor, 1,
                vectors ? OpenBlasNative.CharVectors : OpenBlasNative.CharNone,
                lower ? OpenBlasNative.CharLower : OpenBlasNative.CharUpper,
                n, pa, lda, pb, ldb, pw);
        }
    }

    /// <inheritdoc />
    public override unsafe int Gees(bool vectors, int n, Span<double> a, int lda,
        Span<double> wr, Span<double> wi, Span<double> vs, int ldvs)
    {
        if (n == 0)
        {
            return 0;
        }

        int sorted = 0;
        Span<double> vsOut = vs.IsEmpty ? stackalloc double[1] : vs;
        fixed (double* pa = a)
        fixed (double* pwr = wr)
        fixed (double* pwi = wi)
        fixed (double* pvs = vsOut)
        {
            return OpenBlasNative.Dgees(OpenBlasNative.LapackColMajor,
                vectors ? OpenBlasNative.CharVectors : OpenBlasNative.CharNone,
                OpenBlasNative.CharNone, 0, n, pa, lda, &sorted, pwr, pwi, pvs, Math.Max(ldvs, 1));
        }
    }

    /// <inheritdoc />
    public override unsafe int Gges(bool vectors, int n, Span<double> a, int lda, Span<double> b, int ldb,
        Span<double> alphar, Span<double> alphai, Span<double> beta,
        Span<double> vsl, int ldvsl, Span<double> vsr, int ldvsr)
    {
        if (n == 0)
        {
            return 0;
        }

        int sorted = 0;
        Span<double> vslOut = vsl.IsEmpty ? stackalloc double[1] : vsl;
        Span<double> vsrOut = vsr.IsEmpty ? stackalloc double[1] : vsr;
        byte job = vectors ? OpenBlasNative.CharVectors : OpenBlasNative.CharNone;
        fixed (double* pa = a)
        fixed (double* pb = b)
        fixed (double* par = alphar)
        fixed (double* pai = alphai)
        fixed (double* pbe = beta)
        fixed (double* pl = vslOut)
        fixed (double* pr = vsrOut)
        {
            return OpenBlasNative.Dgges(OpenBlasNative.LapackColMajor, job, job,
                OpenBlasNative.CharNone, 0, n, pa, lda, pb, ldb, &sorted,
                par, pai, pbe, pl, Math.Max(ldvsl, 1), pr, Math.Max(ldvsr, 1));
        }
    }

    /// <inheritdoc />
    public override unsafe int Trsen(ReadOnlySpan<bool> select, int n, Span<double> t, int ldt,
        Span<double> q, int ldq, Span<double> wr, Span<double> wi)
    {
        if (n == 0)
        {
            return 0;
        }

        // LAPACK's logical is a 32-bit int per entry, not a byte.
        var flags = new int[n];
        for (int i = 0; i < n; i++)
        {
            flags[i] = select[i] ? 1 : 0;
        }

        int kept = 0;
        double condition = 0;
        double separation = 0;
        fixed (double* pt = t)
        fixed (double* pq = q)
        fixed (double* pwr = wr)
        fixed (double* pwi = wi)
        fixed (int* pf = flags)
        {
            return OpenBlasNative.Dtrsen(OpenBlasNative.LapackColMajor,
                OpenBlasNative.CharNone, OpenBlasNative.CharVectors, pf, n,
                pt, ldt, pq, ldq, pwr, pwi, &kept, &condition, &separation);
        }
    }

    /// <inheritdoc />
    public override unsafe void Zgemm(int m, int n, int k, ReadOnlySpan<Complex> a, int lda,
        ReadOnlySpan<Complex> b, int ldb, Span<Complex> c, int ldc)
    {
        if (m == 0 || n == 0)
        {
            return;
        }

        if (k == 0)
        {
            for (int col = 0; col < n; col++)
            {
                c.Slice(col * ldc, m).Clear();
            }

            return;
        }

        Complex one = Complex.One;
        Complex zero = Complex.Zero;
        fixed (Complex* pa = a)
        fixed (Complex* pb = b)
        fixed (Complex* pc = c)
        {
            OpenBlasNative.Zgemm(OpenBlasNative.CblasColMajor,
                OpenBlasNative.CblasNoTrans, OpenBlasNative.CblasNoTrans,
                m, n, k, &one, pa, lda, pb, ldb, &zero, pc, ldc);
        }
    }

    /// <inheritdoc />
    public override unsafe int Zgetrf(int m, int n, Span<Complex> a, int lda, Span<int> ipiv)
    {
        if (m == 0 || n == 0)
        {
            return 0;
        }

        fixed (Complex* pa = a)
        fixed (int* pivots = ipiv)
        {
            return OpenBlasNative.Zgetrf(OpenBlasNative.LapackColMajor, m, n, pa, lda, pivots);
        }
    }

    /// <inheritdoc />
    public override unsafe void Zgetrs(int n, int nrhs, ReadOnlySpan<Complex> a, int lda,
        ReadOnlySpan<int> ipiv, Span<Complex> b, int ldb)
    {
        if (n == 0 || nrhs == 0)
        {
            return;
        }

        fixed (Complex* pa = a)
        fixed (int* pivots = ipiv)
        fixed (Complex* pb = b)
        {
            _ = OpenBlasNative.Zgetrs(OpenBlasNative.LapackColMajor, OpenBlasNative.CharNoTrans,
                n, nrhs, pa, lda, pivots, pb, ldb);
        }
    }

    /// <inheritdoc />
    public override unsafe int Zgetri(int n, Span<Complex> a, int lda, ReadOnlySpan<int> ipiv)
    {
        if (n == 0)
        {
            return 0;
        }

        fixed (Complex* pa = a)
        fixed (int* pivots = ipiv)
        {
            return OpenBlasNative.Zgetri(OpenBlasNative.LapackColMajor, n, pa, lda, pivots);
        }
    }

    /// <inheritdoc />
    public override unsafe int Zgeev(bool vectors, int n, Span<Complex> a, int lda,
        Span<Complex> w, Span<Complex> vr, int ldvr)
    {
        if (n == 0)
        {
            return 0;
        }

        Complex left = Complex.Zero;
        Span<Complex> vrOut = vr.IsEmpty ? stackalloc Complex[1] : vr;
        fixed (Complex* pa = a)
        fixed (Complex* pw = w)
        fixed (Complex* pvr = vrOut)
        {
            return OpenBlasNative.Zgeev(OpenBlasNative.LapackColMajor,
                OpenBlasNative.CharNone,
                vectors ? OpenBlasNative.CharVectors : OpenBlasNative.CharNone,
                n, pa, lda, pw, &left, 1, pvr, Math.Max(ldvr, 1));
        }
    }

    /// <inheritdoc />
    public override unsafe int Zgees(int n, Span<Complex> a, int lda,
        Span<Complex> w, Span<Complex> vs, int ldvs)
    {
        if (n == 0)
        {
            return 0;
        }

        int sorted = 0;
        fixed (Complex* pa = a)
        fixed (Complex* pw = w)
        fixed (Complex* pvs = vs)
        {
            return OpenBlasNative.Zgees(OpenBlasNative.LapackColMajor,
                OpenBlasNative.CharVectors, OpenBlasNative.CharNone, 0, n,
                pa, lda, &sorted, pw, pvs, Math.Max(ldvs, 1));
        }
    }

    /// <inheritdoc />
    public override unsafe int Zgesdd(SvdVectors job, int m, int n, Span<Complex> a, int lda,
        Span<double> s, Span<Complex> u, int ldu, Span<Complex> vt, int ldvt)
    {
        if (m == 0 || n == 0)
        {
            return 0;
        }

        Span<Complex> uOut = u.IsEmpty ? stackalloc Complex[1] : u;
        Span<Complex> vtOut = vt.IsEmpty ? stackalloc Complex[1] : vt;
        fixed (Complex* pa = a)
        fixed (double* ps = s)
        fixed (Complex* pu = uOut)
        fixed (Complex* pvt = vtOut)
        {
            return OpenBlasNative.Zgesdd(OpenBlasNative.LapackColMajor, JobCharacter(job), m, n,
                pa, lda, ps, pu, Math.Max(ldu, 1), pvt, Math.Max(ldvt, 1));
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
