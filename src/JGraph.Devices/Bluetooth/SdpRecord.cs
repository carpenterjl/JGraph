namespace JGraph.Devices.Bluetooth;

/// <summary>The little of the SDP data element grammar a serial port channel lookup needs.</summary>
public static class SdpRecord
{
    /// <summary>The RFCOMM protocol UUID in a protocol descriptor list.</summary>
    private const uint Rfcomm = 0x0003;

    /// <summary>
    /// The RFCOMM channel in a protocol descriptor list (attribute 0x0004): the sequence of protocol
    /// sequences whose first element is the RFCOMM UUID, whose second is the channel. Null when the
    /// list names no RFCOMM channel or does not parse.
    /// </summary>
    public static int? RfcommChannel(ReadOnlySpan<byte> list)
    {
        try
        {
            int at = 0;
            if (!TrySequence(list, ref at, out int start, out int end))
            {
                return null;
            }

            int inner = start;
            while (inner < end)
            {
                if (!TrySequence(list, ref inner, out int protocolStart, out int protocolEnd))
                {
                    return null;
                }

                int element = protocolStart;
                if (TryUuid(list, ref element, out uint uuid) && uuid == Rfcomm && element < protocolEnd && TryUnsigned(list, ref element, out uint channel))
                {
                    return (int)channel;
                }

                inner = protocolEnd;
            }
        }
        catch (IndexOutOfRangeException)
        {
        }

        return null;
    }

    private static (int Type, int Length, int Header) Header(ReadOnlySpan<byte> data, int at)
    {
        int type = data[at] >> 3;
        int size = data[at] & 7;
        return size switch
        {
            0 => (type, type == 0 ? 0 : 1, 1),
            1 => (type, 2, 1),
            2 => (type, 4, 1),
            3 => (type, 8, 1),
            4 => (type, 16, 1),
            5 => (type, data[at + 1], 2),
            6 => (type, (data[at + 1] << 8) | data[at + 2], 3),
            _ => (type, (data[at + 1] << 24) | (data[at + 2] << 16) | (data[at + 3] << 8) | data[at + 4], 5),
        };
    }

    private static bool TrySequence(ReadOnlySpan<byte> data, ref int at, out int start, out int end)
    {
        (int type, int length, int header) = Header(data, at);
        start = at + header;
        end = start + length;
        at = end;
        return type is 6 or 7 && end <= data.Length;
    }

    private static bool TryUuid(ReadOnlySpan<byte> data, ref int at, out uint uuid)
    {
        (int type, int length, int header) = Header(data, at);
        int start = at + header;
        at = start + length;
        uuid = 0;
        if (type != 3)
        {
            return false;
        }

        // A 128-bit UUID on the Bluetooth base carries its short form in its first four bytes.
        int take = Math.Min(length, 4);
        for (int i = 0; i < take; i++)
        {
            uuid = (uuid << 8) | data[start + i];
        }

        return true;
    }

    private static bool TryUnsigned(ReadOnlySpan<byte> data, ref int at, out uint value)
    {
        (int type, int length, int header) = Header(data, at);
        int start = at + header;
        at = start + length;
        value = 0;
        if (type != 1 || length > 4)
        {
            return false;
        }

        for (int i = 0; i < length; i++)
        {
            value = (value << 8) | data[start + i];
        }

        return true;
    }
}
