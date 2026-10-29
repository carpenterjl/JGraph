namespace JGraph.Scripting.Designer;

/// <summary>What an item of an app's document tab menu does.</summary>
public enum DesignTabMenuCommand
{
    /// <summary>Makes the app's folder the current folder.</summary>
    ChangeCurrentFolder,

    /// <summary>Adds the app's folder to the search path.</summary>
    AddToSearchPath,

    /// <summary>Removes the app's folder from the search path.</summary>
    RemoveFromSearchPath,

    /// <summary>Shows the app's folder in Explorer.</summary>
    ShowInExplorer,

    /// <summary>Copies the app's full file name to the clipboard.</summary>
    CopyFullPath,

    /// <summary>Opens the Start Page for a new app.</summary>
    NewApp,

    /// <summary>Compares the app to another file.</summary>
    CompareTo,

    /// <summary>Closes the app.</summary>
    Close,

    /// <summary>Closes every other app.</summary>
    CloseAllExcept,

    /// <summary>Closes the apps whose tabs are to the right of this one.</summary>
    CloseAllToTheRight,

    /// <summary>Closes every app.</summary>
    CloseAll,

    /// <summary>Puts the tabs in the order of their titles.</summary>
    Alphabetize,
}

/// <summary>An item of a document tab's menu; a null <see cref="Command"/> is a separator.</summary>
/// <param name="Command">What it does, or null for a separator.</param>
/// <param name="Text">Its words.</param>
/// <param name="Enabled">Whether it can be chosen.</param>
public sealed record DesignTabMenuItem(DesignTabMenuCommand? Command, string Text, bool Enabled = true)
{
    /// <summary>A separator.</summary>
    public static DesignTabMenuItem Separator { get; } = new(null, string.Empty, false);
}

/// <summary>
/// The context menu of an app's document tab (App Designer window plan, stage W2), as R2025b builds
/// it: <c>AppTabActions</c> gives a saved app's items - Change Current Folder to (disabled when it is
/// the current folder already), Add to or Remove from Search Path, Show in Explorer, Copy Full Path
/// to Clipboard - then New App and Compare To for every app; the document container
/// (<c>MW/layout/DocumentContainer._populateMenu</c>) adds a separator, Close, and with more than
/// one document Close All Except, Close All to the Right (when there are tabs to the right) and Close
/// All, then a separator and Alphabetize. The container's Maximize, Undock, tiling and tab-position
/// items are left out: JGraph's window has none of them.
/// </summary>
public static class DesignTabMenu
{
    /// <summary>The items for the tab of <paramref name="fullFileName"/> (null for an app never saved), the <paramref name="index"/>th of <paramref name="count"/> tabs.</summary>
    /// <param name="fullFileName">The app's file, or null.</param>
    /// <param name="title">The tab's title, for Close and Close All Except.</param>
    /// <param name="index">The tab's place, from 0.</param>
    /// <param name="count">How many tabs there are.</param>
    /// <param name="isCurrentFolder">Whether a folder is the session's current folder.</param>
    /// <param name="isOnPath">Whether a folder is on the session's search path.</param>
    public static IReadOnlyList<DesignTabMenuItem> Items(string? fullFileName, string title, int index, int count,
        Func<string, bool> isCurrentFolder, Func<string, bool> isOnPath)
    {
        ArgumentNullException.ThrowIfNull(isCurrentFolder);
        ArgumentNullException.ThrowIfNull(isOnPath);
        List<DesignTabMenuItem> items = [];
        if (fullFileName is not null && Path.GetDirectoryName(fullFileName) is { Length: > 0 } folder)
        {
            string shown = Abbreviate(folder);
            items.Add(new(DesignTabMenuCommand.ChangeCurrentFolder, $"Change Current Folder to {shown}", !isCurrentFolder(folder)));
            items.Add(isOnPath(folder)
                ? new(DesignTabMenuCommand.RemoveFromSearchPath, $"Remove {shown} from Search Path")
                : new(DesignTabMenuCommand.AddToSearchPath, $"Add {shown} to Search Path"));
            items.Add(new(DesignTabMenuCommand.ShowInExplorer, "Show in Explorer"));
            items.Add(new(DesignTabMenuCommand.CopyFullPath, "Copy Full Path to Clipboard"));
        }

        items.Add(new(DesignTabMenuCommand.NewApp, "New App"));
        items.Add(new(DesignTabMenuCommand.CompareTo, "Compare To"));
        items.Add(DesignTabMenuItem.Separator);
        items.Add(new(DesignTabMenuCommand.Close, $"Close {title}"));
        if (count > 1)
        {
            items.Add(new(DesignTabMenuCommand.CloseAllExcept, $"Close All Except {title}"));
            if (index < count - 1)
            {
                items.Add(new(DesignTabMenuCommand.CloseAllToTheRight, "Close All to the Right"));
            }

            items.Add(new(DesignTabMenuCommand.CloseAll, "Close All"));
            items.Add(DesignTabMenuItem.Separator);
            items.Add(new(DesignTabMenuCommand.Alphabetize, "Alphabetize"));
        }

        return items;
    }

    /// <summary>
    /// A folder as the menu names it, R2025b's <c>PlatformUtil.abbreviateAppFilePath</c>: whole when
    /// shorter than 30 characters; else its first parts while they stay under 15 characters, "...",
    /// and its last parts while they stay under 15.
    /// </summary>
    public static string Abbreviate(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (folder.Length < 30)
        {
            return folder;
        }

        char separator = folder.Contains('\\') ? '\\' : '/';
        var parts = new List<string>(folder.Split(separator));
        string head = parts[0];
        parts.RemoveAt(0);
        while (parts.Count > 0 && head.Length + parts[0].Length < 15)
        {
            head = head + separator + parts[0];
            parts.RemoveAt(0);
        }

        head += separator;
        string tail = string.Empty;
        if (parts.Count > 0)
        {
            tail = parts[^1];
            parts.RemoveAt(parts.Count - 1);
        }

        while (parts.Count > 0 && tail.Length + parts[^1].Length < 15)
        {
            tail = parts[^1] + separator + tail;
            parts.RemoveAt(parts.Count - 1);
        }

        return head + "..." + separator + tail;
    }
}
