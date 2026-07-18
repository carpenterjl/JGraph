using System.IO;
using System.Media;
using JGraph.Scripting;
using JGraph.Signal;

namespace JGraph.Application.Services;

/// <summary>
/// The app's <see cref="IScriptAudio"/>: renders the samples to an in-memory 16-bit PCM WAV stream
/// and plays it with <see cref="SoundPlayer"/> — dependency-free, MATLAB <c>sound</c> semantics
/// (non-blocking; a new call replaces the current playback). The player and its stream are kept
/// alive for the duration of playback.
/// </summary>
public sealed class AppScriptAudio : IScriptAudio
{
    private SoundPlayer? _player;
    private MemoryStream? _stream;

    /// <inheritdoc />
    public void Play(double[] samples, int sampleRate)
    {
        var stream = new MemoryStream();
        WaveFile.Write16BitPcm(stream, samples, sampleRate);
        stream.Position = 0;

        var player = new SoundPlayer(stream);
        Swap(player, stream).Play();
    }

    private SoundPlayer Swap(SoundPlayer player, MemoryStream stream)
    {
        SoundPlayer? previous;
        MemoryStream? previousStream;
        lock (this)
        {
            previous = _player;
            previousStream = _stream;
            _player = player;
            _stream = stream;
        }

        previous?.Stop();
        previous?.Dispose();
        previousStream?.Dispose();
        return player;
    }
}
