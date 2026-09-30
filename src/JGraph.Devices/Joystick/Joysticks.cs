using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGraph.Devices.Joystick;

/// <summary>What a joystick has: axes, buttons, points of view and force-feedback axes.</summary>
public sealed record JoystickCaps(string Name, int Axes, int Buttons, int Povs, int Forces);

/// <summary>A joystick's state: axes scaled to [-1, 1], buttons, and points of view in degrees (-1 when centred).</summary>
public sealed record JoystickState(double[] Axes, bool[] Buttons, double[] Povs);

/// <summary>
/// The joysticks a script's <c>vrjoystick</c> reads (device classes plan, stage D6): the connected ones in
/// the order Windows numbers them, each one's capabilities and its state.
/// </summary>
public interface IJoystickBackend
{
    /// <summary>The system's IDs of the joysticks connected now, in order: <c>vrjoystick(k)</c> is the k-th.</summary>
    IReadOnlyList<int> Connected();

    /// <summary>A connected joystick's capabilities, or null when it is gone.</summary>
    JoystickCaps? Caps(int id);

    /// <summary>A connected joystick's state now, or null when it is gone.</summary>
    JoystickState? Read(int id);
}

/// <summary>Where <c>vrjoystick</c>'s joysticks come from: a simulated set a test installed, or Windows'.</summary>
public static class JoystickBackends
{
    [ThreadStatic]
    private static IJoystickBackend? t_simulated;

    /// <summary>The backend for scripts on the calling thread.</summary>
    public static IJoystickBackend Current =>
        t_simulated ?? (OperatingSystem.IsWindows() ? WinMmJoysticks.Instance : NoJoysticks.Instance);

    /// <summary>Installs a simulated backend for the calling thread (null removes it).</summary>
    public static void Simulate(IJoystickBackend? backend) => t_simulated = backend;

    private sealed class NoJoysticks : IJoystickBackend
    {
        public static readonly NoJoysticks Instance = new();

        public IReadOnlyList<int> Connected() => [];

        public JoystickCaps? Caps(int id) => null;

        public JoystickState? Read(int id) => null;
    }
}

/// <summary>
/// Game controllers through WinMM's joystick API (<c>joyGetPosEx</c>): every DirectInput-class
/// controller Windows' joystick driver reports, with up to six axes (X, Y, Z, R, U, V), 32 buttons and
/// a point of view. It has no force feedback.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class WinMmJoysticks : IJoystickBackend
{
    public static readonly WinMmJoysticks Instance = new();

    private const uint JOYERR_NOERROR = 0;
    private const uint JOY_RETURNALL = 0xFF;
    private const uint JOYCAPS_HASZ = 0x01;
    private const uint JOYCAPS_HASR = 0x02;
    private const uint JOYCAPS_HASU = 0x04;
    private const uint JOYCAPS_HASV = 0x08;
    private const uint JOYCAPS_HASPOV = 0x10;
    private const uint JOY_POVCENTERED = 0xFFFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOYINFOEX
    {
        public uint dwSize;
        public uint dwFlags;
        public uint dwXpos;
        public uint dwYpos;
        public uint dwZpos;
        public uint dwRpos;
        public uint dwUpos;
        public uint dwVpos;
        public uint dwButtons;
        public uint dwButtonNumber;
        public uint dwPOV;
        public uint dwReserved1;
        public uint dwReserved2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOYCAPSW
    {
        public ushort wMid;
        public ushort wPid;
        public fixed char szPname[32];
        public uint wXmin;
        public uint wXmax;
        public uint wYmin;
        public uint wYmax;
        public uint wZmin;
        public uint wZmax;
        public uint wNumButtons;
        public uint wPeriodMin;
        public uint wPeriodMax;
        public uint wRmin;
        public uint wRmax;
        public uint wUmin;
        public uint wUmax;
        public uint wVmin;
        public uint wVmax;
        public uint wCaps;
        public uint wMaxAxes;
        public uint wNumAxes;
        public uint wMaxButtons;
        public fixed char szRegKey[32];
        public fixed char szOEMVxD[260];
    }

    [LibraryImport("winmm.dll")]
    private static partial uint joyGetNumDevs();

    [LibraryImport("winmm.dll")]
    private static partial uint joyGetDevCapsW(nuint id, JOYCAPSW* caps, uint size);

    [LibraryImport("winmm.dll")]
    private static partial uint joyGetPosEx(uint id, JOYINFOEX* info);

    public IReadOnlyList<int> Connected()
    {
        var ids = new List<int>();
        uint count = Math.Min(joyGetNumDevs(), 16);
        for (uint id = 0; id < count; id++)
        {
            var info = new JOYINFOEX { dwSize = (uint)sizeof(JOYINFOEX), dwFlags = JOY_RETURNALL };
            if (joyGetPosEx(id, &info) == JOYERR_NOERROR)
            {
                ids.Add((int)id);
            }
        }

        return ids;
    }

    private static bool CapsOf(int id, out JOYCAPSW caps)
    {
        JOYCAPSW c;
        bool ok = joyGetDevCapsW((nuint)id, &c, (uint)sizeof(JOYCAPSW)) == JOYERR_NOERROR;
        caps = c;
        return ok;
    }

    public JoystickCaps? Caps(int id)
    {
        if (!CapsOf(id, out JOYCAPSW c))
        {
            return null;
        }

        string name = new string(c.szPname).TrimEnd('\0');
        return new JoystickCaps(name, AxisRanges(c).Count, (int)Math.Min(c.wNumButtons, 32), (c.wCaps & JOYCAPS_HASPOV) != 0 ? 1 : 0, 0);
    }

    /// <summary>The axes a controller has, in WinMM's order, each with its range.</summary>
    private static List<(Func<JOYINFOEX, uint> Position, uint Min, uint Max)> AxisRanges(JOYCAPSW c)
    {
        var axes = new List<(Func<JOYINFOEX, uint>, uint, uint)>
        {
            (static i => i.dwXpos, c.wXmin, c.wXmax),
            (static i => i.dwYpos, c.wYmin, c.wYmax),
        };
        if ((c.wCaps & JOYCAPS_HASZ) != 0)
        {
            axes.Add((static i => i.dwZpos, c.wZmin, c.wZmax));
        }

        if ((c.wCaps & JOYCAPS_HASR) != 0)
        {
            axes.Add((static i => i.dwRpos, c.wRmin, c.wRmax));
        }

        if ((c.wCaps & JOYCAPS_HASU) != 0)
        {
            axes.Add((static i => i.dwUpos, c.wUmin, c.wUmax));
        }

        if ((c.wCaps & JOYCAPS_HASV) != 0)
        {
            axes.Add((static i => i.dwVpos, c.wVmin, c.wVmax));
        }

        return axes;
    }

    public JoystickState? Read(int id)
    {
        if (!CapsOf(id, out JOYCAPSW c))
        {
            return null;
        }

        var info = new JOYINFOEX { dwSize = (uint)sizeof(JOYINFOEX), dwFlags = JOY_RETURNALL };
        if (joyGetPosEx((uint)id, &info) != JOYERR_NOERROR)
        {
            return null;
        }

        JOYINFOEX now = info;
        double[] axes = AxisRanges(c).Select(a => Scale(a.Position(now), a.Min, a.Max)).ToArray();
        bool[] buttons = Enumerable.Range(0, (int)Math.Min(c.wNumButtons, 32)).Select(b => (now.dwButtons & (1u << b)) != 0).ToArray();
        double[] povs = (c.wCaps & JOYCAPS_HASPOV) != 0 ? [now.dwPOV == JOY_POVCENTERED ? -1 : now.dwPOV / 100.0] : [];
        return new JoystickState(axes, buttons, povs);
    }

    /// <summary>A position in [min, max] as a value in [-1, 1].</summary>
    public static double Scale(uint position, uint min, uint max) =>
        max <= min ? 0 : Math.Clamp(((double)position - min) / (max - min) * 2 - 1, -1, 1);
}

/// <summary>A joystick whose state a test sets: the simulated backend's one controller.</summary>
public sealed class SimulatedJoysticks : IJoystickBackend
{
    /// <summary>The simulated controllers, by system ID; a missing one is unplugged.</summary>
    public Dictionary<int, (JoystickCaps Caps, JoystickState State)> Devices { get; } = new();

    public IReadOnlyList<int> Connected() => Devices.Keys.Order().ToList();

    public JoystickCaps? Caps(int id) => Devices.TryGetValue(id, out var d) ? d.Caps : null;

    public JoystickState? Read(int id) => Devices.TryGetValue(id, out var d) ? d.State : null;
}
