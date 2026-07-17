using JGraph.Core.Primitives;

namespace JGraph.Core.Data;

/// <summary>
/// A read-only source of 2D samples for a plot. Abstracting the data behind this interface lets plot
/// objects consume arrays today and observable collections, files, or live streams later without any
/// change to rendering. Array-backed sources additionally expose contiguous spans via
/// <see cref="TryGetSpans"/> for zero-copy decimation of very large datasets.
/// </summary>
public interface IDataSeries
{
    /// <summary>The number of samples.</summary>
    int Count { get; }

    /// <summary>Whether the X samples are non-decreasing, enabling windowed decimation.</summary>
    bool IsXAscending { get; }

    /// <summary>The X value of a sample.</summary>
    double GetX(int index);

    /// <summary>The Y value of a sample.</summary>
    double GetY(int index);

    /// <summary>The extent of the X samples (finite values only).</summary>
    DataRange XBounds { get; }

    /// <summary>The extent of the Y samples (finite values only).</summary>
    DataRange YBounds { get; }

    /// <summary>
    /// Exposes the underlying contiguous X and Y storage when available. Returns false for sources
    /// that are not array-backed; callers then fall back to <see cref="GetX"/>/<see cref="GetY"/>.
    /// </summary>
    bool TryGetSpans(out ReadOnlySpan<double> xs, out ReadOnlySpan<double> ys);
}
