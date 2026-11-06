using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The packages and class folders plan, stage P1 (ADR 0307): a <c>+pkg</c> folder's members answer
/// to their dotted names through the code tree over the file index. The parity fixture
/// <c>p1_packages</c> pins every measured behaviour against R2025b; these tests pin the machinery
/// the fixture cannot see — the index's folder listing, the tree's walk, the refresh after a write
/// in the same statement, the handle's cache — and the words a plain run has no R2025b beside.
/// </summary>
[Collection("JG facade")]
public class JgsPackageTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "jgraph-p1-packages-" + Guid.NewGuid().ToString("N"));

    private readonly string _library;

    public JgsPackageTests()
    {
        JG.Reset();
        _library = Path.Combine(_folder, "lib");
        Directory.CreateDirectory(Path.Combine(_library, "+pkg", "+sub"));
        Directory.CreateDirectory(Path.Combine(_library, "+pkg", "private"));
        Directory.CreateDirectory(Path.Combine(_folder, "+pkg"));
        File.WriteAllText(Path.Combine(_library, "+pkg", "f.m"), "function r = f(x)\nr = x * 10;\nend\n");
        File.WriteAllText(Path.Combine(_library, "+pkg", "q.m"), "function r = q()\nr = p();\nend\n");
        File.WriteAllText(Path.Combine(_library, "+pkg", "private", "p.m"), "function r = p()\nr = 'private p';\nend\n");
        File.WriteAllText(Path.Combine(_library, "+pkg", "+sub", "h.m"), "function r = h(x)\nr = x - 1;\nend\n");
        File.WriteAllText(Path.Combine(_library, "+pkg", "dup.m"), "function r = dup()\nr = 'lib dup';\nend\n");
        File.WriteAllText(Path.Combine(_folder, "+pkg", "dup.m"), "function r = dup()\nr = 'cur dup';\nend\n");
        File.WriteAllText(Path.Combine(_folder, "+pkg", "s.m"), "sval = 42;\n");
    }

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private ScriptContext Context() => new(_output, static (_, _) => { }, _folder);

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(code, Context(), default);

    private static object? Value(ScriptRunResult result, string name) =>
        Assert.Single(result.Variables, v => v.Name == name).RawValue;

    private string Escaped(string path) => path.Replace("\\", "\\\\");

    /// <summary>The index lists a search folder's +package sub-folders, and the tree walks them in search order, first folder first.</summary>
    [Fact]
    public void TheTreeWalksPackagesAcrossTheSearchFolders()
    {
        var index = new JgsFileIndex(() => [_folder, _library], () => 0, static _ => false, static _ => { });
        var tree = new JgsCodeTree(index);

        Assert.True(index.AnyPackages);
        Assert.True(tree.IsPackage(["pkg"]));
        Assert.True(tree.IsPackage(["pkg", "sub"]));
        Assert.False(tree.IsPackage(["pkg", "private"]));
        Assert.False(tree.IsPackage(["nosuch"]));

        JgsCodeTree.PackageHit[] hits = tree.Hits(["pkg"]).ToArray();
        Assert.Equal(2, hits.Length);
        Assert.Equal(Path.Combine(_folder, "+pkg"), hits[0].Folder);
        Assert.Equal(Path.Combine(_library, "+pkg"), hits[1].Folder);

        Assert.Equal(Path.Combine(_folder, "+pkg"), tree.MemberFolder(["pkg"], "dup")!.Value.Folder);
        Assert.Equal(Path.Combine(_library, "+pkg"), tree.MemberFolder(["pkg"], "f")!.Value.Folder);
        Assert.Null(tree.MemberFolder(["pkg"], "p"));
        Assert.True(tree.HasSubPackage(["pkg"], "sub"));

        (IReadOnlyList<string> members, IReadOnlyList<string> subs, _) = tree.Contents(["pkg"]);
        Assert.Equal(["dup", "f", "q", "s"], members);
        Assert.Equal(["sub"], subs);
        Assert.Equal("pkg.sub.", JgsCodeTree.PackagePrefix(Path.Combine(_library, "+pkg", "+sub", "h.m")));
        Assert.Equal(string.Empty, JgsCodeTree.PackagePrefix(Path.Combine(_library, "h.m")));
        Assert.Equal(["pkg", "sub", "h"], JgsCodeTree.DottedParts("pkg.sub.h")!);
        Assert.Null(JgsCodeTree.DottedParts("pkg"));
        Assert.Null(JgsCodeTree.DottedParts("pkg..h"));
        Assert.Null(JgsCodeTree.DottedParts("pkg.1h"));
    }

    /// <summary>A member written in one statement answers its dotted name in the next, through the host's write seam; one written by another program is seen at the next statement by the folder's time.</summary>
    [Fact]
    public async Task AMemberWrittenDuringTheRunAnswersAtOnce()
    {
        string late = Escaped(Path.Combine(_library, "+pkg", "late.m"));
        ScriptRunResult result = await RunMatlab($"""
            addpath('{Escaped(_library)}');
            before = pkg.f(2);
            fid = fopen('{late}', 'w'); fprintf(fid, 'function r = late()\nr = 7;\nend\n'); fclose(fid);
            after = pkg.late();
            delete('{late}');
            gone = '';
            try
                pkg.late();
            catch e
                gone = e.identifier;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(20.0, Value(result, "before"));
        Assert.Equal(7.0, Value(result, "after"));
        Assert.Equal("MATLAB:undefinedVarOrClass", Value(result, "gone"));
    }

    /// <summary>A package folder made current with cd gives its files no bare names, and its package still answers through the folder holding it.</summary>
    [Fact]
    public async Task APackageFolderAsTheCurrentFolderGivesNoBareNames()
    {
        ScriptRunResult result = await RunMatlab($"""
            addpath('{Escaped(_library)}');
            old = cd('{Escaped(Path.Combine(_library, "+pkg"))}');
            bare = '';
            try
                f(2);
            catch e
                bare = e.identifier;
            end
            dotted = pkg.f(2);
            w = which('f');
            cd(old);
            back = pwd;
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("MATLAB:UndefinedFunction", Value(result, "bare"));
        Assert.Equal(20.0, Value(result, "dotted"));
        Assert.Equal(string.Empty, Value(result, "w"));
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(_folder)), (string)Value(result, "back")!, ignoreCase: true);
    }

    /// <summary>Two handles to one member share their target, so they compare equal, and clear pkg.f forgets the member by its dotted name.</summary>
    [Fact]
    public async Task HandlesShareTheMemberAndClearForgetsIt()
    {
        ScriptRunResult result = await RunMatlab($"""
            addpath('{Escaped(_library)}');
            a = @pkg.f;
            b = @pkg.f;
            same = isequal(a, b);
            clear pkg.f
            c = @pkg.f;
            reloaded = ~isequal(a, c);
            still = c(3);
            s = pkg.q();
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(true, Value(result, "same"));
        Assert.Equal(true, Value(result, "reloaded"));
        Assert.Equal(30.0, Value(result, "still"));
        Assert.Equal("private p", Value(result, "s"));
    }

    /// <summary>A script in a package runs in the caller's workspace by its dotted name, and refuses to be a value or take arguments with R2025b's words.</summary>
    [Fact]
    public async Task AScriptInAPackageRunsByItsDottedName()
    {
        ScriptRunResult result = await RunMatlab("""
            pkg.s
            ran = sval;
            asValue = '';
            try
                x = pkg.s;
            catch e
                asValue = e.identifier;
            end
            withArg = '';
            try
                pkg.s(1);
            catch e
                withArg = e.message;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(42.0, Value(result, "ran"));
        Assert.Equal("MATLAB:scriptNotAFunction", Value(result, "asValue"));
        Assert.StartsWith("Execution of script s as a function is not supported:\n", (string)Value(result, "withArg")!);
    }

    /// <summary>Two statements juxtaposed on one line are MATLAB's parse error, carried through eval under MATLAB's identifier.</summary>
    [Fact]
    public async Task JuxtaposedStatementsAreRefusedAsMatlabRefusesThem()
    {
        ScriptRunResult result = await RunMatlab("""
            id = '';
            try
                eval('pkg.f 2');
            catch e
                id = e.identifier;
            end
            eval('if true, k = 1; end');
            eval('for n = 1:2 k = k + n; end');
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("MATLAB:m_missing_operator", Value(result, "id"));
        Assert.Equal(4.0, Value(result, "k"));
    }

    // --- Stage P2 (ADR 0308): classes in packages, their qualified names and the words over them -------

    /// <summary>An enumeration member keeps its name through save and load, in and out of a package (found in P2: a loaded member came back as a default instance).</summary>
    [Fact]
    public async Task AnEnumerationMemberSurvivesSaveAndLoad()
    {
        File.WriteAllText(Path.Combine(_library, "+pkg", "E.m"), "classdef E\n    enumeration\n        A, B\n    end\nend\n");
        File.WriteAllText(Path.Combine(_folder, "P2PlainEnum.m"), "classdef P2PlainEnum\n    enumeration\n        Red, Green\n    end\nend\n");
        string file = Escaped(Path.Combine(_folder, "enums.mat"));
        ScriptRunResult result = await RunMatlab($"""
            addpath('{Escaped(_library)}');
            e = pkg.E.B;
            p = P2PlainEnum.Green;
            save('{file}', 'e', 'p');
            clear e p pkg.E P2PlainEnum
            S = load('{file}');
            eClass = class(S.e);
            eName = char(S.e);
            eSame = S.e == pkg.E.B;
            pName = char(S.p);
            pClass = class(S.p);
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("pkg.E", Value(result, "eClass"));
        Assert.Equal("B", Value(result, "eName"));
        Assert.Equal(true, Value(result, "eSame"));
        Assert.Equal("Green", Value(result, "pName"));
        Assert.Equal("P2PlainEnum", Value(result, "pClass"));
    }

    /// <summary>nargin of a class name counts its constructor's parameters (R2025b: nargin('pkg.C') is 1), and none for a class that writes no constructor; every constructor answered -1 before P2.</summary>
    [Fact]
    public async Task NarginOfAClassNameCountsItsConstructor()
    {
        File.WriteAllText(Path.Combine(_library, "+pkg", "C.m"), "classdef C\n    methods\n        function o = C(a, b)\n        end\n        function r = m(o)\n            r = 1;\n        end\n    end\nend\n");
        File.WriteAllText(Path.Combine(_folder, "P2Bare.m"), "classdef P2Bare\n    properties\n        V = 0\n    end\nend\n");
        ScriptRunResult result = await RunMatlab($"""
            addpath('{Escaped(_library)}');
            packaged = nargin('pkg.C');
            packagedOut = nargout('pkg.C');
            bare = nargin('P2Bare');
            notAFile = '';
            try
                nargin('pkg.C.m');
            catch e
                notAFile = e.identifier;
            end
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal(2.0, Value(result, "packaged"));
        Assert.Equal(1.0, Value(result, "packagedOut"));
        Assert.Equal(0.0, Value(result, "bare"));
        Assert.Equal("MATLAB:narginout:notValidMfile", Value(result, "notAFile"));
    }

    /// <summary>meta.package.fromName answers the plan's struct (decision PD5): the lists by qualified name, scripts among the functions, the parent chain, and a 0-by-0 for no package.</summary>
    [Fact]
    public async Task MetaPackageFromNameIsAStructOfTheListing()
    {
        File.WriteAllText(Path.Combine(_library, "+pkg", "C.m"), "classdef C\nend\n");
        ScriptRunResult result = await RunMatlab($$"""
            addpath('{{Escaped(_library)}}');
            p = meta.package.fromName('pkg');
            name = p.Name;
            classes = strjoin({p.ClassList.Name}, ',');
            functions = strjoin({p.FunctionList.Name}, ',');
            inner = strjoin({p.InnerNamespaces.Name}, ',');
            legacy = strjoin({p.PackageList.Name}, ',');
            noParent = isempty(p.OuterNamespace) && isempty(p.ContainingPackage);
            sub = meta.package.fromName('pkg.sub');
            parent = sub.ContainingPackage.Name;
            grandparentEmpty = isempty(sub.ContainingPackage.ContainingPackage);
            none = isempty(meta.package.fromName('nosuchpkg'));
            """);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("pkg", Value(result, "name"));
        Assert.Equal("pkg.C", Value(result, "classes"));
        Assert.Equal("dup,f,q,s", Value(result, "functions"));
        Assert.Equal("pkg.sub", Value(result, "inner"));
        Assert.Equal("pkg.sub", Value(result, "legacy"));
        Assert.Equal(true, Value(result, "noParent"));
        Assert.Equal("pkg", Value(result, "parent"));
        Assert.Equal(true, Value(result, "grandparentEmpty"));
        Assert.Equal(true, Value(result, "none"));
    }
}
