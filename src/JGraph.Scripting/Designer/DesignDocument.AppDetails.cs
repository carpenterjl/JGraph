using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.Designer;

/// <content>
/// App Details and the app's help text (App Designer window plan W8a): <c>SetAppDetailsCommand</c>'s one step over the details,
/// the input arguments, the single instance and the thumbnail; the app row's edits; <c>HelpCommentsCommand</c>.
/// </content>
public sealed partial class DesignDocument
{
    private bool _detailsChanged;

    /// <summary>The app's details, read from its package the first time an <c>.mlapp</c>'s are asked for.</summary>
    public DesignPlainTextDetails AppDetails => Details ??= DetailsOf(Path, AppName);

    /// <summary>What App Details holds now.</summary>
    public DesignAppDetailsValues AppDetailsValues => AppDetails.Values;

    /// <summary>Whether an app's dialog holds a plain-text app's fields (<c>PlainTextAppDetailsDialogContent</c>): the author's longer limit and the thumbnail's source.</summary>
    public bool DetailsAsPlainText => Format == DesignFileFormat.PlainText;

    /// <summary>
    /// App Details' OK (<c>_handleOkButton</c>, <c>SetAppDetailsCommand</c>): whichever of the details, the input arguments and
    /// the single instance differ from what the app holds, as one step. A version that will not do leaves the one held; input
    /// arguments that will not do are left.
    /// </summary>
    public void SetAppDetails(DesignAppDetailsValues details, string? inputArguments, bool? singleInstance)
    {
        ArgumentNullException.ThrowIfNull(details);
        DesignAppDetailsValues held = AppDetailsValues;
        if (DesignAppDetails.WhyNotVersion(details.Version) is not null)
        {
            details = details with { Version = held.Version };
        }

        var edits = new List<Edit>();
        if (Code is not null && inputArguments is not null && WhyNotInputParameters(inputArguments) is null)
        {
            edits.AddRange(InputParameterEdits(SanitizeInputParameters(inputArguments)));
        }

        if (Code is not null && singleInstance is { } single && single != Code.SingleInstance)
        {
            edits.Add(new SingleInstanceEdit(Design.Figure, Code.SingleInstance, single));
        }

        if (details != held)
        {
            edits.Add(new DetailsEdit(Design.Figure, held, details));
        }

        if (edits.Count > 0)
        {
            EndGesture(commit: false);
            Do([.. edits]);
        }
    }

    /// <summary>One of the app row's text rows edited (<c>_InspectableRootNodeTranslator</c>).</summary>
    /// <returns>Null when set or nothing changed; else why not (<c>_handleVersionSet</c>).</returns>
    public string? SetAppDetail(string name, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        DesignAppDetailsValues held = AppDetailsValues;
        DesignAppDetailsValues edited;
        switch (name)
        {
            case nameof(DesignAppDetailsValues.Name):
                edited = held with { Name = text };
                break;
            case nameof(DesignAppDetailsValues.Version):
                if (DesignAppDetails.WhyNotVersion(text) is { } why)
                {
                    return why;
                }

                edited = held with { Version = text };
                break;
            case nameof(DesignAppDetailsValues.Author):
                edited = held with { Author = text };
                break;
            case nameof(DesignAppDetailsValues.Summary):
                edited = held with { Summary = text };
                break;
            case nameof(DesignAppDetailsValues.Description):
                edited = held with { Description = text };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, "App Details has no such row.");
        }

        if (edited != held)
        {
            EndGesture(commit: false);
            Do([new DetailsEdit(Design.Figure, held, edited)]);
        }

        return null;
    }

    /// <summary>The app's help text as its class holds it (<c>getHelpComments</c>, <c>HelpCommentsParser</c>).</summary>
    public (string Summary, string Description) HelpText => Code is null ? (string.Empty, string.Empty) : DesignHelpText.Parse(Code.HeadComments, Code.ClassName);

    /// <summary>
    /// The help text's OK (<c>HelpCommentsDialogController._handleOkButton</c>, <c>HelpCommentsCommand</c>): the description
    /// broken to the class's width, and written under the class line, as one step when it differs from what the class holds.
    /// </summary>
    public void SetHelpText(string summary, string description)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(description);
        if (Code is null)
        {
            return;
        }

        string wrapped = description.Length == 0 ? description : DesignHelpText.WrappedDescription(description, Style.Indent(1).Length);
        if ((summary, wrapped) == HelpText)
        {
            return;
        }

        EndGesture(commit: false);
        Do([new HelpTextEdit(Design.Figure, [.. Code.HeadComments], DesignHelpText.Lines(Code.ClassName, summary, wrapped, Style.Indent(1)))]);
    }

    /// <summary>What a save hands the package: its details when they were edited, else null, which keeps the package's.</summary>
    private MlappMetadata? DetailsForPackage() => _detailsChanged ? MetadataOf(Details) : null;

    /// <summary>App Details changed; the file is written from them at the next save.</summary>
    private sealed record DetailsEdit(DesignComponent Component, DesignAppDetailsValues Before, DesignAppDetailsValues After) : CodeEdit(Component)
    {
        public override void Do(DesignDocument document)
        {
            document.AppDetails.Values = After;
            document._detailsChanged = true;
        }

        public override void Undo(DesignDocument document)
        {
            document.AppDetails.Values = Before;
            document._detailsChanged = true;
        }
    }

    /// <summary>The help text under the class line changed.</summary>
    private sealed record HelpTextEdit(DesignComponent Component, IReadOnlyList<string> Before, IReadOnlyList<string> After) : CodeEdit(Component)
    {
        public override void Do(DesignDocument document) => Put(document, After);

        public override void Undo(DesignDocument document) => Put(document, Before);

        private static void Put(DesignDocument document, IReadOnlyList<string> lines)
        {
            document.Code!.HeadComments.Clear();
            document.Code!.HeadComments.AddRange(lines);
        }
    }
}
