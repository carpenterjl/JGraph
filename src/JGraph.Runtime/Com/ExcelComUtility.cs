using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using JGraph.Deploy;

namespace JGraph.Runtime.Com;

/// <summary>
/// JGraph's COM utility library for Excel add-ins (compiler and SDK plan, stage K17d; decision
/// KO10): the engine's side of <c>jgcomutil.dll</c> (<c>native\jgcomutil</c>), the counterpart of
/// MATLAB Runtime's <c>mwcomutil.dll</c>, whose classes a client creates as <c>JGraphComUtil.MWUtil25.2</c>,
/// <c>JGraphComUtil.MWFlags</c>, <c>MWStruct</c>, <c>MWComplex</c>, <c>MWSparse</c>, <c>MWArg</c> and
/// <c>MWField</c> (<c>MWArrayFormatFlags</c> and <c>MWDataConversionFlags</c> too, which R2025b's library
/// registers but cannot create). The library's module in jgrt is found once, from the DLL beside
/// <c>jgrt.dll</c>, for the objects a component makes of these classes.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class ExcelComUtilityLibrary
{
    private static readonly object Gate = new();
    private static nint _module;
    private static bool _moduleLooked;

    /// <summary>Whether a component has been opened in this process, which is when R2025b's <c>IsMCRInitialized</c> turns true (and stays so after <c>MWTerminateApplication</c>).</summary>
    public static bool RuntimeInitialized { get; internal set; }

    /// <summary>The Excel Application <c>MWInitApplication</c> was given, or null.</summary>
    public static object? Application { get; internal set; }

    /// <summary>jgrt's module for the utility library's type library, loaded from the DLL beside jgrt.dll; 0 when jgrt is not bound or the DLL is not there.</summary>
    public static nint Module
    {
        get
        {
            lock (Gate)
            {
                if (!_moduleLooked && _module == 0)
                {
                    _moduleLooked = true;
                    string? path = ExcelAddIn.DefaultUtilityPath;
                    if (path is not null && Native.NativeComCalls.IsBound)
                    {
                        _module = Native.NativeComCalls.LoadModule(path);
                    }
                }

                return _module;
            }
        }
    }

    /// <summary>The object of the library's coclass <paramref name="className"/>, served from <paramref name="module"/> (jgrt's module for the DLL the client created it through).</summary>
    /// <exception cref="ArgumentException">The library has no such class.</exception>
    public static ComServed Create(string className, nint module)
    {
        lock (Gate)
        {
            if (_module == 0 && module != 0)
            {
                _module = module;
                _moduleLooked = true;
            }
        }

        nint served = module != 0 ? module : Module;
        return className switch
        {
            "MWUtil" => new ExcelComUtility(served),
            "MWFlags" => new ExcelFlags(served, ExcelComGuids.UtilityVersionedProgId("MWFlags")),
            "MWArrayFormatFlags" => new ExcelArrayFormatFlags(served, ExcelComGuids.UtilityVersionedProgId("MWArrayFormatFlags")),
            "MWDataConversionFlags" => new ExcelDataConversionFlags(served, ExcelComGuids.UtilityVersionedProgId("MWDataConversionFlags")),
            "MWField" => new ExcelField(served),
            "MWStruct" => new ExcelStruct(served),
            "MWComplex" => new ExcelComplex(served),
            "MWSparse" => new ExcelSparse(served),
            "MWArg" => new ExcelArg(served),
            _ => throw new ArgumentException($"The JGraph COM utility library has no class {className}.", nameof(className)),
        };
    }
}

/// <summary>
/// <c>JGraphComUtil.MWUtil25.2</c>: MathWorks' <c>MWUtil</c> for JGraph's add-ins, its nine methods as
/// R2025b's answer them (k17_com: util.*; k17_com_extra: x.util.*): <c>MWPack</c> packing its
/// arguments into a one-based array of Variants (a VT_EMPTY or missing one left out, a matrix spread
/// element by element, a one-dimensional array kept whole), <c>MWUnpack</c> handing the elements of
/// an array of Variants (a matrix column by column; anything else one element) to its slots from
/// <c>nStartAt</c>, a slot that is a Range written through it; <c>MWDate2VariantDate</c> taking the
/// default <c>DateBias</c> off a double; <c>MWInitApplication</c> keeping the Excel Application an
/// Excel object names through its <c>Application</c> property; <c>IsMCRJVMEnabled</c> false (JGraph has
/// no JVM); <c>IsMCRInitialized</c> true once a component has been opened; <c>MWTerminateApplication</c>
/// ending nothing, since JGraph's objects live by their references (ADR 0317).
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class ExcelComUtility(nint module) : ComServed(module, ExcelComGuids.UtilityVersionedProgId("MWUtil"))
{
    public override Guid InterfaceId => ExcelComGuids.UtilInterface;

    public override unsafe object? Invoke(string member, ComInvokeKind kind, ComArguments arguments)
    {
        switch (member)
        {
            case "MWPack":
            {
                var packed = new List<object?>();
                for (int k = 1; k < arguments.Count; k++)
                {
                    object? value = arguments.Value(k);
                    if (value is not null)
                    {
                        packed.Add(value);
                    }
                }

                if (packed.Count == 1 && packed[0] is Array alone && alone.Rank >= 2)
                {
                    // One matrix alone is the pack, spread row by row as its elements are laid out to a client
                    // (x.util.pack_2d); beside other arguments it is one element (x.util.unpack_range_two_slots).
                    packed = [.. RowMajor(alone)];
                }

                ExcelVariants.WriteObjectSlot(arguments.At(0), packed.Count == 0 ? null : ExcelConversion.Fill1(typeof(object), packed.Count, k => packed[k]), autoResize: false, this);
                return null;
            }

            case "MWUnpack":
            {
                object? packed = arguments.Value(0);
                int start = arguments.Value(1) is IConvertible c and not string ? c.ToInt32(CultureInfo.InvariantCulture) : 0;
                bool autoResize = arguments.Value(2) is IConvertible b and not string && b.ToBoolean(CultureInfo.InvariantCulture);
                object?[] elements = packed switch
                {
                    null => [],
                    object?[] variants => variants,
                    Array array when array.GetType().GetElementType() == typeof(object) => ExcelConversion.ColumnMajor(array),
                    _ => [packed],
                };
                if (start < 0)
                {
                    // Nothing is handed out from a start before the first element (x.util.unpack_negative_start).
                    return null;
                }

                for (int slot = 3, k = start; slot < arguments.Count && k < elements.Length; slot++, k++)
                {
                    Variant* target = arguments.At(slot);
                    if (target == null || ExcelVariants.IsMissing(target) && (target->Vt & ExcelVariants.VtByRef) == 0)
                    {
                        continue;
                    }

                    ExcelVariants.WriteObjectSlot(target, elements[k], autoResize, this);
                }

                return null;
            }

            case "MWDate2VariantDate":
            {
                // A double alone: the datenum less the default bias, still a double (util.MWDate2VariantDate.*).
                Variant* slot = arguments.At(0);
                if (arguments.Value(0) is double d)
                {
                    ExcelVariants.WriteObjectSlot(slot, d - ExcelVariants.DateBias, autoResize: false, this);
                }

                return null;
            }

            case "MWInitApplication":
            case "MWInitApplicationWithMCROptions":
            {
                if (member == "MWInitApplicationWithMCROptions")
                {
                    object? options = arguments.Value(1);
                    if (options is not string[] && !(options is Array a && a.GetType().GetElementType() == typeof(string)))
                    {
                        throw new ComCallException(ComCallException.InvalidArgument, ProgId, "The JGraph Runtime option list must be a string array.");
                    }
                }

                object? app = arguments.Value(0);
                if (app is null || !Marshal.IsComObject(app))
                {
                    throw Fail("MWInitApplication takes Excel's Application object, or a Workbook, Worksheet or Range of it.");
                }

                // Any Excel object names its Application (x.util.init_worksheet, init_workbook); an object that does not is refused (preinit.init.dictionary).
                try
                {
                    ExcelComUtilityLibrary.Application = app.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, app, null, CultureInfo.InvariantCulture);
                }
                catch (Exception ex) when (ex is TargetInvocationException or MissingMemberException or COMException)
                {
                    throw Fail("MWInitApplication takes an Excel object: the one given has no Application property.");
                }

                return null;
            }

            case "CreateRemoteObject":
            {
                // The object of the ProgID given, made in this process; nothing for a ProgID no class has (util.CreateRemoteObject.unknown).
                string progId = arguments.Value(0) as string ?? "";
                Type? type = progId.Length == 0 ? null : Type.GetTypeFromProgID(progId, throwOnError: false);
                return type is null ? null : Activator.CreateInstance(type);
            }

            case "IsMCRJVMEnabled":
                return false;
            case "IsMCRInitialized":
                return ExcelComUtilityLibrary.RuntimeInitialized;
            case "MWTerminateApplication":
                // R2025b's ends its runtime (a call after it ends the host); JGraph's objects live by their references.
                return null;
            default:
                throw NoMember(member);
        }
    }

    /// <summary>The elements of a matrix in row-major order, the last index fastest (x.util.pack_2d: [1 2; 3 4] packs as 1, 2, 3, 4).</summary>
    private static IEnumerable<object?> RowMajor(Array array)
    {
        int rank = array.Rank;
        int[] lengths = [.. Enumerable.Range(0, rank).Select(array.GetLength)];
        int[] lower = [.. Enumerable.Range(0, rank).Select(array.GetLowerBound)];
        var subs = new int[rank];
        int count = array.Length;
        for (int k = 0; k < count; k++)
        {
            int rest = k;
            for (int d = rank - 1; d >= 0; d--)
            {
                subs[d] = rest % lengths[d] + lower[d];
                rest /= lengths[d];
            }

            yield return array.GetValue(subs);
        }
    }
}
