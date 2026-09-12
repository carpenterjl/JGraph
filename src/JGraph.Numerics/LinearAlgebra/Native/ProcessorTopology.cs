using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace JGraph.Numerics.LinearAlgebra.Native;

/// <summary>
/// How many physical cores this machine has, as distinct from how many logical processors
/// <see cref="Environment.ProcessorCount"/> counts.
/// </summary>
/// <remarks>
/// <para>
/// The distinction decides the native thread count. A blocked factorization is a stream of fused
/// multiply-adds, and the two hyperthreads of one core share the units that run them: measured on an
/// 8-core / 16-thread i7-11700F, <c>dgetrf</c> at n = 2000 takes 0.074 s on 16 threads and 0.047 s
/// on 8, and <c>dgetri</c> 0.182 s against 0.150 s. Only <c>dgemm</c> prefers the wider count, and
/// then by about a tenth — so one thread per physical core is the default, and
/// <c>JGRAPH_BLAS_THREADS</c> is there for a machine that disagrees.
/// </para>
/// <para>
/// On a hybrid part only the performance cores count. OpenBLAS partitions a level-3 call evenly
/// across its threads and waits for the slowest, and an efficiency core is not a slightly slower
/// peer: pinned to the eight E-cores of an i7-12700H, <c>dgeqrf</c> at n = 1200 took 0.80 s against
/// 0.062 s on the six P-cores. Giving the E-cores eight fourteenths of every panel update made
/// <c>dpotrf</c> at n = 1600 2.3x slower on fourteen threads than on six.
/// </para>
/// </remarks>
internal static unsafe partial class ProcessorTopology
{
    /// <summary>Windows' <c>LOGICAL_PROCESSOR_RELATIONSHIP</c> value for "one physical core".</summary>
    private const int RelationProcessorCore = 0;

    /// <summary>
    /// The number of physical cores in the fastest efficiency class — every core on a homogeneous
    /// part, the performance cores on a hybrid one — or null when the platform will not say.
    /// </summary>
    internal static int? PerformanceCoreCount()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            int length = 0;
            GetLogicalProcessorInformationEx(RelationProcessorCore, null, ref length);
            if (length <= 0)
            {
                return null;
            }

            var buffer = new byte[length];
            fixed (byte* start = buffer)
            {
                if (!GetLogicalProcessorInformationEx(RelationProcessorCore, start, ref length))
                {
                    return null;
                }

                // A sequence of variable-length records, each starting with its relationship and its
                // own size in bytes. One record per physical core is exactly what we are counting,
                // and each carries the core's efficiency class one byte past the record's flags:
                // higher is faster, and a homogeneous part reports zero for all of them.
                int best = -1;
                int cores = 0;
                int offset = 0;
                while (offset + 10 <= length)
                {
                    int relationship = Unsafe.ReadUnaligned<int>(start + offset);
                    int size = Unsafe.ReadUnaligned<int>(start + offset + 4);
                    if (size <= 0)
                    {
                        break;
                    }

                    if (relationship == RelationProcessorCore)
                    {
                        int efficiency = start[offset + 9];
                        if (efficiency > best)
                        {
                            best = efficiency;
                            cores = 0;
                        }

                        if (efficiency == best)
                        {
                            cores++;
                        }
                    }

                    offset += size;
                }

                return cores > 0 ? cores : null;
            }
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetLogicalProcessorInformationEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLogicalProcessorInformationEx(int relationship, byte* buffer, ref int returnedLength);
}
