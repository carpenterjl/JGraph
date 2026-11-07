using JGraph.Deploy;

namespace JGraph.Runtime.Com;

/// <summary>
/// An Excel add-in's component object (compiler and SDK plan, stage K17c): what a client gets from
/// <c>CreateObject("&lt;addin&gt;.&lt;Class&gt;")</c>, served by jgrt.dll's generic object over this.
/// It opens the add-in's archive (resource 2 of its DLL, or <c>&lt;addin&gt;.ctf</c> beside it) as a
/// <see cref="Component"/>, so every object of one add-in shares its session, as R2025b's singleton
/// module does (k17_com: count.secondobject), and serves the members its type library declares:
/// <c>MWFlags</c>, each function as <c>f(nargout, out..., in...)</c> with R2025b's measured rules for
/// <c>nargout</c>, <c>varargin</c> and <c>varargout</c> (k17_com: two.*, varout.*, both.*, varin.*,
/// opt.*), and <c>help(name)</c>, a function's H1 line. An error the function raises reaches the
/// client as E_FAIL with the versioned ProgID as its source and R2025b's text as its description.
/// Unlike R2025b's, the object works before <c>MWInitApplication</c>: JGraph's add-in needs no
/// Excel to run (k17_com_preinit: preinit.call, a divergence).
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class ExcelComponent : ComServed, IDisposable
{
    private readonly Component _component;
    private readonly ExcelTypeLibInfo _library;
    private readonly Dictionary<string, ExcelTypeLibFunction> _functions;

    private ExcelComponent(Component component, ExcelTypeLibInfo library, nint module)
        : base(module, library.VersionedProgId)
    {
        _component = component;
        _library = library;
        _functions = library.Functions.ToDictionary(static f => f.Name, StringComparer.Ordinal);
        Flags = new ExcelFlags(module, library.VersionedProgId);
        Adopt(Flags);
    }

    /// <summary>The object's flags: its own until a client puts another flags object, which then is them (flags.put).</summary>
    public ExcelFlags Flags { get; private set; }

    public override Guid InterfaceId => _library.InterfaceId;

    /// <summary>The add-in's functions, as its type library declares them.</summary>
    public IReadOnlyList<ExcelTypeLibFunction> Functions => _library.Functions;

    /// <summary>
    /// Opens the add-in DLL at <paramref name="dllPath"/>: its type library for the members, its
    /// archive (embedded, else <c>&lt;addin&gt;.ctf</c> beside it) for the component.
    /// </summary>
    /// <exception cref="FileNotFoundException">The archive is neither in the DLL nor beside it.</exception>
    /// <exception cref="InvalidDataException">The DLL carries no type library.</exception>
    public static ExcelComponent Open(string dllPath, nint module)
    {
        ArgumentException.ThrowIfNullOrEmpty(dllPath);
        string full = Path.GetFullPath(dllPath);
        ExcelTypeLibInfo library = ExcelTypeLib.Read(full);
        string archive = PeResources.Read(full, 10, 2) is not null ? full : Path.Combine(Path.GetDirectoryName(full)!, library.LibraryName + ".ctf");
        if (!File.Exists(archive))
        {
            throw new FileNotFoundException(
                $"The add-in's archive was not found: it is neither embedded in {Path.GetFileName(full)} nor beside it as {library.LibraryName}.ctf.", archive);
        }

        Component component = Component.Open(archive);
        return new ExcelComponent(component, library, module);
    }

    public override unsafe object? Invoke(string member, ComInvokeKind kind, ComArguments arguments)
    {
        switch (member)
        {
            case "MWFlags":
                if (kind == ComInvokeKind.PropertyGet)
                {
                    return Flags;
                }

                // flags.put: the given flags object becomes this object's flags (7 then 693960: putting the
                // original back reads its own bias, so the put keeps the object, not a copy of its settings).
                ExcelFlags given = Flags.Given<ExcelFlags>(arguments, member);
                if (!ReferenceEquals(given, Flags))
                {
                    Flags = given;
                    Adopt(given);
                }

                return null;
            case "help":
            {
                // help.*: the H1 line of the function named exactly, else '' (help.case, help.unknown).
                Variant* name = ExcelVariants.Deref(arguments.At(0));
                string asked = name == null ? "" : System.Runtime.InteropServices.Marshal.GetObjectForNativeVariant((IntPtr)name) as string ?? "";
                return _functions.TryGetValue(asked, out ExcelTypeLibFunction? f) ? f.Help : "";
            }

            default:
                if (_functions.TryGetValue(member, out ExcelTypeLibFunction? function))
                {
                    return Call(function, arguments);
                }

                throw NoMember(member);
        }
    }

    /// <summary>
    /// <c>f([in] long nargout, [in, out] VARIANT* out..., [in] VARIANT in...)</c>: the function called
    /// for <c>nargout</c> outputs, the first <c>nargout</c> slots filled and the rest left as they
    /// were (two.nargout1), <c>varargout</c>'s slot a cell of the outputs past the fixed ones
    /// (varout.*, both.*); a <c>varargin</c> argument that is a one-dimensional array of Variants
    /// spread into arguments (varin.vararray, both.packed) and anything else one argument;
    /// trailing arguments not given left out and one missing in the middle refused.
    /// </summary>
    private unsafe object? Call(ExcelTypeLibFunction function, ComArguments arguments)
    {
        int at = 0;
        int nargout = 0;
        var slots = new List<nint>();
        if (function.Outputs.Count > 0)
        {
            Variant* count = ExcelVariants.Deref(arguments.At(at++));
            nargout = count == null ? 0 : ReadNargout(count);
            for (int k = 0; k < function.Outputs.Count; k++)
            {
                slots.Add((nint)arguments.At(at++));
            }
        }

        if (nargout < 0)
        {
            // R2025b's add-in ends its host here (a stack overflow in CallComFcn; k17_com_preinit: preinit.crash.nargoutneg).
            throw Fail($"The number of outputs asked for, {nargout}, cannot be negative.");
        }

        var inputs = new List<RuntimeValue?>();
        for (int i = 0; i < function.Inputs.Count; i++)
        {
            Variant* argument = arguments.At(at++);
            bool missing = ExcelVariants.IsMissing(argument);
            if (function.HasVarargin && i == function.Inputs.Count - 1)
            {
                if (missing)
                {
                    break;
                }

                Variant* v = ExcelVariants.Deref(argument);
                if (v->Vt == (ExcelVariants.VtArray | ExcelVariants.VtVariant)
                    && System.Runtime.InteropServices.Marshal.GetObjectForNativeVariant((IntPtr)v) is object?[] spread)
                {
                    foreach (object? element in spread)
                    {
                        inputs.Add(FromElement(element));
                    }
                }
                else
                {
                    inputs.Add(ExcelVariants.Read(argument, this));
                }

                break;
            }

            inputs.Add(missing ? null : ExcelVariants.Read(argument, this));
        }

        int last = inputs.FindLastIndex(static v => v is not null);
        for (int i = 0; i < last; i++)
        {
            if (inputs[i] is null)
            {
                // R2025b refuses too, with no text at all (opt.middle, err.defaultmiddle); JGraph says which.
                throw Fail($"Input argument {i + 1} ({function.Inputs[Math.Min(i, function.Inputs.Count - 1)]}) was not given; only trailing arguments may be left out.");
            }
        }

        RuntimeValue[] given = [.. inputs.Take(last + 1).Select(static v => v!)];
        RuntimeValue[] outputs;
        try
        {
            outputs = _component.Call(function.Name, nargout, given);
        }
        catch (DeployedException failure)
        {
            throw Fail(Native.NativeCalls.ClientMessage(failure));
        }
        catch (ArgumentException failure)
        {
            throw Fail(failure.Message);
        }
        catch (ObjectDisposedException)
        {
            throw Fail("The add-in's runtime has ended.");
        }

        int fixedOutputs = function.HasVarargout ? function.Outputs.Count - 1 : function.Outputs.Count;
        for (int k = 0; k < Math.Min(nargout, fixedOutputs) && k < outputs.Length; k++)
        {
            ExcelVariants.WriteSlot((Variant*)slots[k], outputs[k], this);
        }

        if (function.HasVarargout && nargout > fixedOutputs && outputs.Length > fixedOutputs)
        {
            RuntimeValue[] rest = [.. outputs.Skip(fixedOutputs).Take(nargout - fixedOutputs)];
            ExcelVariants.WriteSlot((Variant*)slots[fixedOutputs], RuntimeCell.Row(rest), this);
        }

        return null;
    }

    private unsafe int ReadNargout(Variant* count)
    {
        ushort vt = (ushort)(count->Vt & ~ExcelVariants.VtByRef);
        if (vt == ExcelVariants.VtI4 || vt == ExcelVariants.VtInt)
        {
            return (count->Vt & ExcelVariants.VtByRef) != 0 ? *(int*)count->Pointer : count->I4;
        }

        object? value = System.Runtime.InteropServices.Marshal.GetObjectForNativeVariant((IntPtr)count);
        return value is IConvertible c and not string
            ? c.ToInt32(System.Globalization.CultureInfo.InvariantCulture)
            : throw Fail("nargout must be a number.", ComCallException.TypeMismatch);
    }

    /// <summary>One element of a spread <c>varargin</c> array, as its own argument.</summary>
    private unsafe RuntimeValue FromElement(object? element)
    {
        Variant temporary;
        temporary.Vt = ExcelVariants.VtEmpty;
        temporary.I8 = 0;
        if (element is not null)
        {
            System.Runtime.InteropServices.Marshal.GetNativeVariantForObject(element, (IntPtr)(&temporary));
        }

        try
        {
            return ExcelVariants.Read(&temporary, this);
        }
        finally
        {
            ExcelVariants.VariantClear(&temporary);
        }
    }

    /// <summary>The client's last reference went: the flags' wrappers go with it (<see cref="ComServed.Released"/>), and the component's hold on its session.</summary>
    protected override void OnReleased() => Dispose();

    public void Dispose() => _component.Dispose();
}
