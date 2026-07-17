using JGraph.Core.Model;

namespace JGraph.Demo;

/// <summary>One entry in the demo gallery: a name, a category, and a factory that builds its figure.</summary>
internal sealed class GalleryExample
{
    public GalleryExample(string name, string category, Func<FigureModel> build)
    {
        Name = name;
        Category = category;
        Build = build;
    }

    public string Name { get; }

    public string Category { get; }

    public Func<FigureModel> Build { get; }

    public override string ToString() => Name;
}
