using System.IO;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Packages (the packages and class folders plan, stage P1, ADR 0307): a dotted chain of plain
/// names whose head is a <c>+package</c> folder on the search path names the package's member —
/// a function file, a script or a class file — and a call, a bare mention, a handle and a name
/// given as text all reach it. What R2025b measured and this follows: a variable of the head's
/// name wins over the package; a file <c>pkg.m</c> beside <c>+pkg</c> is another thing, and the
/// dotted name still reaches the package, even over a Constant property of a class <c>pkg</c>; a
/// function and a sub-package of one name coexist, the function answering unless another dot
/// follows; members never see each other by bare name; a package's <c>private/</c> is its
/// members' alone and never a sub-package; a bare dotted function name is a call.
/// </summary>
internal sealed partial class Interpreter
{
    /// <summary>
    /// Reads <paramref name="member"/> as a package member when it is one: the callable itself in
    /// callee position, and called with no arguments on a bare mention (<c>x = pkg.f</c> is the
    /// call <c>pkg.f()</c>, as a bare name is). False when the chain names no package member, so the
    /// other readings of a dot go ahead and a miss is refused by them with R2025b's words.
    /// </summary>
    private bool TryPackageInFront(MemberExpr member, JgsEnvironment env, bool autoCall, out JgsValue value)
    {
        value = JgsValue.Null;
        if (!TryPackageCallable(member, env, out IJgsCallable? callable))
        {
            return false;
        }

        if (!autoCall)
        {
            value = JgsValue.Function(callable);
            return true;
        }

        // A statement's bare mention asks for nothing (V9.1), which is how a script in a package
        // runs by its dotted name; anywhere else the mention is a value wanted, and a script refuses.
        value = ReferenceEquals(_bareStatement, member) && callable is IJgsMultiCallable asked
            ? FirstOrNull(asked.CallMultiple(System.Array.Empty<JgsValue>(), 0, member.Line, member.Column))
            : CallHeld(callable, System.Array.Empty<JgsValue>(), member.Line, member.Column);
        return true;
    }

    /// <summary>
    /// The package member a dotted chain of plain names stands for, or false: every link a literal
    /// name, the head bound to no variable (a variable wins, measured), the first part a package
    /// in some search folder, each middle part a sub-package of it, the last part a code file in
    /// the package — the first search folder holding one wins. Costs nothing in a session with
    /// no package anywhere, and one set lookup per search folder per segment in one that has.
    /// </summary>
    private bool TryPackageCallable(MemberExpr member, JgsEnvironment env, out IJgsCallable callable)
    {
        callable = null!;
        if (FunctionPath is not { } path || !path.Index.AnyPackages
            || DottedName(member) is not { } dotted || RootName(member) is not { } head)
        {
            return false;
        }

        if (_resolver.Lookup(head, env).Layer == ResolutionLayer.Bound)
        {
            return false;
        }

        if (!path.TryResolveDotted(dotted, out JgsValue value, out _, out _) || value.Type != JgsType.Function)
        {
            return false;
        }

        callable = value.AsCallable;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="name"/> is a local function of the file <paramref name="file"/> — a
    /// function the file holds that is not its main one — which R2025b's <c>dbstack</c> prints as
    /// <c>file&gt;name</c>.
    /// </summary>
    internal bool IsLocalFunctionOf(string file, string name) =>
        file.Length > 0 && _files.TryGetValue(file, out FunctionFile? storage)
        && storage.TryGet(name, out _) && !storage.IsMain(name);

    /// <summary>
    /// A frame's name as <c>dbstack</c> prints it (R2025b, measured in the packages plan's P1):
    /// qualified by the package its file sits in, a local function as <c>file&gt;name</c>
    /// (<c>pkg.stkloc&gt;inner</c>), a method as the class's own naming.
    /// </summary>
    internal string PrintedFrameName(string name, string file)
    {
        string prefix = JgsCodeTree.PackagePrefix(file);
        string stem = file.Length > 0 ? Path.GetFileNameWithoutExtension(file) : string.Empty;
        return prefix + (IsLocalFunctionOf(file, name) ? $"{stem}>{name}" : name);
    }
}
