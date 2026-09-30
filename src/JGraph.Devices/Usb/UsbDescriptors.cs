using System.Buffers.Binary;
using System.Text;

namespace JGraph.Devices.Usb;

/// <summary>An endpoint of an interface.</summary>
public sealed record UsbEndpoint(int Address, string Direction, string TransferType, int MaxPacketSize, int Interval);

/// <summary>
/// A class-specific descriptor, with its subtype named from its class's specification and the fields
/// JGraph decodes (HID, DFU and CDC in full; audio, MIDI and video frames in part). A field's value is
/// a number or text.
/// </summary>
public sealed record UsbClassDescriptor(int Type, int Subtype, string Name, byte[] Bytes, IReadOnlyList<KeyValuePair<string, object>> Fields);

/// <summary>An interface (one alternate setting of it), its endpoints and its class-specific descriptors.</summary>
public sealed record UsbInterface(
    int Number,
    int AlternateSetting,
    int Class,
    int Subclass,
    int Protocol,
    string ClassName,
    int NameIndex,
    IReadOnlyList<UsbEndpoint> Endpoints,
    IReadOnlyList<UsbClassDescriptor> ClassSpecific);

/// <summary>An interface association: interfaces that make one function of a composite device.</summary>
public sealed record UsbAssociation(int FirstInterface, int InterfaceCount, int Class, int Subclass, int Protocol, string ClassName, int NameIndex);

/// <summary>A configuration descriptor, decoded.</summary>
public sealed record UsbConfiguration(
    int Value,
    int NameIndex,
    bool SelfPowered,
    bool RemoteWakeup,
    int MaxPowerMilliamps,
    IReadOnlyList<UsbInterface> Interfaces,
    IReadOnlyList<UsbAssociation> Associations);

/// <summary>One capability of a BOS descriptor.</summary>
public sealed record UsbCapability(int Type, string Name, byte[] Bytes, string? PlatformId);

/// <summary>
/// Decodes USB descriptors (USB 2.0 chapter 9, USB 3.2 chapter 9, and the class specifications named
/// below): a device's configuration with its interface associations, interfaces, endpoints and
/// class-specific descriptors, and a BOS descriptor's capabilities with MS OS 2.0 and WebUSB recognised
/// by their platform UUIDs.
/// </summary>
public static class UsbDescriptors
{
    /// <summary>The MS OS 2.0 descriptor set's platform capability UUID.</summary>
    public const string MsOs20Uuid = "d8dd60df-4589-4cc7-9cd2-659d9e648a9f";

    /// <summary>WebUSB's platform capability UUID.</summary>
    public const string WebUsbUuid = "3408b638-09a9-47a0-8bfd-a0768815b665";

    /// <summary>The name of a class code (usb.org "Defined Class Codes"), with the subclass where it decides.</summary>
    public static string ClassName(int cls, int subclass = -1, int protocol = -1) => cls switch
    {
        0x00 => "Per interface",
        0x01 => subclass == 0x03 ? "MIDI" : "Audio",
        0x02 => subclass switch
        {
            0x02 => "CDC ACM",
            0x06 => "CDC ECM",
            0x0D => "CDC NCM",
            0x0C => "CDC EEM",
            0x0E => "CDC MBIM",
            _ => "CDC",
        },
        0x03 => "HID",
        0x05 => "Physical",
        0x06 => "Still Image",
        0x07 => "Printer",
        0x08 => "Mass Storage",
        0x09 => "Hub",
        0x0A => "CDC Data",
        0x0B => "Smart Card",
        0x0D => "Content Security",
        0x0E => "Video",
        0x0F => "Personal Healthcare",
        0x10 => "Audio/Video",
        0x11 => "Billboard",
        0x12 => "USB Type-C Bridge",
        0x13 => "Bulk Display",
        0x14 => "MCTP",
        0x3C => "I3C",
        0xDC => "Diagnostic",
        0xE0 => subclass == 0x01 && protocol == 0x03 ? "RNDIS" : subclass == 0x01 && protocol == 0x01 ? "Bluetooth" : "Wireless Controller",
        0xEF => subclass == 0x04 && protocol == 0x01 ? "RNDIS" : "Miscellaneous",
        0xFE => subclass switch
        {
            0x01 => "DFU",
            0x02 => "IrDA Bridge",
            0x03 => "USBTMC",
            _ => "Application Specific",
        },
        0xFF => "Vendor Specific",
        _ => $"Class 0x{cls:X2}",
    };

    /// <summary>
    /// The string indices a device's descriptors name, in order: its manufacturer, product and serial
    /// number, its configuration's name, each function's and each interface's.
    /// </summary>
    public static IReadOnlyList<int> StringIndices(ReadOnlySpan<byte> device, ReadOnlySpan<byte> configuration)
    {
        var indices = new List<int>();
        void Add(int index)
        {
            if (index != 0 && !indices.Contains(index))
            {
                indices.Add(index);
            }
        }

        if (device.Length >= 17)
        {
            Add(device[14]);
            Add(device[15]);
            Add(device[16]);
        }

        UsbConfiguration decoded = Configuration(configuration);
        Add(decoded.NameIndex);
        foreach (UsbAssociation association in decoded.Associations)
        {
            Add(association.NameIndex);
        }

        foreach (UsbInterface f in decoded.Interfaces)
        {
            Add(f.NameIndex);
        }

        return indices;
    }

    /// <summary>Decodes a configuration descriptor and everything after it.</summary>
    public static UsbConfiguration Configuration(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 9 || bytes[1] != 2)
        {
            return new UsbConfiguration(0, 0, false, false, 0, [], []);
        }

        int value = bytes[5];
        int name = bytes[6];
        int attributes = bytes[7];
        int power = bytes[8] * 2;
        var interfaces = new List<UsbInterface>();
        var associations = new List<UsbAssociation>();
        int at = bytes[0];
        (int Number, int Alt, int Class, int Sub, int Proto, int Name)? current = null;
        var endpoints = new List<UsbEndpoint>();
        var classSpecific = new List<UsbClassDescriptor>();
        var audio = new AudioContext();

        void Close()
        {
            if (current is { } c)
            {
                interfaces.Add(new UsbInterface(c.Number, c.Alt, c.Class, c.Sub, c.Proto, ClassName(c.Class, c.Sub, c.Proto), c.Name,
                    endpoints.ToArray(), classSpecific.ToArray()));
            }

            endpoints.Clear();
            classSpecific.Clear();
        }

        while (at + 2 <= bytes.Length)
        {
            int length = bytes[at];
            if (length < 2 || at + length > bytes.Length)
            {
                break;
            }

            ReadOnlySpan<byte> d = bytes.Slice(at, length);
            switch (d[1])
            {
                case 4 when length >= 9:
                    Close();
                    current = (d[2], d[3], d[5], d[6], d[7], d[8]);
                    break;
                case 5 when length >= 7:
                {
                    int address = d[2];
                    string type = (d[3] & 3) switch { 0 => "control", 1 => "isochronous", 2 => "bulk", _ => "interrupt" };
                    endpoints.Add(new UsbEndpoint(address, (address & 0x80) != 0 ? "in" : "out", type,
                        BinaryPrimitives.ReadUInt16LittleEndian(d[4..]) & 0x7FF, d[6]));
                    break;
                }

                case 11 when length >= 8:
                    associations.Add(new UsbAssociation(d[2], d[3], d[4], d[5], d[6], ClassName(d[4], d[5], d[6]), d[7]));
                    break;
                default:
                    classSpecific.Add(ClassSpecific(d, current?.Class ?? -1, current?.Sub ?? -1, audio));
                    break;
            }

            at += length;
        }

        Close();
        return new UsbConfiguration(value, name, (attributes & 0x40) != 0, (attributes & 0x20) != 0, power, interfaces, associations);
    }

    /// <summary>What an audio function's header said: which version of the class its units follow.</summary>
    private sealed class AudioContext
    {
        public bool Version2 { get; set; }
    }

    /// <summary>A class-specific descriptor in the interface of class <paramref name="cls"/>, named and decoded.</summary>
    private static UsbClassDescriptor ClassSpecific(ReadOnlySpan<byte> span, int cls, int subclass, AudioContext audio)
    {
        byte[] d = span.ToArray();
        int type = d[1];
        int subtype = d.Length >= 3 ? d[2] : -1;
        var fields = new List<KeyValuePair<string, object>>();
        void Field(string name, object value) => fields.Add(new(name, value));
        int U16(int at) => at + 2 <= d.Length ? BinaryPrimitives.ReadUInt16LittleEndian(d[at..]) : 0;
        uint U32(int at) => at + 4 <= d.Length ? BinaryPrimitives.ReadUInt32LittleEndian(d[at..]) : 0;
        int U8(int at) => at < d.Length ? d[at] : 0;

        string name;
        switch (cls, type)
        {
            // HID 1.11, 6.2.1.
            case (0x03, 0x21):
                name = "HID";
                Field("HIDVersion", Bcd(U16(2)));
                Field("CountryCode", (double)U8(4));
                Field("Descriptors", (double)U8(5));
                if (U8(6) == 0x22)
                {
                    Field("ReportDescriptorLength", (double)U16(7));
                }

                subtype = -1;
                break;

            // DFU 1.1, 4.1.3, and ST's DfuSe (bcdDFUVersion 1.1a).
            case (0xFE, 0x21) when subclass == 0x01:
                name = "DFU Functional";
                Field("CanDownload", (U8(2) & 1) != 0 ? 1.0 : 0.0);
                Field("CanUpload", (U8(2) & 2) != 0 ? 1.0 : 0.0);
                Field("ManifestationTolerant", (U8(2) & 4) != 0 ? 1.0 : 0.0);
                Field("WillDetach", (U8(2) & 8) != 0 ? 1.0 : 0.0);
                Field("DetachTimeout", (double)U16(3));
                Field("TransferSize", (double)U16(5));
                if (d.Length >= 9)
                {
                    Field("DFUVersion", Bcd(U16(7)));
                }

                subtype = -1;
                break;

            // CDC 1.2, 5.2.3.
            case (0x02, 0x24):
                name = CdcSubtype(subtype);
                switch (subtype)
                {
                    case 0x00:
                        Field("CDCVersion", Bcd(U16(3)));
                        break;
                    case 0x01:
                        Field("Capabilities", (double)U8(3));
                        Field("DataInterface", (double)U8(4));
                        break;
                    case 0x02:
                        Field("Capabilities", (double)U8(3));
                        break;
                    case 0x06:
                        Field("ControlInterface", (double)U8(3));
                        Field("SubordinateInterfaces", string.Join(' ', d[4..].ToArray().Select(static b => b.ToString(System.Globalization.CultureInfo.InvariantCulture))));
                        break;
                    case 0x0F:
                        Field("MACAddressIndex", (double)U8(3));
                        Field("MaxSegmentSize", (double)U16(8));
                        break;
                    case 0x1A:
                        Field("NCMVersion", Bcd(U16(3)));
                        Field("Capabilities", (double)U8(5));
                        break;
                }

                break;

            // Audio 1.0 and 2.0: the units and terminals of a control interface, a streaming interface's
            // general and format descriptors, MIDI's jacks.
            case (0x01, 0x24):
                name = subclass switch
                {
                    0x01 => AudioControlSubtype(subtype, audio),
                    0x02 => subtype switch { 1 => "AS General", 2 => "Format Type", 3 => "Format Specific", _ => $"AS Subtype 0x{subtype:X2}" },
                    0x03 => subtype switch { 1 => "MS Header", 2 => "MIDI IN Jack", 3 => "MIDI OUT Jack", 4 => "Element", _ => $"MS Subtype 0x{subtype:X2}" },
                    _ => $"Audio Subtype 0x{subtype:X2}",
                };
                if (subclass == 0x01 && subtype == 1)
                {
                    audio.Version2 = U16(3) >= 0x0200;
                    Field("ADCVersion", Bcd(U16(3)));
                }
                else if (subclass == 0x01 && subtype is 2 or 3)
                {
                    Field("TerminalID", (double)U8(3));
                    Field("TerminalType", $"0x{U16(4):X4}");
                }
                else if (subclass == 0x03 && subtype is 2 or 3)
                {
                    Field("JackType", U8(3) == 1 ? "embedded" : "external");
                    Field("JackID", (double)U8(4));
                }

                break;
            case (0x01, 0x25):
                name = subclass == 0x03 ? "MS Endpoint" : "AS Endpoint";
                break;

            // Video 1.5: a control interface's units and terminals, a streaming interface's formats and
            // frames (their size and default rate).
            case (0x0E, 0x24):
                name = subclass == 0x01 ? VideoControlSubtype(subtype) : VideoStreamingSubtype(subtype);
                if (subclass == 0x01 && subtype == 1)
                {
                    Field("UVCVersion", Bcd(U16(3)));
                }
                else if (subclass == 0x02 && subtype is 0x04 or 0x10 && d.Length >= 21)
                {
                    Field("FormatIndex", (double)U8(3));
                    Field("FourCC", Encoding.ASCII.GetString(d, 5, 4).TrimEnd('\0'));
                }
                else if (subclass == 0x02 && subtype == 0x06)
                {
                    Field("FormatIndex", (double)U8(3));
                }
                else if (subclass == 0x02 && subtype is 0x05 or 0x07 or 0x11 && d.Length >= 26)
                {
                    Field("FrameIndex", (double)U8(3));
                    Field("Width", (double)U16(5));
                    Field("Height", (double)U16(7));
                    uint interval = U32(21);
                    Field("FrameRate", interval == 0 ? 0.0 : Math.Round(1e7 / interval, 3));
                }

                break;
            case (0x0E, 0x25):
                name = "VC Interrupt Endpoint";
                break;

            case (_, 0x21):
                name = "Class Descriptor 0x21";
                break;
            default:
                name = type switch
                {
                    0x24 => "Class-Specific Interface",
                    0x25 => "Class-Specific Endpoint",
                    _ => $"Descriptor 0x{type:X2}",
                };
                break;
        }

        return new UsbClassDescriptor(type, subtype, name, d, fields);
    }

    /// <summary>A binary-coded decimal version: 0x0111 is "1.11", DfuSe's 0x011A is "1.1a".</summary>
    private static string Bcd(int value)
    {
        int major = value >> 8;
        int minor = value & 0xFF;
        return (minor & 0x0F) > 9
            ? $"{major:X}.{minor >> 4:X}{(char)('a' + (minor & 0x0F) - 10)}"
            : $"{major:X}.{minor:X2}";
    }

    private static string CdcSubtype(int subtype) => subtype switch
    {
        0x00 => "Header",
        0x01 => "Call Management",
        0x02 => "Abstract Control Management",
        0x03 => "Direct Line Management",
        0x04 => "Telephone Ringer",
        0x05 => "Telephone Call and Line State Reporting",
        0x06 => "Union",
        0x07 => "Country Selection",
        0x08 => "Telephone Operational Modes",
        0x09 => "USB Terminal",
        0x0A => "Network Channel Terminal",
        0x0B => "Protocol Unit",
        0x0C => "Extension Unit",
        0x0D => "Multi-Channel Management",
        0x0E => "CAPI Control Management",
        0x0F => "Ethernet Networking",
        0x10 => "ATM Networking",
        0x11 => "Wireless Handset Control Model",
        0x12 => "Mobile Direct Line Model",
        0x13 => "MDLM Detail",
        0x14 => "Device Management Model",
        0x15 => "OBEX",
        0x16 => "Command Set",
        0x17 => "Command Set Detail",
        0x18 => "Telephone Control Model",
        0x19 => "OBEX Service Identifier",
        0x1A => "NCM",
        0x1B => "MBIM",
        0x1C => "MBIM Extended",
        _ => $"CDC Subtype 0x{subtype:X2}",
    };

    private static string AudioControlSubtype(int subtype, AudioContext audio) => subtype switch
    {
        0x01 => "AC Header",
        0x02 => "Input Terminal",
        0x03 => "Output Terminal",
        0x04 => "Mixer Unit",
        0x05 => "Selector Unit",
        0x06 => "Feature Unit",
        0x07 => audio.Version2 ? "Effect Unit" : "Processing Unit",
        0x08 => audio.Version2 ? "Processing Unit" : "Extension Unit",
        0x09 => "Extension Unit",
        0x0A => "Clock Source",
        0x0B => "Clock Selector",
        0x0C => "Clock Multiplier",
        0x0D => "Sample Rate Converter",
        _ => $"AC Subtype 0x{subtype:X2}",
    };

    private static string VideoControlSubtype(int subtype) => subtype switch
    {
        0x01 => "VC Header",
        0x02 => "Input Terminal",
        0x03 => "Output Terminal",
        0x04 => "Selector Unit",
        0x05 => "Processing Unit",
        0x06 => "Extension Unit",
        0x07 => "Encoding Unit",
        _ => $"VC Subtype 0x{subtype:X2}",
    };

    private static string VideoStreamingSubtype(int subtype) => subtype switch
    {
        0x01 => "VS Input Header",
        0x02 => "VS Output Header",
        0x03 => "Still Image Frame",
        0x04 => "Format Uncompressed",
        0x05 => "Frame Uncompressed",
        0x06 => "Format MJPEG",
        0x07 => "Frame MJPEG",
        0x0A => "Format MPEG2-TS",
        0x0C => "Format DV",
        0x0D => "Color Matching",
        0x10 => "Format Frame Based",
        0x11 => "Frame Frame Based",
        0x12 => "Format Stream Based",
        0x13 => "Format H.264",
        0x14 => "Frame H.264",
        _ => $"VS Subtype 0x{subtype:X2}",
    };

    /// <summary>Decodes a BOS descriptor's device capabilities.</summary>
    public static IReadOnlyList<UsbCapability> Capabilities(ReadOnlySpan<byte> bytes)
    {
        var result = new List<UsbCapability>();
        if (bytes.Length < 5 || bytes[1] != 15)
        {
            return result;
        }

        int at = bytes[0];
        while (at + 3 <= bytes.Length)
        {
            int length = bytes[at];
            if (length < 3 || at + length > bytes.Length)
            {
                break;
            }

            ReadOnlySpan<byte> d = bytes.Slice(at, length);
            int type = d[2];
            string? platform = type == 5 && length >= 20 ? new Guid(d.Slice(4, 16)).ToString() : null;
            string name = type switch
            {
                1 => "Wireless USB",
                2 => "USB 2.0 Extension",
                3 => "SuperSpeed USB",
                4 => "Container ID",
                5 => platform == MsOs20Uuid ? "MS OS 2.0" : platform == WebUsbUuid ? "WebUSB" : "Platform",
                6 => "Power Delivery",
                7 => "Battery Info",
                8 => "PD Consumer Port",
                9 => "PD Provider Port",
                10 => "SuperSpeed Plus",
                11 => "Precision Time Measurement",
                12 => "Wireless USB Ext",
                13 => "Billboard",
                14 => "Authentication",
                15 => "Billboard Ex",
                16 => "Configuration Summary",
                _ => $"Capability 0x{type:X2}",
            };
            result.Add(new UsbCapability(type, name, d.ToArray(), platform));
            at += length;
        }

        return result;
    }
}
