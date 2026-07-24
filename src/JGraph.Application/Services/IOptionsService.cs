namespace JGraph.Application.Services;

/// <summary>Opens the Options dialog. Kept behind a seam so the view-model never news up a window.</summary>
public interface IOptionsService
{
    /// <summary>Shows the modal Options dialog over the active window.</summary>
    void ShowOptions();
}
