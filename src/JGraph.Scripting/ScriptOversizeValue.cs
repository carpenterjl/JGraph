namespace JGraph.Scripting;

/// <summary>
/// What a variable too large to copy for the data viewer carries in place of its value (open item
/// 19): the host can say "too large" from the value rather than guess from the class name, which
/// says <c>double</c> for an array and <c>double</c> for a scalar alike.
/// </summary>
/// <param name="Elements">How many elements the value holds.</param>
public sealed record ScriptOversizeValue(long Elements);
