namespace JGraph.Scripting;

/// <summary>What kind of input a waiting verb was released by.</summary>
public enum ScriptInputKind
{
    /// <summary>Nothing yet.</summary>
    None,

    /// <summary>A key went down.</summary>
    Key,

    /// <summary>A mouse button went down.</summary>
    Button,
}

/// <summary>One thing the window reported, as a verb that waits for input sees it.</summary>
/// <param name="Kind">A key or a button.</param>
/// <param name="Character">The character a key produced, or empty.</param>
/// <param name="Button">1 left, 2 middle, 3 right; 0 for a key.</param>
/// <param name="X">Where the pointer was, in figure pixels, counting from the left.</param>
/// <param name="Y">Where the pointer was, in figure pixels, counting down from the top.</param>
public readonly record struct ScriptInput(
    ScriptInputKind Kind, string Character, int Button, double X, double Y);

/// <summary>
/// The last press the interface reported, and a count of how many there have been. This is what
/// <c>pause</c>, <c>waitforbuttonpress</c> and <c>ginput</c> wait on.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately <em>beside</em> <see cref="ScriptEventQueue"/> rather than inside it. That queue
/// carries work for callbacks and only ever holds an event some object has a callback for — which is
/// what makes an unscripted window cost nothing. A verb that waits for a key needs to hear the key
/// whether or not anybody has a <c>KeyPressFcn</c>, so it cannot read that queue, and putting it
/// there would mean queueing events nothing will run.
/// </para>
/// <para>
/// A counter rather than a flag, because the question a waiting verb asks is "has anything happened
/// <em>since I started</em>" — a flag would let a press from before the call release it, and clearing
/// one first would race the interface thread that sets it.
/// </para>
/// </remarks>
public static class ScriptInputWatch
{
    private static readonly object Gate = new();
    private static ScriptInput _last;
    private static long _count;

    /// <summary>How many presses have been reported since the process started.</summary>
    public static long Count
    {
        get { lock (Gate) { return _count; } }
    }

    /// <summary>
    /// The most recent press, and the count at which it happened. A verb that noted the count before
    /// it began waiting compares against this one to know whether what it is looking at is new.
    /// </summary>
    public static (ScriptInput Input, long Count) Latest
    {
        get { lock (Gate) { return (_last, _count); } }
    }

    /// <summary>Records a press. Called by the interface, on the interface's own thread.</summary>
    public static void Record(ScriptInput input)
    {
        lock (Gate)
        {
            _last = input;
            _count++;
        }
    }

    /// <summary>Forgets everything — run start, and between tests.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _last = default;
            _count = 0;
        }
    }
}
