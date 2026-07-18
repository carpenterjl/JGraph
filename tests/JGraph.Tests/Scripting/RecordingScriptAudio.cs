using JGraph.Scripting;

namespace JGraph.Tests.Scripting;

/// <summary>A test double for <see cref="IScriptAudio"/> that records every Play call.</summary>
internal sealed class RecordingScriptAudio : IScriptAudio
{
    public List<(double[] Samples, int SampleRate)> Played { get; } = new();

    public void Play(double[] samples, int sampleRate) => Played.Add((samples, sampleRate));
}
