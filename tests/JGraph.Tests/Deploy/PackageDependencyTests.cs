using JGraph.Deploy;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// The dependency analysis over <c>+package</c> members and <c>@Class</c> folders (packages and
/// class folders plan, stage P6, ADR 0312) against what R2025b's
/// <c>matlab.codetools.requiredFilesAndProducts</c> answered for the same tree
/// (<c>tools/matlab-checklist/ui-probes/p6/p6_depend.out</c>; the parity fixture <c>p6_deploy</c>
/// pins the same answers through the builtin).
/// </summary>
public sealed class PackageDependencyTests : IDisposable
{
    private static readonly HashSet<string> Builtins = new(JgsScriptEngine.BuiltinNames(), StringComparer.Ordinal);

    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-p6-" + Guid.NewGuid().ToString("N"))).FullName;

    public PackageDependencyTests() => MakeTree();

    private string Cur => Path.Combine(_root, "cur");

    private string Lib => Path.Combine(_root, "lib");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }

    private const string Thing =
        "lib/@Thing/Thing.m lib/@Thing/extent.m lib/@Thing/private/hidden.m lib/@Thing/scale.m lib/@Thing/staticOne.m lib/@Thing/usesfar.m";

    private const string ThingAndFar = Thing + " lib/farhelper.m";

    [Theory]
    [InlineData("usepkg", "lib/+pkg/f.m " + ThingAndFar)]
    [InlineData("useclass", "lib/+pkg/C.m")]
    [InlineData("usesubclass", "lib/+pkg/+sub/K.m lib/+pkg/C.m")]
    [InlineData("usefolderclass", "lib/+pkg/@D/D.m lib/+pkg/@D/dm.m lib/+pkg/@D/private/dp.m lib/+pkg/@D/unused.m")]
    [InlineData("usesub", "lib/+pkg/+sub/h.m")]
    [InlineData("usescript", "lib/+pkg/s.m")]
    [InlineData("usesubfolder", "lib/@Sub/Sub.m lib/@Sub/extent.m " + ThingAndFar)]
    [InlineData("usehandle", "lib/+pkg/f.m " + ThingAndFar)]
    [InlineData("usemeta", "lib/+pkg/C.m " + ThingAndFar)]
    [InlineData("usestatic", "lib/+pkg/C.m")]
    [InlineData("useenum", "lib/+pkg/E.m")]
    [InlineData("useimport", "lib/+pkg/f.m")]
    [InlineData("useimportclass", "lib/+pkg/C.m")]
    [InlineData("useimportsub", "lib/+pkg/+sub/h.m")]
    [InlineData("usewild", "lib/+pkg/C.m lib/+pkg/f.m lib/+pkg/two.m")]
    [InlineData("usefeval", "")]
    [InlineData("usepragma", "lib/+pkg/f.m")]
    [InlineData("usepragmaclass", ThingAndFar)]
    [InlineData("usemax", "lib/+pkg/max.m")]
    [InlineData("usedotvar", "")]
    [InlineData("usenosuch", "")]
    [InlineData("usechain", "lib/+pkg/f.m lib/+pkg/g2.m")]
    [InlineData("useprivate", "lib/+pkg/private/p.m lib/+pkg/q.m")]
    [InlineData("usehandleobj", "lib/@HThing/HThing.m lib/@HThing/bump.m")]
    [InlineData("usemethodhandle", ThingAndFar)]
    [InlineData("usebarestatic", ThingAndFar)]
    [InlineData("usetwo", "lib/+other/f.m lib/+pkg/f.m")]
    [InlineData("useonlything", ThingAndFar)]
    [InlineData("usepkgscriptroot", "lib/+pkg/f.m " + ThingAndFar)]
    public void ARootTakesWhatR2025bTook(string root, string expected)
    {
        Assert.Equal(Set($"cur/{root}.m " + expected), Files(Analyze(Path.Combine(Cur, root + ".m"))));
    }

    [Fact]
    public void AMemberFileAClassFileAndTheFoldersThemselvesAreRoots()
    {
        // rfp.member, rfp.classfile: a member brings what it calls, a class file its whole folder.
        Assert.Equal(Set("lib/+pkg/f.m lib/+pkg/g2.m"), Files(Analyze(Path.Combine(Lib, "+pkg", "g2.m"))));
        Assert.Equal(Set(ThingAndFar), Files(Analyze(Path.Combine(Lib, "@Thing", "Thing.m"))));

        // rfp.pkgfolder: every file under the package, nothing outside it; rfp.classfolder: the folder and what its files call.
        Assert.Equal(
            Set("lib/+pkg/+sub/K.m lib/+pkg/+sub/h.m lib/+pkg/@D/D.m lib/+pkg/@D/dm.m lib/+pkg/@D/private/dp.m lib/+pkg/@D/unused.m "
                + "lib/+pkg/C.m lib/+pkg/E.m lib/+pkg/f.m lib/+pkg/g2.m lib/+pkg/max.m lib/+pkg/mk2.m lib/+pkg/private/p.m lib/+pkg/q.m lib/+pkg/s.m lib/+pkg/two.m"),
            Files(Analyze(Path.Combine(Lib, "+pkg"))));
        Assert.Equal(Set(ThingAndFar), Files(Analyze(Path.Combine(Lib, "@Thing"))));
    }

    [Fact]
    public void TopOnlyTakesTheFolderWithoutAnalysingItsFiles_AndNothingOffThePath()
    {
        // rfp.toponly: the class folder's files come with the root, farhelper (what a method file calls) does not.
        Assert.Equal(Set("cur/usepkg.m lib/+pkg/f.m " + Thing),
            Files(Analyze(Path.Combine(Cur, "usepkg.m"), _ => new DependencyOptions { CurrentFolder = Cur, SearchPath = [Lib], TopOnly = true })));

        // rfp.usepkg.nolib: with the library off the path a dotted name and a class folder reach nothing.
        Assert.Equal(Set("cur/usepkg.m"), Files(Analyze(Path.Combine(Cur, "usepkg.m"), _ => new DependencyOptions { CurrentFolder = Cur, SearchPath = [] })));
    }

    [Fact]
    public void AMethodFileAsARootBringsItsFolderAndWhatTheFolderCalls()
    {
        // rfp.methodfile: R2025b took the folder's files and left farhelper out; JGraph analyses the
        // folder's files as any taken code, so the helper comes too (ADR 0312, divergences).
        Assert.Equal(Set(ThingAndFar), Files(Analyze(Path.Combine(Lib, "@Thing", "scale.m"))));
    }

    [Fact]
    public void TheLayoutKeepsThePackageAndClassFoldersOffThePath()
    {
        DependencyReport report = Analyze(Path.Combine(Cur, "usepkg.m"));
        CtfBuild build = CtfLayout.FromReport("usepkg", Path.Combine(Cur, "usepkg.m"), report);
        string lib = Lib[Path.GetPathRoot(Lib)!.Length..].Replace('\\', '/');
        Assert.Equal(["usepkg", lib], build.MatlabPath); // mcc.usepkg.manifest.dirs: usepkg and the library, never +pkg or @Thing
        Assert.Contains((Path.Combine(Lib, "+pkg", "f.m"), lib + "/+pkg/f.m"), build.Files);
        Assert.Contains((Path.Combine(Lib, "@Thing", "private", "hidden.m"), lib + "/@Thing/private/hidden.m"), build.Files);
        Assert.Equal("usepkg", build.MainFunction);
    }

    // --- The tree ---------------------------------------------------------------------------------

    private DependencyReport Analyze(string root, Func<DependencyOptions, DependencyOptions>? options = null)
    {
        var plain = new DependencyOptions { CurrentFolder = Cur, SearchPath = [Lib] };
        var analysis = new JgsDependencyAnalyzer(options?.Invoke(plain) ?? plain, Builtins.Contains);
        return analysis.Analyze([root]);
    }

    private SortedSet<string> Files(DependencyReport report) =>
        new(report.Files.Select(f => Path.GetRelativePath(_root, f.Path).Replace('\\', '/')), StringComparer.Ordinal);

    private static SortedSet<string> Set(string files) =>
        new(files.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    private void Put(string relative, params string[] lines)
    {
        string path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
    }

    /// <summary>p6_layout's tree, as the probe wrote it.</summary>
    private void MakeTree()
    {
        Put("lib/+pkg/f.m", "function r = f(x)", "r = x * 10;", "end");
        Put("lib/+pkg/g2.m", "function r = g2(x)", "r = pkg.f(x) + 1;", "end");
        Put("lib/+pkg/two.m", "function [a, b] = two(x)", "a = x;", "b = -x;", "end");
        Put("lib/+pkg/q.m", "function r = q()", "r = p();", "end");
        Put("lib/+pkg/private/p.m", "function r = p()", "r = 'private p';", "end");
        Put("lib/+pkg/s.m", "disp('script s ran');", "sval = 42;");
        Put("lib/+pkg/max.m", "function r = max(x)", "r = 'pkg max';", "end");
        Put("lib/+pkg/mk2.m", "function r = mk2()", "import pkg.C", "r = C(2);", "end");
        Put("lib/+pkg/C.m", "classdef C", "    properties (Constant)", "        K = 7", "    end", "    properties", "        V = 0", "    end",
            "    methods", "        function o = C(v)", "            if nargin, o.V = v; end", "        end",
            "        function r = m(o)", "            r = o.V * 2;", "        end", "    end",
            "    methods (Static)", "        function r = stat(x)", "            r = x + 100;", "        end", "    end", "end");
        Put("lib/+pkg/E.m", "classdef E", "    enumeration", "        A, B", "    end", "end");
        Put("lib/+pkg/+sub/h.m", "function r = h(x)", "r = x - 1;", "end");
        Put("lib/+pkg/+sub/K.m", "classdef K < pkg.C", "    methods", "        function r = m(o)", "            r = m@pkg.C(o) + 1;", "        end", "    end", "end");
        Put("lib/+pkg/@D/D.m", "classdef D", "    properties", "        V", "    end", "    methods", "        function o = D(v)", "            o.V = v;", "        end", "        r = dm(o)", "    end", "end");
        Put("lib/+pkg/@D/dm.m", "function r = dm(o)", "r = dp(o.V);", "end");
        Put("lib/+pkg/@D/private/dp.m", "function r = dp(v)", "r = v + 1000;", "end");
        Put("lib/+pkg/@D/unused.m", "function r = unused(o)", "r = o.V;", "end");
        Put("lib/+other/f.m", "function r = f(x)", "r = x * 1000;", "end");

        Put("lib/@Thing/Thing.m", "classdef Thing", "    properties", "        W = 0", "    end", "    methods",
            "        function o = Thing(w)", "            if nargin, o.W = w; end", "        end", "        r = extent(o)", "    end",
            "    methods (Static)", "        r = staticOne(x)", "    end", "end");
        Put("lib/@Thing/extent.m", "function r = extent(o)", "r = o.W * o.W;", "end");
        Put("lib/@Thing/scale.m", "function r = scale(o, k)", "% not declared in the classdef", "r = hidden(o.W) * k;", "end");
        Put("lib/@Thing/private/hidden.m", "function r = hidden(w)", "r = w + 0.5;", "end");
        Put("lib/@Thing/staticOne.m", "function r = staticOne(x)", "r = x * 3;", "end");
        Put("lib/@Thing/usesfar.m", "function r = usesfar(o)", "r = farhelper(o.W);", "end");
        Put("lib/farhelper.m", "function r = farhelper(w)", "r = w * 11;", "end");
        Put("lib/unusedlib.m", "function r = unusedlib(w)", "r = w;", "end");
        Put("lib/@Sub/Sub.m", "classdef Sub < Thing", "    methods", "        function o = Sub(w)", "            o@Thing(w);", "        end", "    end", "end");
        Put("lib/@Sub/extent.m", "function r = extent(o)", "r = -1;", "end");
        Put("lib/@HThing/HThing.m", "classdef HThing < handle", "    properties", "        N = 0", "    end", "end");
        Put("lib/@HThing/bump.m", "function bump(o)", "o.N = o.N + 1;", "end");

        Put("cur/usepkg.m", "function usepkg()", "beep('off');", "fprintf('f\\t%g\\n', pkg.f(2));", "t = Thing(3);", "fprintf('extent\\t%g\\n', t.extent());",
            "fprintf('scale\\t%g\\n', scale(t, 2));", "fprintf('deployed\\t%d\\n', isdeployed);", "end");
        Put("cur/useclass.m", "function r = useclass()", "r = pkg.C(3).m();", "end");
        Put("cur/usesubclass.m", "function r = usesubclass()", "r = pkg.sub.K(1).m();", "end");
        Put("cur/usefolderclass.m", "function r = usefolderclass()", "r = pkg.D(1).dm();", "end");
        Put("cur/usesub.m", "function r = usesub()", "r = pkg.sub.h(5);", "end");
        Put("cur/usescript.m", "function r = usescript()", "pkg.s", "r = sval;", "end");
        Put("cur/usesubfolder.m", "function r = usesubfolder()", "r = Sub(2).extent();", "end");
        Put("cur/usehandle.m", "function r = usehandle()", "h = @pkg.f;", "g = @Thing.staticOne;", "r = h(2) + g(1);", "end");
        Put("cur/usemeta.m", "function r = usemeta()", "r = {?pkg.C, ?Thing};", "end");
        Put("cur/usestatic.m", "function r = usestatic()", "r = pkg.C.K + pkg.C.stat(1);", "end");
        Put("cur/useenum.m", "function r = useenum()", "r = pkg.E.A;", "end");
        Put("cur/useimport.m", "function r = useimport()", "import pkg.f", "r = f(2);", "end");
        Put("cur/useimportclass.m", "function r = useimportclass()", "import pkg.C", "r = C(1).m();", "end");
        Put("cur/useimportsub.m", "function r = useimportsub()", "import pkg.sub.*", "r = h(1);", "end");
        Put("cur/usewild.m", "function r = usewild()", "import pkg.*", "r = f(1) + C(1).m() + two(1);", "end");
        Put("cur/usefeval.m", "function r = usefeval()", "r = feval('pkg.f', 2);", "end");
        Put("cur/usepragma.m", "function r = usepragma()", "%#function pkg.f", "r = 1;", "end");
        Put("cur/usepragmaclass.m", "function r = usepragmaclass()", "%#function Thing", "r = 1;", "end");
        Put("cur/usemax.m", "function r = usemax()", "r = pkg.max(1);", "end");
        Put("cur/usedotvar.m", "function r = usedotvar()", "pkg = struct('f', 1);", "r = pkg.f;", "end");
        Put("cur/usenosuch.m", "function r = usenosuch()", "r = pkg.nosuch(1);", "end");
        Put("cur/usechain.m", "function r = usechain()", "r = pkg.g2(1);", "end");
        Put("cur/useprivate.m", "function r = useprivate()", "r = pkg.q();", "end");
        Put("cur/usehandleobj.m", "function r = usehandleobj()", "o = HThing;", "o.bump();", "r = o.N;", "end");
        Put("cur/usemethodhandle.m", "function r = usemethodhandle()", "h = @extent;", "r = h(Thing(2));", "end");
        Put("cur/usebarestatic.m", "function r = usebarestatic()", "r = Thing.staticOne(2);", "end");
        Put("cur/usetwo.m", "function r = usetwo()", "r = pkg.f(1) + other.f(1);", "end");
        Put("cur/useonlything.m", "function r = useonlything()", "r = Thing(2).W;", "end");
        Put("cur/usepkgscriptroot.m", "pkg.f(1);", "Thing(1);");
    }
}
