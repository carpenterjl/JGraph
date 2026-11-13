using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.Designer;

/// <summary>What a Run of the designer's said: why it did not run, null when it did, and the error's identifier.</summary>
/// <param name="Message">Why the app did not run, or null.</param>
/// <param name="Identifier">The error's identifier, or null.</param>
public sealed record DesignRunOutcome(string? Message, string? Identifier)
{
    /// <summary>The identifier of an error in the arguments Run was given (<c>appdesigner.internal.appalert.AppArgumentException</c>'s kind), said in Run's drop-down, on its text field.</summary>
    public const string ArgumentError = "MATLAB:appdesigner:appdesigner:AppArgumentError";

    /// <summary>The identifier of the refusal that offers Change Folder.</summary>
    public const string ShadowedByCurrentFolder = "MATLAB:appdesigner:appdesigner:AppNameShadowedByCWD";
}

/// <content>
/// What an app runs with (App Designer window plan W8b): the argument sets (<c>RunArgumentsModel</c>, <c>DeleteRunCommandWithArgs</c>), and the thumbnail Run captures
/// (<c>runAppHelper</c>, <c>captureScreenshot</c>).
/// </content>
public sealed partial class DesignDocument
{
    private DesignRunArguments? _runArguments;
    private bool _runArgumentsChanged;

    // HasRunSinceLastSave: true for an app as opened, false once it is saved.
    private bool _ranSinceSave = true;

    /// <summary>The app's argument sets; an <c>.mlapp</c>'s are read from its model the first time they are asked for.</summary>
    public DesignRunArguments RunArguments => _runArguments ??= new DesignRunArguments(ReadRunArguments());

    private IReadOnlyList<string>? ReadRunArguments()
    {
        try
        {
            return Format == DesignFileFormat.Mlapp && Path is { } path && System.IO.File.Exists(path) ? JgsMlapp.ReadRunConfigurations(path) : null;
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary><paramref name="set"/> made what Run runs with (<c>setArgumentSet</c>): the app is modified when that changes what it keeps first; not an edit to undo, as R2025b's is not.</summary>
    public void UseRunArguments(string set)
    {
        if (RunArguments.Use(set))
        {
            _runArgumentsChanged = true;
            _textDirty = true;
            OnChanged();
        }
    }

    /// <summary><paramref name="set"/> taken out of the drop-down (<c>DeleteRunCommandWithArgs</c>): one step, whose Undo makes it the most recent again.</summary>
    public void RemoveRunArguments(string set)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (set.Length > 0 && RunArguments.All.Contains(set))
        {
            EndGesture(commit: false);
            Do([new RunArgumentsEdit(Design.Figure, set)]);
        }
    }

    /// <summary>Whether Run takes a picture of the app once it stands (<c>runAppHelper</c>): its thumbnail is automatic, and it has not run since it was saved.</summary>
    public bool WantsThumbnailCapture => !_ranSinceSave && AppDetailsValues.Mode == DesignScreenshotMode.Auto;

    /// <summary>The app has run (<c>HasRunSinceLastSave</c>).</summary>
    public void RanSinceSave() => _ranSinceSave = true;

    /// <summary>
    /// The picture Run took, written straight into the file (<c>captureScreenshot</c>), the rest of it as it is and the app not modified by it: an <c>.mlapp</c> keeps it as a
    /// PNG; a plain-text app as a JPEG fitted in its box, in the section <c>autoCapture='true'</c> says is automatic. Details held are brought along for the next save.
    /// </summary>
    /// <exception cref="System.IO.IOException">The file could not be written.</exception>
    /// <exception cref="InvalidDataException">The picture could not be read.</exception>
    public void TakeThumbnail(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (Path is null)
        {
            return;
        }

        string uri;
        if (Format == DesignFileFormat.PlainText)
        {
            string temporary = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
            try
            {
                System.IO.File.WriteAllBytes(temporary, png);
                uri = DesignAppDetails.UriForPlainText(temporary);
            }
            finally
            {
                System.IO.File.Delete(temporary);
            }

            string text = JgsSourceText.Read(Path);
            string written = DesignPlainText.WithAutoThumbnail(text, uri);
            if (written != text)
            {
                System.IO.File.WriteAllText(Path, written);
                if (Text == text)
                {
                    Text = written;
                }
            }
        }
        else
        {
            uri = DesignAppDetails.DataUri(png, "png");
            JgsMlapp.WriteThumbnail(Path, uri);
        }

        AppDetails.ThumbnailDataUri = uri;
        _renderedText = null;
    }

    /// <summary>What a save hands the package: the sets when they changed, else null, which keeps the model's.</summary>
    private IReadOnlyList<string>? RunArgumentsForPackage() => _runArgumentsChanged ? _runArguments?.All : null;

    /// <summary>What a save settles beside the text: details and sets are in the file, and the app has not run since.</summary>
    private void Saved()
    {
        _detailsChanged = false;
        _runArgumentsChanged = false;
        _ranSinceSave = false;
    }

    /// <summary>An argument set was taken out of the drop-down.</summary>
    private sealed record RunArgumentsEdit(DesignComponent Component, string Set) : CodeEdit(Component)
    {
        public override void Do(DesignDocument document)
        {
            document.RunArguments.Remove(Set);
            document._runArgumentsChanged = true;
        }

        public override void Undo(DesignDocument document)
        {
            document.RunArguments.Use(Set);
            document._runArgumentsChanged = true;
        }
    }
}
