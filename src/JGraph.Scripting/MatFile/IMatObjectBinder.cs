using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.MatFile;

/// <summary>
/// What the MAT-file reader asks of whoever owns the classes and the workspace when a file holds
/// an object or a function handle (V6, ADR 0167, appendix A #111 and #113). The reader knows the
/// bytes — a class name, the property values, the text of a handle and what it captured — and
/// nothing about how an instance is built or a handle re-made; the interpreter knows that and
/// nothing about the bytes. A reader with no binder refuses both kinds by name, as it always has.
/// </summary>
internal interface IMatObjectBinder
{
    /// <summary>
    /// A fresh instance of <paramref name="className"/> with every property at its default, before
    /// the saved values are set: the same order MATLAB loads in, which runs no constructor. An
    /// instance is made before its properties are read so that a handle whose property holds an
    /// alias of itself can be loaded. A class that cannot be found answers whatever the binder
    /// decides stands for the variable, after saying so.
    /// </summary>
    JgsValue NewObject(string className, bool deleted, string variable);

    /// <summary>Sets the saved property values on an instance <see cref="NewObject"/> made.</summary>
    void SetProperties(JgsValue instance, IReadOnlyDictionary<string, JgsValue> properties);

    /// <summary>
    /// A function handle re-made from what was saved: the text of an anonymous function with the
    /// variables it captured, or the name of a function (<paramref name="type"/> is
    /// <c>anonymous</c> or <c>simple</c>, as <c>functions</c> reports it).
    /// </summary>
    JgsValue Function(string text, string type, IReadOnlyDictionary<string, JgsValue>? workspace, string variable);
}
