using JGraph.Scripting;

namespace JGraph.Tests.Scripting;

/// <summary>An <see cref="IScriptOutput"/> test double that records everything written to it.</summary>
internal sealed class RecordingScriptOutput : IScriptOutput
{
    public List<string> Normal { get; } = new();

    public List<string> Errors { get; } = new();

    public string NormalText => string.Concat(Normal);

    public string ErrorText => string.Concat(Errors);

    public void Write(string text) => Normal.Add(text);

    public void WriteLine(string text) => Normal.Add(text + "\n");

    public void WriteError(string text) => Errors.Add(text);

    /// <summary>The text written so far, as lines with the trailing newlines and any <c>\r</c> stripped.</summary>
    public IReadOnlyList<string> NormalLines =>
        NormalText.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private int _mark;

    /// <summary>Draws a line under everything written so far, so a later run can be asserted alone.</summary>
    public void Mark() => _mark = Normal.Count;

    /// <summary>The text written since the last <see cref="Mark"/> — one run's output, isolated.</summary>
    public string TextSinceMark => string.Concat(Normal.Skip(_mark));

    /// <summary>How many times <c>clc</c> cleared the display (the recorded text is kept for asserts).</summary>
    public int ClearCount { get; private set; }

    public void Clear() => ClearCount++;
}
