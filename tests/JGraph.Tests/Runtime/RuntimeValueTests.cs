using System.Numerics;
using JGraph.Api;
using JGraph.Deploy;
using JGraph.Runtime;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Runtime;

/// <summary>
/// The JGraph Runtime's value model (compiler and SDK plan, stage K4): each class a deployed function
/// can take and return, through a component and back unchanged, what the engine sees of it, and the
/// values the model refuses.
/// </summary>
[Collection("JG facade")]
public sealed class RuntimeValueTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k4v-" + Guid.NewGuid().ToString("N"))).FullName;

    private readonly string? _cacheBefore = Environment.GetEnvironmentVariable("MCR_CACHE_ROOT");
    private readonly Component _values;

    public RuntimeValueTests()
    {
        JG.Reset();
        Environment.SetEnvironmentVariable("MCR_CACHE_ROOT", Path.Combine(_root, "cache"));
        Assert.True(JgRuntime.Initialize(RuntimeOptions.None, _output), "a runtime was left running");
        _values = Component.Open(Library("values", [
            ("echo.m", "function varargout = echo(varargin)\nvarargout = varargin;\nend\n"),
            ("describe.m", "function d = describe(x)\nd = sprintf('%s %s', class(x), mat2str(size(x)));\nend\n"),
            ("handle.m", "function h = handle\nh = @(x) x + 1;\nend\n"),
            ("apply.m", "function y = apply(h, x)\ny = h(x);\nend\n"),
        ]));
    }

    public void Dispose()
    {
        _values.Dispose();
        JgRuntime.Terminate();
        Environment.SetEnvironmentVariable("MCR_CACHE_ROOT", _cacheBefore);
        JG.Reset();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }

    private string Library(string component, IReadOnlyList<(string Name, string Text)> files)
    {
        string source = Directory.CreateDirectory(Path.Combine(_root, "src_" + component)).FullName;
        var entries = new List<(string Source, string ArchivePath)>();
        foreach ((string name, string text) in files)
        {
            string path = Path.Combine(source, name);
            File.WriteAllText(path, text);
            entries.Add((path, component + "/" + name));
        }

        string archive = Path.Combine(_root, component + ".ctf");
        CtfArchiveWriter.Write(archive, new CtfBuild
        {
            ComponentName = component,
            TargetType = CtfTargetType.DotNetAssembly,
            Files = entries,
            MatlabPath = [component],
            PublicFunctions = [.. files.Select(f => new CtfFunction(Path.GetFileNameWithoutExtension(f.Name), component + "/" + f.Name, [], []))],
        });
        return archive;
    }

    private string Describe(RuntimeValue value) =>
        Assert.IsType<RuntimeChar>(Assert.Single(_values.Call("describe", 1, value))).Characters;

    public static TheoryData<string, string> Classes() => new()
    {
        { "double 2x3", "double [2 3]" },
        { "double 2x2x2", "double [2 2 2]" },
        { "double empty", "double [0 0]" },
        { "double 0x3", "double [0 3]" },
        { "complex", "double [1 2]" },
        { "single", "single [1 3]" },
        { "int8", "int8 [1 3]" },
        { "uint8", "uint8 [2 2]" },
        { "int16", "int16 [1 2]" },
        { "uint16", "uint16 [1 1]" },
        { "int32", "int32 [1 2]" },
        { "uint32", "uint32 [1 1]" },
        { "int64", "int64 [1 2]" },
        { "uint64", "uint64 [1 1]" },
        { "logical", "logical [2 2]" },
        { "logical scalar", "logical [1 1]" },
        { "char row", "char [1 5]" },
        { "char empty", "char [0 0]" },
        { "char matrix", "char [2 3]" },
        { "string", "string [1 1]" },
        { "strings", "string [2 2]" },
        { "cell", "cell [1 3]" },
        { "cell 2x2", "cell [2 2]" },
        { "struct", "struct [1 1]" },
        { "struct array", "struct [1 2]" },
        { "sparse", "double [3 3]" },
    };

    private static RuntimeValue Sample(string name) => name switch
    {
        "double 2x3" => RuntimeNumeric.Of([1, 2, 3, 4, 5, 6], 2, 3),
        "double 2x2x2" => RuntimeNumeric.Of([1, 2, 3, 4, 5, 6, 7, 8], 2, 2, 2),
        "double empty" => RuntimeValue.Empty(),
        "double 0x3" => RuntimeNumeric.Of([], 0, 3),
        "complex" => RuntimeNumeric.Complex([new Complex(1, 2), new Complex(3, -4)]),
        "single" => RuntimeNumeric.Single([1.5f, -2.25f, 1e-3f]),
        "int8" => RuntimeNumeric.Int8(new sbyte[] { -128, 0, 127 }),
        "uint8" => RuntimeNumeric.UInt8(new byte[] { 0, 1, 254, 255 }, 2, 2),
        "int16" => RuntimeNumeric.Int16(new short[] { -32768, 32767 }),
        "uint16" => RuntimeNumeric.UInt16(new ushort[] { 65535 }),
        "int32" => RuntimeNumeric.Int32(new[] { int.MinValue, int.MaxValue }),
        "uint32" => RuntimeNumeric.UInt32(new[] { uint.MaxValue }),
        "int64" => RuntimeNumeric.Int64(new[] { -(1L << 53), 1L << 53 }),
        "uint64" => RuntimeNumeric.UInt64(new[] { 1UL << 53 }),
        "logical" => new RuntimeLogical([true, false, false, true], [2, 2]),
        "logical scalar" => RuntimeValue.Scalar(true),
        "char row" => RuntimeValue.Text("hello"),
        "char empty" => RuntimeValue.Text(""),
        "char matrix" => RuntimeChar.Matrix("abc", "def"),
        "string" => RuntimeValue.String("one"),
        "strings" => new RuntimeString(["a", null, "", "dd"], [2, 2]),
        "cell" => RuntimeCell.Row(RuntimeValue.Scalar(1), RuntimeValue.Text("two"), RuntimeCell.Row(RuntimeValue.Scalar(false))),
        "cell 2x2" => new RuntimeCell([RuntimeValue.Scalar(1), RuntimeValue.Empty(), RuntimeValue.Text("x"), RuntimeValue.String(null)], [2, 2]),
        "struct" => RuntimeStruct.Scalar(("b", RuntimeValue.Scalar(2)), ("a", RuntimeValue.Text("first"))),
        "struct array" => new RuntimeStruct(
            ["x", "y"],
            [
                new Dictionary<string, RuntimeValue> { ["x"] = RuntimeValue.Scalar(1), ["y"] = RuntimeValue.Text("p") },
                new Dictionary<string, RuntimeValue> { ["x"] = RuntimeNumeric.Of([2, 3]), ["y"] = RuntimeValue.Empty() },
            ],
            [1, 2]),
        "sparse" => new RuntimeSparse(3, 3, [0, 2, 1], [0, 2, 2, 3], [1, 5, -2]),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Classes))]
    public void AValue_ReachesTheEngineAsItsClass_AndComesBackUnchanged(string name, string described)
    {
        RuntimeValue value = Sample(name);
        Assert.Equal(described, Describe(value));
        RuntimeValue back = Assert.Single(_values.Call("echo", 1, value));
        Assert.Equal(value, back);
        Assert.Equal(value.ClassName, back.ClassName);
        Assert.Equal(value.Dimensions, back.Dimensions);
    }

    [Fact]
    public void SeveralValues_MakeSeveralOutputs_InOrder()
    {
        RuntimeValue[] back = _values.Call("echo", 3, RuntimeValue.Scalar(1), RuntimeValue.Text("b"), RuntimeValue.Scalar(true));
        Assert.Equal([RuntimeValue.Scalar(1), RuntimeValue.Text("b"), RuntimeValue.Scalar(true)], back);
    }

    [Fact]
    public void AFunctionHandle_IsOpaque_AndOnlyItsComponentTakesItBack()
    {
        RuntimeOpaque handle = Assert.IsType<RuntimeOpaque>(Assert.Single(_values.Call("handle", 1)));
        Assert.Equal("function_handle", handle.ClassName);
        Assert.Equal([1, 1], handle.Dimensions);
        Assert.Equal(RuntimeValue.Scalar(42), Assert.Single(_values.Call("apply", 1, handle, RuntimeValue.Scalar(41))));

        using Component other = Component.Open(Library("other", [("apply.m", "function y = apply(h, x)\ny = h(x);\nend\n")]));
        Assert.Throws<ArgumentException>(() => other.Call("apply", 1, handle, RuntimeValue.Scalar(1)));
    }

    [Fact]
    public void WhatJGraphsOwnStorageCannotKeep_ComesBackAsJGraphHoldsIt()
    {
        // An empty array carries no class in JGraph, a char row with no characters is 0-by-0, and
        // an element with no imaginary part is real: the value model is lossless for what the
        // engine holds, and these three are what the engine does not hold (divergences, ADR 0260).
        Assert.Equal(RuntimeValue.Empty(), Assert.Single(_values.Call("echo", 1, new RuntimeLogical([], [0, 0]))));
        Assert.Equal(RuntimeValue.Text(""), Assert.Single(_values.Call("echo", 1, new RuntimeChar("", [1, 0]))));
        Assert.Equal(RuntimeNumeric.Of([1, 2]), Assert.Single(_values.Call("echo", 1, new RuntimeNumeric("double", [1, 2], [0, 0], [1, 2]))));
    }

    [Fact]
    public void TheModel_RefusesWhatAClassCannotHold()
    {
        Assert.Throws<ArgumentException>(() => new RuntimeNumeric("int8", [128], null, [1, 1]));
        Assert.Throws<ArgumentException>(() => new RuntimeNumeric("uint8", [1.5], null, [1, 1]));
        Assert.Throws<ArgumentException>(() => RuntimeNumeric.Int64(new[] { (1L << 53) + 1 }));
        Assert.Throws<ArgumentException>(() => new RuntimeNumeric("int32", [1], [1], [1, 1]));
        Assert.Throws<ArgumentException>(() => new RuntimeNumeric("logical", [1], null, [1, 1]));
        Assert.Throws<ArgumentException>(() => RuntimeNumeric.Of([1, 2, 3], 2, 2));
        Assert.Throws<ArgumentException>(() => RuntimeChar.Matrix("ab", "c"));
        Assert.Throws<ArgumentException>(() => new RuntimeStruct(["a"], [new Dictionary<string, RuntimeValue> { ["b"] = RuntimeValue.Empty() }], [1, 1]));
        Assert.Throws<ArgumentException>(() => new RuntimeSparse(2, 2, [2], [0, 1, 1], [1]));

        // A single keeps a single's precision, and a size drops its trailing singletons.
        Assert.Equal((double)(float)0.1, new RuntimeNumeric("single", [0.1], null, [1, 1]).ScalarValue);
        Assert.Equal([2, 3], RuntimeNumeric.Of([1, 2, 3, 4, 5, 6], 2, 3, 1, 1).Dimensions);
        Assert.Equal([3, 1], RuntimeNumeric.Of([1, 2, 3], 3).Dimensions);
    }
}
