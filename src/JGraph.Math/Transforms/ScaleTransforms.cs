using JGraph.Core.Model;

namespace JGraph.Maths.Transforms;

/// <summary>The identity scale used by linear axes.</summary>
public sealed class LinearScaleTransform : IScaleTransform
{
    public static readonly LinearScaleTransform Instance = new();

    public AxisScaleType ScaleType => AxisScaleType.Linear;

    public bool IsValidData(double value) => double.IsFinite(value);

    public double Forward(double dataValue) => dataValue;

    public double Inverse(double linearValue) => linearValue;
}

/// <summary>The base-10 logarithmic scale used by logarithmic axes.</summary>
public sealed class LogarithmicScaleTransform : IScaleTransform
{
    /// <summary>Smallest positive value mapped; values at or below zero are clamped to this floor.</summary>
    public const double MinPositive = 1e-300;

    public static readonly LogarithmicScaleTransform Instance = new();

    public AxisScaleType ScaleType => AxisScaleType.Logarithmic;

    public bool IsValidData(double value) => double.IsFinite(value) && value > 0;

    public double Forward(double dataValue)
    {
        double clamped = dataValue <= 0 ? MinPositive : dataValue;
        return System.Math.Log10(clamped);
    }

    public double Inverse(double linearValue) => System.Math.Pow(10, linearValue);
}

/// <summary>Creates <see cref="IScaleTransform"/> instances for axis scale types.</summary>
public static class ScaleTransforms
{
    /// <summary>Returns the transform for a scale type. Unimplemented scales fall back to linear.</summary>
    public static IScaleTransform For(AxisScaleType scaleType) => scaleType switch
    {
        AxisScaleType.Logarithmic => LogarithmicScaleTransform.Instance,
        _ => LinearScaleTransform.Instance,
    };
}
