namespace JGraph.Application.Theming;

/// <summary>
/// One application theme: its stable id, the name shown in Options, and the resource dictionary
/// that carries its values.
/// </summary>
/// <param name="Id">
/// The identifier persisted in the user's settings. It must never change — a rename silently
/// resets every user's theme back to the default on their next launch.
/// </param>
/// <param name="Name">The display name, e.g. "Dark".</param>
/// <param name="IsDark">
/// Whether this is a dark theme. Consumers that cannot read our brushes — AvalonDock's own chrome,
/// the editor's syntax-highlighting definitions — need this one bit to pick their variant.
/// </param>
/// <param name="Source">The pack URI of the theme's <c>ResourceDictionary</c>.</param>
public sealed record AppThemeDescriptor(string Id, string Name, bool IsDark, Uri Source);
