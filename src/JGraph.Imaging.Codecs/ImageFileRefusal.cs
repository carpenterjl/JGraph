namespace JGraph.Imaging.Codecs;

/// <summary>
/// A file a reader refuses in R2025b's words: the identifier and message its reader raises (open item
/// 344, ADR 0380). The scripting layer turns it into an error with that identifier.
/// </summary>
public sealed class ImageFileRefusal : IOException
{
    /// <summary>Creates a refusal with R2025b's identifier and message.</summary>
    public ImageFileRefusal(string identifier, string message)
        : base(message)
    {
        Identifier = identifier;
    }

    /// <summary>The identifier R2025b's reader raises, such as <c>MATLAB:imagesci:imbmpinfo:invalidBMPmetadata</c>.</summary>
    public string Identifier { get; }
}

/// <summary>A warning a reader gives as it reads, with R2025b's identifier and message (open item 344, ADR 0380).</summary>
/// <param name="Identifier">The warning's identifier.</param>
/// <param name="Text">The warning's message.</param>
public sealed record ImageFileWarning(string Identifier, string Text);
