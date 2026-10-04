using System.Runtime.InteropServices;

namespace JGraph.Core.Model;

/// <summary>
/// The fonts a component names, as this machine has them, and how wide their text is (app-building
/// plan, section A, stage U3). One table and one measurement for everything that needs them: a
/// <c>uicontrol</c>'s <c>Extent</c>, <c>textwrap</c>, <c>listfonts</c> and the window's controls.
/// <para>
/// Text is measured through GDI at a large em and scaled down, which gives the font's design
/// advances — what the window's controls lay text out by — with no window and no drawing surface, so
/// a headless script measures what a shown one would.
/// </para>
/// </summary>
public static class UiFonts
{
    /// <summary>The em, in pixels, text is measured at before it is scaled to the size asked for.</summary>
    private const int MeasureEm = 2048;

    private static readonly object Gate = new();
    private static readonly Dictionary<(string Family, bool Bold, bool Italic), IntPtr> Fonts = new();
    private static IntPtr _dc;
    private static IReadOnlyList<string>? _listed;
    private static HashSet<string>? _scalable;

    /// <summary>
    /// The family a component's <c>FontName</c> is drawn in. MATLAB's classic default MS Sans Serif is
    /// a bitmap font, which Windows' own substitute for is Microsoft Sans Serif; Helvetica, the
    /// uifigure default, is Arial here; and a name the machine has no outline font for is Arial too,
    /// which is what R2025b measures such a name as (probe <c>u3_metrics</c>).
    /// </summary>
    public static string Family(string? name)
    {
        string wanted = (name ?? string.Empty).Trim();
        string mapped = wanted.ToLowerInvariant() switch
        {
            "" or "ms sans serif" => "Microsoft Sans Serif",
            "helvetica" or "sansserif" or "dialog" => "Arial",
            "fixedwidth" or "courier" or "monospaced" => "Courier New",
            "times" or "serif" or "ms serif" => "Times New Roman",
            "ms shell dlg" or "ms shell dlg 2" => "Tahoma",
            _ => wanted,
        };

        HashSet<string> scalable = Scalable();
        return scalable.Count == 0 || scalable.Contains(mapped) ? mapped : "Arial";
    }

    /// <summary>
    /// Every font family the machine lists, sorted without regard to case — what <c>listfonts</c>
    /// answers. Empty where the machine cannot be asked.
    /// </summary>
    public static IReadOnlyList<string> Families()
    {
        lock (Gate)
        {
            EnsureListed();
            return _listed!;
        }
    }

    /// <summary>
    /// The width of one line of text and the height of a line, in pixels of 1/96 inch, in the family
    /// <see cref="Family"/> answers for <paramref name="fontName"/> at <paramref name="sizePixels"/>.
    /// </summary>
    public static (double Width, double LineHeight) Measure(
        string text, string? fontName, double sizePixels, bool bold = false, bool italic = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!(sizePixels > 0) || !double.IsFinite(sizePixels))
        {
            return (0, 0);
        }

        string family = Family(fontName);
        if (OperatingSystem.IsWindows())
        {
            lock (Gate)
            {
                try
                {
                    if (MeasureWithGdi(text, family, bold, italic) is { } measured)
                    {
                        double scale = sizePixels / MeasureEm;
                        return (measured.Width * scale, measured.Line * scale);
                    }
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
                {
                    // No GDI here: the estimate below.
                }
            }
        }

        // Where the machine cannot be asked: about half an em a character, and a line of 1.15 em.
        return (text.Length * sizePixels * 0.55, sizePixels * 1.15);
    }

    private static HashSet<string> Scalable()
    {
        lock (Gate)
        {
            EnsureListed();
            return _scalable!;
        }
    }

    private static void EnsureListed()
    {
        if (_listed is not null)
        {
            return;
        }

        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scalable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                IntPtr dc = Dc();
                if (dc != IntPtr.Zero)
                {
                    var request = new LOGFONTW { lfCharSet = 1 /* DEFAULT_CHARSET */ };
                    EnumFontFamExProc collect = (ref ENUMLOGFONTEXW font, IntPtr _, uint type, IntPtr _) =>
                    {
                        string face = font.elfLogFont.lfFaceName;

                        // A name starting '@' is the vertical-writing twin of a family already listed.
                        if (face.Length > 0 && face[0] != '@')
                        {
                            all.Add(face);
                            if ((type & 0x1) == 0) // not RASTER_FONTTYPE
                            {
                                scalable.Add(face);
                            }
                        }

                        return 1;
                    };
                    EnumFontFamiliesExW(dc, ref request, collect, IntPtr.Zero, 0);
                    GC.KeepAlive(collect);
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // Listed as none.
            }
        }

        var sorted = all.ToList();
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        _listed = sorted;
        _scalable = scalable;
    }

    private static IntPtr Dc()
    {
        if (_dc == IntPtr.Zero)
        {
            _dc = CreateCompatibleDC(IntPtr.Zero);
        }

        return _dc;
    }

    private static (double Width, double Line)? MeasureWithGdi(string text, string family, bool bold, bool italic)
    {
        IntPtr dc = Dc();
        if (dc == IntPtr.Zero)
        {
            return null;
        }

        if (!Fonts.TryGetValue((family, bold, italic), out IntPtr font))
        {
            font = CreateFontW(
                -MeasureEm, 0, 0, 0, bold ? 700 : 400, italic ? 1u : 0u, 0, 0,
                1 /* DEFAULT_CHARSET */, 4 /* OUT_TT_PRECIS */, 0, 4 /* ANTIALIASED_QUALITY */, 0, family);
            if (font == IntPtr.Zero)
            {
                return null;
            }

            Fonts[(family, bold, italic)] = font;
        }

        IntPtr previous = SelectObject(dc, font);
        try
        {
            if (!GetTextMetricsW(dc, out TEXTMETRICW metrics))
            {
                return null;
            }

            double width = 0;
            if (text.Length > 0)
            {
                if (!GetTextExtentPoint32W(dc, text, text.Length, out SIZE size))
                {
                    return null;
                }

                width = size.cx;
            }

            return (width, metrics.tmHeight + metrics.tmExternalLeading);
        }
        finally
        {
            SelectObject(dc, previous);
        }
    }

    // --- GDI -------------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TEXTMETRICW
    {
        public int tmHeight;
        public int tmAscent;
        public int tmDescent;
        public int tmInternalLeading;
        public int tmExternalLeading;
        public int tmAveCharWidth;
        public int tmMaxCharWidth;
        public int tmWeight;
        public int tmOverhang;
        public int tmDigitizedAspectX;
        public int tmDigitizedAspectY;
        public char tmFirstChar;
        public char tmLastChar;
        public char tmDefaultChar;
        public char tmBreakChar;
        public byte tmItalic;
        public byte tmUnderlined;
        public byte tmStruckOut;
        public byte tmPitchAndFamily;
        public byte tmCharSet;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LOGFONTW
    {
        public int lfHeight;
        public int lfWidth;
        public int lfEscapement;
        public int lfOrientation;
        public int lfWeight;
        public byte lfItalic;
        public byte lfUnderline;
        public byte lfStrikeOut;
        public byte lfCharSet;
        public byte lfOutPrecision;
        public byte lfClipPrecision;
        public byte lfQuality;
        public byte lfPitchAndFamily;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string lfFaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ENUMLOGFONTEXW
    {
        public LOGFONTW elfLogFont;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string elfFullName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string elfStyle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string elfScript;
    }

    private delegate int EnumFontFamExProc(ref ENUMLOGFONTEXW font, IntPtr metrics, uint fontType, IntPtr lParam);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(
        int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut,
        uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTextExtentPoint32W(IntPtr hdc, string text, int length, out SIZE size);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTextMetricsW(IntPtr hdc, out TEXTMETRICW metrics);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int EnumFontFamiliesExW(
        IntPtr hdc, ref LOGFONTW logFont, EnumFontFamExProc proc, IntPtr lParam, uint flags);
}
