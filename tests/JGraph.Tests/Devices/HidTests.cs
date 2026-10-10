using System.Runtime.Versioning;
using JGraph.Devices.Hid;
using JGraph.Devices.Joystick;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Devices;

/// <summary>
/// HID and the joystick (device classes plan, stage D6, ADR 0189): vrjoystick on a simulated
/// controller (its state, the picks of axis, button and pov, caps, the refusals), WinMM's scaling,
/// and the HID list and a collection's capabilities on whatever this machine has.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection("JG facade")]
public class HidTests
{
    [Theory]
    [InlineData(0u, 0u, 65535u, -1.0)]
    [InlineData(65535u, 0u, 65535u, 1.0)]
    [InlineData(32767u, 0u, 65534u, 0.0)]
    [InlineData(10u, 10u, 10u, 0.0)]
    public void WinMmPositionsScaleToPlusMinusOne(uint position, uint min, uint max, double scaled) =>
        Assert.Equal(scaled, WinMmJoysticks.Scale(position, min, max), 6);

    [Fact]
    public void AJoystickReadsItsAxesButtonsAndPointOfView()
    {
        var sim = new SimulatedJoysticks();
        sim.Devices[3] = (new JoystickCaps("Pad", 3, 4, 1, 0),
            new JoystickState([-1, 0.5, 0], [true, false, true, false], [90]));
        JoystickBackends.Simulate(sim);
        try
        {
            Assert.Equal("-1 0.5 0|1 0 1 0|90|0.5 -1|1 1|90|3 4 1 0|JGraph:vrjoystick:NoForceFeedback|MATLAB:badsubscript|sl3d:vrjoystick:notconnected|closed", Run("""
                j = vrjoystick(1);
                [a, b, p] = read(j);
                fprintf('%g %g %g|%d %d %d %d|%g|', a, b, p);
                fprintf('%g %g|%d %d|%g|', axis(j, [2 1]), button(j, [1 3]), pov(j));
                c = caps(j);
                fprintf('%d %d %d %d|', c.Axes, c.Buttons, c.POVs, c.Forces);
                try, force(j, 1, 0.5); catch e, fprintf('%s|', e.identifier); end
                try, axis(j, 4); catch e, fprintf('%s|', e.identifier); end
                try, vrjoystick(2); catch e, fprintf('%s|', e.identifier); end
                close(j);
                if ~isvalid(j), fprintf('closed'); end
                """));
        }
        finally
        {
            JoystickBackends.Simulate(null);
        }
    }

    [Fact]
    public void AnUnpluggedJoystickIsNotConnected()
    {
        JoystickBackends.Simulate(new GoneAfterOpening());
        try
        {
            Assert.Equal("sl3d:vrjoystick:notconnected|sl3d:vrjoystick:notconnected", Run("""
                j = vrjoystick(1);
                try, read(j); catch e, fprintf('%s|', e.identifier); end
                try, caps(j); catch e, fprintf('%s', e.identifier); end
                """));
        }
        finally
        {
            JoystickBackends.Simulate(null);
        }
    }

    /// <summary>A controller that is listed when a script opens it and gone when the script reads it.</summary>
    private sealed class GoneAfterOpening : IJoystickBackend
    {
        public IReadOnlyList<int> Connected() => [0];

        public JoystickCaps? Caps(int id) => null;

        public JoystickState? Read(int id) => null;
    }

    [Fact]
    public void TheHidListReadsThisMachine()
    {
        IReadOnlyList<HidInfo> list = HidDevice.List();
        Assert.All(list, static h => Assert.StartsWith(@"\\?\", h.Path, StringComparison.Ordinal));
        HidInfo? open = list.FirstOrDefault(static h => !h.SystemOwned && h.InputReportLength > 0);
        if (open is null)
        {
            return;
        }

        // A collection Windows does not keep opens, and its capabilities come from its preparsed data.
        using HidDevice device = HidDevice.Open(open.Path);
        Assert.NotEmpty(device.Capabilities());
        Assert.Equal(open.UsagePage, device.Info.UsagePage);
    }

    private static string Run(string code)
    {
        var output = new RecordingScriptOutput();
        ScriptRunResult result = JgsRunner.Run(
            code, new ScriptContext(output, (_, _) => { }, null), default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + output.ErrorText);
        return output.NormalText;
    }
}
