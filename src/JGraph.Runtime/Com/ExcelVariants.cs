using System.Globalization;
using System.Runtime.InteropServices;

namespace JGraph.Runtime.Com;

/// <summary>A VARIANT as COM lays it out: the type word, then eight bytes of value at offset 8.</summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct Variant
{
    [FieldOffset(0)] public ushort Vt;
    [FieldOffset(8)] public nint Pointer;
    [FieldOffset(8)] public int I4;
    [FieldOffset(8)] public long I8;
    [FieldOffset(8)] public short I2;
    [FieldOffset(8)] public double R8;
}

/// <summary>The arguments of one member call, as jgrt hands them over: a pointer per declared parameter.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed unsafe class ComArguments(Variant** argv, int count)
{
    /// <summary>How many parameters were passed.</summary>
    public int Count { get; } = count;

    /// <summary>The <paramref name="index"/>th argument, or null past the end.</summary>
    public Variant* At(int index) => index >= 0 && index < Count && argv != null ? argv[index] : null;

    /// <summary>The <paramref name="index"/>th argument as the COM marshaller reads it; null when missing or VT_EMPTY.</summary>
    public object? Value(int index)
    {
        Variant* v = ExcelVariants.Deref(At(index));
        return v == null || ExcelVariants.IsMissing(v) ? null : Marshal.GetObjectForNativeVariant((IntPtr)v);
    }
}

/// <summary>
/// The VARIANTs at an Excel add-in's call boundary (compiler and SDK plan, stages K17c and K17d):
/// reading one as the COM marshaller does, writing one, telling a missing argument, and the slots
/// a client passes by reference. The conversion rules themselves are <see cref="ExcelConversion"/>'s.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static unsafe class ExcelVariants
{
    public const ushort VtEmpty = 0;
    public const ushort VtNull = 1;
    public const ushort VtI2 = 2;
    public const ushort VtI4 = 3;
    public const ushort VtR4 = 4;
    public const ushort VtR8 = 5;
    public const ushort VtCy = 6;
    public const ushort VtDate = 7;
    public const ushort VtBstr = 8;
    public const ushort VtDispatch = 9;
    public const ushort VtError = 10;
    public const ushort VtBool = 11;
    public const ushort VtVariant = 12;
    public const ushort VtUnknown = 13;
    public const ushort VtDecimal = 14;
    public const ushort VtI1 = 16;
    public const ushort VtUI1 = 17;
    public const ushort VtUI2 = 18;
    public const ushort VtUI4 = 19;
    public const ushort VtI8 = 20;
    public const ushort VtUI8 = 21;
    public const ushort VtInt = 22;
    public const ushort VtUInt = 23;
    public const ushort VtArray = 0x2000;
    public const ushort VtByRef = 0x4000;

    /// <summary>DISP_E_PARAMNOTFOUND: a missing optional argument, which a VBA caller leaves out and a .NET one passes as Type.Missing.</summary>
    public const int ParamNotFound = unchecked((int)0x80020004);

    /// <summary>The days from MATLAB's datenum epoch to the OLE date's (30 December 1899), the default DateBias.</summary>
    public const int DateBias = 693960;

    /// <summary>The VARIANT a byref chain of VARIANTs ends at.</summary>
    public static Variant* Deref(Variant* v)
    {
        while (v != null && v->Vt == (VtByRef | VtVariant) && v->Pointer != 0)
        {
            v = (Variant*)v->Pointer;
        }

        return v;
    }

    /// <summary>
    /// Whether an argument was not given: VT_EMPTY, or the VT_ERROR a missing optional argument
    /// arrives as. R2025b takes a trailing one as not passed and refuses one in the middle (k17_com:
    /// opt.emptylast, opt.middle, both.missingvarargin).
    /// </summary>
    public static bool IsMissing(Variant* v)
    {
        v = Deref(v);
        if (v == null)
        {
            return true;
        }

        ushort vt = (ushort)(v->Vt & ~VtByRef);
        if (vt == VtEmpty)
        {
            return true;
        }

        if (vt == VtError)
        {
            int code = (v->Vt & VtByRef) != 0 ? *(int*)v->Pointer : v->I4;
            return code == ParamNotFound;
        }

        return false;
    }

    /// <summary>What a VARIANT holds, as the COM marshaller reads it; null for VT_EMPTY and for a missing argument.</summary>
    public static object? Read(Variant* v)
    {
        v = Deref(v);
        if (v == null)
        {
            return null;
        }

        ushort vt = (ushort)(v->Vt & ~VtByRef);
        if (vt == VtEmpty)
        {
            return null;
        }

        return Marshal.GetObjectForNativeVariant((IntPtr)v);
    }

    /// <summary>Writes <paramref name="value"/> into <paramref name="target"/>, cleared first: a served object wrapped (inside an array too), anything else as the marshaller writes it.</summary>
    public static void Write(object? value, Variant* target)
    {
        if (target == null)
        {
            return;
        }

        VariantClear(target);
        switch (value)
        {
            case null:
                return;
            case ComServed served:
                target->Pointer = ComObjects.Wrap(served);
                target->Vt = VtDispatch;
                return;
            default:
                Marshal.GetNativeVariantForObject(Wrapped(value), (IntPtr)target);
                return;
        }
    }

    /// <summary>
    /// A value with every served object in it replaced by a runtime-callable wrapper of its COM
    /// object, so an array of Variants holding one marshals the object jgrt serves and not a .NET
    /// class object (x.OutputArrayFormat.2.complex: an array of MWComplex objects).
    /// </summary>
    public static object Wrapped(object value)
    {
        if (value is ComServed served)
        {
            nint wrapper = ComObjects.Wrap(served);
            try
            {
                return Marshal.GetObjectForIUnknown(wrapper);
            }
            finally
            {
                Marshal.Release(wrapper);
            }
        }

        if (value is Array array && array.GetType().GetElementType() == typeof(object))
        {
            int[] lengths = [.. Enumerable.Range(0, array.Rank).Select(array.GetLength)];
            int[] lower = [.. Enumerable.Range(0, array.Rank).Select(array.GetLowerBound)];
            bool any = false;
            foreach (object? element in array)
            {
                if (element is ComServed || (element is Array inner && inner.GetType().GetElementType() == typeof(object)))
                {
                    any = true;
                    break;
                }
            }

            if (!any)
            {
                return value;
            }

            Array made = Array.CreateInstance(typeof(object), lengths, lower);
            var subs = new int[array.Rank];
            object?[] elements = ExcelConversion.ColumnMajor(array);
            for (int k = 0; k < elements.Length; k++)
            {
                int rest = k;
                for (int d = 0; d < array.Rank; d++)
                {
                    subs[d] = rest % lengths[d] + lower[d];
                    rest /= lengths[d];
                }

                made.SetValue(elements[k] is null ? null : Wrapped(elements[k]!), subs);
            }

            return made;
        }

        return value;
    }

    /// <summary>
    /// Writes an output into the slot a client gave for it: through a byref VARIANT (what VBA and a
    /// .NET [ref] pass), into a byref scalar by conversion, and nowhere for a slot passed by value
    /// (k17_com: err.slotnotbyref answers nothing and no error). A slot holding a Range (or another
    /// object with Value2) is written through the Range (stage K17d; <see cref="ExcelConversion.WriteRange"/>),
    /// which replaces the slot's object by the range written.
    /// </summary>
    public static void WriteSlot(Variant* slot, RuntimeValue value, ExcelFlags flags, ComServed owner, int depth = 0)
    {
        if (slot == null || (slot->Vt & VtByRef) == 0)
        {
            return;
        }

        if ((slot->Vt & ~VtByRef) == VtVariant)
        {
            Variant* target = Deref(slot);
            if (target == null)
            {
                return;
            }

            if ((target->Vt & VtByRef) != 0 && (target->Vt & ~VtByRef) != VtVariant)
            {
                // The client's own variable is a typed one ([ref]$d of a double): written through by conversion.
                WriteSlot(target, value, flags, owner, depth);
                return;
            }

            if ((target->Vt & ~VtByRef) is VtDispatch or VtUnknown && Pointer(target) != 0
                && ComObjects.Of(Native.NativeComCalls.Unwrap(Pointer(target))) is null)
            {
                object range = Marshal.GetObjectForIUnknown(Pointer(target));
                object? written = ExcelConversion.WriteRange(range, value, flags, owner, depth);
                if (written is not null && !ReferenceEquals(written, range))
                {
                    Write(written, target);
                }

                return;
            }

            Write(ExcelConversion.ToClient(value, flags, owner, depth), target);
            return;
        }

        if ((slot->Vt & ~VtByRef) is VtDispatch or VtUnknown)
        {
            nint pointer = *(nint*)slot->Pointer;
            if (pointer != 0 && ComObjects.Of(Native.NativeComCalls.Unwrap(pointer)) is null)
            {
                object range = Marshal.GetObjectForIUnknown(pointer);
                object? written = ExcelConversion.WriteRange(range, value, flags, owner, depth);
                if (written is not null && !ReferenceEquals(written, range))
                {
                    Marshal.Release(pointer);
                    *(nint*)slot->Pointer = Marshal.GetIDispatchForObject(written);
                }
            }

            return;
        }

        Variant made;
        made.Vt = VtEmpty;
        made.I8 = 0;
        Write(ExcelConversion.ToClient(value, flags, owner, depth), &made);
        ushort wanted = (ushort)(slot->Vt & ~VtByRef);
        try
        {
            if (made.Vt != wanted)
            {
                int hr = VariantChangeType(&made, &made, 0, wanted);
                if (hr < 0)
                {
                    throw new ComCallException(ComCallException.TypeMismatch, owner.ProgId,
                        $"An output of class {value.ClassName} cannot be stored in the variable given for it.");
                }
            }

            switch (wanted)
            {
                case VtR8: *(double*)slot->Pointer = made.R8; break;
                case VtI4: case VtInt: case VtUI4: case VtUInt: case VtError: *(int*)slot->Pointer = made.I4; break;
                case VtI2: case VtUI2: case VtBool: *(short*)slot->Pointer = made.I2; break;
                case VtI8: case VtUI8: case VtDate: case VtCy: *(long*)slot->Pointer = made.I8; break;
                case VtBstr: case VtDispatch: case VtUnknown:
                    *(nint*)slot->Pointer = made.Pointer;
                    made.Vt = VtEmpty;
                    break;
                default:
                    throw new ComCallException(ComCallException.TypeMismatch, owner.ProgId, "An output cannot be stored in the variable given for it.");
            }
        }
        finally
        {
            VariantClear(&made);
        }
    }

    /// <summary>
    /// Writes <paramref name="value"/>, already converted, into a byref slot (a utility method's
    /// <c>[in, out] VARIANT*</c>): through a Range when the slot holds one (nothing clears the cell:
    /// x.util.unpack_range_null_element), into a byref VARIANT, or into a byref scalar by conversion
    /// (a client's <c>[ref]$d</c> of a double is VT_BYREF | VT_R8, util.MWDate2VariantDate.datenum)
    /// when the value is of that very type, and not at all otherwise: R2025b's utility writes a
    /// number into a client's string variable no more than into one it cannot (x.util.pack_prefilled,
    /// x.util.unpack_start_last leave 'old' as it was).
    /// </summary>
    public static void WriteObjectSlot(Variant* slot, object? value, bool autoResize, ComServed owner)
    {
        if (slot == null || (slot->Vt & VtByRef) == 0)
        {
            return;
        }

        // The chain of byref VARIANTs ends at the client's own: a Range (by value, or by reference as a
        // [ref]$r of a COM object arrives), a VARIANT to write into, or a byref scalar (ITypeInfo::Invoke
        // hands a [ref]$d of a double over as a pointer to its VT_BYREF | VT_R8 VARIANT).
        slot = Deref(slot);
        if ((slot->Vt & ~VtByRef) is VtDispatch or VtUnknown && Pointer(slot) != 0 && ComObjects.Of(Native.NativeComCalls.Unwrap(Pointer(slot))) is null)
        {
            object range = Marshal.GetObjectForIUnknown(Pointer(slot));
            object? written;
            if (value is null)
            {
                try
                {
                    ExcelConversion.Set(range, null);
                }
                catch (Exception ex) when (ex is System.Reflection.TargetInvocationException or COMException or MissingMemberException)
                {
                    // Not a Range after all; the slot keeps its object.
                }

                return;
            }

            written = ExcelConversion.WriteRangeObject(range, value, autoResize, transpose: false, owner);
            if (written is not null && !ReferenceEquals(written, range))
            {
                if ((slot->Vt & VtByRef) != 0)
                {
                    Marshal.Release(*(nint*)slot->Pointer);
                    *(nint*)slot->Pointer = Marshal.GetIDispatchForObject(written);
                }
                else
                {
                    Write(written, slot);
                }
            }

            return;
        }

        if ((slot->Vt & VtByRef) != 0 && (slot->Vt & ~VtByRef) != VtVariant)
        {
            Variant made;
            made.Vt = VtEmpty;
            made.I8 = 0;
            Write(value, &made);
            ushort wanted = (ushort)(slot->Vt & ~VtByRef);
            try
            {
                if (made.Vt != wanted)
                {
                    return;
                }

                switch (wanted)
                {
                    case VtR8: *(double*)slot->Pointer = made.R8; break;
                    case VtR4: *(float*)slot->Pointer = *(float*)&made.R8; break;
                    case VtI4: case VtInt: case VtUI4: case VtUInt: case VtError: *(int*)slot->Pointer = made.I4; break;
                    case VtI2: case VtUI2: case VtBool: *(short*)slot->Pointer = made.I2; break;
                    case VtI8: case VtUI8: case VtDate: case VtCy: *(long*)slot->Pointer = made.I8; break;
                    case VtBstr: case VtDispatch: case VtUnknown:
                        *(nint*)slot->Pointer = made.Pointer;
                        made.Vt = VtEmpty;
                        break;
                    default:
                        break;
                }
            }
            finally
            {
                VariantClear(&made);
            }

            return;
        }

        Write(value, slot);
    }

    private static nint Pointer(Variant* v) => (v->Vt & VtByRef) != 0 ? *(nint*)v->Pointer : v->Pointer;

    /// <summary>The text oleaut32 makes of a VARIANT (a date in the user's format, True and False, a number's digits): what <c>VariantChangeType</c> to BSTR answers.</summary>
    public static string? TextOf(object value)
    {
        Variant source;
        source.Vt = VtEmpty;
        source.I8 = 0;
        Variant made;
        made.Vt = VtEmpty;
        made.I8 = 0;
        Marshal.GetNativeVariantForObject(value, (IntPtr)(&source));
        try
        {
            int hr = VariantChangeType(&made, &source, 0, VtBstr);
            if (hr < 0)
            {
                return null;
            }

            return Marshal.GetObjectForNativeVariant((IntPtr)(&made)) as string;
        }
        finally
        {
            VariantClear(&made);
            VariantClear(&source);
        }
    }

    [DllImport("oleaut32.dll")]
    public static extern int VariantClear(Variant* variant);

    [DllImport("oleaut32.dll")]
    private static extern int VariantChangeType(Variant* destination, Variant* source, ushort flags, ushort vt);
}
