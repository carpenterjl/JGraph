using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace JGraph.Deploy;

/// <summary>A function a .NET class wraps: its inputs and outputs as its file declares them, its help's first line, and its file.</summary>
public sealed record DotNetFunction(string Name, IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs, string H1, string File)
{
    /// <summary>The named inputs, varargin aside.</summary>
    public int NamedInputs => Inputs.Count - (TakesVarargin ? 1 : 0);

    /// <summary>The named outputs, varargout aside.</summary>
    public int NamedOutputs => Outputs.Count - (GivesVarargout ? 1 : 0);

    /// <summary>Whether the last input is varargin.</summary>
    public bool TakesVarargin => Inputs is [.., "varargin"];

    /// <summary>Whether the last output is varargout.</summary>
    public bool GivesVarargout => Outputs is [.., "varargout"];
}

/// <summary>A class of a .NET assembly and the functions it wraps, in the order its files were given.</summary>
public sealed record DotNetClass(string Name, IReadOnlyList<DotNetFunction> Functions);

/// <summary>What a .NET assembly build writes, besides its archive.</summary>
public sealed record DotNetAssemblyOptions
{
    /// <summary>The folder the build writes into.</summary>
    public required string OutputFolder { get; init; }

    /// <summary><c>-W dotnet:</c>'s parts.</summary>
    public required DotNetWrapper Wrapper { get; init; }

    /// <summary>The classes, the <c>-W</c> class first.</summary>
    public required IReadOnlyList<DotNetClass> Classes { get; init; }

    /// <summary>The assembly's version.</summary>
    public ProgramVersion Version { get; init; } = ProgramVersion.Default;

    /// <summary>Whether the archive goes into the assemblies (false for <c>-C</c>: <c>&lt;name&gt;.ctf</c> beside them).</summary>
    public bool EmbedArchive { get; init; } = true;

    /// <summary><c>-g</c>: a debug build with symbols.</summary>
    public bool Debug { get; init; }

    /// <summary>mcc's words, as the generated files' header records them.</summary>
    public IReadOnlyList<string> MccArguments { get; init; } = [];

    /// <summary>JGraph.MATLAB.NET.Arrays.dll, which the assemblies are compiled against; beside JGraph when not given.</summary>
    public string? ArraysAssembly { get; init; }

    /// <summary>When the build ran, for the header.</summary>
    public DateTime Built { get; init; } = DateTime.Now;
}

/// <summary>
/// A .NET assembly for the MWArray API (compiler and SDK plan, stage K8), as R2025b's mcc
/// <c>-W dotnet:</c> builds one (<c>record-k8.ps1</c>, <c>recordings/k8_out</c>): for each class a
/// <c>&lt;Class&gt;.cs</c> in its MWArray form and a <c>&lt;Class&gt;Native.cs</c> in its native form, in
/// R2025b's generated shape with JGraph's namespaces and header; both compiled (with Roslyn, where
/// R2025b runs csc or the .NET SDK) into <c>&lt;name&gt;.dll</c> and <c>&lt;name&gt;Native.dll</c> with the
/// archive as a manifest resource and their XML documentation; for .NET 5.0 the two <c>.csproj</c>
/// files R2025b builds with, for 4.0 its <c>&lt;name&gt;Version.cs</c>; the overview, GettingStarted.html
/// and the readme in JGraph's own words. Every assembly targets .NET 8 (decision KO4).
/// </summary>
public static class DotNetAssembly
{
    /// <summary>The assembly a client and the generated classes use, where R2025b's is MWArray.dll.</summary>
    public const string ArraysAssemblyName = "JGraph.MATLAB.NET.Arrays";

    private const int Width = 90;

    /// <summary>JGraph.MATLAB.NET.Arrays.dll beside JGraph, or null when it is not there.</summary>
    public static string? ArraysAssemblyBesideJGraph()
    {
        foreach (string? folder in new[] { Path.GetDirectoryName(typeof(DotNetAssembly).Assembly.Location), AppContext.BaseDirectory })
        {
            if (folder is { Length: > 0 } && Path.Combine(folder, ArraysAssemblyName + ".dll") is var candidate && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Writes the assembly <paramref name="build"/> makes, and answers its files in the order
    /// <c>compiler.build.Results.Files</c> lists them (k8_build: the assembly, the native one, the
    /// overview, a separate archive) and the archive's manifest. GettingStarted.html is compiler.build's
    /// (<see cref="GettingStarted"/>): R2025b's mcc alone writes none (k8_build: mcc_plain).
    /// </summary>
    /// <exception cref="MccException">The generated classes would not compile.</exception>
    /// <exception cref="FileNotFoundException">JGraph.MATLAB.NET.Arrays.dll is missing.</exception>
    public static (IReadOnlyList<string> Files, CtfManifest Manifest) Write(CtfBuild build, DotNetAssemblyOptions options)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(options);
        string arrays = options.ArraysAssembly ?? ArraysAssemblyBesideJGraph()
            ?? throw new FileNotFoundException($"{ArraysAssemblyName}.dll was not found beside JGraph.");
        string folder = Path.GetFullPath(options.OutputFolder);
        Directory.CreateDirectory(folder);
        string name = options.Wrapper.ShortName;
        string ctfName = name + ".ctf";
        var crlf = new UTF8Encoding(false);

        byte[] archive;
        CtfManifest manifest;
        using (var stream = new MemoryStream())
        {
            manifest = CtfArchiveWriter.Write(stream, build with { TargetType = CtfTargetType.DotNetAssembly });
            archive = stream.ToArray();
        }

        string separate = Path.Combine(folder, ctfName);
        if (options.EmbedArchive)
        {
            File.Delete(separate);
        }
        else
        {
            File.WriteAllBytes(separate, archive);
        }

        var managed = new List<(string File, string Text)>();
        var native = new List<(string File, string Text)>();
        for (int c = 0; c < options.Classes.Count; c++)
        {
            DotNetClass cls = options.Classes[c];
            managed.Add((cls.Name + ".cs", ClassSource(options, cls, nativeApi: false, first: c == 0)));
            native.Add((cls.Name + "Native.cs", ClassSource(options, cls, nativeApi: true, first: c == 0)));
        }

        foreach ((string file, string text) in native.Concat(managed))
        {
            File.WriteAllText(Path.Combine(folder, file), Crlf(text), crlf);
        }

        string version = options.Version.ToString();
        string? versionFile = null;
        if (options.Wrapper.Framework == "4.0")
        {
            versionFile = name + "Version.cs";
            File.WriteAllText(Path.Combine(folder, versionFile), Crlf(VersionSource(version)), crlf);
        }
        else
        {
            File.WriteAllText(Path.Combine(folder, name + ".csproj"), Crlf(Project(managed.Select(static m => m.File), ctfName, arrays, version, options.EmbedArchive)), crlf);
            File.WriteAllText(Path.Combine(folder, name + "Native.csproj"), Crlf(Project(native.Select(static m => m.File), ctfName, arrays, version, options.EmbedArchive)), crlf);
        }

        string assembly = Path.Combine(folder, name + ".dll");
        string nativeAssembly = Path.Combine(folder, name + "Native.dll");
        ResourceDescription[] resources = options.EmbedArchive
            ? [new ResourceDescription(ctfName, () => new MemoryStream(archive), isPublic: true)]
            : [];
        Compile(name, managed, version, [arrays], assembly, options.Debug, options.Wrapper.KeyFile, resources);
        Compile(name + "Native", native, version, [arrays], nativeAssembly, options.Debug, options.Wrapper.KeyFile, resources);

        string overview = Path.Combine(folder, name + "_overview.html");
        File.WriteAllText(overview, Crlf(Overview(options)), crlf);
        File.WriteAllText(Path.Combine(folder, "readme.txt"), Crlf(Readme(options)), crlf);

        var files = new List<string> { assembly, nativeAssembly, overview };
        if (!options.EmbedArchive)
        {
            files.Add(separate);
        }

        return (files, manifest);
    }

    /// <summary>A class's source in its MWArray or native form, in R2025b's generated shape.</summary>
    public static string ClassSource(DotNetAssemblyOptions options, DotNetClass cls, bool nativeApi, bool first)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(cls);
        string type = nativeApi ? "Object" : "MWArray";
        string ns = options.Wrapper.AssemblyName + (nativeApi ? "Native" : "");
        var s = new StringBuilder();
        s.Append("/*\n");
        s.Append("* JGraph Compiler: ").Append(RuntimeLayout.CurrentVersion).Append(" (MATLAB Compiler 25.2, R2025b)\n");
        s.Append("* Date: ").Append(AscTime(options.Built)).Append('\n');
        s.Append("* Arguments:\n");
        string arguments = string.Concat(new[] { "-B", "macro_default" }.Concat(options.MccArguments).Select(static w => "\"" + w + "\""));
        for (int at = 0; at < arguments.Length; at += 88)
        {
            s.Append("* ").Append(arguments.AsSpan(at, Math.Min(88, arguments.Length - at))).Append('\n');
        }

        s.Append("*/\n");
        s.Append("using System;\nusing System.Reflection;\n");
        if (options.EmbedArchive)
        {
            s.Append("using System.IO;\n");
        }

        s.Append("using JGraph.MATLAB.NET.Arrays;\nusing JGraph.MATLAB.NET.Utility;\n\n");
        if (first)
        {
            s.Append("#if SHARED\n[assembly: System.Reflection.AssemblyKeyFile(@\"").Append(options.Wrapper.KeyFile ?? "").Append("\")]\n#endif\n\n");
        }

        s.Append("namespace ").Append(ns).Append("\n{\n\n");
        s.Append("  /// <summary>\n");
        Wrap(s, "  /// ", $"The {cls.Name} class provides a CLS compliant, {(nativeApi ? "Object (native)" : "MWArray")} interface to the MATLAB functions contained in the files:");
        foreach (DotNetFunction function in cls.Functions)
        {
            s.Append("  /// <newpara></newpara>\n");
            Wrap(s, "  /// ", function.File);
        }

        s.Append("  /// </summary>\n  /// <remarks>\n");
        s.Append("  /// @Version ").Append(options.Version.Major).Append('.').Append(options.Version.Minor).Append('\n');
        s.Append("  /// </remarks>\n");
        s.Append("  public class ").Append(cls.Name).Append(" : IDisposable\n  {\n");
        s.Append(Constructors(cls.Name, options));
        foreach (DotNetFunction function in cls.Functions)
        {
            Methods(s, function, type, nativeApi);
        }

        s.Append("""

    /// <summary>
    /// This method will cause a MATLAB figure window to behave as a modal dialog box.
    /// The method will not return until all the figure windows associated with this
    /// component have been closed.
    /// </summary>
    /// <remarks>
    /// An application should only call this method when required to keep the
    /// MATLAB figure window from disappearing.  Other techniques, such as calling
    /// Console.ReadLine() from the application should be considered where
    /// possible.</remarks>
    ///
    public void WaitForFiguresToDie()
    {
      mcr.WaitForFiguresToDie();
    }



    #endregion Methods

    #region Class Members

    private static MWMCR mcr= null;

    private static Exception ex_= null;

    private bool disposed= false;

    #endregion Class Members
  }
}

""".Replace("\r\n", "\n", StringComparison.Ordinal));
        return s.ToString();
    }

    private static string Constructors(string cls, DotNetAssemblyOptions options)
    {
        string name = options.Wrapper.ShortName;
        string open = options.EmbedArchive
            ? $$"""
          string ctfFileName = "{{name}}.ctf";

          Stream embeddedCtfStream = null;

          String[] resourceStrings = assembly.GetManifestResourceNames();

          foreach (String name in resourceStrings)
          {
            if (name.Contains(ctfFileName))
            {
              embeddedCtfStream = assembly.GetManifestResourceStream(name);
              break;
            }
          }
          mcr= new MWMCR("",
                         ctfFilePath, embeddedCtfStream, true);
"""
            : $$"""
          mcr= new MWMCR("{{name}}",
                         ctfFilePath, true);
""";
        string text = $$"""
    #region Constructors

    /// <summary internal= "true">
    /// The static constructor instantiates and initializes the MATLAB Runtime instance.
    /// </summary>
    static {{cls}}()
    {
      if (MWMCR.MCRAppInitialized)
      {
        try
        {
          System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();

          string ctfFilePath= assembly.Location;

		  int lastDelimiter = ctfFilePath.LastIndexOf(@"/");

	      if (lastDelimiter == -1)
		  {
		    lastDelimiter = ctfFilePath.LastIndexOf(@"\");
		  }

          ctfFilePath= ctfFilePath.Remove(lastDelimiter, (ctfFilePath.Length - lastDelimiter));

{{open}}
        }
        catch(Exception ex)
        {
          ex_ = new Exception("MWArray assembly failed to be initialized", ex);
        }
      }
      else
      {
        ex_ = new ApplicationException("MWArray assembly could not be initialized");
      }
    }


    /// <summary>
    /// Constructs a new instance of the {{cls}} class.
    /// </summary>
    public {{cls}}()
    {
      if(ex_ != null)
      {
        throw ex_;
      }
    }


    #endregion Constructors

    #region Finalize

    /// <summary internal= "true">
    /// Class destructor called by the CLR garbage collector.
    /// </summary>
    ~{{cls}}()
    {
      Dispose(false);
    }


    /// <summary>
    /// Frees the native resources associated with this object
    /// </summary>
    public void Dispose()
    {
      Dispose(true);

      GC.SuppressFinalize(this);
    }


    /// <summary internal= "true">
    /// Internal dispose function
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
      if (!disposed)
      {
        disposed= true;

        if (disposing)
        {
          // Free managed resources;
        }

        // Free native resources
      }
    }


    #endregion Finalize

    #region Methods


""";
        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>A function's methods: one output for each count of inputs, many outputs for each, and the array form.</summary>
    private static void Methods(StringBuilder s, DotNetFunction f, string type, bool nativeApi)
    {
        IReadOnlyList<string> named = [.. f.Inputs.Take(f.NamedInputs)];
        bool single = f.NamedOutputs > 0;
        string plural = type + "s";

        void Docs(string summary, int inputs, bool many, bool varargin)
        {
            s.Append("    /// <summary>\n");
            Wrap(s, "    /// ", summary);
            s.Append("    /// </summary>\n    /// <remarks>\n    /// M-Documentation:\n");
            Wrap(s, "    /// ", f.H1);
            s.Append("    /// </remarks>\n");
            if (many)
            {
                s.Append("    /// <param name=\"numArgsOut\">The number of output arguments to return.</param>\n");
            }

            for (int i = 0; i < inputs; i++)
            {
                s.Append("    /// <param name=\"").Append(named[i]).Append("\">Input argument #").Append(i + 1).Append("</param>\n");
            }

            if (varargin)
            {
                Wrap(s, "    /// ", $"<param name=\"varargin\">Array of {plural} representing the input arguments {inputs + 1} through varargin.length{(inputs > 0 ? "+" + inputs : "")}</param>");
            }

            if (many)
            {
                s.Append("    /// <returns>An Array of length \"numArgsOut\" containing the output\n    /// arguments.</returns>\n");
            }
            else if (single)
            {
                s.Append("    /// <returns>An ").Append(type).Append(" containing the first output argument.</returns>\n");
            }

            s.Append("    ///\n");
        }

        string Arguments(int inputs) => string.Join(", ", named.Take(inputs).Select(n => type + " " + n));
        string Passed(int inputs) => inputs == 0 ? $"new {type}[]{{}}" : string.Join(", ", named.Take(inputs));

        void Body(bool varargin, bool many)
        {
            string target = many ? "numArgsOut, " : single ? "" : "0, ";
            string prefix = single || many ? "return " : "";
            if (!varargin)
            {
                return;
            }

            if (named.Count == 0)
            {
                s.Append("      ").Append(prefix).Append("mcr.EvaluateFunction(").Append(target).Append('"').Append(f.Name).Append("\", varargin);\n");
            }
            else if (nativeApi)
            {
                s.Append("      Object[] argsIn= {").Append(string.Join(", ", named)).Append(", varargin};\n\n");
                s.Append("      ").Append(prefix).Append("mcr.EvaluateFunction(").Append(target).Append('"').Append(f.Name).Append("\", argsIn);\n");
            }
            else
            {
                s.Append("      MWArray[] args = {").Append(string.Join(", ", named)).Append("};\n");
                s.Append("""
      int nonVarargInputNum = args.Length;
      int varargInputNum = varargin.Length;
      int totalNumArgs = varargInputNum > 0 ? (nonVarargInputNum + varargInputNum) : nonVarargInputNum;
      Array newArr = Array.CreateInstance(typeof(MWArray), totalNumArgs);

      int idx = 0;

      for (idx = 0; idx < nonVarargInputNum; idx++)
        newArr.SetValue(args[idx],idx);

      if (varargInputNum > 0)
      {
        for (int i = 0; i < varargInputNum; i++)
        {
          newArr.SetValue(varargin[i], idx);
          idx++;
        }
      }


""".Replace("\r\n", "\n", StringComparison.Ordinal));
                s.Append("      ").Append(prefix).Append("mcr.EvaluateFunction(").Append(target).Append('"').Append(f.Name).Append("\", (MWArray[])newArr );\n");
            }

        }

        // One output (none for a function with only varargout), for each count of named inputs.
        string singleType = single ? type : "void";
        for (int k = 0; k <= named.Count; k++)
        {
            Docs($"Provides a {(single ? "single" : "void")} output, {k}-input {type}interface to the {f.Name} MATLAB function.", k, many: false, varargin: false);
            Signature(s, $"    public {singleType} {f.Name}({Arguments(k)})", singleType);
            s.Append("    {\n      ").Append(single ? "return " : "").Append("mcr.EvaluateFunction(").Append(single ? "" : "0, ")
                .Append('"').Append(f.Name).Append("\", ").Append(Passed(k)).Append(");\n    }\n\n\n");
        }

        if (f.TakesVarargin)
        {
            int k = named.Count;
            Docs($"Provides a {(single ? "single" : "void")} output, {k + 1}-input {type}interface to the {f.Name} MATLAB function.", k, many: false, varargin: true);
            Signature(s, $"    public {singleType} {f.Name}({(k > 0 ? Arguments(k) + ", " : "")}params {type}[] varargin)", singleType);
            s.Append("    {\n");
            Body(varargin: true, many: false);
            s.Append("    }\n\n\n");
        }

        // Many outputs.
        for (int k = 0; k <= named.Count; k++)
        {
            Docs($"Provides the standard {k}-input {type} interface to the {f.Name} MATLAB function.", k, many: true, varargin: false);
            Signature(s, $"    public {type}[] {f.Name}(int numArgsOut{(k > 0 ? ", " + Arguments(k) : "")})", type + "[]");
            s.Append("    {\n      return mcr.EvaluateFunction(numArgsOut, \"").Append(f.Name).Append("\", ").Append(Passed(k)).Append(");\n    }\n\n\n");
        }

        if (f.TakesVarargin)
        {
            int k = named.Count;
            Docs($"Provides the standard {k + 1}-input {type} interface to the {f.Name} MATLAB function.", k, many: true, varargin: true);
            Signature(s, $"    public {type}[] {f.Name}(int numArgsOut, {(k > 0 ? Arguments(k) + ", " : "")}params {type}[] varargin)", type + "[]");
            s.Append("    {\n");
            Body(varargin: true, many: true);
            s.Append("    }\n\n\n");
        }

        // The array form: the MWArray class's for a function with outputs, the native class's always.
        if (!nativeApi && (single || f.GivesVarargout))
        {
            s.Append("    /// <summary>\n");
            Wrap(s, "    /// ", $"Provides an interface for the {f.Name} function in which the input and output arguments are specified as an array of MWArrays.");
            s.Append("    /// </summary>\n    /// <remarks>\n    /// This method will allocate and return by reference the output argument\n    /// array.<newpara></newpara>\n    /// M-Documentation:\n");
            Wrap(s, "    /// ", f.H1);
            s.Append("    /// </remarks>\n    /// <param name=\"numArgsOut\">The number of output arguments to return</param>\n");
            s.Append("    /// <param name= \"argsOut\">Array of MWArray output arguments</param>\n    /// <param name= \"argsIn\">Array of MWArray input arguments</param>\n    ///\n");
            s.Append("    public void ").Append(f.Name).Append("(int numArgsOut, ref MWArray[] argsOut, MWArray[] argsIn)\n    {\n");
            s.Append("      mcr.EvaluateFunction(\"").Append(f.Name).Append("\", numArgsOut, ref argsOut, argsIn);\n    }\n\n\n");
        }
        else if (nativeApi)
        {
            s.Append("    /// <summary>\n");
            Wrap(s, "    /// ", $"Provides an interface for the {f.Name} function in which the input and output arguments are specified as an array of Objects.");
            s.Append("    /// </summary>\n    /// <remarks>\n    /// This method will allocate and return by reference the output argument\n    /// array.<newpara></newpara>\n    /// M-Documentation:\n");
            Wrap(s, "    /// ", f.H1);
            s.Append("    /// </remarks>\n    /// <param name=\"numArgsOut\">The number of output arguments to return</param>\n");
            s.Append("    /// <param name= \"argsOut\">Array of Object output arguments</param>\n    /// <param name= \"argsIn\">Array of Object input arguments</param>\n");
            s.Append("    /// <param name= \"varArgsIn\">Array of Object representing variable input\n    /// arguments</param>\n    ///\n");
            s.Append("    [MATLABSignature(\"").Append(f.Name).Append("\", ").Append(f.NamedInputs).Append(", ").Append(f.Outputs.Count).Append(", ").Append(f.TakesVarargin ? 1 : 0).Append(")]\n");
            s.Append("    protected void ").Append(f.Name).Append("(int numArgsOut, ref Object[] argsOut, Object[] argsIn, params Object[] varArgsIn)\n    {\n");
            s.Append("        mcr.EvaluateFunctionForTypeSafeCall(\"").Append(f.Name).Append("\", numArgsOut, ref argsOut, argsIn, varArgsIn);\n    }\n");
        }
    }

    /// <summary>A method's signature, broken after the last blank that keeps it within R2025b's width.</summary>
    private static void Signature(StringBuilder s, string line, string returnType)
    {
        if (line.Length > Width)
        {
            int at = line.LastIndexOf(' ', Width - 1);
            if (at > 0)
            {
                s.Append(line.AsSpan(0, at + 1)).Append('\n').Append(new string(' ', 11 + returnType.Length + 2)).Append(line.AsSpan(at + 1)).Append('\n');
                return;
            }
        }

        s.Append(line).Append('\n');
    }

    /// <summary>Text after <paramref name="prefix"/>, broken into lines of R2025b's width at blanks (a longer word cut).</summary>
    private static void Wrap(StringBuilder s, string prefix, string text)
    {
        // R2025b breaks at blanks to keep a line under 90 columns, and cuts a longer word at 90 (k8_out).
        int room = Width - 1 - prefix.Length;
        int cut = Width - prefix.Length;
        var line = new StringBuilder();
        foreach (string word in text.Split(' '))
        {
            string rest = word;
            if (line.Length > 0 && line.Length + 1 + rest.Length > room)
            {
                s.Append(prefix).Append(line).Append('\n');
                line.Clear();
            }

            while (rest.Length > cut)
            {
                if (line.Length > 0)
                {
                    s.Append(prefix).Append(line).Append('\n');
                    line.Clear();
                }

                s.Append(prefix).Append(rest.AsSpan(0, cut)).Append('\n');
                rest = rest[cut..];
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(rest);
        }

        if (line.Length > 0)
        {
            s.Append(prefix).Append(line).Append('\n');
        }
    }

    /// <summary>C's asctime, as R2025b's header dates: <c>Sat Oct 24 19:23:23 2026</c>.</summary>
    private static string AscTime(DateTime when) =>
        when.ToString("ddd MMM ", CultureInfo.InvariantCulture) + when.Day.ToString(CultureInfo.InvariantCulture).PadLeft(2)
        + when.ToString(" HH:mm:ss yyyy", CultureInfo.InvariantCulture);

    /// <summary>R2025b's <c>&lt;name&gt;Version.cs</c> for a .NET 4.0 build.</summary>
    private static string VersionSource(string version) => $$"""

using System;
using System.Reflection;

// Set version number for the assembly.
#if __IN_MCC
[assembly: AssemblyVersionAttribute("{{version}}")]
#endif

""".Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>R2025b's <c>.csproj</c> for a .NET 5.0 build, naming JGraph's arrays assembly where R2025b's names MWArray.dll.</summary>
    private static string Project(IEnumerable<string> sources, string ctfName, string arrays, string version, bool embed) =>
        $$"""
<Project Sdk = "Microsoft.NET.Sdk">
	<PropertyGroup>
		<TargetFramework>net8.0</TargetFramework>
		<PlatformTarget>AnyCPU</PlatformTarget>
		<OutputType>library</OutputType>
		<DebugType>None</DebugType>
		<Optimize>true</Optimize>
		<GenerateDocumentationFile>true</GenerateDocumentationFile>
		<ProduceReferenceAssembly>false</ProduceReferenceAssembly>
		<CopyReferenceAssemblies>false</CopyReferenceAssemblies>
		<DefineConstants>$(DefineConstants); __IN_MCC</DefineConstants>
		<EnableDefaultCompileItems>false</EnableDefaultCompileItems>
		<Version>{{version}}</Version>
	</PropertyGroup>
	<ItemGroup>
		<Compile Remove = "*.cs" />
{{string.Concat(sources.Select(static f => $"\t\t<Compile Include = \"{f}\" />\n"))}}{{(embed ? $"\t\t<EmbeddedResource Include = \"{ctfName}\" />\n" : "")}}		<Reference Include = "{{arrays}}">
			<Private>False</Private>
		</Reference>
	</ItemGroup>
</Project>
""".Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>
    /// Compiles one assembly in process against .NET 8's reference assemblies and
    /// <paramref name="references"/>, writing its documentation and, for a debug build, its symbols.
    /// </summary>
    internal static void Compile(string assemblyName, IReadOnlyList<(string File, string Text)> sources, string version,
        IReadOnlyList<string> references, string output, bool debug, string? keyFile, IReadOnlyList<ResourceDescription> resources)
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: ["__IN_MCC"]);
        var trees = new List<SyntaxTree>();
        foreach ((string file, string text) in sources)
        {
            trees.Add(CSharpSyntaxTree.ParseText(text, parse, Path.Combine(Path.GetDirectoryName(output)!, file), Encoding.UTF8));
        }

        // R2025b sets the version through Version.cs or the project; one generated line does it here.
        trees.Add(CSharpSyntaxTree.ParseText($"[assembly: System.Reflection.AssemblyVersion(\"{version}\")]\n[assembly: System.Reflection.AssemblyFileVersion(\"{version}\")]\n", parse));

        // .NET 8's reference assemblies, as a client compiles against them: the generated classes then
        // name System.Runtime, not the runtime's own System.Private.CoreLib.
        string referenceFolder = ReferenceAssemblies()
            ?? throw new MccException("JGraph:mcc:noReferenceAssemblies",
                "The .NET 8 reference assemblies were found neither beside JGraph (toolbox\\dotnetbuilder\\ref\\net8.0) nor in a .NET SDK's "
                + "Microsoft.NETCore.App.Ref 8.0 pack: install the .NET 8 SDK, or reinstall JGraph.");
        var metadata = new List<MetadataReference>();
        foreach (string path in Directory.EnumerateFiles(referenceFolder, "*.dll"))
        {
            metadata.Add(MetadataReference.CreateFromFile(path));
        }

        metadata.AddRange(references.Select(static r => MetadataReference.CreateFromFile(r)));
        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: debug ? OptimizationLevel.Debug : OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Disable,
            generalDiagnosticOption: ReportDiagnostic.Default,
            deterministic: true);
        if (keyFile is { } key)
        {
            compileOptions = compileOptions.WithCryptoKeyFile(Path.GetFullPath(key)).WithStrongNameProvider(new DesktopStrongNameProvider());
        }

        CSharpCompilation compilation = CSharpCompilation.Create(assemblyName, trees, metadata, compileOptions);

        string documentation = Path.ChangeExtension(output, ".xml");
        string symbols = Path.ChangeExtension(output, ".pdb");
        EmitResult result;
        using (FileStream dll = File.Create(output + ".partial"))
        using (FileStream xml = File.Create(documentation))
        using (FileStream? pdb = debug ? File.Create(symbols) : null)
        {
            result = compilation.Emit(dll, pdbStream: pdb, xmlDocumentationStream: xml, manifestResources: resources,
                options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        }

        if (!result.Success)
        {
            File.Delete(output + ".partial");
            IEnumerable<string> errors = result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error).Take(10).Select(static d => d.ToString());
            throw new MccException("JGraph:mcc:dotnetCompileFailed",
                $"The generated classes of {assemblyName}.dll did not compile:\n{string.Join("\n", errors)}");
        }

        File.Move(output + ".partial", output, overwrite: true);
        if (!debug)
        {
            File.Delete(symbols);
        }
    }

    /// <summary>
    /// The folder of .NET 8's reference assemblies: the copy beside JGraph, else the newest 8.0
    /// targeting pack of the .NET installation this process runs on; null when neither is there.
    /// </summary>
    public static string? ReferenceAssemblies()
    {
        foreach (string? folder in new[] { Path.GetDirectoryName(typeof(DotNetAssembly).Assembly.Location), AppContext.BaseDirectory })
        {
            if (folder is { Length: > 0 } && Path.Combine(folder, "toolbox", "dotnetbuilder", "ref", "net8.0") is var carried
                && File.Exists(Path.Combine(carried, "System.Runtime.dll")))
            {
                return carried;
            }
        }

        // ...\dotnet\shared\Microsoft.NETCore.App\8.0.x\System.Private.CoreLib.dll: the packs are beside shared.
        string? root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(typeof(object).Assembly.Location))));
        string packs = Path.Combine(root ?? string.Empty, "packs", "Microsoft.NETCore.App.Ref");
        if (!Directory.Exists(packs))
        {
            return null;
        }

        return Directory.EnumerateDirectories(packs, "8.0.*")
            .Select(static d => (Folder: Path.Combine(d, "ref", "net8.0"), Version: Version.TryParse(Path.GetFileName(d), out Version? v) ? v : new Version()))
            .Where(static p => File.Exists(Path.Combine(p.Folder, "System.Runtime.dll")))
            .OrderByDescending(static p => p.Version)
            .Select(static p => p.Folder)
            .FirstOrDefault();
    }

    /// <summary><c>&lt;name&gt;_overview.html</c>: the classes and functions, and how a client calls them, in JGraph's words.</summary>
    private static string Overview(DotNetAssemblyOptions options)
    {
        string name = options.Wrapper.ShortName;
        var s = new StringBuilder();
        s.Append("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<title>").Append(name).Append(" .NET assembly</title>\n</head>\n<body>\n");
        s.Append("<h1>").Append(name).Append(" .NET assembly</h1>\n");
        s.Append("<p>Built by JGraph's mcc for the MWArray API. A client references <code>").Append(name).Append(".dll</code> (or <code>")
            .Append(name).Append("Native.dll</code> for the native API) and <code>").Append(ArraysAssemblyName)
            .Append(".dll</code>, and runs on the JGraph Runtime ").Append(RuntimeLayout.CurrentVersion).Append(" on .NET 8 or later.</p>\n");
        s.Append("<h2>Classes</h2>\n");
        foreach (DotNetClass cls in options.Classes)
        {
            s.Append("<h3>").Append(options.Wrapper.AssemblyName).Append('.').Append(cls.Name).Append("</h3>\n<ul>\n");
            foreach (DotNetFunction f in cls.Functions)
            {
                s.Append("<li><code>").Append(f.Name).Append("</code> (").Append(f.Inputs.Count).Append(" inputs, ")
                    .Append(f.Outputs.Count).Append(" outputs): ").Append(System.Net.WebUtility.HtmlEncode(f.H1)).Append("</li>\n");
            }

            s.Append("</ul>\n");
        }

        s.Append("<h2>Calling a function</h2>\n<pre>\nusing JGraph.MATLAB.NET.Arrays;\n\nvar c = new ").Append(options.Wrapper.AssemblyName).Append('.')
            .Append(options.Classes[0].Name).Append("();\nMWArray[] outputs = c.").Append(options.Classes[0].Functions.FirstOrDefault()?.Name ?? "f")
            .Append("(numArgsOut, new MWNumericArray(1.0));\n</pre>\n</body>\n</html>\n");
        return s.ToString();
    }

    /// <summary>
    /// Writes <c>GettingStarted.html</c> into <paramref name="folder"/> for the assembly
    /// <paramref name="name"/>, for the MWArray API or with <paramref name="dataApi"/> the MATLAB Data
    /// API, and answers its path.
    /// </summary>
    public static string WriteGettingStarted(string folder, string name, bool dataApi = false)
    {
        string path = Path.Combine(folder, "GettingStarted.html");
        File.WriteAllText(path, Crlf(dataApi ? DataGettingStarted(name) : GettingStarted(name)), new UTF8Encoding(false));
        return path;
    }

    /// <summary><c>GettingStarted.html</c> for a MATLAB Data API archive, in JGraph's words.</summary>
    private static string DataGettingStarted(string name) =>
        $"""
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>Getting started with {name}</title>
</head>
<body>
<h1>Getting started with {name}</h1>
<ol>
<li>Install the JGraph Runtime {RuntimeLayout.CurrentVersion}, or set JGRAPH_RUNTIME_ROOT to its folder.</li>
<li>In a .NET 8 project, reference the runtime's extern\dotnet\net8.0\{DotNetDataAssembly.RuntimeAssemblyName}.dll and, for the strongly typed interface, {name}.dll.</li>
<li>Open the archive with <code>dynamic matlab = MATLABRuntime.StartMATLAB("{name}.ctf");</code> and call its functions as members of <code>matlab</code>,
or through the interface's <code>MATLABFunctions</code> and classes.</li>
</ol>
</body>
</html>

""".Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary><c>GettingStarted.html</c>, in JGraph's words.</summary>
    private static string GettingStarted(string name) =>
        $"""
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>Getting started with {name}</title>
</head>
<body>
<h1>Getting started with {name}</h1>
<ol>
<li>Install the JGraph Runtime {RuntimeLayout.CurrentVersion}, or set JGRAPH_RUNTIME_ROOT to its folder.</li>
<li>In a .NET 8 project, reference {name}.dll and the runtime's toolbox\dotnetbuilder\bin\win64\net8.0\{ArraysAssemblyName}.dll.</li>
<li>Make an instance of a class and call its functions with MWArray arguments; see {name}_overview.html.</li>
</ol>
</body>
</html>

""".Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>readme.txt: JGraph's own text under R2025b's title line and four numbered sections (k8_build: main.readme).</summary>
    public static string Readme(DotNetAssemblyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string name = options.Wrapper.ShortName;
        var s = new StringBuilder();
        s.Append(name).Append(" MATLAB .NET Assembly (.NET Component)\n\n");
        s.Append("1. Prerequisites for Deployment \n\n");
        s.Append("The JGraph Runtime ").Append(RuntimeLayout.CurrentVersion).Append(" must be installed on the computer the assembly runs on,\n")
            .Append("or its folder named by the JGRAPH_RUNTIME_ROOT environment variable. Clients run on .NET 8 or later.\n\n");
        s.Append("2. Files to Deploy and Package\n\n");
        s.Append("-").Append(name).Append(".dll\n   -contains the generated component using MWArray API. \n");
        s.Append("-").Append(name).Append("Native.dll\n   -contains the generated component using native API.\n");
        if (!options.EmbedArchive)
        {
            s.Append("-").Append(name).Append(".ctf\n   -the deployable archive, kept beside the assemblies.\n");
        }

        s.Append("-").Append(ArraysAssemblyName).Append(".dll\n   -the MWArray classes, from the JGraph Runtime's toolbox\\dotnetbuilder\\bin\\win64\\net8.0 folder.\n");
        s.Append("-This readme file\n\n\n");
        s.Append("Auto-generated Documentation Templates:\n\n");
        s.Append(name).Append("_overview.html - the classes, their functions, and how a client calls them.\n\n");
        s.Append(name).Append(".xml - the code comments of the ").Append(name).Append(" classes and methods.\n\n");
        s.Append("3. Resources\n\n");
        s.Append("The MWArray classes keep MathWorks' names under the JGraph.MATLAB.NET namespaces: a client written\n")
            .Append("for MATLAB's assembly changes its using lines from MathWorks.MATLAB.NET to JGraph.MATLAB.NET.\n\n");
        s.Append("4. Definitions\n\n");
        s.Append("The JGraph Runtime is the engine a built component runs on, installed once per JGraph version.\n");
        return s.ToString();
    }

    private static string Crlf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
}
