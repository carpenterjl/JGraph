namespace JGraph.Scripting;

/// <summary>
/// The host's audio playback service, behind the <c>sound(y, fs)</c> builtin — mirroring
/// <see cref="IScriptFigureFiles"/> so headless hosts and tests can substitute a fake. Invoked on
/// the engine's background thread.
/// </summary>
public interface IScriptAudio
{
    /// <summary>
    /// Starts playing <paramref name="samples"/> (normalized to [-1, 1], clipped beyond) at
    /// <paramref name="sampleRate"/> Hz, without blocking — MATLAB's <c>sound</c> semantics; scripts
    /// wait with <c>pause(seconds)</c> when they need playback to finish.
    /// </summary>
    void Play(double[] samples, int sampleRate);
}
