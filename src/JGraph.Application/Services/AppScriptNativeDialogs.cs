using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using JGraph.Scripting;
using Microsoft.Win32;

namespace JGraph.Application.Services;

/// <summary>
/// The system's own dialogs for a script's <c>uigetfile</c>, <c>uiputfile</c>, <c>uigetdir</c>,
/// <c>uisetcolor</c> and <c>uisetfont</c> (app-building plan, U4). A script calls from its own
/// thread and waits; each dialog is shown on the thread the windows live on, over whichever window
/// is active, and is modal to it the way these dialogs are everywhere else on Windows.
/// </summary>
public sealed class AppScriptNativeDialogs : IScriptNativeDialogs
{
    private readonly Dispatcher _ui;

    /// <summary>Creates the dialogs over the dispatcher of the thread that owns the windows.</summary>
    public AppScriptNativeDialogs(Dispatcher ui) => _ui = ui ?? throw new ArgumentNullException(nameof(ui));

    private static Window? ActiveWindow() =>
        System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(static w => w.IsActive)
        ?? System.Windows.Application.Current?.MainWindow;

    private static string FilterText(IReadOnlyList<ScriptFileFilter> filters) =>
        string.Join("|", filters.Select(static f =>
            $"{(f.Description.Length > 0 ? f.Description : f.Pattern).Replace('|', ' ')}|{f.Pattern}"));

    private static void StartAt(FileDialog dialog, string? initialPath)
    {
        if (string.IsNullOrEmpty(initialPath))
        {
            return;
        }

        if (System.IO.Directory.Exists(initialPath))
        {
            dialog.InitialDirectory = initialPath;
            return;
        }

        string? folder = System.IO.Path.GetDirectoryName(initialPath);
        if (!string.IsNullOrEmpty(folder) && System.IO.Directory.Exists(folder))
        {
            dialog.InitialDirectory = folder;
        }

        dialog.FileName = System.IO.Path.GetFileName(initialPath);
    }

    /// <inheritdoc />
    public (IReadOnlyList<string> Paths, int FilterIndex)? OpenFiles(
        string title, IReadOnlyList<ScriptFileFilter> filters, string? initialPath, bool multiSelect) =>
        _ui.Invoke<(IReadOnlyList<string>, int)?>(() =>
        {
            var dialog = new OpenFileDialog { Title = title, Filter = FilterText(filters), Multiselect = multiSelect, CheckFileExists = true };
            StartAt(dialog, initialPath);
            return dialog.ShowDialog(ActiveWindow()) == true ? (dialog.FileNames, dialog.FilterIndex) : null;
        });

    /// <inheritdoc />
    public (string Path, int FilterIndex)? SaveFile(string title, IReadOnlyList<ScriptFileFilter> filters, string? initialPath) =>
        _ui.Invoke<(string, int)?>(() =>
        {
            var dialog = new SaveFileDialog { Title = title, Filter = FilterText(filters), OverwritePrompt = true, AddExtension = true };
            StartAt(dialog, initialPath);
            return dialog.ShowDialog(ActiveWindow()) == true ? (dialog.FileName, dialog.FilterIndex) : null;
        });

    /// <inheritdoc />
    public string? PickFolder(string title, string? initialPath) =>
        _ui.Invoke(() =>
        {
            var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
            if (!string.IsNullOrEmpty(initialPath) && System.IO.Directory.Exists(initialPath))
            {
                dialog.InitialDirectory = initialPath;
            }

            return dialog.ShowDialog(ActiveWindow()) == true ? dialog.FolderName : null;
        });

    /// <inheritdoc />
    public (double R, double G, double B)? PickColor(string title, (double R, double G, double B)? initial) =>
        _ui.Invoke<(double, double, double)?>(() =>
        {
            // The common colour dialog keeps sixteen custom colours for as long as it is given
            // somewhere to keep them; this process keeps them between calls.
            IntPtr custom = Marshal.AllocHGlobal(16 * sizeof(int));
            try
            {
                Marshal.Copy(CustomColors, 0, custom, 16);
                var choice = new ChooseColorData
                {
                    Size = Marshal.SizeOf<ChooseColorData>(),
                    Owner = OwnerHandle(),
                    CustomColors = custom,
                    Flags = CcFullOpen | CcAnyColor | (initial is null ? 0 : CcRgbInit),
                    Result = initial is { } c ? ToColorRef(c.R, c.G, c.B) : 0,
                };
                if (!ChooseColor(ref choice))
                {
                    return null;
                }

                Marshal.Copy(custom, CustomColors, 0, 16);
                return ((choice.Result & 0xFF) / 255.0, ((choice.Result >> 8) & 0xFF) / 255.0, ((choice.Result >> 16) & 0xFF) / 255.0);
            }
            finally
            {
                Marshal.FreeHGlobal(custom);
            }
        });

    /// <inheritdoc />
    public ScriptFontChoice? PickFont(string title, ScriptFontChoice? initial) =>
        _ui.Invoke(() =>
        {
            var font = new LogFont { FaceName = initial?.Name ?? "Segoe UI", CharSet = 1 };
            if (initial is not null)
            {
                font.Height = -(int)Math.Round(initial.SizePoints * 96 / 72);
                font.Weight = initial.Bold ? 700 : 400;
                font.Italic = (byte)(initial.Italic ? 1 : 0);
            }

            IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<LogFont>());
            try
            {
                Marshal.StructureToPtr(font, memory, fDeleteOld: false);
                var choice = new ChooseFontData
                {
                    Size = Marshal.SizeOf<ChooseFontData>(),
                    Owner = OwnerHandle(),
                    LogFont = memory,
                    Flags = CfScreenFonts | CfForceFontExist | CfNoScriptSel | (initial is null ? 0 : CfInitToLogFontStruct),
                };
                if (!ChooseFont(ref choice))
                {
                    return null;
                }

                LogFont picked = Marshal.PtrToStructure<LogFont>(memory);

                // PointSize is in tenths of a point, which is the one size the dialog states outright.
                return new ScriptFontChoice(picked.FaceName, choice.PointSize / 10.0, picked.Weight >= 600, picked.Italic != 0);
            }
            finally
            {
                Marshal.FreeHGlobal(memory);
            }
        });

    private static readonly int[] CustomColors = Enumerable.Repeat(0x00FFFFFF, 16).ToArray();

    private static IntPtr OwnerHandle() =>
        ActiveWindow() is { } window ? new WindowInteropHelper(window).Handle : IntPtr.Zero;

    private static int ToColorRef(double r, double g, double b)
    {
        static int Byte(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return Byte(r) | (Byte(g) << 8) | (Byte(b) << 16);
    }

    private const int CcRgbInit = 0x1;
    private const int CcFullOpen = 0x2;
    private const int CcAnyColor = 0x100;
    private const int CfScreenFonts = 0x1;
    private const int CfInitToLogFontStruct = 0x40;
    private const int CfForceFontExist = 0x10000;
    private const int CfNoScriptSel = 0x800000;

    [StructLayout(LayoutKind.Sequential)]
    private struct ChooseColorData
    {
        public int Size;
        public IntPtr Owner;
        public IntPtr Instance;
        public int Result;
        public IntPtr CustomColors;
        public int Flags;
        public IntPtr CustomData;
        public IntPtr Hook;
        public IntPtr TemplateName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct LogFont
    {
        public int Height;
        public int Width;
        public int Escapement;
        public int Orientation;
        public int Weight;
        public byte Italic;
        public byte Underline;
        public byte StrikeOut;
        public byte CharSet;
        public byte OutPrecision;
        public byte ClipPrecision;
        public byte Quality;
        public byte PitchAndFamily;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct ChooseFontData
    {
        public int Size;
        public IntPtr Owner;
        public IntPtr Dc;
        public IntPtr LogFont;
        public int PointSize;
        public int Flags;
        public int Colors;
        public IntPtr CustomData;
        public IntPtr Hook;
        public IntPtr TemplateName;
        public IntPtr Instance;
        public IntPtr Style;
        public short FontType;
        public short Alignment;
        public int SizeMin;
        public int SizeMax;
    }

    [DllImport("comdlg32.dll", EntryPoint = "ChooseColorW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChooseColor(ref ChooseColorData choice);

    [DllImport("comdlg32.dll", EntryPoint = "ChooseFontW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChooseFont(ref ChooseFontData choice);
}
