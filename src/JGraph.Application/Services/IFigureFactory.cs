using JGraph.Core.Model;

namespace JGraph.Application.Services;

/// <summary>Creates figures for the application shell. Injected so the sample content is swappable.</summary>
public interface IFigureFactory
{
    /// <summary>Creates a sample figure to display on startup.</summary>
    FigureModel CreateSample();
}
