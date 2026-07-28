namespace JGraph.Scripting.MatFile;

/// <summary>The MAT-file v5 wire constants shared by the writer and reader.</summary>
internal static class MatConstants
{
    // Data element types.
    public const int MiInt8 = 1;
    public const int MiUInt8 = 2;
    public const int MiInt16 = 3;
    public const int MiUInt16 = 4;
    public const int MiInt32 = 5;
    public const int MiUInt32 = 6;
    public const int MiSingle = 7;
    public const int MiDouble = 9;
    public const int MiInt64 = 12;
    public const int MiUInt64 = 13;
    public const int MiMatrix = 14;
    public const int MiCompressed = 15;
    public const int MiUtf8 = 16;

    // Array classes (the first byte of the array-flags word).
    public const int MxCell = 1;
    public const int MxStruct = 2;
    public const int MxChar = 4;
    public const int MxDouble = 6;
    public const int MxSingle = 7;
    public const int MxInt8 = 8;
    public const int MxUInt8 = 9;
    public const int MxInt16 = 10;
    public const int MxUInt16 = 11;
    public const int MxInt32 = 12;
    public const int MxUInt32 = 13;
    public const int MxInt64 = 14;
    public const int MxUInt64 = 15;

    // Array-flag bits.
    public const int FlagComplex = 0x0800;
    public const int FlagLogical = 0x0200;

    /// <summary>Struct field names are stored in fixed 32-byte slots (31 chars + terminator).</summary>
    public const int FieldNameLength = 32;
}
