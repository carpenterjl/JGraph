using System.Globalization;

namespace JGraph.Devices.Bluetooth;

/// <summary>A named GATT attribute: its name and its UUID in R2025b's shortest form.</summary>
public sealed record GattName(string Name, string Uuid);

/// <summary>A named service and the characteristics R2025b names under it.</summary>
public sealed record GattService(string Name, string Uuid, GattName[] Characteristics);

/// <summary>
/// The GATT names and UUID forms of R2025b's <c>ServicesCharacteristicsDescriptorsInfo</c>
/// (measured, probe_ble_internals and probe_ble_internals2): a UUID is written as four hex digits, as
/// eight that start 0000, or as 128 bits; the Bluetooth base UUID's members are shown by their four
/// digits and every other by all 128 bits; an attribute no table names is <c>Custom</c>, and a
/// characteristic is named only under the service that table puts it in.
/// </summary>
public static partial class GattNames
{
    /// <summary>The Bluetooth base UUID's tail.</summary>
    private const string BaseTail = "-0000-1000-8000-00805F9B34FB";

    /// <summary>What an attribute no table names is called.</summary>
    public const string Custom = "Custom";

    /// <summary>Every service R2025b names, the standard ones first.</summary>
    public static IEnumerable<GattService> Services => StandardServices.Concat(CustomServices);

    /// <summary>The descriptors R2025b names.</summary>
    public static IReadOnlyList<GattName> Descriptors => StandardDescriptors;

    /// <summary>
    /// A UUID written in one of R2025b's three forms, as its canonical 128-bit text (upper case); null
    /// when the text is none of them.
    /// </summary>
    public static string? TryCanonical(string text)
    {
        string t = text.Trim();
        if (t.Length == 4 && IsHex(t))
        {
            return $"0000{t.ToUpperInvariant()}{BaseTail}";
        }

        if (t.Length == 8 && IsHex(t) && t.StartsWith("0000", StringComparison.Ordinal))
        {
            return $"{t.ToUpperInvariant()}{BaseTail}";
        }

        return Guid.TryParseExact(t, "D", out Guid guid) ? guid.ToString("D").ToUpperInvariant() : null;
    }

    /// <summary>The canonical form of a UUID known to be well formed.</summary>
    public static string Canonical(string uuid) => TryCanonical(uuid) ?? uuid.ToUpperInvariant();

    /// <summary>A 16-bit value as its canonical UUID.</summary>
    public static string Canonical(int shortUuid) => $"0000{shortUuid.ToString("X4", CultureInfo.InvariantCulture)}{BaseTail}";

    /// <summary>The form R2025b shows: the four digits of a base UUID, all 128 bits of any other.</summary>
    public static string Shortest(string canonical) =>
        canonical.Length == 36 && canonical.StartsWith("0000", StringComparison.Ordinal) && canonical.EndsWith(BaseTail, StringComparison.OrdinalIgnoreCase)
            ? canonical.Substring(4, 4).ToUpperInvariant()
            : canonical.ToUpperInvariant();

    /// <summary>A service's name, or <see cref="Custom"/>.</summary>
    public static string ServiceName(string uuid)
    {
        string shortest = Shortest(Canonical(uuid));
        return Services.FirstOrDefault(s => s.Uuid.Equals(shortest, StringComparison.OrdinalIgnoreCase))?.Name ?? Custom;
    }

    /// <summary>A characteristic's name under its service, or <see cref="Custom"/>.</summary>
    public static string CharacteristicName(string serviceUuid, string uuid)
    {
        string service = Shortest(Canonical(serviceUuid));
        string shortest = Shortest(Canonical(uuid));
        GattService? known = Services.FirstOrDefault(s => s.Uuid.Equals(service, StringComparison.OrdinalIgnoreCase));
        return known?.Characteristics.FirstOrDefault(c => c.Uuid.Equals(shortest, StringComparison.OrdinalIgnoreCase))?.Name ?? Custom;
    }

    /// <summary>A descriptor's name, or <see cref="Custom"/>.</summary>
    public static string DescriptorName(string uuid)
    {
        string shortest = Shortest(Canonical(uuid));
        return StandardDescriptors.FirstOrDefault(d => d.Uuid.Equals(shortest, StringComparison.OrdinalIgnoreCase))?.Name ?? Custom;
    }

    private static bool IsHex(string text) => text.All(Uri.IsHexDigit);
}
