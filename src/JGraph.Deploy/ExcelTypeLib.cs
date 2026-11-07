using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;

namespace JGraph.Deploy;

/// <summary>
/// The GUIDs an Excel add-in's COM registration uses (compiler and SDK plan, stage K17c). R2025b's
/// are deterministic too: its type library's id depends on the add-in's name alone, its interface
/// and class ids on the names and the version (<c>k17_excel</c>: guids.*, the mcc_same* builds);
/// its derivation is MathWorks' own, so JGraph derives its own the same way from the same
/// inputs (a name-based UUID, SHA-1 under a JGraph namespace), a recorded divergence (ADR 0316).
/// </summary>
public static class ExcelComGuids
{
    /// <summary>The namespace the name-based ids are derived under.</summary>
    private static readonly Guid Namespace = new("5a0f3c2e-8c1b-4f6a-9d3e-2b7a1c4e8f10");

    /// <summary>JGraph's component category for its Excel add-ins (decision KO10), where R2025b's add-ins register under MathWorks'.</summary>
    public static readonly Guid Category = new("C4E7A6D2-5B0F-4E3A-8D91-7F2B6A1C9E35");

    /// <summary>The AppID every add-in names, as R2025b's name MathWorks' one.</summary>
    public static readonly Guid AppId = new("9A3D5F71-2C8E-4B06-A1D4-6E0F7B2C3D58");

    /// <summary>The flags interface every add-in's class exposes through <c>MWFlags</c> (MathWorks' member names, JGraph's id; decision KO10).</summary>
    public static readonly Guid FlagsInterface = new("3F8B1E6A-9C2D-4A57-B8E1-5D0C7A9F2B64");

    /// <summary>The array format flags' interface.</summary>
    public static readonly Guid ArrayFormatFlagsInterface = new("6B2C9D41-7E8F-4C03-A5D6-1F9E3B7C8A20");

    /// <summary>The data conversion flags' interface.</summary>
    public static readonly Guid DataConversionFlagsInterface = new("8D1F4A73-2B6C-4E95-9C0A-7E3D5F1B6A42");

    /// <summary>The type library's id: the add-in's name alone, as R2025b's depends on it alone.</summary>
    public static Guid TypeLib(string addIn) => Derive("typelib:" + addIn);

    /// <summary>The coclass's id: the names and the version.</summary>
    public static Guid Class(string addIn, string className, ProgramVersion version) =>
        Derive(FormattableString.Invariant($"coclass:{addIn}.{className}:{version.Major}.{version.Minor}"));

    /// <summary>The interface's id: the names and the version.</summary>
    public static Guid Interface(string addIn, string className, ProgramVersion version) =>
        Derive(FormattableString.Invariant($"interface:{addIn}.I{className}:{version.Major}.{version.Minor}"));

    /// <summary>A name-based UUID (RFC 4122 version 5) under the JGraph namespace.</summary>
    internal static Guid Derive(string name)
    {
        byte[] space = Namespace.ToByteArray();
        // The namespace in network order, as the RFC hashes it.
        Swap(space);
        byte[] text = Encoding.UTF8.GetBytes(name);
        byte[] all = new byte[space.Length + text.Length];
        space.CopyTo(all, 0);
        text.CopyTo(all, space.Length);
        byte[] hash = SHA1.HashData(all);
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        Swap(bytes);
        return new Guid(bytes);
    }

    private static void Swap(byte[] bytes)
    {
        Array.Reverse(bytes, 0, 4);
        Array.Reverse(bytes, 4, 2);
        Array.Reverse(bytes, 6, 2);
    }
}

/// <summary>What an add-in's type library says about one exported function.</summary>
/// <param name="Name">The method's name, the function's.</param>
/// <param name="Inputs">The input parameters' names, in order (<c>varargin</c> last when the function takes it).</param>
/// <param name="Outputs">The output parameters' names (<c>varargout</c> last when the function gives it); none for a function without outputs, which has no <c>nargout</c> either.</param>
/// <param name="Help">The function's H1 line, the method's documentation string.</param>
public sealed record ExcelTypeLibFunction(string Name, IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs, string Help)
{
    /// <summary>Whether the last input is <c>varargin</c>.</summary>
    public bool HasVarargin => Inputs.Count > 0 && Inputs[^1] == "varargin";

    /// <summary>Whether the last output is <c>varargout</c>.</summary>
    public bool HasVarargout => Outputs.Count > 0 && Outputs[^1] == "varargout";
}

/// <summary>An add-in's type library as read back from its DLL.</summary>
public sealed record ExcelTypeLibInfo(
    string LibraryName, Guid LibraryId, ProgramVersion Version, string ClassName, Guid ClassId, string InterfaceName, Guid InterfaceId,
    IReadOnlyList<ExcelTypeLibFunction> Functions)
{
    /// <summary>The version-independent ProgID, <c>&lt;addIn&gt;.&lt;Class&gt;</c>.</summary>
    public string ProgId => LibraryName + "." + ClassName;

    /// <summary>The versioned ProgID, <c>&lt;addIn&gt;.&lt;Class&gt;.&lt;major&gt;_&lt;minor&gt;</c>, which a client's error names as its source.</summary>
    public string VersionedProgId => ProgId + "." + ExcelAddIn.ProgIdSuffix(Version);
}

/// <summary>
/// An Excel add-in's type library (compiler and SDK plan, stage K17c), written with oleaut32's
/// <c>CreateTypeLib2</c> and no MIDL (decision KO9) in the shape R2025b's <c>&lt;addin&gt;_idl.idl</c>
/// declares (<c>k17_excel</c>: plain.file.k17xl_idl.idl): a library named for the add-in and
/// versioned <c>major.minor</c>; a dual interface <c>I&lt;Class&gt;</c> with <c>MWFlags</c> at DISPID 1
/// (get and put), the functions from DISPID 2 in file order, each <c>HRESULT f([in] long nargout,
/// [in, out] VARIANT* out..., [in] VARIANT in...)</c> and a function without outputs with no
/// <c>nargout</c>, and <c>help([in] BSTR funcname, [out, retval] BSTR*)</c> last; and the coclass
/// <c>&lt;Class&gt;</c>. R2025b's library imports MathWorks' <c>mwcomutil.tlb</c> for its flags
/// interfaces; JGraph's declares them in the add-in's own library under JGraph's ids with
/// MathWorks' member names (decision KO10; stage K17d's utility library may move them). Each
/// function's documentation string is its H1 line, which the <c>help</c> property answers and
/// VBA's object browser shows, where R2025b writes <c>Method f</c>.
/// </summary>
/// <remarks>
/// A type library's names are case-insensitive, the first spelling kept: a library <c>k17xl</c>
/// makes its coclass <c>K17Xl</c> read back as <c>k17xl</c>, in R2025b's libraries as in JGraph's
/// (<c>accxl_1_0.dll</c>: library accxl, coclass accxl, docstring "AccXl Class"). The add-in's and
/// the class's names as given are read from the documentation strings, which keep their case:
/// <c>&lt;addIn&gt; &lt;major&gt;.&lt;minor&gt; Type Library</c> and <c>&lt;Class&gt; Class</c>.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class ExcelTypeLib
{
    private const int TypeFlagDual = 0x40;
    private const int TypeFlagOleAutomation = 0x100;
    private const int TypeFlagDispatchable = 0x1000;
    private const int TypeFlagHidden = 0x10;
    private const int TypeFlagCanCreate = 0x2;
    private const int ImplTypeFlagDefault = 0x1;
    private const int FirstMemberSlot = 7;

    /// <summary>The most functions one add-in may export (<see cref="ExcelAddIn.MaxFunctions"/>).</summary>
    public const int MaxFunctions = ExcelAddIn.MaxFunctions;

    private static readonly Guid StdOle = new("00020430-0000-0000-C000-000000000046");
    private static readonly Guid IDispatchId = new("00020400-0000-0000-C000-000000000046");

    /// <summary>Writes the type library for <paramref name="options"/> and answers its bytes.</summary>
    /// <exception cref="InvalidOperationException">The add-in exports more functions than the template serves.</exception>
    public static byte[] Write(ExcelAddInOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Functions.Count > MaxFunctions)
        {
            throw new InvalidOperationException($"An Excel add-in can export at most {MaxFunctions} functions; this one exports {options.Functions.Count}.");
        }

        string file = Path.Combine(Path.GetTempPath(), "jgraph-" + Guid.NewGuid().ToString("N") + ".tlb");
        try
        {
            new Writer(options, file).Run();
            return File.ReadAllBytes(file);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // A temporary file left behind harms nothing.
            }
        }
    }

    /// <summary>Reads the type library an add-in DLL (or a <c>.tlb</c>) carries.</summary>
    /// <exception cref="InvalidDataException">The file carries no type library, or one with no coclass.</exception>
    public static ExcelTypeLibInfo Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        int hr = LoadTypeLibEx(path, 0, out ITypeLib lib);
        if (hr < 0 || lib is null)
        {
            throw new InvalidDataException($"'{path}' carries no type library (0x{hr:X8}).");
        }

        try
        {
            lib.GetDocumentation(-1, out string libraryName, out string libraryDoc, out _, out _);
            lib.GetLibAttr(out IntPtr libAttrPointer);
            TYPELIBATTR libAttr = Marshal.PtrToStructure<TYPELIBATTR>(libAttrPointer);
            lib.ReleaseTLibAttr(libAttrPointer);
            var version = new ProgramVersion(libAttr.wMajorVerNum, libAttr.wMinorVerNum, 0, 0);
            libraryName = AddInNameOf(libraryDoc, libraryName, version);
            int count = lib.GetTypeInfoCount();
            for (int k = 0; k < count; k++)
            {
                lib.GetTypeInfo(k, out ITypeInfo info);
                info.GetTypeAttr(out IntPtr attrPointer);
                TYPEATTR attr = Marshal.PtrToStructure<TYPEATTR>(attrPointer);
                info.ReleaseTypeAttr(attrPointer);
                if (attr.typekind != TYPEKIND.TKIND_COCLASS)
                {
                    continue;
                }

                info.GetDocumentation(-1, out string className, out string classDoc, out _, out _);
                className = ClassNameOf(classDoc, className);
                info.GetRefTypeOfImplType(0, out int href);
                info.GetRefTypeInfo(href, out ITypeInfo iface);
                iface.GetTypeAttr(out IntPtr ifaceAttrPointer);
                TYPEATTR ifaceAttr = Marshal.PtrToStructure<TYPEATTR>(ifaceAttrPointer);
                if (ifaceAttr.typekind == TYPEKIND.TKIND_DISPATCH)
                {
                    // The dual's dispatch view lists IUnknown's and IDispatch's members too; its vtable view lists the declared ones.
                    iface.ReleaseTypeAttr(ifaceAttrPointer);
                    iface.GetRefTypeOfImplType(-1, out int vtableHref);
                    iface.GetRefTypeInfo(vtableHref, out iface);
                    iface.GetTypeAttr(out ifaceAttrPointer);
                    ifaceAttr = Marshal.PtrToStructure<TYPEATTR>(ifaceAttrPointer);
                }

                iface.GetDocumentation(-1, out string interfaceName, out _, out _, out _);
                var functions = new List<ExcelTypeLibFunction>();
                for (int f = 0; f < ifaceAttr.cFuncs; f++)
                {
                    iface.GetFuncDesc(f, out IntPtr descPointer);
                    FUNCDESC desc = Marshal.PtrToStructure<FUNCDESC>(descPointer);
                    var names = new string[desc.cParams + 1];
                    iface.GetNames(desc.memid, names, names.Length, out int named);
                    iface.GetDocumentation(desc.memid, out _, out string doc, out _, out _);
                    string name = names[0];
                    if (desc.invkind == INVOKEKIND.INVOKE_FUNC && name != "MWFlags" && name != "help")
                    {
                        var inputs = new List<string>();
                        var outputs = new List<string>();
                        for (int p = 0; p < desc.cParams; p++)
                        {
                            ELEMDESC param = Marshal.PtrToStructure<ELEMDESC>(desc.lprgelemdescParam + p * Marshal.SizeOf<ELEMDESC>());
                            string paramName = p + 1 < named ? names[p + 1] : "";
                            if (paramName == "nargout" && p == 0)
                            {
                                continue;
                            }

                            if ((param.desc.paramdesc.wParamFlags & PARAMFLAG.PARAMFLAG_FOUT) != 0)
                            {
                                outputs.Add(paramName);
                            }
                            else
                            {
                                inputs.Add(paramName);
                            }
                        }

                        functions.Add(new ExcelTypeLibFunction(name, inputs, outputs, doc ?? ""));
                    }

                    iface.ReleaseFuncDesc(descPointer);
                }

                iface.ReleaseTypeAttr(ifaceAttrPointer);
                return new ExcelTypeLibInfo(libraryName, libAttr.guid, version, className, attr.guid, interfaceName, ifaceAttr.guid, functions);
            }

            throw new InvalidDataException($"The type library of '{path}' declares no coclass.");
        }
        finally
        {
            Marshal.ReleaseComObject(lib);
        }
    }

    /// <summary>The add-in's name as given, from the library's documentation string <c>&lt;name&gt; &lt;M&gt;.&lt;m&gt; Type Library</c>; the library's name otherwise.</summary>
    internal static string AddInNameOf(string? doc, string name, ProgramVersion version)
    {
        string tail = FormattableString.Invariant($" {version.Major}.{version.Minor} Type Library");
        return doc is not null && doc.EndsWith(tail, StringComparison.Ordinal) && doc.Length > tail.Length ? doc[..^tail.Length] : name;
    }

    /// <summary>The class's name as given, from the coclass's documentation string <c>&lt;Class&gt; Class</c>; the coclass's name otherwise.</summary>
    internal static string ClassNameOf(string? doc, string name) =>
        doc is not null && doc.EndsWith(" Class", StringComparison.Ordinal) && doc.Length > 6 ? doc[..^6] : name;

    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int LoadTypeLibEx(string file, int regKind, out ITypeLib typeLib);

    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int LoadRegTypeLib(ref Guid id, ushort major, ushort minor, int lcid, out ITypeLib typeLib);

    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int CreateTypeLib2(SYSKIND sysKind, string file, out ICreateTypeLib2 typeLib);

    /// <summary>One run of the writer, holding the unmanaged memory the descriptors need until the library is saved.</summary>
    private sealed class Writer(ExcelAddInOptions options, string file)
    {
        private readonly List<IntPtr> _memory = [];

        public void Run()
        {
            int hr = CreateTypeLib2(SYSKIND.SYS_WIN64, file, out ICreateTypeLib2 lib);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                string addIn = options.AddInName;
                string className = options.ClassName;
                // The library's version is the DLL's: major.minor, a major of 0 made 1.0 (k17_excel: v05 and vzero
                // declare "k17v05 1.0 Type Library" and "k17vz 1.0 Type Library"; v3456 "3.4").
                ProgramVersion version = options.Version.Major == 0 ? new ProgramVersion(1, 0, 0, 0) : options.Version;
                lib.SetName(addIn);
                lib.SetVersion((ushort)version.Major, (ushort)version.Minor);
                Guid libId = ExcelComGuids.TypeLib(addIn);
                lib.SetGuid(ref libId);
                lib.SetDocString(FormattableString.Invariant($"{addIn} {version.Major}.{version.Minor} Type Library"));
                lib.SetLcid(0);

                // The base every interface derives from.
                Guid stdole = StdOle;
                Marshal.ThrowExceptionForHR(LoadRegTypeLib(ref stdole, 2, 0, 0, out ITypeLib standard));
                Guid dispatchId = IDispatchId;
                standard.GetTypeInfoOfGuid(ref dispatchId, out ITypeInfo dispatch);

                // Every type is created first, so each can refer to the others.
                ICreateTypeInfo arrayFlags = Create(lib, "IMWArrayFormatFlags", TYPEKIND.TKIND_INTERFACE);
                ICreateTypeInfo conversionFlags = Create(lib, "IMWDataConversionFlags", TYPEKIND.TKIND_INTERFACE);
                ICreateTypeInfo flags = Create(lib, "IMWFlags", TYPEKIND.TKIND_INTERFACE);
                ICreateTypeInfo component = Create(lib, "I" + className, TYPEKIND.TKIND_INTERFACE);
                ICreateTypeInfo coclass = Create(lib, className, TYPEKIND.TKIND_COCLASS);

                // IMWArrayFormatFlags: MathWorks' six properties, longs and VARIANT_BOOLs (mwcomtypes.idl).
                Interface(arrayFlags, ExcelComGuids.ArrayFormatFlagsInterface, "IMWArrayFormatFlags Interface", dispatch, hidden: true);
                int index = 0;
                Property(arrayFlags, ref index, 1, "InputArrayFormat", VarEnum.VT_I4);
                Property(arrayFlags, ref index, 2, "InputArrayIndFlag", VarEnum.VT_I4);
                Property(arrayFlags, ref index, 3, "OutputArrayFormat", VarEnum.VT_I4);
                Property(arrayFlags, ref index, 4, "OutputArrayIndFlag", VarEnum.VT_I4);
                Property(arrayFlags, ref index, 5, "AutoResizeOutput", VarEnum.VT_BOOL);
                Property(arrayFlags, ref index, 6, "TransposeOutput", VarEnum.VT_BOOL);
                arrayFlags.LayOut();

                Interface(conversionFlags, ExcelComGuids.DataConversionFlagsInterface, "IMWDataConversionFlags Interface", dispatch, hidden: true);
                index = 0;
                Property(conversionFlags, ref index, 1, "CoerceNumericToType", VarEnum.VT_I4);
                Property(conversionFlags, ref index, 2, "InputDateFormat", VarEnum.VT_I4);
                Property(conversionFlags, ref index, 3, "OutputAsDate", VarEnum.VT_BOOL);
                Property(conversionFlags, ref index, 4, "DateBias", VarEnum.VT_I4);
                Property(conversionFlags, ref index, 5, "ReplaceMissing", VarEnum.VT_I4);
                Property(conversionFlags, ref index, 6, "ReplaceMatlabNaN", VarEnum.VT_I4);
                conversionFlags.LayOut();

                // IMWFlags: the two flag objects and Clone.
                Interface(flags, ExcelComGuids.FlagsInterface, "IMWFlags Interface", dispatch, hidden: true);
                flags.AddRefTypeInfo((ITypeInfo)arrayFlags, out int arrayHref);
                flags.AddRefTypeInfo((ITypeInfo)conversionFlags, out int conversionHref);
                flags.AddRefTypeInfo((ITypeInfo)flags, out int flagsSelfHref);
                index = 0;
                ObjectProperty(flags, ref index, 1, "ArrayFormatFlags", arrayHref);
                ObjectProperty(flags, ref index, 2, "DataConversionFlags", conversionHref);
                // Clone(IMWFlags** ppFlags) as mwcomtypes.idl declares it: the slot by reference, no retval (k17_com: flags.clone).
                Function(flags, index++, 3, INVOKEKIND.INVOKE_FUNC, "Clone", "Method Clone",
                    [Param("ppFlags", Pointer(Pointer(UserDefined(flagsSelfHref))), PARAMFLAG.PARAMFLAG_FIN | PARAMFLAG.PARAMFLAG_FOUT)]);
                flags.LayOut();

                // I<Class>: MWFlags at 1, the functions from 2, help last (plain.file.k17xl_idl.idl).
                Interface(component, ExcelComGuids.Interface(addIn, className, version), "I" + className + " Interface", dispatch, hidden: false);
                component.AddRefTypeInfo((ITypeInfo)flags, out int flagsHref);
                index = 0;
                ObjectProperty(component, ref index, 1, "MWFlags", flagsHref);
                int id = 2;
                foreach (ExcelFunction f in options.Functions)
                {
                    var parameters = new List<Parameter>();
                    if (f.Outputs.Count > 0)
                    {
                        parameters.Add(Param("nargout", Plain(VarEnum.VT_I4), PARAMFLAG.PARAMFLAG_FIN));
                        foreach (string output in f.Outputs)
                        {
                            parameters.Add(Param(output, Pointer(Plain(VarEnum.VT_VARIANT)), PARAMFLAG.PARAMFLAG_FIN | PARAMFLAG.PARAMFLAG_FOUT));
                        }
                    }

                    foreach (string input in f.Inputs)
                    {
                        parameters.Add(Param(input, Plain(VarEnum.VT_VARIANT), PARAMFLAG.PARAMFLAG_FIN));
                    }

                    Function(component, index++, id++, INVOKEKIND.INVOKE_FUNC, f.Name, f.H1, parameters);
                }

                Function(component, index++, id, INVOKEKIND.INVOKE_PROPERTYGET, "help", "property help",
                    [Param("funcname", Plain(VarEnum.VT_BSTR), PARAMFLAG.PARAMFLAG_FIN), Param("helptext", Pointer(Plain(VarEnum.VT_BSTR)), PARAMFLAG.PARAMFLAG_FOUT | PARAMFLAG.PARAMFLAG_FRETVAL)]);
                component.LayOut();

                // The coclass with its default interface.
                Guid classId = ExcelComGuids.Class(addIn, className, version);
                coclass.SetGuid(ref classId);
                coclass.SetTypeFlags(TypeFlagCanCreate);
                coclass.SetDocString(className + " Class");
                coclass.AddRefTypeInfo((ITypeInfo)component, out int componentHref);
                coclass.AddImplType(0, componentHref);
                coclass.SetImplTypeFlags(0, ImplTypeFlagDefault);
                coclass.LayOut();

                lib.SaveAllChanges();
                Marshal.ReleaseComObject(standard);
            }
            finally
            {
                Marshal.ReleaseComObject(lib);
                foreach (IntPtr block in _memory)
                {
                    Marshal.FreeHGlobal(block);
                }
            }
        }

        private static ICreateTypeInfo Create(ICreateTypeLib2 lib, string name, TYPEKIND kind)
        {
            lib.CreateTypeInfo(name, kind, out ICreateTypeInfo info);
            return info;
        }

        private static void Interface(ICreateTypeInfo info, Guid id, string doc, ITypeInfo dispatch, bool hidden)
        {
            info.SetGuid(ref id);
            info.SetTypeFlags(TypeFlagDual | TypeFlagOleAutomation | TypeFlagDispatchable | (hidden ? TypeFlagHidden : 0));
            info.SetDocString(doc);
            info.AddRefTypeInfo(dispatch, out int href);
            info.AddImplType(0, href);
        }

        /// <summary>A scalar property: a get with an <c>[out, retval]</c> and a put with an <c>[in]</c>, one DISPID.</summary>
        private void Property(ICreateTypeInfo info, ref int index, int id, string name, VarEnum vt)
        {
            Function(info, index++, id, INVOKEKIND.INVOKE_PROPERTYGET, name, "property " + name,
                [Param("value", Pointer(Plain(vt)), PARAMFLAG.PARAMFLAG_FOUT | PARAMFLAG.PARAMFLAG_FRETVAL)]);
            Function(info, index++, id, INVOKEKIND.INVOKE_PROPERTYPUT, name, "property " + name,
                [Param("value", Plain(vt), PARAMFLAG.PARAMFLAG_FIN)]);
        }

        /// <summary>An object property: a get answering <c>I*</c> and a put taking one.</summary>
        private void ObjectProperty(ICreateTypeInfo info, ref int index, int id, string name, int href)
        {
            Function(info, index++, id, INVOKEKIND.INVOKE_PROPERTYGET, name, "property " + name,
                [Param("ppFlags", Pointer(Pointer(UserDefined(href))), PARAMFLAG.PARAMFLAG_FOUT | PARAMFLAG.PARAMFLAG_FRETVAL)]);
            Function(info, index++, id, INVOKEKIND.INVOKE_PROPERTYPUT, name, "property " + name,
                [Param("pFlags", Pointer(UserDefined(href)), PARAMFLAG.PARAMFLAG_FIN)]);
        }

        /// <summary>A parameter: its name and its ELEMDESC in unmanaged memory.</summary>
        private readonly record struct Parameter(string Name, IntPtr Desc);

        private void Function(ICreateTypeInfo info, int index, int id, INVOKEKIND kind, string name, string doc, IReadOnlyList<Parameter> parameters)
        {
            IntPtr paramArray = IntPtr.Zero;
            if (parameters.Count > 0)
            {
                int size = Marshal.SizeOf<ELEMDESC>();
                paramArray = Allocate(size * parameters.Count);
                for (int p = 0; p < parameters.Count; p++)
                {
                    ELEMDESC elem = Marshal.PtrToStructure<ELEMDESC>(parameters[p].Desc);
                    Marshal.StructureToPtr(elem, paramArray + p * size, false);
                }
            }

            var desc = new FUNCDESC
            {
                memid = id,
                lprgscode = IntPtr.Zero,
                lprgelemdescParam = paramArray,
                funckind = FUNCKIND.FUNC_PUREVIRTUAL,
                invkind = kind,
                callconv = CALLCONV.CC_STDCALL,
                cParams = (short)parameters.Count,
                cParamsOpt = 0,
                oVft = (short)((FirstMemberSlot + index) * IntPtr.Size),
                cScodes = 0,
                wFuncFlags = 0,
            };
            desc.elemdescFunc.tdesc.vt = (short)VarEnum.VT_HRESULT;
            IntPtr descPointer = Allocate(Marshal.SizeOf<FUNCDESC>());
            Marshal.StructureToPtr(desc, descPointer, false);
            info.AddFuncDesc(index, descPointer);
            // The function's name and its parameters'; a property put leaves its value parameter unnamed.
            IEnumerable<string> named = parameters.Select(static p => p.Name);
            if (kind is INVOKEKIND.INVOKE_PROPERTYPUT or INVOKEKIND.INVOKE_PROPERTYPUTREF)
            {
                named = named.Take(Math.Max(parameters.Count - 1, 0));
            }

            string[] all = [name, .. named];

            IntPtr nameArray = Allocate(IntPtr.Size * all.Length);
            for (int n = 0; n < all.Length; n++)
            {
                IntPtr text = Marshal.StringToHGlobalUni(all[n]);
                _memory.Add(text);
                Marshal.WriteIntPtr(nameArray, n * IntPtr.Size, text);
            }

            info.SetFuncAndParamNames(index, nameArray, all.Length);
            info.SetFuncDocString(index, doc);
        }

        private Parameter Param(string name, IntPtr typeDesc, PARAMFLAG flags)
        {
            var elem = new ELEMDESC();
            elem.tdesc = Marshal.PtrToStructure<TYPEDESC>(typeDesc);
            elem.desc.paramdesc.wParamFlags = flags;
            elem.desc.paramdesc.lpVarValue = IntPtr.Zero;
            IntPtr pointer = Allocate(Marshal.SizeOf<ELEMDESC>());
            Marshal.StructureToPtr(elem, pointer, false);
            return new Parameter(name, pointer);
        }

        private IntPtr Plain(VarEnum vt)
        {
            var desc = new TYPEDESC { vt = (short)vt, lpValue = IntPtr.Zero };
            IntPtr pointer = Allocate(Marshal.SizeOf<TYPEDESC>());
            Marshal.StructureToPtr(desc, pointer, false);
            return pointer;
        }

        private IntPtr Pointer(IntPtr pointee)
        {
            var desc = new TYPEDESC { vt = (short)VarEnum.VT_PTR, lpValue = pointee };
            IntPtr pointer = Allocate(Marshal.SizeOf<TYPEDESC>());
            Marshal.StructureToPtr(desc, pointer, false);
            return pointer;
        }

        private IntPtr UserDefined(int href)
        {
            var desc = new TYPEDESC { vt = (short)VarEnum.VT_USERDEFINED, lpValue = href };
            IntPtr pointer = Allocate(Marshal.SizeOf<TYPEDESC>());
            Marshal.StructureToPtr(desc, pointer, false);
            return pointer;
        }

        private IntPtr Allocate(int size)
        {
            IntPtr block = Marshal.AllocHGlobal(size);
            Marshal.Copy(new byte[size], 0, block, size);
            _memory.Add(block);
            return block;
        }
    }

    [ComImport, Guid("0002040F-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateTypeLib2
    {
        // ICreateTypeLib
        void CreateTypeInfo([MarshalAs(UnmanagedType.LPWStr)] string name, TYPEKIND kind, out ICreateTypeInfo info);
        void SetName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void SetVersion(ushort major, ushort minor);
        void SetGuid(ref Guid guid);
        void SetDocString([MarshalAs(UnmanagedType.LPWStr)] string doc);
        void SetHelpFileName([MarshalAs(UnmanagedType.LPWStr)] string file);
        void SetHelpContext(int context);
        void SetLcid(int lcid);
        void SetLibFlags(int flags);
        void SaveAllChanges();

        // ICreateTypeLib2
        void DeleteTypeInfo([MarshalAs(UnmanagedType.LPWStr)] string name);
        void SetCustData(ref Guid guid, IntPtr value);
        void SetHelpStringContext(int context);
        void SetHelpStringDll([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("00020405-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateTypeInfo
    {
        void SetGuid(ref Guid guid);
        void SetTypeFlags(int flags);
        void SetDocString([MarshalAs(UnmanagedType.LPWStr)] string doc);
        void SetHelpContext(int context);
        void SetVersion(ushort major, ushort minor);
        void AddRefTypeInfo(ITypeInfo info, out int href);
        void AddFuncDesc(int index, IntPtr funcDesc);
        void AddImplType(int index, int href);
        void SetImplTypeFlags(int index, int flags);
        void SetAlignment(ushort alignment);
        void SetSchema([MarshalAs(UnmanagedType.LPWStr)] string schema);
        void AddVarDesc(int index, IntPtr varDesc);
        void SetFuncAndParamNames(int index, IntPtr names, int count);
        void SetVarName(int index, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void SetTypeDescAlias(IntPtr typeDesc);
        void DefineFuncAsDllEntry(int index, [MarshalAs(UnmanagedType.LPWStr)] string dll, [MarshalAs(UnmanagedType.LPWStr)] string procName);
        void SetFuncDocString(int index, [MarshalAs(UnmanagedType.LPWStr)] string doc);
        void SetVarDocString(int index, [MarshalAs(UnmanagedType.LPWStr)] string doc);
        void SetFuncHelpContext(int index, int context);
        void SetVarHelpContext(int index, int context);
        void SetMops(int index, [MarshalAs(UnmanagedType.BStr)] string mops);
        void SetTypeIdldesc(IntPtr idlDesc);
        void LayOut();
    }
}
