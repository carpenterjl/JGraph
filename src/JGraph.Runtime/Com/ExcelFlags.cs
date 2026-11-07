using JGraph.Deploy;

namespace JGraph.Runtime.Com;

/// <summary>
/// An add-in's <c>MWFlags</c> (compiler and SDK plan, stage K17c): MathWorks' flags object with its
/// two members, <c>ArrayFormatFlags</c> and <c>DataConversionFlags</c>, and <c>Clone</c>, under the
/// member names and defaults R2025b's answers (k17_com: flags.defaults, flags.clone, flags.put).
/// Each component object has one; a put copies the given object's settings into it, so the same
/// object is answered before and after. The flags are kept here; their effect on the conversions is
/// stage K17d's.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class ExcelFlags : ComServed
{
    public ExcelFlags(nint module, string progId)
        : base(module, progId)
    {
        ArrayFormat = new ExcelArrayFormatFlags(module, progId);
        DataConversion = new ExcelDataConversionFlags(module, progId);
        Adopt(ArrayFormat);
        Adopt(DataConversion);
    }

    public override Guid InterfaceId => ExcelComGuids.FlagsInterface;

    public ExcelArrayFormatFlags ArrayFormat { get; private set; }

    public ExcelDataConversionFlags DataConversion { get; private set; }

    public override object? Invoke(string member, ComInvokeKind kind, ComArguments arguments)
    {
        switch (member)
        {
            case "ArrayFormatFlags":
                if (kind == ComInvokeKind.PropertyGet)
                {
                    return ArrayFormat;
                }

                ArrayFormat = Given<ExcelArrayFormatFlags>(arguments, member);
                Adopt(ArrayFormat);
                return null;
            case "DataConversionFlags":
                if (kind == ComInvokeKind.PropertyGet)
                {
                    return DataConversion;
                }

                DataConversion = Given<ExcelDataConversionFlags>(arguments, member);
                Adopt(DataConversion);
                return null;
            case "Clone":
            {
                // Clone([out] IMWFlags**): a new object with these settings, independent of this one (flags.clone).
                var copy = new ExcelFlags(Module, ProgId);
                copy.CopyFrom(this);
                return copy;
            }

            default:
                throw NoMember(member);
        }
    }

    public void CopyFrom(ExcelFlags other)
    {
        ArrayFormat.CopyFrom(other.ArrayFormat);
        DataConversion.CopyFrom(other.DataConversion);
    }

    /// <summary>The served object a put handed over, which must be one of the kind the property holds.</summary>
    internal unsafe T Given<T>(ComArguments arguments, string member) where T : ComServed
    {
        Variant* v = ExcelVariants.Deref(arguments.At(0));
        nint pointer = v != null && (v->Vt & ~ExcelVariants.VtByRef) is ExcelVariants.VtDispatch or ExcelVariants.VtUnknown
            ? ((v->Vt & ExcelVariants.VtByRef) != 0 ? *(nint*)v->Pointer : v->Pointer)
            : 0;
        if (pointer != 0 && ComObjects.Of(Native.NativeComCalls.Unwrap(pointer)) is T ours)
        {
            return ours;
        }

        throw Fail($"{member} takes a {typeof(T).Name.Replace("Excel", "MW", StringComparison.Ordinal)} object made by this add-in.", ComCallException.TypeMismatch);
    }
}

/// <summary>MathWorks' <c>MWArrayFormatFlags</c>: six properties, as R2025b's defaults answer them (flags.defaults).</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class ExcelArrayFormatFlags(nint module, string progId) : ComServed(module, progId)
{
    public override Guid InterfaceId => ExcelComGuids.ArrayFormatFlagsInterface;

    /// <summary>mwArrayFormatMatrix (1) by default.</summary>
    public int InputArrayFormat { get; set; } = 1;

    public int InputArrayIndFlag { get; set; }

    /// <summary>mwArrayFormatAsIs (0) by default.</summary>
    public int OutputArrayFormat { get; set; }

    public int OutputArrayIndFlag { get; set; }

    public bool AutoResizeOutput { get; set; } = true;

    public bool TransposeOutput { get; set; }

    public override object? Invoke(string member, ComInvokeKind kind, ComArguments arguments)
    {
        bool get = kind == ComInvokeKind.PropertyGet;
        switch (member)
        {
            case "InputArrayFormat":
                if (get) return InputArrayFormat;
                InputArrayFormat = Flag.Int(arguments, this, member);
                return null;
            case "InputArrayIndFlag":
                if (get) return InputArrayIndFlag;
                InputArrayIndFlag = Flag.Int(arguments, this, member);
                return null;
            case "OutputArrayFormat":
                if (get) return OutputArrayFormat;
                OutputArrayFormat = Flag.Int(arguments, this, member);
                return null;
            case "OutputArrayIndFlag":
                if (get) return OutputArrayIndFlag;
                OutputArrayIndFlag = Flag.Int(arguments, this, member);
                return null;
            case "AutoResizeOutput":
                if (get) return AutoResizeOutput;
                AutoResizeOutput = Flag.Bool(arguments, this, member);
                return null;
            case "TransposeOutput":
                if (get) return TransposeOutput;
                TransposeOutput = Flag.Bool(arguments, this, member);
                return null;
            default:
                throw NoMember(member);
        }
    }

    public void CopyFrom(ExcelArrayFormatFlags other)
    {
        InputArrayFormat = other.InputArrayFormat;
        InputArrayIndFlag = other.InputArrayIndFlag;
        OutputArrayFormat = other.OutputArrayFormat;
        OutputArrayIndFlag = other.OutputArrayIndFlag;
        AutoResizeOutput = other.AutoResizeOutput;
        TransposeOutput = other.TransposeOutput;
    }
}

/// <summary>MathWorks' <c>MWDataConversionFlags</c>: six properties, as R2025b's defaults answer them (DateBias 693960).</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class ExcelDataConversionFlags(nint module, string progId) : ComServed(module, progId)
{
    public override Guid InterfaceId => ExcelComGuids.DataConversionFlagsInterface;

    /// <summary>mwTypeDefault (0) by default.</summary>
    public int CoerceNumericToType { get; set; }

    /// <summary>mwDateFormatNumeric (0) by default.</summary>
    public int InputDateFormat { get; set; }

    public bool OutputAsDate { get; set; }

    public int DateBias { get; set; } = ExcelVariants.DateBias;

    /// <summary>mwReplaceZero (0) by default.</summary>
    public int ReplaceMissing { get; set; }

    /// <summary>mwReplaceWithEmptyCell (0) by default.</summary>
    public int ReplaceMatlabNaN { get; set; }

    public override object? Invoke(string member, ComInvokeKind kind, ComArguments arguments)
    {
        bool get = kind == ComInvokeKind.PropertyGet;
        switch (member)
        {
            case "CoerceNumericToType":
                if (get) return CoerceNumericToType;
                CoerceNumericToType = Flag.Int(arguments, this, member);
                return null;
            case "InputDateFormat":
                if (get) return InputDateFormat;
                InputDateFormat = Flag.Int(arguments, this, member);
                return null;
            case "OutputAsDate":
                if (get) return OutputAsDate;
                OutputAsDate = Flag.Bool(arguments, this, member);
                return null;
            case "DateBias":
                if (get) return DateBias;
                DateBias = Flag.Int(arguments, this, member);
                return null;
            case "ReplaceMissing":
                if (get) return ReplaceMissing;
                ReplaceMissing = Flag.Int(arguments, this, member);
                return null;
            case "ReplaceMatlabNaN":
                if (get) return ReplaceMatlabNaN;
                ReplaceMatlabNaN = Flag.Int(arguments, this, member);
                return null;
            default:
                throw NoMember(member);
        }
    }

    public void CopyFrom(ExcelDataConversionFlags other)
    {
        CoerceNumericToType = other.CoerceNumericToType;
        InputDateFormat = other.InputDateFormat;
        OutputAsDate = other.OutputAsDate;
        DateBias = other.DateBias;
        ReplaceMissing = other.ReplaceMissing;
        ReplaceMatlabNaN = other.ReplaceMatlabNaN;
    }
}

/// <summary>A flag's value as a put hands it over: a long or a VARIANT_BOOL, coerced by the type library's Invoke before it arrives.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static unsafe class Flag
{
    public static int Int(ComArguments arguments, ComServed owner, string member)
    {
        Variant* v = ExcelVariants.Deref(arguments.At(0));
        if (v == null)
        {
            throw new ComCallException(ComCallException.TypeMismatch, owner.ProgId, $"{member} takes a number.");
        }

        object? value = System.Runtime.InteropServices.Marshal.GetObjectForNativeVariant((IntPtr)v);
        return value is IConvertible c and not string ? c.ToInt32(System.Globalization.CultureInfo.InvariantCulture)
            : throw new ComCallException(ComCallException.TypeMismatch, owner.ProgId, $"{member} takes a number.");
    }

    public static bool Bool(ComArguments arguments, ComServed owner, string member)
    {
        Variant* v = ExcelVariants.Deref(arguments.At(0));
        if (v == null)
        {
            throw new ComCallException(ComCallException.TypeMismatch, owner.ProgId, $"{member} takes True or False.");
        }

        object? value = System.Runtime.InteropServices.Marshal.GetObjectForNativeVariant((IntPtr)v);
        return value is IConvertible c and not string ? c.ToBoolean(System.Globalization.CultureInfo.InvariantCulture)
            : throw new ComCallException(ComCallException.TypeMismatch, owner.ProgId, $"{member} takes True or False.");
    }
}
