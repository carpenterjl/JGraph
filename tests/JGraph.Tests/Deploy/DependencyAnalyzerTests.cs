using JGraph.Deploy;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// The dependency analysis (compiler and SDK plan, stage K2) against what R2025b's <c>mcc -m -C</c>
/// put in its archives for probe_k2_depend's tree (<c>tools/matlab-checklist/compiler-probes/recordings/k2_mcc.out.txt</c>),
/// one build per flag, and the rule tables, the pragmas and the log files' layout.
/// </summary>
public sealed class DependencyAnalyzerTests : IDisposable
{
    private static readonly HashSet<string> Builtins = new(JgsScriptEngine.BuiltinNames(), StringComparer.Ordinal);

    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k2-" + Guid.NewGuid().ToString("N"))).FullName;

    public DependencyAnalyzerTests() => MakeTree();

    private string App => Path.Combine(_root, "app");

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

    // --- The builds R2025b recorded ---------------------------------------------------------------

    private const string Plain =
        "lib/k2_libdeep.m lib/k2_libfn.m app/k2_cellfn.m app/k2_cls.m app/k2_cmd.m app/k2_data.mat app/k2_deep.m "
        + "app/k2_from_local.m app/k2_from_method.m app/k2_from_script.m app/k2_from_sub.m app/k2_handle.m "
        + "app/k2_helper.fig app/k2_helper.m app/k2_main.fig app/k2_main.m app/k2_notes.txt app/k2_pragma.m "
        + "app/k2_script.m app/private/k2_priv.m";

    [Fact]
    public void A_build_takes_what_mcc_took() => Assert.Equal(Set(Plain), Files(Build()));

    [Fact]
    public void Without_the_library_on_the_path_its_files_are_not_taken() =>
        Assert.Equal(Set(Plain.Replace("lib/k2_libdeep.m lib/k2_libfn.m ", "")), Files(Build(onPath: false)));

    [Fact]
    public void Minus_X_takes_no_data_files_but_keeps_the_figures() =>
        Assert.Equal(Set(Plain.Replace("app/k2_data.mat ", "").Replace("app/k2_notes.txt ", "")),
            Files(Build(options: o => new DependencyOptions { CurrentFolder = o.CurrentFolder, SearchPath = o.SearchPath, DetectDataFiles = false })));

    [Fact]
    public void Minus_I_puts_a_folder_on_the_build_path() =>
        Assert.Equal(Set(Plain), Files(Build(onPath: false, options: o => new DependencyOptions { CurrentFolder = o.CurrentFolder, IncludeFolders = [Lib] })));

    [Fact]
    public void Minus_a_takes_folders_whole_files_past_an_exclude_and_names_relative_to_the_current_folder()
    {
        DependencyReport report = Build(options: o => new DependencyOptions
        {
            CurrentFolder = o.CurrentFolder,
            SearchPath = o.SearchPath,
            AdditionalFiles = [Path.Combine(_root, "pack"), "k2_excl.m", Path.Combine(Lib, "k2_libdeep.m"), "k2_unnamed.txt"],
        });
        Assert.Equal(Set(Plain + " pack/deeper/k2_deeper.txt pack/deeper/k2_packdeep.m pack/k2_pack.txt pack/k2_packfn.m "
            + "app/k2_excl.m app/k2_unnamed.txt"), Files(report));
        Assert.Equal(DependencyReason.Additional, report.Files.Single(f => f.Path.EndsWith("k2_excl.m", StringComparison.Ordinal)).Reason);
    }

    [Fact]
    public void A_wildcard_takes_one_folders_matches() =>
        Assert.Equal(Set(Plain + " pack/k2_pack.txt"),
            Files(Build(options: o => new DependencyOptions
            {
                CurrentFolder = o.CurrentFolder, SearchPath = o.SearchPath, AdditionalFiles = [Path.Combine(_root, "pack", "*.txt")],
            })));

    [Fact]
    public void Two_files_of_one_name_build_without_an_error() =>
        Assert.Equal(Set(Plain + " other/k2_helper.m"),
            Files(Build(options: o => new DependencyOptions
            {
                CurrentFolder = o.CurrentFolder, SearchPath = o.SearchPath, AdditionalFiles = [Path.Combine(_root, "other", "k2_helper.m")],
            })));

    [Fact]
    public void Minus_N_leaves_a_toolbox_function_unresolved_and_minus_p_puts_it_back()
    {
        DependencyReport cleared = Build(options: o => new DependencyOptions { CurrentFolder = o.CurrentFolder, SearchPath = o.SearchPath, ClearToolboxPath = true });
        Assert.Equal(Set(Plain), Files(cleared));
        UnresolvedSymbol symbol = Assert.Single(cleared.Unresolved);
        Assert.Equal((Path.Combine(App, "k2_main.m"), "butter", "mcc_flag"), (symbol.File, symbol.Symbol, symbol.Reason));
        Assert.Equal(new[] { 1 }, cleared.Products.Select(p => p.Number));

        DependencyReport restored = Build(options: o => new DependencyOptions
        {
            CurrentFolder = o.CurrentFolder, SearchPath = o.SearchPath, ClearToolboxPath = true, ToolboxFolders = ["signal"],
        });
        Assert.Empty(restored.Unresolved);
        Assert.Equal(new[] { 1, 8 }, restored.Products.Select(p => p.Number));
    }

    [Fact]
    public void An_added_script_is_analysed_as_a_root_is()
    {
        var analysis = new JgsDependencyAnalyzer(new DependencyOptions
        {
            CurrentFolder = App, SearchPath = [Lib], AdditionalFiles = ["k2_script.m"],
        }, Builtins.Contains);
        DependencyReport report = analysis.Analyze([Path.Combine(App, "k2_helper.m")]);
        Assert.Equal(Set("app/k2_from_script.m app/k2_from_sub.m app/k2_helper.fig app/k2_helper.m app/k2_script.m app/private/k2_priv.m"),
            Files(report));
    }

    [Fact]
    public void A_build_names_the_non_deployable_functions_the_code_calls_in_order()
    {
        DependencyReport report = Build();
        Assert.Equal(new[] { "edit", "help" }, report.Excluded.Select(e => e.Name));
        ExcludedDependency edit = report.Excluded[0];
        Assert.Equal("toolbox/matlab/codetools/edit.m", edit.R2025bFile);
        Assert.Equal("MATLAB:depfun:req:ExcludedBy", edit.MessageId);
        Assert.StartsWith("The file or function is non-deployable", edit.Reason, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(App, "k2_main.m"), edit.NamedBy);
    }

    [Fact]
    public void A_build_says_why_it_took_each_file()
    {
        DependencyReport report = Build();
        DependencyReason Why(string name) => report.Files.Single(f => Path.GetFileName(f.Path) == name).Reason;
        Assert.Equal(DependencyReason.Root, Why("k2_main.m"));
        Assert.Equal(DependencyReason.Code, Why("k2_helper.m"));
        Assert.Equal(DependencyReason.Pragma, Why("k2_pragma.m"));
        Assert.Equal(DependencyReason.Figure, Why("k2_helper.fig"));
        Assert.Equal(DependencyReason.Data, Why("k2_notes.txt"));
        Assert.Equal(new[] { 1, 8 }, report.Products.Select(p => p.Number));
    }

    [Fact]
    public void Files_are_ordered_by_full_path()
    {
        DependencyReport report = Build();
        Assert.Equal(report.Files.Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal), report.Files.Select(f => f.Path));
    }

    [Fact]
    public void Top_only_takes_what_the_roots_name()
    {
        DependencyReport report = Build(options: o => new DependencyOptions { CurrentFolder = o.CurrentFolder, SearchPath = o.SearchPath, TopOnly = true });
        Assert.Equal(Set("app/k2_cellfn.m app/k2_cls.m app/k2_cmd.m app/k2_data.mat app/k2_from_local.m app/k2_handle.m "
            + "app/k2_helper.fig app/k2_helper.m app/k2_main.fig app/k2_main.m app/k2_notes.txt app/k2_pragma.m "
            + "app/k2_script.m lib/k2_libfn.m"), Files(report));
    }

    [Fact]
    public void A_file_off_the_path_finds_its_private_files_and_nothing_beside_it()
    {
        var analysis = new JgsDependencyAnalyzer(new DependencyOptions { CurrentFolder = _root }, Builtins.Contains);
        Assert.Equal(Set("app/k2_helper.m app/private/k2_priv.m"), Files(analysis.Analyze([Path.Combine(App, "k2_helper.m")])));

        var beside = new JgsDependencyAnalyzer(new DependencyOptions { CurrentFolder = _root, SearchRootFolders = true }, Builtins.Contains);
        Assert.Equal(Set("app/k2_from_sub.m app/k2_helper.fig app/k2_helper.m app/private/k2_priv.m"),
            Files(beside.Analyze([Path.Combine(App, "k2_helper.m")])));
    }

    [Fact]
    public void A_name_differing_in_case_is_not_a_file()
    {
        Put(App, "k2_case.m", "function k2_case", "K2_DEEP(1);", "end");
        var analysis = new JgsDependencyAnalyzer(new DependencyOptions { CurrentFolder = App }, Builtins.Contains);
        Assert.Equal(Set("app/k2_case.m"), Files(analysis.Analyze([Path.Combine(App, "k2_case.m")])));
    }

    [Fact]
    public void A_file_that_does_not_parse_is_taken_as_it_is()
    {
        Put(App, "k2_broken.m", "function k2_broken", "x = (1;", "end");
        var analysis = new JgsDependencyAnalyzer(new DependencyOptions { CurrentFolder = App }, Builtins.Contains);
        Assert.Equal(Set("app/k2_broken.m"), Files(analysis.Analyze([Path.Combine(App, "k2_broken.m")])));
    }

    // --- Pragmas, tables, logs --------------------------------------------------------------------

    [Fact]
    public void The_lexer_hands_back_the_pragmas_a_source_holds()
    {
        var pragmas = new List<SourcePragma>();
        Parser.Parse("%#function fa, fb\nx = 1; %#function fd\n  %#exclude fg\n% #function no\ns = '%#function no';\n%#ok<NASGU>\n",
            "", JgsDialect.Matlab, pragmas);
        Assert.Equal(
            new[]
            {
                new SourcePragma(1, "function", "fa, fb"), new SourcePragma(2, "function", "fd"), new SourcePragma(3, "exclude", "fg"),
                new SourcePragma(6, "ok", "<NASGU>"),
            },
            pragmas);
    }

    [Fact]
    public void The_tables_say_what_R2025b_said()
    {
        Assert.Equal(new[] { 1, 8 }, DeploymentRules.ProductsOf("butter"));
        Assert.Equal(new[] { 1 }, DeploymentRules.ProductsOf("ones"));
        Assert.Equal(new[] { 1 }, DeploymentRules.ProductsOf("k2_not_a_name"));
        Assert.Equal("signal", DeploymentRules.ToolboxOf("butter"));
        Assert.Equal(new RuntimeProduct("Signal Processing Toolbox", "25.2", 8), DeploymentRules.Product(8));
        Assert.Equal("toolbox/matlab/codetools/edit.m", DeploymentRules.NonDeployableFile("edit"));
        Assert.Equal("toolbox/matlab/helptools/help.m", DeploymentRules.NonDeployableFile("help"));
        Assert.Null(DeploymentRules.NonDeployableFile("mcrcachedir")); // the runtime answers it
        Assert.Null(DeploymentRules.NonDeployableFile("clc")); // a built-in: its .m is only its help
        Assert.Equal(25, DeploymentRules.DataReaders.Count);
    }

    [Fact]
    public void The_logs_are_laid_out_as_R2025b_writes_them()
    {
        var empty = new DependencyReport([], [], [], []);
        Assert.Equal("Excluded Files\nExcluded Files\t Exclusion Message ID\t Reason For Exclusion\t Exclusion Rule\n", BuildLogs.ExcludedFiles(empty));
        Assert.Equal("Path\tSymbol\tReason\r\n", BuildLogs.UnresolvedSymbols(empty, f => f));
        Assert.Equal("35010\t", BuildLogs.RequiredProducts());
        Assert.Equal("{\r\n\t\"RuntimeDependencies\": [\r\n\t\t{\"ID\":35010, \"DisplayName\":\"JGraph Runtime\", \"Optional\":\"no\"}\r\n\t]\r\n}\r\n",
            BuildLogs.BuildResult());

        DependencyReport report = Build(options: o => new DependencyOptions { CurrentFolder = o.CurrentFolder, SearchPath = o.SearchPath, ClearToolboxPath = true });
        Assert.Equal("Excluded Files\nExcluded Files\t Exclusion Message ID\t Reason For Exclusion\t Exclusion Rule\n"
            + "edit\tMATLAB:depfun:req:ExcludedBy\t" + DeploymentRules.NonDeployableReason + "\t\n"
            + "help\tMATLAB:depfun:req:ExcludedBy\t" + DeploymentRules.NonDeployableReason + "\t\n", BuildLogs.ExcludedFiles(report));
        Assert.Equal("Path\tSymbol\tReason\r\nk2_main\\k2_main.m\tbutter\tmcc_flag\r\n",
            BuildLogs.UnresolvedSymbols(report, f => "k2_main\\" + Path.GetRelativePath(App, f)));

        string output = Path.Combine(_root, "out");
        BuildLogs.WriteAll(output, report, f => f);
        Assert.Equal(
            new[] { "buildresult.json", "includedSupportPackages.txt", "mccExcludedFiles.log", "requiredMCRProducts.txt", "unresolvedSymbols.txt" },
            Directory.GetFiles(output).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(0, new FileInfo(Path.Combine(output, "includedSupportPackages.txt")).Length);
    }

    // --- The tree ---------------------------------------------------------------------------------

    private DependencyReport Build(bool onPath = true, Func<DependencyOptions, DependencyOptions>? options = null)
    {
        var plain = new DependencyOptions { CurrentFolder = App, SearchPath = onPath ? [Lib] : [] };
        var analysis = new JgsDependencyAnalyzer(options?.Invoke(plain) ?? plain, Builtins.Contains);
        return analysis.Analyze([Path.Combine(App, "k2_main.m")]);
    }

    private SortedSet<string> Files(DependencyReport report) =>
        new(report.Files.Select(f => Path.GetRelativePath(_root, f.Path).Replace('\\', '/')), StringComparer.Ordinal);

    private static SortedSet<string> Set(string files) =>
        new(files.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    private static void Put(string folder, string name, params string[] lines)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), string.Join("\n", lines) + "\n");
    }

    /// <summary>probe_k2_depend's tree, as <c>k2_make_tree.m</c> writes it.</summary>
    private void MakeTree()
    {
        Put(App, "k2_main.m",
            "function k2_main(varargin)", "%#function k2_pragma", "%#exclude k2_excl", "k2_helper(1);", "k2_script",
            "o = k2_cls(2); o.go();", "x = load('k2_data.mat');", "t = fileread(\"k2_notes.txt\");",
            "s = readtable('sub/k2_table.csv');", "f = fopen('k2_missing.txt');", "im = imread(fullfile('a', 'b.png'));",
            "feval('k2_byname');", "g = str2func('k2_strfn');", "h = @k2_handle;", "c = cellfun(@k2_cellfn, {1});",
            "k2_excl();", "k2_shadow = 3; disp(k2_shadow)", "k2_cmd hello", "q = x.k2_field;", "w = 'k2_inquote';",
            "k2_libfn(1);", "unknown_k2_fn(3);", "edit k2_main", "help k2_main", "y = butter(2, 0.5);", "z = local_one(1);",
            "end", "function r = local_one(a)", "r = k2_from_local(a);", "end");
        Put(App, "k2_helper.m", "function k2_helper(a)", "k2_priv(a);", "helper_sub(a);", "end",
            "function helper_sub(a)", "k2_from_sub(a);", "end");
        Put(Path.Combine(App, "private"), "k2_priv.m", "function k2_priv(a)", "disp(a)", "end");
        Put(App, "k2_from_sub.m", "function k2_from_sub(a)", "disp(a)", "end");
        Put(App, "k2_script.m", "k2_from_script(1);");
        Put(App, "k2_from_script.m", "function k2_from_script(a)", "disp(a)", "end");
        Put(App, "k2_cls.m", "classdef k2_cls", "    properties", "        V", "    end", "    methods",
            "        function o = k2_cls(v)", "            o.V = v;", "        end", "        function go(o)",
            "            k2_from_method(o.V);", "        end", "    end", "end");
        Put(App, "k2_from_method.m", "function k2_from_method(a)", "k2_deep(a);", "end");
        Put(App, "k2_deep.m", "function k2_deep(a)", "disp(a)", "end");
        Put(App, "k2_from_local.m", "function r = k2_from_local(a)", "r = a;", "end");
        foreach (string n in (string[])["k2_byname", "k2_strfn", "k2_pragma", "k2_handle", "k2_cellfn", "k2_excl",
                     "k2_shadow", "k2_cmd", "k2_field", "k2_inquote", "k2_unused"])
        {
            Put(App, n + ".m", $"function varargout = {n}(varargin)", "varargout = {1};", "end");
        }

        foreach (string data in (string[])["k2_data.mat", "k2_helper.fig", "k2_main.fig", "k2_notes.txt", "k2_unnamed.txt"])
        {
            Put(App, data, "k2");
        }

        Put(Path.Combine(App, "sub"), "k2_table.csv", "a,b", "1,2");
        Put(Lib, "k2_libfn.m", "function k2_libfn(a)", "k2_libdeep(a);", "end");
        Put(Lib, "k2_libdeep.m", "function k2_libdeep(a)", "disp(a)", "end");
        Put(Path.Combine(_root, "other"), "k2_helper.m", "function k2_helper(a)", "disp(-a)", "end");
        Put(Path.Combine(_root, "pack"), "k2_packfn.m", "function k2_packfn", "k2_packdeep;", "end");
        Put(Path.Combine(_root, "pack", "deeper"), "k2_packdeep.m", "function k2_packdeep", "disp(1)", "end");
        Put(Path.Combine(_root, "pack"), "k2_pack.txt", "pack text");
        Put(Path.Combine(_root, "pack", "deeper"), "k2_deeper.txt", "deeper text");
    }
}
