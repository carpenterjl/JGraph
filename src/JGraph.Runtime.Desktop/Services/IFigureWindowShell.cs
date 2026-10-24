namespace JGraph.Runtime.Desktop.Services;

/// <summary>
/// What a figure window offers beyond the figure itself, which depends on the program it is in
/// (compiler and SDK plan, stage K5). JGraph's own application opens its script editor and its
/// Options dialog from a figure's toolbar, and its settings name the theme a new figure starts in;
/// a deployed program has neither an editor nor settings, and its windows show neither button.
/// </summary>
public interface IFigureWindowShell
{
    /// <summary>The name of the figure theme a new figure window starts in, or null for the first registered one.</summary>
    string? PreferredFigureTheme { get; }

    /// <summary>Opens the script editor, or null where there is none.</summary>
    Action? OpenScript { get; }

    /// <summary>Opens the Options dialog, or null where there is none.</summary>
    Action? ShowOptions { get; }
}

/// <summary>The shell of a program that is not JGraph's application: no editor, no Options, no preferred theme.</summary>
public sealed class DeployedFigureWindowShell : IFigureWindowShell
{
    /// <summary>The one instance.</summary>
    public static DeployedFigureWindowShell Instance { get; } = new();

    private DeployedFigureWindowShell()
    {
    }

    /// <inheritdoc />
    public string? PreferredFigureTheme => null;

    /// <inheritdoc />
    public Action? OpenScript => null;

    /// <inheritdoc />
    public Action? ShowOptions => null;
}
