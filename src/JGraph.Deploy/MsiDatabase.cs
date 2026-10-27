using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace JGraph.Deploy;

/// <summary>
/// A Windows Installer package opened read-only through <c>msi.dll</c> (compiler and SDK plan, stage
/// K18): its tables read with SQL, which is how a built installer is inspected without ever being
/// run, and how <see cref="RuntimeInstaller"/> knows a JGraph Runtime MSI for what it is (its
/// <c>UpgradeCode</c> and <c>ProductVersion</c> properties).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiDatabase : IDisposable
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorMoreData = 234;
    private const uint ErrorNoMoreItems = 259;

    private IntPtr _handle;

    private MsiDatabase(IntPtr handle, string path)
    {
        _handle = handle;
        Path = path;
    }

    /// <summary>The package's path.</summary>
    public string Path { get; }

    /// <summary>Opens the package at <paramref name="path"/> read-only.</summary>
    /// <exception cref="FileNotFoundException">There is no such file.</exception>
    /// <exception cref="InvalidDataException">The file is not a Windows Installer package.</exception>
    public static MsiDatabase Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The installer package was not found.", path);
        }

        uint status = MsiOpenDatabaseW(path, IntPtr.Zero, out IntPtr handle);
        if (status != ErrorSuccess || handle == IntPtr.Zero)
        {
            throw new InvalidDataException($"'{path}' is not a Windows Installer package (msi.dll error {status}).");
        }

        return new MsiDatabase(handle, path);
    }

    /// <summary>The <c>Property</c> table's value for <paramref name="name"/>, or null when it has none.</summary>
    public string? Property(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        IReadOnlyList<string[]> rows = Rows($"SELECT `Value` FROM `Property` WHERE `Property` = '{name.Replace("'", "''", StringComparison.Ordinal)}'");
        return rows.Count > 0 ? rows[0][0] : null;
    }

    /// <summary>The names of the package's tables.</summary>
    public IReadOnlyList<string> Tables => [.. Rows("SELECT `Name` FROM `_Tables`").Select(static r => r[0])];

    /// <summary>Whether the package has a table named <paramref name="table"/>.</summary>
    public bool HasTable(string table) => Tables.Contains(table, StringComparer.Ordinal);

    /// <summary>
    /// The rows <paramref name="query"/> selects, each field as text (an integer as its digits, a
    /// null as an empty string, a stream as its length in bytes).
    /// </summary>
    /// <exception cref="InvalidOperationException">The query is not one the package can run.</exception>
    public IReadOnlyList<string[]> Rows(string query)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
        uint status = MsiDatabaseOpenViewW(_handle, query, out IntPtr view);
        if (status != ErrorSuccess)
        {
            throw new InvalidOperationException($"The query '{query}' could not be opened on '{Path}' (msi.dll error {status}).");
        }

        var rows = new List<string[]>();
        try
        {
            status = MsiViewExecute(view, IntPtr.Zero);
            if (status != ErrorSuccess)
            {
                throw new InvalidOperationException($"The query '{query}' could not be run on '{Path}' (msi.dll error {status}).");
            }

            while (true)
            {
                status = MsiViewFetch(view, out IntPtr record);
                if (status == ErrorNoMoreItems)
                {
                    break;
                }

                if (status != ErrorSuccess)
                {
                    throw new InvalidOperationException($"The query '{query}' failed on '{Path}' (msi.dll error {status}).");
                }

                try
                {
                    uint fields = MsiRecordGetFieldCount(record);
                    string[] row = new string[fields];
                    for (uint i = 1; i <= fields; i++)
                    {
                        row[i - 1] = Field(record, i);
                    }

                    rows.Add(row);
                }
                finally
                {
                    _ = MsiCloseHandle(record);
                }
            }
        }
        finally
        {
            _ = MsiViewClose(view);
            _ = MsiCloseHandle(view);
        }

        return rows;
    }

    private static string Field(IntPtr record, uint index)
    {
        uint length = 0;
        var buffer = new StringBuilder(1);
        uint status = MsiRecordGetStringW(record, index, buffer, ref length);
        if (status == ErrorMoreData)
        {
            length++;
            buffer = new StringBuilder((int)length);
            status = MsiRecordGetStringW(record, index, buffer, ref length);
        }

        return status == ErrorSuccess ? buffer.ToString() : "";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            _ = MsiCloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiOpenDatabaseW(string path, IntPtr persist, out IntPtr handle);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiDatabaseOpenViewW(IntPtr database, string query, out IntPtr view);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewExecute(IntPtr view, IntPtr record);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewFetch(IntPtr view, out IntPtr record);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewClose(IntPtr view);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiRecordGetFieldCount(IntPtr record);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiRecordGetStringW(IntPtr record, uint field, StringBuilder value, ref uint length);

    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiCloseHandle(IntPtr handle);
}
