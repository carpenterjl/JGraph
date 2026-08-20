using JGraph.Core.Model;
using JGraph.Interaction;

namespace JGraph.Controls;

/// <summary>A press over the figure: which figure, what it landed on, and with which button.</summary>
public sealed class ObjectClickedEventArgs : EventArgs
{
    public ObjectClickedEventArgs(FigureModel figure, FigureHit hit, PointerButton button)
    {
        Figure = figure;
        Hit = hit;
        Button = button;
    }

    public FigureModel Figure { get; }

    public FigureHit Hit { get; }

    public PointerButton Button { get; }
}
