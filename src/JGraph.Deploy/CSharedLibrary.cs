using System.Globalization;
using System.Text;

namespace JGraph.Deploy;

/// <summary>A function a C shared library wraps: its inputs and outputs as its file declares them, and its file.</summary>
/// <remarks>A script wraps as a function of no arguments, a class as its constructor taking and giving any number (k11_build: script, classdef).</remarks>
public sealed record CLibraryFunction(string Name, IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs, string File)
{
    /// <summary>Whether the last input is varargin.</summary>
    public bool TakesVarargin => Inputs is [.., "varargin"];

    /// <summary>Whether the last output is varargout.</summary>
    public bool GivesVarargout => Outputs is [.., "varargout"];

    /// <summary>The name its wrappers take after <c>mlx</c> and <c>mlf</c>: the first letter a capital (<c>mlxK11plain</c>).</summary>
    public string WrapperName => char.ToUpperInvariant(Name[0]) + Name[1..];
}

/// <summary>How far a C shared library build goes, as <c>-T</c> says (k11_build).</summary>
public enum CLibraryStage
{
    /// <summary><c>-T link:lib</c>, the default: the library, its import library and its archive.</summary>
    Link,

    /// <summary><c>-T compile:lib</c>: the generated source compiled into <c>&lt;name&gt;.obj</c>, nothing linked.</summary>
    Compile,

    /// <summary><c>-T codegen</c> or <c>-c</c>: the generated text and the archive, nothing compiled.</summary>
    Codegen,
}

/// <summary>
/// The C compiler a library is built with: its name as R2025b's mbuild names it (<c>Microsoft Visual
/// C++ 2022 (C)</c>) and a way to run one of its tools (<c>cl.exe</c>, <c>link.exe</c>) from a folder,
/// answering the exit code and what the tool wrote.
/// </summary>
public sealed record CToolchain(string Name, Func<string, string, string, (int ExitCode, string Output)> Run);

/// <summary>What a C shared library build writes, besides its archive.</summary>
public sealed record CSharedLibraryOptions
{
    /// <summary>The folder the build writes into.</summary>
    public required string OutputFolder { get; init; }

    /// <summary>The library's name: <c>&lt;name&gt;.dll</c>, <c>&lt;name&gt;Initialize</c>.</summary>
    public required string LibraryName { get; init; }

    /// <summary>The functions the library wraps, in the order their files were given.</summary>
    public required IReadOnlyList<CLibraryFunction> Functions { get; init; }

    /// <summary>The library's version resource.</summary>
    public ProgramVersion Version { get; init; } = ProgramVersion.Default;

    /// <summary>False for <c>-C</c>: the archive is written beside the library as <c>&lt;name&gt;.ctf</c>.</summary>
    public bool EmbedArchive { get; init; } = true;

    /// <summary><c>-g</c>: compiled without optimization, with its symbols in <c>&lt;name&gt;.pdb</c>.</summary>
    public bool Debug { get; init; }

    /// <summary>How far the build goes.</summary>
    public CLibraryStage Stage { get; init; } = CLibraryStage.Link;

    /// <summary>mcc's words, as the generated files' header records them.</summary>
    public IReadOnlyList<string> MccArguments { get; init; } = [];

    /// <summary>When the build ran, for the header.</summary>
    public DateTime Built { get; init; } = DateTime.Now;

    /// <summary>The folder holding <c>mclmcrrt.h</c>; beside JGraph, else the JGraph Runtime's, when not given.</summary>
    public string? IncludeFolder { get; init; }

    /// <summary>The folder holding <c>jgrt.lib</c>; beside JGraph, else the JGraph Runtime's, when not given.</summary>
    public string? LibraryFolder { get; init; }

    /// <summary>The compiler; needed unless the build only generates.</summary>
    public CToolchain? Toolchain { get; init; }

    /// <summary>The temporary folder the object file is compiled into for a linked build; the system's when not given.</summary>
    public string? ScratchFolder { get; init; }
}

/// <summary>A C shared library build's files, and what its compiler said.</summary>
/// <param name="Files">The files in the order <c>compiler.build.Results.Files</c> lists them: the header, the library, the import library.</param>
/// <param name="Manifest">The archive's manifest.</param>
/// <param name="Transcript">The compiler's and the linker's command lines and what they wrote, for <c>-v</c>.</param>
public sealed record CSharedLibraryBuild(IReadOnlyList<string> Files, CtfManifest Manifest, string Transcript);

/// <summary>A C compile or link that failed, with what the tool said.</summary>
public sealed class CLibraryBuildException(string message, string transcript) : Exception(message)
{
    /// <summary>The command lines run and what the tools wrote.</summary>
    public string Transcript { get; } = transcript;
}

/// <summary>
/// A C shared library (compiler and SDK plan, stage K11), as R2025b's mcc <c>-W lib:&lt;name&gt;</c>
/// builds one (<c>record-k11.ps1</c>, <c>recordings/k11_out</c>): <c>&lt;name&gt;.h</c> and
/// <c>&lt;name&gt;.c</c> in R2025b's generated text (for each function an <c>mlx</c> wrapper taking
/// MATLAB's argument arrays and an <c>mlf</c> wrapper taking its arguments by name, beside
/// <c>&lt;name&gt;Initialize</c>, <c>InitializeWithHandlers</c>, <c>Terminate</c> and
/// <c>PrintStackTrace</c>), <c>&lt;name&gt;.def</c> and <c>&lt;name&gt;.exports</c>, all with JGraph's
/// header; the source compiled and linked with MSVC as R2025b's mbuild compiles it, against the
/// JGraph Runtime's headers and <c>jgrt.lib</c>, into <c>&lt;name&gt;.dll</c> (R2025b's version
/// resource, the archive appended) with <c>&lt;name&gt;.lib</c> and <c>&lt;name&gt;.exp</c>; and the
/// readme in JGraph's words. The generated code calls only the C API <c>jgrt.dll</c> exports (stage
/// K10), so a client compiled against the header runs on the JGraph Runtime unchanged.
/// </summary>
/// <remarks>
/// JGraph compiles with the C runtime linked statically (<c>/MT</c>, where mbuild uses <c>/MD</c>), as
/// <c>jgrt.dll</c> is built, so a library needs no Visual C++ redistributable beside the JGraph Runtime.
/// </remarks>
public static class CSharedLibrary
{
    /// <summary>The widest line R2025b's generator lets a broken line run to, its blank before a break aside.</summary>
    private const int Width = 89;

    /// <summary>The headers' folder beside JGraph: <c>extern\include</c>, as MATLAB keeps <c>mclmcrrt.h</c>.</summary>
    public static string IncludeFolderBesideJGraph => Path.Combine(AppContext.BaseDirectory, "extern", "include");

    /// <summary>The import library's folder beside JGraph: <c>extern\lib\win64\microsoft</c>, as MATLAB keeps <c>mclmcrrt.lib</c>.</summary>
    public static string LibraryFolderBesideJGraph => Path.Combine(AppContext.BaseDirectory, "extern", "lib", RuntimeLayout.Platform, "microsoft");

    /// <summary>
    /// Writes the library <paramref name="build"/> makes. A linked build answers the header, the
    /// library and the import library; a compiled one the header and the object file; a generated one
    /// the header, the source and the archive.
    /// </summary>
    /// <exception cref="CLibraryBuildException">The compiler or the linker failed; nothing the build wrote is left.</exception>
    /// <exception cref="FileNotFoundException">The JGraph Runtime's headers or <c>jgrt.lib</c> are missing.</exception>
    public static CSharedLibraryBuild Write(CtfBuild build, CSharedLibraryOptions options)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(options);
        string folder = Path.GetFullPath(options.OutputFolder);
        Directory.CreateDirectory(folder);
        string name = options.LibraryName;
        string Out(string extension) => Path.Combine(folder, name + extension);
        var utf8 = new UTF8Encoding(false);

        byte[] archive;
        CtfManifest manifest;
        using (var stream = new MemoryStream())
        {
            manifest = CtfArchiveWriter.Write(stream, build with { TargetType = CtfTargetType.CSharedLibrary });
            archive = stream.ToArray();
        }

        var written = new List<string> { Out(".h"), Out(".c"), Out(".exports") };
        File.WriteAllText(Out(".h"), Crlf(Header(options)), utf8);
        File.WriteAllText(Out(".c"), Crlf(Source(options)), utf8);
        File.WriteAllText(Out(".exports"), Crlf(Exports(options)), utf8);
        if (options.Stage == CLibraryStage.Codegen)
        {
            // R2025b writes no .def for a build it does not compile (k11_build: codegen, dashc).
            File.Delete(Out(".def"));
            File.WriteAllBytes(Out(".ctf"), archive);
            File.WriteAllText(Path.Combine(folder, "readme.txt"), Readme(options), utf8);
            return new CSharedLibraryBuild([Out(".h"), Out(".c"), Out(".ctf")], manifest, "");
        }

        File.WriteAllText(Out(".def"), Crlf(DefFile(options)), utf8);
        written.Add(Out(".def"));
        CToolchain toolchain = options.Toolchain ?? throw new ArgumentException("A compiled build needs a C compiler.", nameof(options));
        string include = options.IncludeFolder ?? FoundFolder(IncludeFolderBesideJGraph, RuntimeLayout.Find()?.IncludeFolder, "mclmcrrt.h");
        string libraries = options.LibraryFolder ?? FoundFolder(LibraryFolderBesideJGraph, RuntimeLayout.Find()?.LibraryFolder, "jgrt.lib");
        var transcript = new StringBuilder();
        string scratch = Path.Combine(options.ScratchFolder ?? Path.GetTempPath(), "jgcl_" + Guid.NewGuid().ToString("N"));
        try
        {
            string objectFolder = options.Stage == CLibraryStage.Compile ? folder : scratch;
            Directory.CreateDirectory(objectFolder);
            string obj = Path.Combine(objectFolder, name + ".obj");
            string compile = "/c /MT /Zc:__cplusplus /Zp8 /GR /W3 /EHsc- /Zc:wchar_t- /nologo "
                + (options.Debug ? "/Z7" : "/O2 /DNDEBUG")
                + $" /DMATLAB_DEFAULT_RELEASE=R2017b /D_CRT_SECURE_NO_DEPRECATE /I{Quote(folder)} /I{Quote(include)} {Quote(Out(".c"))} /Fo{Quote(obj)}";
            Tool(toolchain, "cl.exe", compile, folder, transcript, written, obj);
            if (options.Stage == CLibraryStage.Compile)
            {
                // The archive is written beside the object file: R2025b writes none at all for
                // -T compile:lib, so its object file has no archive to be linked with (k11_build: compile).
                File.WriteAllBytes(Out(".ctf"), archive);
                File.WriteAllText(Path.Combine(folder, "readme.txt"), Readme(options), utf8);
                return new CSharedLibraryBuild([Out(".h"), obj, Out(".ctf")], manifest, transcript.ToString());
            }

            string debug = options.Debug ? $" /DEBUG /INCREMENTAL:NO /PDB:{Quote(Out(".pdb"))}" : "";
            string link = $"/nologo{debug} /DLL /MANIFEST:NO /def:{Quote(Out(".def"))} {Quote(obj)} /MACHINE:AMD64 "
                + $"/LIBPATH:{Quote(libraries)} jgrt.lib kernel32.lib /out:{Quote(Out(".dll"))}";
            written.AddRange([Out(".dll"), Out(".lib"), Out(".exp")]);
            if (options.Debug)
            {
                written.Add(Out(".pdb"));
            }

            Tool(toolchain, "link.exe", link, folder, transcript, written, Out(".dll"));
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The system's temporary folder clears what a tool still holds open.
            }
        }

        // R2025b's version block, then the archive after the library's last byte, where the
        // runtime's mclGetEmbeddedCtfStream finds it; or beside it for -C.
        PeResources.Stamp(Out(".dll"), [], StandaloneProgram.VersionBlock(options.Version, VersionFileTypeDll), new Dictionary<string, byte[]>());
        if (options.EmbedArchive)
        {
            using FileStream dll = File.Open(Out(".dll"), FileMode.Append, FileAccess.Write);
            dll.Write(archive);
            File.Delete(Out(".ctf"));
        }
        else
        {
            File.WriteAllBytes(Out(".ctf"), archive);
        }

        File.WriteAllText(Path.Combine(folder, "readme.txt"), Readme(options), utf8);
        return new CSharedLibraryBuild([Out(".h"), Out(".dll"), Out(".lib")], manifest, transcript.ToString());
    }

    /// <summary>A version block's file type for a library (VFT_DLL), where R2025b writes 0, the unknown type (k11_pe).</summary>
    private const uint VersionFileTypeDll = 2;

    private static string FoundFolder(string beside, string? runtime, string file)
    {
        if (File.Exists(Path.Combine(beside, file)))
        {
            return beside;
        }

        if (runtime is not null && File.Exists(Path.Combine(runtime, file)))
        {
            return runtime;
        }

        throw new FileNotFoundException(
            $"The JGraph Runtime's {file} was found neither beside JGraph ({beside}) nor in an installed JGraph Runtime {RuntimeLayout.CurrentVersion}.", file);
    }

    /// <summary>Runs one tool, recording its command line and what it wrote; a failure removes what the build wrote.</summary>
    private static void Tool(CToolchain toolchain, string tool, string arguments, string folder, StringBuilder transcript, List<string> written, string made)
    {
        transcript.Append(Path.GetFileNameWithoutExtension(tool)).Append(' ').Append(arguments).Append('\n');
        (int exit, string output) = toolchain.Run(tool, arguments, folder);
        transcript.Append(output.Replace("\r\n", "\n", StringComparison.Ordinal));
        if (exit == 0 && File.Exists(made))
        {
            return;
        }

        foreach (string file in written)
        {
            File.Delete(file);
        }

        string said = string.Join("\n", output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Where(static l => l.Contains(" error ", StringComparison.Ordinal) || l.Contains("fatal error", StringComparison.Ordinal)).Take(10));
        throw new CLibraryBuildException(
            $"{(tool == "cl.exe" ? "Compiling" : "Linking")} the C shared library failed ({tool} exited with {exit})."
            + (said.Length > 0 ? "\n" + said : ""), transcript.ToString());
    }

    private static string Quote(string path) => "\"" + path.TrimEnd('\\') + "\"";

    // --- The generated text ---------------------------------------------------------------

    /// <summary>
    /// The header comment JGraph writes where R2025b names its compiler: JGraph's compiler and the
    /// release it follows, the date as C's asctime writes it, and mcc's words wrapped at 77 columns
    /// after <c> * </c>, as R2025b wraps them.
    /// </summary>
    private static string Banner(CSharedLibraryOptions options)
    {
        var s = new StringBuilder();
        s.Append("/*\n");
        s.Append(" * JGraph Compiler: ").Append(RuntimeLayout.CurrentVersion).Append(" (MATLAB Compiler 25.2, R2025b)\n");
        s.Append(" * Date: ").Append(AscTime(options.Built)).Append('\n');
        s.Append(" * Arguments:\n");
        string arguments = string.Concat(new[] { "-B", "macro_default" }.Concat(options.MccArguments).Select(static w => "\"" + w + "\""));
        for (int at = 0; at < arguments.Length; at += 77)
        {
            s.Append(" * ").Append(arguments.AsSpan(at, Math.Min(77, arguments.Length - at))).Append('\n');
        }

        s.Append(" */\n");
        return s.ToString();
    }

    /// <summary><c>&lt;name&gt;.h</c>, with line feeds (written with CR LF, as R2025b writes it).</summary>
    public static string Header(CSharedLibraryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string lib = options.LibraryName;
        string api = $"LIB_{lib}_C_API";
        var s = new StringBuilder(Banner(options));
        s.Append('\n');
        s.Append("#ifndef ").Append(lib).Append("_h\n");
        s.Append("#define ").Append(lib).Append("_h 1\n\n");
        s.Append("#if defined(__cplusplus) && !defined(mclmcrrt_h) && defined(__linux__)\n");
        s.Append("#  pragma implementation \"mclmcrrt.h\"\n");
        s.Append("#endif\n");
        s.Append("#include \"mclmcrrt.h\"\n");
        s.Append("#ifdef __cplusplus\n");
        s.Append("extern \"C\" { // sbcheck:ok:extern_c\n");
        s.Append("#endif\n\n");
        s.Append("/* This symbol is defined in shared libraries. Define it here\n");
        s.Append(" * (to nothing) in case this isn't a shared library. \n");
        s.Append(" */\n");
        s.Append("#ifndef ").Append(api).Append(" \n");
        s.Append("#define ").Append(api).Append(" /* No special import/export declaration */\n");
        s.Append("#endif\n\n");
        s.Append("/* GENERAL LIBRARY FUNCTIONS -- START */\n\n");
        s.Append("extern ").Append(api).Append(" \n");
        s.Append("bool MW_CALL_CONV ").Append(lib).Append("InitializeWithHandlers(\n");
        s.Append("       mclOutputHandlerFcn error_handler, \n");
        s.Append("       mclOutputHandlerFcn print_handler);\n\n");
        s.Append("extern ").Append(api).Append(" \n");
        s.Append("bool MW_CALL_CONV ").Append(lib).Append("Initialize(void);\n");
        s.Append("extern ").Append(api).Append(" \n");
        s.Append("void MW_CALL_CONV ").Append(lib).Append("Terminate(void);\n\n");
        s.Append("extern ").Append(api).Append(" \n");
        s.Append("void MW_CALL_CONV ").Append(lib).Append("PrintStackTrace(void);\n\n");
        s.Append("/* GENERAL LIBRARY FUNCTIONS -- END */\n\n");
        s.Append("/* C INTERFACE -- MLX WRAPPERS FOR USER-DEFINED MATLAB FUNCTIONS -- START */\n\n");
        foreach (CLibraryFunction f in options.Functions)
        {
            s.Append("extern ").Append(api).Append(" \n");
            s.Append(Broken(MlxSignature(f) + ";", "", MlxOpen(f).Length)).Append("\n\n");
        }

        s.Append("/* C INTERFACE -- MLX WRAPPERS FOR USER-DEFINED MATLAB FUNCTIONS -- END */\n\n");
        s.Append("/* C INTERFACE -- MLF WRAPPERS FOR USER-DEFINED MATLAB FUNCTIONS -- START */\n\n");
        foreach (CLibraryFunction f in options.Functions)
        {
            s.Append("extern ").Append(api).Append(' ').Append(MlfSignature(f, lib)).Append(";\n\n");
        }

        s.Append("#ifdef __cplusplus\n}\n#endif\n");
        s.Append("/* C INTERFACE -- MLF WRAPPERS FOR USER-DEFINED MATLAB FUNCTIONS -- END */\n\n");
        s.Append("#endif\n");
        return s.ToString();
    }

    /// <summary><c>&lt;name&gt;.c</c>, with line feeds (written with CR LF, as R2025b writes it).</summary>
    public static string Source(CSharedLibraryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string lib = options.LibraryName;
        string api = $"LIB_{lib}_C_API";
        var s = new StringBuilder(Banner(options));
        s.Append('\n');
        s.Append("#define EXPORTING_").Append(lib).Append(" 1\n");
        s.Append("#include \"").Append(lib).Append(".h\"\n\n");
        s.Append("static HMCRINSTANCE _mcr_inst = NULL; /* don't use nullptr; this may be either C or C++ */\n\n");
        s.Append("#if defined( _MSC_VER) || defined(__LCC__) || defined(__MINGW64__)\n");
        s.Append("#ifdef __LCC__\n#undef EXTERN_C\n#endif\n");
        s.Append("#define NOMINMAX\n#include <windows.h>\n#undef interface\n\n");
        s.Append("static char path_to_dll[_MAX_PATH];\n\n");
        s.Append("BOOL WINAPI DllMain(HINSTANCE hInstance, DWORD dwReason, void *pv)\n");
        s.Append("{\n");
        s.Append("    if (dwReason == DLL_PROCESS_ATTACH)\n");
        s.Append("    {\n");
        s.Append("        if (GetModuleFileName(hInstance, path_to_dll, _MAX_PATH) == 0)\n");
        s.Append("            return FALSE;\n");
        s.Append("    }\n");
        s.Append("    else if (dwReason == DLL_PROCESS_DETACH)\n");
        s.Append("    {\n");
        s.Append("    }\n");
        s.Append("    return TRUE;\n");
        s.Append("}\n");
        s.Append("#endif\n");
        s.Append("#ifdef __cplusplus\nextern \"C\" { // sbcheck:ok:extern_c\n#endif\n\n");
        s.Append("static int mclDefaultPrintHandler(const char *s)\n");
        s.Append("{\n");
        s.Append("    return mclWrite(1 /* stdout */, s, sizeof(char)*strlen(s));\n");
        s.Append("}\n\n");
        s.Append("#ifdef __cplusplus\n} /* End extern C block */\n#endif\n\n");
        s.Append("#ifdef __cplusplus\nextern \"C\" { // sbcheck:ok:extern_c\n#endif\n\n");
        s.Append("static int mclDefaultErrorHandler(const char *s)\n");
        s.Append("{\n");
        s.Append("    int written = 0;\n");
        s.Append("    size_t len = 0;\n");
        s.Append("    len = strlen(s);\n");
        s.Append("    written = mclWrite(2 /* stderr */, s, sizeof(char)*len);\n");
        s.Append("    if (len > 0 && s[ len-1 ] != '\\n')\n");
        s.Append("        written += mclWrite(2 /* stderr */, \"\\n\", sizeof(char));\n");
        s.Append("    return written;\n");
        s.Append("}\n\n");
        s.Append("#ifdef __cplusplus\n} /* End extern C block */\n#endif\n\n");
        s.Append("/* This symbol is defined in shared libraries. Define it here\n");
        s.Append(" * (to nothing) in case this isn't a shared library. \n");
        s.Append(" */\n");
        s.Append("#ifndef ").Append(api).Append('\n');
        s.Append("#define ").Append(api).Append(" /* No special import/export declaration */\n");
        s.Append("#endif\n\n");
        s.Append(api).Append(" \n");
        s.Append("bool MW_CALL_CONV ").Append(lib).Append("InitializeWithHandlers(\n");
        s.Append("    mclOutputHandlerFcn error_handler,\n");
        s.Append("    mclOutputHandlerFcn print_handler)\n");
        s.Append("{\n");
        s.Append("    int bResult = 0;\n");
        s.Append("    if (_mcr_inst)\n");
        s.Append("        return true;\n");
        s.Append("    if (!mclmcrInitialize())\n");
        s.Append("        return false;\n");
        s.Append("    if (!GetModuleFileName(GetModuleHandle(\"").Append(lib).Append("\"), path_to_dll, _MAX_PATH))\n");
        s.Append("        return false;\n");
        if (options.EmbedArchive)
        {
            s.Append("    {\n");
            s.Append("        mclCtfStream ctfStream = \n");
            s.Append("            mclGetEmbeddedCtfStream(path_to_dll);\n");
            s.Append("        if (ctfStream) {\n");
            s.Append("            bResult = mclInitializeComponentInstanceEmbedded(&_mcr_inst,\n");
            s.Append("                                                             error_handler, \n");
            s.Append("                                                             print_handler,\n");
            s.Append("                                                             ctfStream,\n");
            s.Append("                                                             NULL);\n");
            s.Append("            mclDestroyStream(ctfStream);\n");
            s.Append("        } else {\n");
            s.Append("            bResult = 0;\n");
            s.Append("        }\n");
            s.Append("    }  \n");
        }
        else
        {
            // -C: the archive is <name>.ctf beside the library (k11_build: separate).
            s.Append("    bResult = mclInitializeComponentInstanceNonEmbeddedStandalone(&_mcr_inst,\n");
            s.Append("        path_to_dll,\n");
            s.Append("        \"").Append(lib).Append("\",\n");
            s.Append("        LibTarget,\n");
            s.Append("        error_handler, \n");
            s.Append("        print_handler,\n");
            s.Append("        NULL);\n");
        }

        s.Append("    if (!bResult)\n");
        s.Append("    return false;\n");
        s.Append("    return true;\n");
        s.Append("}\n\n");
        s.Append(api).Append(" \n");
        s.Append("bool MW_CALL_CONV ").Append(lib).Append("Initialize(void)\n");
        s.Append("{\n");
        s.Append(Broken($"return {lib}InitializeWithHandlers(mclDefaultErrorHandler, mclDefaultPrintHandler);", "    ",
            BodyIndent($"return {lib}InitializeWithHandlers("))).Append('\n');
        s.Append("}\n\n");
        s.Append(api).Append(" \n");
        s.Append("void MW_CALL_CONV ").Append(lib).Append("Terminate(void)\n");
        s.Append("{\n");
        s.Append("    if (_mcr_inst)\n");
        s.Append("        mclTerminateInstance(&_mcr_inst);\n");
        s.Append("}\n\n");
        s.Append(api).Append(" \n");
        s.Append("void MW_CALL_CONV ").Append(lib).Append("PrintStackTrace(void) \n");
        s.Append("{\n");
        s.Append("    char** stackTrace;\n");
        s.Append("    int stackDepth = mclGetStackTrace(&stackTrace);\n");
        s.Append("    int i;\n");
        s.Append("    for(i=0; i<stackDepth; i++)\n");
        s.Append("    {\n");
        s.Append("        mclWrite(2 /* stderr */, stackTrace[i], sizeof(char)*strlen(stackTrace[i]));\n");
        s.Append("        mclWrite(2 /* stderr */, \"\\n\", sizeof(char)*strlen(\"\\n\"));\n");
        s.Append("    }\n");
        s.Append("    mclFreeStackTrace(&stackTrace, stackDepth);\n");
        s.Append("}\n\n\n");
        foreach (CLibraryFunction f in options.Functions)
        {
            s.Append(api).Append(" \n");
            s.Append(Broken(MlxSignature(f), "", MlxOpen(f).Length)).Append('\n');
            s.Append("{\n");
            s.Append(Broken($"return mclFeval(_mcr_inst, \"{f.Name}\", nlhs, plhs, nrhs, prhs);", "    ", BodyIndent("return mclFeval("))).Append('\n');
            s.Append("}\n\n");
        }

        foreach (CLibraryFunction f in options.Functions)
        {
            s.Append(api).Append(" \n");
            s.Append(Broken(MlfSignature(f, lib), "", MlfOpen(f).Length)).Append('\n');
            s.Append("{\n");
            s.Append("    return ").Append(MlfCall(f, lib)).Append(";\n");
            s.Append("}\n\n");
        }

        return s.ToString();
    }

    /// <summary>
    /// Where a body statement's broken lines start (k11_names: long, longlib): two columns left of the
    /// end of <paramref name="open"/>, the statement up to its call's parenthesis, after the body's four,
    /// and never past 89.
    /// </summary>
    private static int BodyIndent(string open) => Math.Min(2 + open.Length, Width);

    /// <summary><c>&lt;name&gt;.def</c>: the library, then every export (k11_out).</summary>
    public static string DefFile(CSharedLibraryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return $"LIBRARY {options.LibraryName}\nEXPORTS\n" + Exports(options);
    }

    /// <summary>
    /// <c>&lt;name&gt;.exports</c>: the four library functions, then each function's <c>mlx</c> and
    /// <c>mlf</c> wrappers, one name to a line and an empty line last.
    /// </summary>
    public static string Exports(CSharedLibraryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string lib = options.LibraryName;
        var s = new StringBuilder();
        foreach (string suffix in new[] { "Initialize", "InitializeWithHandlers", "Terminate", "PrintStackTrace" })
        {
            s.Append(lib).Append(suffix).Append('\n');
        }

        foreach (CLibraryFunction f in options.Functions)
        {
            s.Append("mlx").Append(f.WrapperName).Append('\n');
            s.Append("mlf").Append(f.WrapperName).Append('\n');
        }

        return s.Append('\n').ToString();
    }

    private static string MlxOpen(CLibraryFunction f) => $"bool MW_CALL_CONV mlx{f.WrapperName}(";

    private static string MlxSignature(CLibraryFunction f) => MlxOpen(f) + "int nlhs, mxArray *plhs[], int nrhs, mxArray *prhs[])";

    private static string MlfOpen(CLibraryFunction f) => $"bool MW_CALL_CONV mlf{f.WrapperName}(";

    /// <summary>An <c>mlf</c> wrapper's declaration: <c>nargout</c> when the function gives any output, a pointer per output, then each input.</summary>
    private static string MlfSignature(CLibraryFunction f, string library)
    {
        (IReadOnlyList<string> outputs, IReadOnlyList<string> inputs) = ArgumentNames(f, library);
        var parameters = new List<string>();
        if (outputs.Count > 0)
        {
            parameters.Add("int nargout");
        }

        parameters.AddRange(outputs.Select(static o => "mxArray** " + o));
        parameters.AddRange(inputs.Select(static i => "mxArray* " + i));
        return MlfOpen(f) + string.Join(", ", parameters) + ")";
    }

    /// <summary>
    /// An <c>mlf</c> wrapper's call: the output and input counts, each negative and one more for a
    /// trailing varargout or varargin (k10_out, k11_out), then the arguments in order.
    /// </summary>
    private static string MlfCall(CLibraryFunction f, string library)
    {
        (IReadOnlyList<string> outputs, IReadOnlyList<string> inputs) = ArgumentNames(f, library);
        int fnout = f.GivesVarargout ? -outputs.Count : outputs.Count;
        int fnin = f.TakesVarargin ? -inputs.Count : inputs.Count;
        var call = new StringBuilder();
        call.Append("mclMlfFeval(_mcr_inst, \"").Append(f.Name).Append("\", ").Append(outputs.Count > 0 ? "nargout" : "0")
            .Append(", ").Append(fnout.ToString(CultureInfo.InvariantCulture)).Append(", ").Append(fnin.ToString(CultureInfo.InvariantCulture));
        foreach (string argument in outputs.Concat(inputs))
        {
            call.Append(", ").Append(argument);
        }

        return call.Append(')').ToString();
    }

    /// <summary>
    /// The names R2025b's generator gives a <c>0</c> as an argument's C name (k11_build: cwords;
    /// k11_names: cpp, cw1 to cw3): C's keywords, <c>inline</c>, <c>NULL</c>, <c>bool</c>,
    /// <c>true</c> and <c>class</c>.
    /// </summary>
    private static readonly HashSet<string> R2025bReserved = new(StringComparer.Ordinal)
    {
        "auto", "break", "case", "char", "const", "continue", "default", "do", "double", "else", "enum", "extern", "float", "for",
        "goto", "if", "int", "long", "register", "return", "short", "signed", "sizeof", "static", "struct", "switch", "typedef",
        "union", "unsigned", "void", "volatile", "while", "inline", "NULL", "bool", "true", "class",
    };

    /// <summary>
    /// The names JGraph gives a <c>0</c> as well, each of which R2025b leaves to break the library's
    /// build or a C++ client's (k11_names): the rest of C's and C++'s keywords, and the types and
    /// macros the generated code's parameter lists and calls are made of.
    /// </summary>
    private static readonly HashSet<string> AlsoReserved = new(StringComparer.Ordinal)
    {
        "restrict", "false", "alignas", "alignof", "constexpr", "nullptr", "static_assert", "thread_local", "typeof", "typeof_unqual",
        "and", "and_eq", "asm", "bitand", "bitor", "catch", "char8_t", "char16_t", "char32_t", "compl", "concept", "consteval",
        "constinit", "const_cast", "co_await", "co_return", "co_yield", "decltype", "delete", "dynamic_cast", "explicit", "export",
        "friend", "mutable", "namespace", "new", "noexcept", "not", "not_eq", "operator", "or", "or_eq", "private", "protected",
        "public", "reinterpret_cast", "requires", "static_cast", "template", "this", "throw", "try", "typeid", "typename", "using",
        "virtual", "wchar_t", "xor", "xor_eq",
        "mxArray", "MW_CALL_CONV", "EXTERN_C", "TRUE", "FALSE", "mclMlfFeval",
    };

    /// <summary>
    /// The C names of a function's outputs and inputs in its <c>mlf</c> wrapper, as R2025b's generator
    /// makes them (k11_build: same, tilde, cwords; k11_names): each as the file names it; an ignored
    /// input (<c>~</c>) <c>_U4b</c>, <c>_U4c</c> and so on; an input named as an output with <c>_in</c>
    /// and the first number that names no other argument; a reserved word with a <c>0</c>. JGraph
    /// reserves more words than R2025b (<see cref="AlsoReserved"/>), the library's own macros, and
    /// <c>nargout</c> beside the wrapper's own, so the wrapper compiles where R2025b's would not.
    /// </summary>
    internal static (IReadOnlyList<string> Outputs, IReadOnlyList<string> Inputs) ArgumentNames(CLibraryFunction f, string library = "")
    {
        var taken = new HashSet<string>(f.Outputs.Concat(f.Inputs), StringComparer.Ordinal);
        bool hasNargout = f.Outputs.Count > 0;
        bool Reserved(string name) => R2025bReserved.Contains(name) || AlsoReserved.Contains(name)
            || (hasNargout && name == "nargout")
            || (library.Length > 0 && (name == $"LIB_{library}_C_API" || name == $"EXPORTING_{library}" || name == $"{library}_h"));
        string Unreserved(string name)
        {
            if (!Reserved(name))
            {
                return name;
            }

            string renamed = name + "0";
            while (taken.Contains(renamed))
            {
                renamed += "0";
            }

            taken.Add(renamed);
            return renamed;
        }

        var outputs = new List<string>();
        foreach (string output in f.Outputs)
        {
            outputs.Add(Unreserved(output));
        }

        var inputs = new List<string>();
        int ignored = 0;
        foreach (string input in f.Inputs)
        {
            if (input == "~")
            {
                inputs.Add("_U" + (0x4b + ignored++).ToString("x", CultureInfo.InvariantCulture));
            }
            else if (f.Outputs.Contains(input, StringComparer.Ordinal))
            {
                int k = 1;
                while (taken.Contains($"{input}_in{k}"))
                {
                    k++;
                }

                string renamed = $"{input}_in{k}";
                taken.Add(renamed);
                inputs.Add(renamed);
            }
            else
            {
                inputs.Add(Unreserved(input));
            }
        }

        return (outputs, inputs);
    }

    /// <summary>
    /// R2025b's line breaking (k11_out, k11_names): the text's words, separated by single blanks,
    /// follow <paramref name="prefix"/>; a word goes on the line when the line, its blank before the
    /// word included, and the word stay within 89 columns, the text's last character (a definition's
    /// closing parenthesis, a statement's semicolon) not counted, else on a new line indented by
    /// <paramref name="indent"/>, where the first word goes however long. A broken line keeps its blank.
    /// </summary>
    internal static string Broken(string text, string prefix, int indent)
    {
        var s = new StringBuilder(prefix);
        int column = prefix.Length;
        bool lineStart = true;
        string[] words = text.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i];
            int counted = i == words.Length - 1 ? word.Length - 1 : word.Length;
            if (!lineStart && column + counted > Width)
            {
                s.Append('\n').Append(' ', indent);
                column = indent;
            }

            s.Append(word);
            column += word.Length;
            lineStart = false;
            if (i < words.Length - 1)
            {
                s.Append(' ');
                column++;
            }
        }

        return s.ToString();
    }

    /// <summary>C's asctime, as R2025b's header dates: <c>Sat Oct 24 19:23:23 2026</c>.</summary>
    private static string AscTime(DateTime when) =>
        when.ToString("ddd MMM ", CultureInfo.InvariantCulture) + when.Day.ToString(CultureInfo.InvariantCulture).PadLeft(2)
        + when.ToString(" HH:mm:ss yyyy", CultureInfo.InvariantCulture);

    // --- The readme and the getting-started page --------------------------------------------

    /// <summary>
    /// readme.txt: JGraph's own text under R2025b's title line and three numbered sections
    /// (k11_build: main.readme), with CR LF line ends as R2025b writes them.
    /// </summary>
    public static string Readme(CSharedLibraryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string name = options.LibraryName;
        string version = RuntimeLayout.CurrentVersion;
        var s = new StringBuilder();
        s.Append(name).Append(" C Shared Library\n\n");
        s.Append("1. Prerequisites for Deployment \n\n");
        s.Append("Verify that the JGraph Runtime (").Append(version).Append(") is installed, with its runtime\\win64 folder\n")
            .Append("on the PATH, where a program finds jgrt.dll, the native bridge the library calls.\n")
            .Append("A program built against the library compiles with the runtime's include folder and\n")
            .Append("links with ").Append(name).Append(".lib and the runtime's lib\\win64\\jgrt.lib (on the computer that\n")
            .Append("builds it, JGraph's extern\\include and extern\\lib\\win64\\microsoft hold the same).\n\n");
        s.Append("2. Files to Deploy and Package\n\n\n");
        s.Append("Files to Package\n================\n");
        s.Append('-').Append(name).Append(".dll\n");
        s.Append('-').Append(name).Append(".h\n");
        s.Append('-').Append(name).Append(".lib\n");
        if (!options.EmbedArchive || options.Stage != CLibraryStage.Link)
        {
            s.Append('-').Append(name).Append(".ctf (the deployable archive, kept beside the library)\n");
        }

        s.Append("-The JGraph Runtime installer, when the computer has no JGraph Runtime\n");
        s.Append("-This readme file\n\n\n");
        s.Append("3. Definitions\n\n");
        s.Append("A C shared library is a DLL whose mlx and mlf functions call the MATLAB functions\n")
            .Append("in its deployable archive, appended to the DLL, on the JGraph Runtime; a program\n")
            .Append("starts it with ").Append(name).Append("Initialize and ends it with ").Append(name).Append("Terminate.\n\n\n\n");
        return s.ToString().Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    /// <summary>Writes <c>GettingStarted.html</c> into <paramref name="folder"/> for the library <paramref name="name"/>, and answers its path.</summary>
    public static string WriteGettingStarted(string folder, string name)
    {
        string path = Path.Combine(folder, "GettingStarted.html");
        File.WriteAllText(path, GettingStarted(name), new UTF8Encoding(false));
        return path;
    }

    /// <summary><c>GettingStarted.html</c>, in JGraph's words (R2025b's has line feeds, k11_build: api_default.gettingstarted).</summary>
    private static string GettingStarted(string name) =>
        $"""
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>Getting started with the {name} C shared library</title>
</head>
<body>
<h1>Getting started with the {name} C shared library</h1>
<ol>
<li>Install the JGraph Runtime {RuntimeLayout.CurrentVersion}, or set JGRAPH_RUNTIME_ROOT to its folder, and put its runtime\win64 folder on the PATH.</li>
<li>Compile a program with the runtime's include folder and link it with {name}.lib and jgrt.lib:
<pre>cl /I "%JGRAPH_RUNTIME_ROOT%\include" driverApplication.c {name}.lib "%JGRAPH_RUNTIME_ROOT%\lib\win64\jgrt.lib"</pre></li>
<li>In the program, call <code>mclInitializeApplication</code>, then <code>{name}Initialize</code>, the library's <code>mlx</code> and <code>mlf</code> functions,
<code>{name}Terminate</code> and <code>mclTerminateApplication</code>, with {name}.dll beside the program.</li>
</ol>
</body>
</html>

""".Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Crlf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
}
