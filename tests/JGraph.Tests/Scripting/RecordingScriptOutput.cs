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
}
