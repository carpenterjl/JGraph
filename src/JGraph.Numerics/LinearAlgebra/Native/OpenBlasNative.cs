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

    [LibraryImport(Library, EntryPoint = "cblas_dgemm")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void Dgemm(int order, int transA, int transB,
        int m, int n, int k, double alpha, double* a, int lda,
        double* b, int ldb, double beta, double* c, int ldc);

    [LibraryImport(Library, EntryPoint = "cblas_dsyrk")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void Dsyrk(int order, int uplo, int trans,
        int n, int k, double alpha, double* a, int lda, double beta, double* c, int ldc);

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
