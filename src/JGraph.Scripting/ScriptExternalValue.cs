namespace JGraph.Scripting;

/// <summary>
/// The <see cref="ScriptVariable.RawValue"/> of a variable that holds a value from outside the
/// language (interop plan, stage 10, ADR 0183): a .NET object, a <c>lib.pointer</c>, a libstruct. It
/// has no grid to show, so a host's data viewer says what it is instead of opening it.
/// </summary>
/// <param name="ClassName">What <c>class</c> answers: <c>System.Text.StringBuilder</c>, <c>lib.pointer</c>.</param>
/// <param name="Summary">The short text the Workspace pane shows after the size and class, or null when
/// the class is all there is to say. Made without running any of the object's own code.</param>
/// <param name="Kind">What family the value is from: ".NET object", "C library value".</param>
public sealed record ScriptExternalValue(string ClassName, string? Summary, string Kind);
