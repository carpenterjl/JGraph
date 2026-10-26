using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V1.3's tripwire (ADR 0162): no raw write into a payload, and no wrapper minted over one, except
/// where this list says why.
/// </summary>
/// <remarks>
/// M7 puts the holder check inside the setters, so a write that reaches storage another way is
/// exactly the hole the model cannot see. The spellings scanned here are the ones V1.1's audit
/// found: an indexed store through an accessor, a span store through a buffer or a plane, a
/// scatter into a payload, and a factory fed another value's payload. Each line that remains is
/// allowed by name and by reason, and the list is checked in both directions — an entry that no
/// longer matches any line fails too, so it cannot rot into permission for something else.
/// <para>
/// This is the fast guard that runs with the suite, and it reads one line at a time.
/// `tools/ownership/audit-ownership.py` is the census that follows a payload through the locals
/// that hold it; the gate runs both.
/// </para>
/// </remarks>
public class OwnershipGateTests
{
    private static readonly Regex[] Rules =
    [
        // An indexed store straight through an accessor.
        new(@"\.(AsArray|AsCell|AsStruct|Fields|BoxedElements\(\))\s*\[[^\]]*\]\s*(?<op>[-+*/|&^]?=)(?!=)"),

        // A store through a struct array's element dictionary.
        new(@"\.Elements\s*\[[^\]]*\]\s*\[[^\]]*\]\s*=(?!=)"),

        // A span store into a buffer or a complex plane.
        new(@"\.(AsBuffer|Re|Im)\s*\.AsSpan\(\)\s*\[[^\]]*\]\s*(?<op>[-+*/|&^]?=)(?!=)"),

        // A scatter or fill whose destination is reached through an accessor. These four take the
        // destination first; a kernel whose destination sits elsewhere in the argument list is the
        // Python audit's business, which reads each signature rather than guessing.
        new(@"PackedMath\.(Scatter|ScatterConstant|Fill|FillConstant)\(\s*[A-Za-z_][\w.]*\.(AsBuffer|AsPackedComplex)\b"),

        // A wrapper or payload class minted straight over another value's payload.
        new(@"(JgsValue\.(Packed|Shaped|PackedComplexArray|Cell|StructArray|Object|Array)|new\s+(JgsStructArray|JgsPackedComplex))\(\s*[A-Za-z_][\w.]*\.(AsBuffer|AsCell|AsArray|AsStruct|AsStructArray|AsPackedComplex|BoxedElements\(\)|Elements|Fields)\s*[,)]"),

        // V2.3 (ADR 0163): a workspace binding whose value is neither a fresh computation nor a
        // share. An entry holds a wrapper of its own (M2), so a Declare or TryAssign whose value
        // is not visibly minted (a JgsValue factory, an exception, an empty), copied
        // (CopyForBinding) or shared (Share) is a road the model has to explain by name.
        new(@"\.(Declare|DeclareFunction|TryAssign)\((?![^;]*(CopyForBinding\(|JgsValue\.\w+\(|MakeException\(|EmptyBracket\(|JgsMatrix\.\w+\())"),
    ];

    /// <summary>The lines allowed to write raw, each with the reason the model is not broken by it.</summary>
    private static readonly (string File, string Line, string Why)[] Allowed =
    [
        ("JgsValue.cs",
         "planes.Re.AsSpan()[index] = value.Real;",
         "inside SetPackedComplex, after Detach: this is M7's gate, not a road past it"),
        ("JgsValue.cs",
         "planes.Im.AsSpan()[index] = value.Imaginary;",
         "inside SetPackedComplex, after Detach: this is M7's gate, not a road past it"),
        ("JgsValue.cs",
         "copy.Fields[name] = Share(held);",
         "inside PrivateCopy: the fields being filled are the copy's own, and each child is shared"),
        ("JgsObjectArray.cs",
         "copy.Fields[name] = JgsValue.Share(held);",
         "inside SharedCopy: a fresh instance's own fields, each child shared, as JgsStructArray.SharedCopy fills a copy (open item 65)"),
        ("Interpreter.ObjectArrays.cs",
         "local.Declare(statement.Variable, bound);",
         "a for variable over an array of objects: a counted share of the element, or a column array built for the pass (open item 65)"),
        ("Interpreter.ExternalArrays.cs",
         "local.Declare(statement.Variable, ExternalArrays.Value(array.Build(elements, array.Rows, 1)));",
         "a for variable over an array of external values: a fresh column built for the pass, or the one device handle it holds, wrapped on the way out (open item 36)"),
        ("JgsClass.cs",
         "instance.Fields[property.Spec.Name] = DefaultOf(property, line, col);",
         "a fresh instance's defaults, before anything can hold it"),
        ("JgsBuiltinClasses.cs",
         "clone.Fields[name] = copied;",
         "copy of a matlab.mixin.Copyable object: the clone's own fields, each a share of the source's or a fresh default (U6)"),
        ("JgsClass.cs",
         "instance.Fields[JgsBuiltins.EventNameField] = JgsValue.Str(string.Empty);",
         "event.EventData's inherited EventName, on the fresh instance two statements above (V6, #106)"),
        ("JgsClass.cs",
         "instance.Fields[JgsBuiltins.EventSourceField] = JgsMatrix.FromColumnMajor([], 0, 0);",
         "event.EventData's inherited Source, on the same fresh instance; the empty is minted here (V6, #106)"),
        ("JgsClass.cs",
         "clone.Fields[name] = interpreter.CopyForBinding(value);",
         "the clone's own fields; the source is untouched"),
        ("JgsWorkspaceIo.cs",
         "target.Fields[name] = target.Class.Check(property, value, line, col);",
         "a loaded object's saved property values, on the fresh instance load minted before reading them; each value is fresh from the file (V6, #111)"),
        ("JgsWorkspaceIo.cs",
         "defining.Declare(name, value);",
         "a loaded handle's captured workspace, declared into a static workspace minted for it; each value is fresh from the file (V6, #113)"),
        ("JgsBuiltins.Classes.cs",
         "built.AsStruct[\"cause\"] = column;",
         "the MException struct was built in this method, and the column is a cell minted here over shares of the causes (V6)"),
        ("Interpreter.cs",
         "handler.Declare(name, caught);",
         "the catch variable: an MException minted two lines above, by MakeException or by CaughtException over shares (V6)"),
        ("JgsBuiltins.Video.cs",
         "writer.AsStruct[\"FrameCount\"] = JgsValue.Number(0);",
         "a videoWriter is a handle: its counters are meant to be seen through every name (V2 states it)"),
        ("JgsBuiltins.Video.cs",
         "writer.AsStruct[\"Duration\"] = JgsValue.Number(0);",
         "a videoWriter is a handle (V2 states the contract)"),
        ("JgsBuiltins.Video.cs",
         "writer.AsStruct[\"Height\"] = JgsValue.Number(read.Height);",
         "a videoWriter is a handle (V2 states the contract)"),
        ("JgsBuiltins.Video.cs",
         "writer.AsStruct[\"Width\"] = JgsValue.Number(read.Width);",
         "a videoWriter is a handle (V2 states the contract)"),
        ("JgsBuiltins.Video.cs",
         "writer.AsStruct[\"FrameCount\"] = JgsValue.Number(encoder.FrameCount);",
         "a videoWriter is a handle (V2 states the contract)"),
        ("JgsBuiltins.Video.cs",
         "writer.AsStruct[\"Duration\"] = JgsValue.Number(encoder.FrameCount / FieldNumber(writer, \"FrameRate\"));",
         "a videoWriter is a handle (V2 states the contract)"),

        // --- V2.3: the bindings (Declare / TryAssign) whose value is not visibly fresh or shared ---
        ("Interpreter.HotLoop.cs",
         "if (!env.TryAssign(vslots[i].Name, value))",
         "a compiled loop's vector register, minted by the loop (ADR 0160) and written back once"),
        ("Interpreter.HotLoop.cs",
         "env.Declare(vslots[i].Name, value);",
         "as above: the loop's own register"),
        ("Interpreter.HotLoop.cs",
         "env.Declare(slots[i].Name, value);",
         "a compiled loop's scalar register: a fresh number"),
        ("Interpreter.HotLoop.cs",
         "else if (!env.TryAssign(slots[i].Name, value))",
         "as above: a fresh number"),
        ("JgsEnvironment.cs",
         "slots.Declare(name, value);",
         "a declaration forwarded to the persistent's slot (M9): the caller's store, classified where it is made"),
        ("JgsEnvironment.cs",
         "this.Declare(name, value); // a nested function's own output, as the write's frame holds it",
         "a nested function's first write of its output, declared in its own frame with the wrapper the write hands in (V7)"),
        ("JgsEnvironment.cs",
         "target.Declare(name, value); // the parent's variable, as the write's own frame would hold it",
         "a nested function's write of a shared name, forwarded to the parent's frame with the wrapper the write's own frame would have declared (V7)"),
        ("JgsEnvironment.cs",
         "slots.Declare(name, value); // the slot is the binding, held or cleared",
         "an assignment forwarded to the persistent's slot (M9): the caller's store, classified where it is made"),
        ("Interpreter.cs",
         "env.Declare(let.Name, letValue);",
         "a JGS let: reference semantics (M17), the binding is the value's only holder"),
        ("Interpreter.cs",
         "env.Declare(destructure.Names[n], answers[n]);",
         "a JGS destructuring let over a call's answers (M17)"),
        ("Interpreter.cs",
         "env.Declare(destructure.Names[n], part);",
         "a JGS destructuring let over a tuple's parts (M17)"),
        ("Interpreter.cs",
         "env.Declare(\"ans\", bound);",
         "BindAns: `bound` is the adopted minted answer or CopyForBinding's share, decided one line above"),
        ("Interpreter.cs",
         "if (!scope.TryAssign(name, value))",
         "Rebind: every caller hands in a value it rebuilt (a time or table property, a grown cell, an atomic swap)"),
        ("Interpreter.cs",
         "scope.Declare(name, value);",
         "Rebind, as above"),
        ("Interpreter.cs",
         "local.Declare(statement.Variable, element);",
         "a for variable: the column the walk built, CopyForBinding's share of an element, or a stepped number"),
        ("Interpreter.cs",
         "if (!scope.TryAssign(variable.Name, stored))",
         "EvaluateAssign: `stored` is the adopted minted value or CopyForBinding's share, decided above"),
        ("Interpreter.cs",
         "scope.Declare(variable.Name, stored);",
         "EvaluateAssign, as above"),
        ("Interpreter.cs",
         "conjuredScope.Declare(conjuredName, EmptyOfKind(rhs));",
         "an empty conjured for a first indexed write"),
        ("Interpreter.Composite.cs",
         "scratch.Declare(LevelSlot, start);",
         "a fresh element of the struct array a new level belongs to (NewElement), in the scratch slot the rest of the path is written into (V6)"),
        ("Interpreter.TextWrites.cs",
         "scratch.Declare(LevelSlot, current);",
         "a char row minted as the 1-by-n char matrix it is, in the scratch slot the array roads grow (V6)"),
        ("Interpreter.Writes.cs",
         "scratch.Declare(LevelSlot, OneElementArray(scalar));",
         "a scalar an entry holds, minted as the one-by-one array it reads as, in the scratch slot the write grows (V6)"),
        ("Interpreter.Tables.cs",
         "scratch.Declare(LevelSlot, current);",
         "a table variable, fresh from its getter, in the scratch slot a brace write modifies and the rebuild takes back (V6)"),
        ("Interpreter.ObjectArrays.cs",
         "scratch.Declare(LevelSlot, standIn);",
         "a stand-in of an object array's positions, minted for the write, in the scratch slot the numeric road writes (open item 121, ADR 0233)"),
        ("Interpreter.Composite.cs",
         "scratch.Declare(LevelSlot, slotValue);",
         "a computed level's value in the scratch slot the write modifies and its setter takes back (V6): fresh from its getter, or a share of what storage holds (a dictionary's cell value), which the slot's first write copies"),
        ("Interpreter.cs",
         "env.TryAssign(variable.Name, updated); // TryGet succeeded, so the binding exists",
         "a fresh number"),
        ("Interpreter.cs",
         "ScopeOf(variable.Name, env).Declare(variable.Name, created);",
         "a fresh empty struct"),
        ("Interpreter.cs",
         "ScopeOf(variable.Name, env).Declare(variable.Name, target);",
         "a fresh empty cell"),
        ("JgsBuiltinLayer.cs",
         "_root.Declare(name, value);",
         "a built-in constant registered once into the sealed layer"),
        ("JgsBuiltinLayer.cs",
         "_root.DeclareFunction(name, value);",
         "a built-in registered once into the sealed layer: a callable is immutable"),
        ("Interpreter.cs",
         "env.DeclareFunction(",
         "a hoisted function definition: a callable is immutable"),
        ("JgsClass.cs",
         "local.Declare(output, built);",
         "the object a constructor is building: fresh, what a superclass's constructor left in a frame that has ended, or a share the caller took (U6)"),
        ("JgsClass.cs",
         "scope.DeclareFunction(Name, _constructor);",
         "a class constructor bound by name: a callable is immutable"),
        ("JgsCallable.cs",
         "snapshot.DeclareFunction(name, value);",
         "a captured function binding: a callable is immutable"),
        ("JgsWorkspaceIo.cs",
         "environment.Declare(name, value);",
         "load: the reader's fresh value, which the workspace adopts and the returned struct shares"),
        ("JgsWorkspaceIo.cs",
         "environment.Declare(name, value);",
         "load of a text file: a fresh matrix"),
        ("JgsBuiltinClasses.Style.cs",
         "copy.Fields[field] = held;",
         "uistyle: a fresh value object being built from another's fields, which are shares (U9)"),
        ("JgsBuiltinClasses.Style.cs",
         "copy.Fields[name] = StyleValueOf(name, args[1], line, col);",
         "uistyle: a coerced property value into the fresh object the constructor is building (U9)"),
        ("JgsBuiltins.Trees.cs",
         "style.Fields[name] = JgsBuiltinClasses.StyleValueOf(name, value, line, col);",
         "a style's native setter: a coerced value into the object's own field (U9)"),
        ("JgsBuiltins.Trees.cs",
         "copy.Fields[name] = JgsValue.Share(held);",
         "addStyle: the style copied by value into a fresh object the component keeps (U9)"),
        ("Interpreter.cs",
         "env.Declare(spec.Name, checked_); // a declared class the value did not have yet, or the default",
         "an arguments line's value: DeclaredArgument.Check's answer (the bound value or a fresh conversion of it) or the default just evaluated (K9)"),
        ("Interpreter.cs",
         "element.Declare(spec.Name, elements[k]);",
         "one element of a repeating group, bound in a scratch frame for its validators only (K9)"),
        ("Interpreter.cs",
         "frame.Declare(spec.Name, checked_);",
         "an output's checked value, the assigned value or a fresh conversion of it, back into the frame it came from (K9)"),
        ("JgsArgumentBinding.cs",
         "frame.Declare(signature.Positional[i], copy(arguments[i]));",
         "a positional argument bound by the binder: its copy is the interpreter's CopyForBinding, handed in as a delegate (K9)"),
        ("JgsArgumentBinding.cs",
         "frame.Declare(signature.Positional[p], copy(arguments[i]));",
         "a positional argument bound by the binder: its copy is the interpreter's CopyForBinding, handed in as a delegate (K9)"),
        ("JgsBuiltinClasses.Compiler.cs",
         "copy.Fields[field] = held;",
         "Copied: a fresh options object built from another's fields for a setter to change, as uistyle's copy is (K6)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "options.Fields[name] = JgsValue.Str(Absolute(file));",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "options.Fields[JgsBuiltinClasses.GivenExecutableName] = JgsValue.Str(given);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "options.Fields[JgsBuiltinClasses.GivenOutputDir] = JgsValue.Str(given == \".\" || given.StartsWith(@\".\\\", StringComparison.Ordinal) ? given : absolute);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "options.Fields[name] = JgsValue.Str(ScalarText(value, name, line, col));",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "options.Fields[name] = JgsValue.Str(absolute);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "options.Fields[name] = JgsValue.Str(\"\");",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "options.Fields[name] = Switch(value, name, OptionsClassName, line, col);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "options.Fields[name] = Switch(value, name, \"compiler.internal.build.DependencyOptions\", line, col);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "options.Fields[name] = Switch(value, name, \"compiler.internal.build.AllOptions\", line, col);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "options.Fields[name] = ItemList(value, name, here, line, col);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "options.Fields[name] = EncryptionKey(value, MustExist, Absolute, line, col);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "options.Fields[name] = SupportPackageList(value, line, col);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "options.Fields[name] = delivery switch",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "dependencies.Fields[\"Required\"] = JgsValue.Table(new JGraph.Data.Table(",
         "the Results or Dependencies object compiler.build is minting, before anything can hold it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "dependencies.Fields[\"Optional\"] = JgsValue.Table(new JGraph.Data.Table([]));",
         "the Results or Dependencies object compiler.build is minting, before anything can hold it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "results.Fields[\"BuildType\"] = JgsValue.Str(windowed ? \"standaloneWindowsApplication\" : \"standaloneApplication\");",
         "the Results or Dependencies object compiler.build is minting, before anything can hold it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "results.Fields[\"Files\"] = OptionCellColumn(files);",
         "the Results or Dependencies object compiler.build is minting, before anything can hold it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "results.Fields[\"IncludedSupportPackages\"] = EmptyCellValue();",
         "the Results or Dependencies object compiler.build is minting, before anything can hold it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "results.Fields[\"Options\"] = JgsValue.Object(options);",
         "the Results or Dependencies object compiler.build is minting, before anything can hold it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "results.Fields[\"RuntimeDependencies\"] = JgsValue.Object(dependencies);",
         "the Results or Dependencies object compiler.build is minting, before anything can hold it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "results.Fields[\"BuildID\"] = JgsValue.StringArray([JgsValue.Str(uuid)], 1, 1);",
         "the Results or Dependencies object compiler.build is minting, before anything can hold it (K6, K8)"),
        ("JgsBuiltins.CompilerBuild.cs",
         "results.Fields[\"BuildDuration\"] = JgsValue.Number(seconds);",
         "the Results or Dependencies object compiler.build is minting, before anything can hold it (K6, K8)"),
        ("JgsBuiltins.CompilerDotNet.cs",
         "options.Fields[\"ClassMap\"] = map;",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerDotNet.cs",
         "options.Fields[JgsBuiltinClasses.InterfaceVersion] = JgsValue.Str(\"1.0.0.0\");",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerDotNet.cs",
         "options.Fields[name] = map;",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerDotNet.cs",
         "options.Fields[name] = Switch(value, name, DotNetOptionsClassName, line, col);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerDotNet.cs",
         "options.Fields[name] = JgsValue.Str(ScalarText(value, name, line, col));",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerDotNet.cs",
         "options.Fields[name] = EmptyCellValue();",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerDotNet.cs",
         "options.Fields[name] = OptionCellColumn([.. files.Select(Absolute)]);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerDotNet.cs",
         "options.Fields[name] = JgsValue.Str(\"\");",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerDotNet.cs",
         "options.Fields[name] = JgsValue.Str(Absolute(file));",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerDotNet.cs",
         "options.Fields[JgsBuiltinClasses.GivenAssemblyName] = JgsValue.Str(text);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerDotNet.cs",
         "options.Fields[JgsBuiltinClasses.InterfaceVersion] = JgsValue.Str(text);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerDotNet.cs",
         "options.Fields[JgsBuiltinClasses.InterfaceEmbed] = Switch(value, name, \"compiler.internal.build.InterfaceManager\", line, col);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerDotNet.cs",
         "options.Fields[JgsBuiltinClasses.InterfaceValue] = JgsValue.Str(member);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K6, K8)"),
        ("JgsBuiltins.CompilerDotNet.cs",
         "results.AsObject.Fields[\"BuildType\"] = JgsValue.Str(\"dotNETAssembly\");",
         "the Results or Dependencies object compiler.build is minting, before anything can hold it (K6, K8)"),
        ("JgsBuiltins.CompilerC.cs",
         "options.Fields[name] = FunctionFileList(value, here, line, col);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K11)"),
        ("JgsBuiltins.CompilerC.cs",
         "options.Fields[JgsBuiltinClasses.GivenLibraryName] = JgsValue.Str(CliName(value, name, line, col));",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K11)"),
        ("JgsBuiltins.CompilerC.cs",
         "options.Fields[name] = JgsValue.Str(ScalarText(value, name, line, col));",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K11)"),
        ("JgsBuiltins.CompilerC.cs",
         "options.Fields[name] = Switch(value, name, CLibraryOptionsClassName, line, col);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K11)"),
        ("JgsBuiltins.CompilerC.cs",
         "options.Fields[name] = Switch(value, name, \"compiler.internal.build.SharedLibraryOptions\", line, col);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K11)"),
        ("JgsBuiltins.CompilerC.cs",
         "results.AsObject.Fields[\"BuildType\"] = JgsValue.Str(\"cSharedLibrary\");",
         "the Results or Dependencies object compiler.build is minting, before anything can hold it (K11)"),
        ("JgsBuiltins.CompilerCpp.cs",
         "options.Fields[name] = files;",
         "a compiler options object's own field, written by its constructor or setter with the file list ItemList minted for it (K12)"),
        ("JgsBuiltins.CompilerCpp.cs",
         "options.Fields[JgsBuiltinClasses.GivenLibraryName] = JgsValue.Str(CliName(value, name, line, col));",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K12)"),
        ("JgsBuiltins.CompilerCpp.cs",
         "options.Fields[JgsBuiltinClasses.InterfaceVersion] = JgsValue.Str(text);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K12)"),
        ("JgsBuiltins.CompilerCpp.cs",
         "options.Fields[JgsBuiltinClasses.InterfaceValue] = JgsValue.Str(member);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K12)"),
        ("JgsBuiltins.CompilerCpp.cs",
         "options.Fields[name] = EmptyCellValue();",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K12)"),
        ("JgsBuiltins.CompilerCpp.cs",
         "options.Fields[name] = OptionCellColumn([.. files.Select(Absolute)]);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K12)"),
        ("JgsBuiltins.CompilerCpp.cs",
         "options.Fields[name] = Switch(value, name, \"compiler.internal.build.SharedLibraryOptions\", line, col);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K12)"),
        ("JgsBuiltins.CompilerCpp.cs",
         "results.AsObject.Fields[\"BuildType\"] = JgsValue.Str(\"cppSharedLibrary\");",
         "the Results or Dependencies object compiler.build is minting, before anything can hold it (K12)"),
        ("JgsBuiltins.CompilerPython.cs",
         "options.Fields[name] = FunctionFileList(value, here, line, col);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K13)"),
        ("JgsBuiltins.CompilerPython.cs",
         "options.Fields[JgsBuiltinClasses.GivenPackageName] = JgsValue.Str(CliName(value, name, line, col));",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K13)"),
        ("JgsBuiltins.CompilerPython.cs",
         "options.Fields[name] = EmptyCellValue();",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K13)"),
        ("JgsBuiltins.CompilerPython.cs",
         "options.Fields[name] = OptionCellColumn([.. files.Select(Absolute)]);",
         "a compiler options object's own field, written by its constructor or setter with a value minted for it (K13)"),
        ("JgsBuiltins.CompilerPython.cs",
         "results.AsObject.Fields[\"BuildType\"] = JgsValue.Str(\"pythonPackage\");",
         "the Results or Dependencies object compiler.build is minting, before anything can hold it (K13)"),
    ];

    [Fact]
    public void NoRawWriteIntoAPayloadOutsideTheAllowList()
    {
        List<(string File, int Line, string Text)> found = Scan();
        var allowed = new HashSet<string>(
            Allowed.Select(a => Key(a.File, a.Line)), StringComparer.Ordinal);

        string[] unexplained = found
            .Where(hit => !allowed.Contains(Key(Path.GetFileName(hit.File), hit.Text)))
            .Select(hit => $"{Path.GetFileName(hit.File)}:{hit.Line}  {hit.Text}")
            .ToArray();

        Assert.True(
            unexplained.Length == 0,
            "A payload is written or adopted somewhere the ownership model cannot see (M7/M2). "
            + "Route it through the gated setters, or add it to OwnershipGateTests.Allowed with the "
            + "reason it is safe:" + Environment.NewLine + string.Join(Environment.NewLine, unexplained));
    }

    [Fact]
    public void EveryAllowedLineStillExists()
    {
        List<(string File, int Line, string Text)> found = Scan();
        var live = new HashSet<string>(
            found.Select(hit => Key(Path.GetFileName(hit.File), hit.Text)), StringComparer.Ordinal);

        string[] stale = Allowed
            .Where(a => !live.Contains(Key(a.File, a.Line)))
            .Select(a => $"{a.File}: {a.Line}")
            .ToArray();

        Assert.True(
            stale.Length == 0,
            "An allowed raw write is no longer there. Remove the entry, so the list cannot become "
            + "permission for something else:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    private static string Key(string file, string line) => file + "|" + Normalize(line);

    private static string Normalize(string line) =>
        string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static List<(string File, int Line, string Text)> Scan()
    {
        var found = new List<(string, int, string)>();
        string root = Path.Combine(Repository(), "src", "JGraph.Scripting");
        foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = WithoutComment(lines[i]);
                if (code.Length == 0)
                {
                    continue;
                }

                foreach (Regex rule in Rules)
                {
                    if (rule.IsMatch(code))
                    {
                        found.Add((path, i + 1, lines[i].Trim()));
                        break;
                    }
                }
            }
        }

        return found;
    }

    /// <summary>The line with any trailing comment removed, so prose about a write is not a write.</summary>
    private static string WithoutComment(string line)
    {
        var kept = new StringBuilder(line.Length);
        bool inString = false;
        char quote = '"';
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (!inString && c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                break;
            }

            if (!inString && (c == '"' || c == '\''))
            {
                inString = true;
                quote = c;
            }
            else if (inString && c == quote && (i == 0 || line[i - 1] != '\\'))
            {
                inString = false;
            }

            kept.Append(c);
        }

        string text = kept.ToString().TrimStart();
        return text.StartsWith("///", StringComparison.Ordinal) || text.StartsWith("*", StringComparison.Ordinal)
            ? string.Empty
            : text;
    }

    private static string Repository()
    {
        var at = new DirectoryInfo(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "JGraph.sln")))
        {
            at = at.Parent;
        }

        return at?.FullName
            ?? throw new InvalidOperationException("the repository root is not above the test binaries");
    }
}
