using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace JGraph.Devices.Hid;

/// <summary>hid.dll: the HID class driver's user-mode interface (hidsdi.h, hidpi.h).</summary>
internal static unsafe partial class HidNative
{
    public const int HIDP_STATUS_SUCCESS = 0x00110000;

    public const byte HidP_Input = 0;
    public const byte HidP_Output = 1;
    public const byte HidP_Feature = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public uint Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        public fixed ushort Reserved[17];
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    /// <summary>
    /// HIDP_BUTTON_CAPS and HIDP_VALUE_CAPS share their first 56 bytes (usage page, report ID, the
    /// alias and range flags, the link collection); the union after them is 16 bytes of a range or of
    /// single usages. Both are 72 bytes; the value caps' logical and physical bounds and units sit in
    /// what the button caps call Reserved.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_VALUE_CAPS
    {
        public ushort UsagePage;
        public byte ReportID;
        public byte IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public byte IsRange;
        public byte IsStringRange;
        public byte IsDesignatorRange;
        public byte IsAbsolute;
        public byte HasNull;
        public byte Reserved;
        public ushort BitSize;
        public ushort ReportCount;
        public ushort Reserved2_0;
        public ushort Reserved2_1;
        public ushort Reserved2_2;
        public ushort Reserved2_3;
        public ushort Reserved2_4;
        public uint UnitsExp;
        public uint Units;
        public int LogicalMin;
        public int LogicalMax;
        public int PhysicalMin;
        public int PhysicalMax;
        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;
    }

    [LibraryImport("hid.dll")]
    public static partial void HidD_GetHidGuid(Guid* guid);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetAttributes(SafeFileHandle device, HIDD_ATTRIBUTES* attributes);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetPreparsedData(SafeFileHandle device, nint* preparsed);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_FreePreparsedData(nint preparsed);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetCaps(nint preparsed, HIDP_CAPS* caps);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetValueCaps(byte reportType, HIDP_VALUE_CAPS* caps, ushort* length, nint preparsed);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetButtonCaps(byte reportType, HIDP_VALUE_CAPS* caps, ushort* length, nint preparsed);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetManufacturerString(SafeFileHandle device, char* buffer, uint length);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetProductString(SafeFileHandle device, char* buffer, uint length);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetSerialNumberString(SafeFileHandle device, char* buffer, uint length);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetFeature(SafeFileHandle device, byte* buffer, uint length);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_SetFeature(SafeFileHandle device, byte* buffer, uint length);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetInputReport(SafeFileHandle device, byte* buffer, uint length);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_SetOutputReport(SafeFileHandle device, byte* buffer, uint length);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_SetNumInputBuffers(SafeFileHandle device, uint count);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetNumInputBuffers(SafeFileHandle device, uint* count);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetUsageValue(byte reportType, ushort usagePage, ushort linkCollection, ushort usage, uint* value, nint preparsed, byte* report, uint length);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetScaledUsageValue(byte reportType, ushort usagePage, ushort linkCollection, ushort usage, int* value, nint preparsed, byte* report, uint length);

    [LibraryImport("hid.dll")]
    public static partial int HidP_SetUsageValue(byte reportType, ushort usagePage, ushort linkCollection, ushort usage, uint value, nint preparsed, byte* report, uint length);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetUsages(byte reportType, ushort usagePage, ushort linkCollection, ushort* usages, uint* length, nint preparsed, byte* report, uint reportLength);

    [LibraryImport("hid.dll")]
    public static partial uint HidP_MaxUsageListLength(byte reportType, ushort usagePage, nint preparsed);
}
