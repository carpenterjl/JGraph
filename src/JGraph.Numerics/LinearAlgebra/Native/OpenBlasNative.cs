using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace JGraph.Numerics.LinearAlgebra.Native;

/// <summary>
/// Raw OpenBLAS entry points — CBLAS and LAPACKE symbols, column-major only, blittable arguments
/// only (so the source-generated marshalling is warning-clean). Never call these before
/// <see cref="OpenBlasLoader"/> reports a successful load: the library lives in the application's
/// <c>native\</c> subfolder, which only the loader's resolver knows to probe.
/// </summary>
internal static unsafe partial class OpenBlasNative
{
    /// <summary>The import name the loader's resolver maps to <c>native\libopenblas.dll</c>.</summary>
    internal const string Library = "libopenblas";

    /// <summary>CBLAS_ORDER: column-major.</summary>
    internal const int CblasColMajor = 102;

    /// <summary>CBLAS_TRANSPOSE: no transpose.</summary>
    internal const int CblasNoTrans = 111;

    /// <summary>CBLAS_TRANSPOSE: transpose.</summary>
    internal const int CblasTrans = 112;

    /// <summary>CBLAS_UPLO: lower triangle.</summary>
    internal const int CblasLower = 122;

    /// <summary>LAPACK_COL_MAJOR: the layout every LAPACKE call here uses, and the one our storage already is.</summary>
    internal const int LapackColMajor = 102;

    /// <summary>LAPACK's <c>uplo</c> character for the upper triangle.</summary>
    internal const byte CharUpper = (byte)'U';

    /// <summary>LAPACK's <c>uplo</c> character for the lower triangle.</summary>
    internal const byte CharLower = (byte)'L';

    /// <summary>LAPACK's <c>trans</c> character for "use the matrix as it stands".</summary>
    internal const byte CharNoTrans = (byte)'N';

    /// <summary>LAPACK's <c>trans</c> character for "use the transpose".</summary>
    internal const byte CharTrans = (byte)'T';

    /// <summary>LAPACK's <c>diag</c> character for a diagonal that is stored, not implied.</summary>
    internal const byte CharNonUnit = (byte)'N';

    /// <summary>LAPACK's <c>norm</c> character selecting the 1-norm.</summary>
    internal const byte CharOneNorm = (byte)'1';

    /// <summary>LAPACK's <c>jobz</c>/<c>jobu</c> character asking for every column of the factor.</summary>
    internal const byte CharAll = (byte)'A';

    /// <summary>LAPACK's <c>jobz</c> character asking for the economy-size factors only.</summary>
    internal const byte CharSome = (byte)'S';

    /// <summary>LAPACK's <c>job</c> character declining a factor — values only.</summary>
    internal const byte CharNone = (byte)'N';

    /// <summary>LAPACK's <c>job</c> character asking for the vectors as well as the values.</summary>
    internal const byte CharVectors = (byte)'V';

    /// <summary>LAPACK's <c>side</c> character: the operator multiplies from the left.</summary>
    internal const byte CharLeft = (byte)'L';

    /// <summary>LAPACK's <c>side</c> character: the operator multiplies from the right.</summary>
    internal const byte CharRight = (byte)'R';

    [LibraryImport(Library, EntryPoint = "cblas_dgemm")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void Dgemm(int order, int transA, int transB,
        int m, int n, int k, double alpha, double* a, int lda,
        double* b, int ldb, double beta, double* c, int ldc);

    [LibraryImport(Library, EntryPoint = "cblas_dsyrk")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void Dsyrk(int order, int uplo, int trans,
        int n, int k, double alpha, double* a, int lda, double beta, double* c, int ldc);

    /// <summary>P·A = L·U in place; returns 0, or the 1-based index of a zero pivot on U.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dgetrf")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dgetrf(int layout, int m, int n, double* a, int lda, int* ipiv);

    /// <summary>Solves from a <see cref="Dgetrf"/> factorization, overwriting B with X.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dgetrs")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dgetrs(int layout, byte trans, int n, int nrhs,
        double* a, int lda, int* ipiv, double* b, int ldb);

    /// <summary>Overwrites a <see cref="Dgetrf"/> factorization with the inverse — 4/3·n³, not 2·n³.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dgetri")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dgetri(int layout, int n, double* a, int lda, int* ipiv);

    /// <summary>Estimates the reciprocal condition number from a factorization and ‖A‖.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dgecon")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dgecon(int layout, byte norm, int n, double* a, int lda,
        double anorm, double* rcond);

    /// <summary>Cholesky in place; returns 0, or the order at which definiteness failed.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dpotrf")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dpotrf(int layout, byte uplo, int n, double* a, int lda);

    /// <summary>Triangular solve, overwriting B; returns the 1-based index of a zero diagonal.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dtrtrs")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dtrtrs(int layout, byte uplo, byte trans, byte diag, int n, int nrhs,
        double* a, int lda, double* b, int ldb);

    /// <summary>Least-squares (tall) or minimum-norm (wide) solve; A and B are both overwritten.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dgels")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dgels(int layout, byte trans, int m, int n, int nrhs,
        double* a, int lda, double* b, int ldb);

    /// <summary>A = Q·R in place: R on and above the diagonal, the reflector vectors below.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dgeqrf")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dgeqrf(int layout, int m, int n, double* a, int lda, double* tau);

    /// <summary>Expands the reflectors of a <see cref="Dgeqrf"/> factorization into Q's first n columns.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dorgqr")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dorgqr(int layout, int m, int n, int k, double* a, int lda, double* tau);

    /// <summary>Multiplies C by Q (or Qᵀ) without ever forming Q — the least-squares workhorse.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dormqr")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dormqr(int layout, byte side, byte trans, int m, int n, int k,
        double* a, int lda, double* tau, double* c, int ldc);

    /// <summary>QR with column pivoting, A·P = Q·R; <c>jpvt</c> must arrive zeroed to leave every column free.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dgeqp3")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dgeqp3(int layout, int m, int n, double* a, int lda, int* jpvt, double* tau);

    /// <summary>The divide-and-conquer SVD, A = U·Σ·Vᵀ. A is overwritten whatever the job.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dgesdd")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dgesdd(int layout, byte jobz, int m, int n, double* a, int lda,
        double* s, double* u, int ldu, double* vt, int ldvt);

    /// <summary>The QR-iteration SVD — slower than <see cref="Dgesdd"/>, and the fallback when it fails to converge.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dgesvd")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dgesvd(int layout, byte jobu, byte jobvt, int m, int n, double* a, int lda,
        double* s, double* u, int ldu, double* vt, int ldvt, double* superb);

    /// <summary>The symmetric divide-and-conquer eigensolver: ascending values, orthonormal vectors over A.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dsyevd")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dsyevd(int layout, byte jobz, byte uplo, int n, double* a, int lda, double* w);

    /// <summary>
    /// The general eigensolver. A conjugate pair occupies two consecutive columns of <c>vr</c> —
    /// real part then imaginary part — which is LAPACK's packing, not two complex columns.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dgeev")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dgeev(int layout, byte jobvl, byte jobvr, int n, double* a, int lda,
        double* wr, double* wi, double* vl, int ldvl, double* vr, int ldvr);

    /// <summary>
    /// The real generalized eigensolver for the pencil A − λ·B: eigenvalues as α/β pairs, and
    /// the right eigenvectors when asked, packed the way <c>dgeev</c> packs a conjugate pair.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dggev")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dggev(int layout, byte jobvl, byte jobvr, int n, double* a, int lda,
        double* b, int ldb, double* alphar, double* alphai, double* beta,
        double* vl, int ldvl, double* vr, int ldvr);

    /// <summary>
    /// The symmetric-definite generalized eigensolver, itype 1: A·z = λ·B·z with B positive
    /// definite, values ascending and the vectors scaled so that Zᵀ·B·Z is the identity.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dsygvd")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dsygvd(int layout, int itype, byte jobz, byte uplo, int n,
        double* a, int lda, double* b, int ldb, double* w);

    /// <summary>
    /// The real Schur form A = Z·T·Zᵀ in place. <c>select</c> is a sorting callback this
    /// codebase never uses — always pass zero with <c>sort = 'N'</c>; reordering is
    /// <see cref="Dtrsen"/>'s job.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dgees")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dgees(int layout, byte jobvs, byte sort, nint select, int n,
        double* a, int lda, int* sdim, double* wr, double* wi, double* vs, int ldvs);

    /// <summary>
    /// The generalized Schur (QZ) factorization in place: A and B become the quasi-triangular
    /// and triangular factors, with the orthogonal Q and Z alongside. <c>selctg</c> is the
    /// unused sorting callback — always zero, with <c>sort = 'N'</c>.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dgges")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dgges(int layout, byte jobvsl, byte jobvsr, byte sort, nint selctg,
        int n, double* a, int lda, double* b, int ldb, int* sdim,
        double* alphar, double* alphai, double* beta,
        double* vsl, int ldvsl, double* vsr, int ldvsr);

    /// <summary>
    /// Reorders a real Schur form so the selected eigenvalues lead. <c>select</c> is LAPACK's
    /// logical array — int32 per entry, nonzero for chosen — and both halves of a conjugate
    /// pair must carry the same flag.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_dtrsen")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Dtrsen(int layout, byte job, byte compq, int* select, int n,
        double* t, int ldt, double* q, int ldq, double* wr, double* wi, int* m, double* s, double* sep);

    /// <summary>
    /// C := α·A·B + β·C over interleaved complex doubles —
    /// <see cref="System.Numerics.Complex"/>'s own layout, so a pinned span goes straight
    /// through. α and β arrive by pointer, in the CBLAS complex convention.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "cblas_zgemm")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void Zgemm(int order, int transA, int transB, int m, int n, int k,
        Complex* alpha, Complex* a, int lda, Complex* b, int ldb, Complex* beta, Complex* c, int ldc);

    /// <summary>Solves complex A·X = B from a <see cref="Zgetrf"/> factorization, overwriting B with X.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_zgetrs")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Zgetrs(int layout, byte trans, int n, int nrhs, Complex* a, int lda,
        int* ipiv, Complex* b, int ldb);

    /// <summary>The complex LU factorization in place — <see cref="Dgetrf"/> over complex doubles.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_zgetrf")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Zgetrf(int layout, int m, int n, Complex* a, int lda, int* ipiv);

    /// <summary>Overwrites a <see cref="Zgetrf"/> factorization with the inverse.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_zgetri")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Zgetri(int layout, int n, Complex* a, int lda, int* ipiv);

    /// <summary>
    /// The complex general eigensolver. Unlike <see cref="Dgeev"/> there is no packing to undo:
    /// every eigenvector is simply a complex column.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_zgeev")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Zgeev(int layout, byte jobvl, byte jobvr, int n, Complex* a, int lda,
        Complex* w, Complex* vl, int ldvl, Complex* vr, int ldvr);

    /// <summary>
    /// The complex Schur factorization. Unlike <see cref="Dgees"/> the answer is genuinely
    /// triangular — there are no two-by-two blocks, because complex arithmetic has nowhere it needs
    /// to hide a conjugate pair.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_zgees")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Zgees(int layout, byte jobvs, byte sort, nint select, int n,
        Complex* a, int lda, int* sdim, Complex* w, Complex* vs, int ldvs);

    /// <summary>The complex divide-and-conquer SVD; the singular values themselves stay real.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_zgesdd")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Zgesdd(int layout, byte jobz, int m, int n, Complex* a, int lda,
        double* s, Complex* u, int ldu, Complex* vt, int ldvt);

    /// <summary>The complex QR-iteration SVD — the fallback when <see cref="Zgesdd"/> fails to converge.</summary>
    [LibraryImport(Library, EntryPoint = "LAPACKE_zgesvd")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int Zgesvd(int layout, byte jobu, byte jobvt, int m, int n, Complex* a, int lda,
        double* s, Complex* u, int ldu, Complex* vt, int ldvt, double* superb);

    [LibraryImport(Library, EntryPoint = "openblas_set_num_threads")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void SetNumThreads(int count);

    [LibraryImport(Library, EntryPoint = "openblas_get_num_threads")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int GetNumThreads();

    /// <summary>Returns a static <c>char*</c> build-configuration string. Do not free it.</summary>
    [LibraryImport(Library, EntryPoint = "openblas_get_config")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nint GetConfig();
}
