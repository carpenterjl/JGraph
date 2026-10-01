using JGraph.Devices.Simulation;
using JGraph.Scripting.Jgs.Devices;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Cameras (device classes plan, stage D11, ADR 0195): <c>webcamlist</c> and <c>webcam</c> of the
/// MATLAB Support Package for USB Webcams, over Media Foundation, and the test-only
/// <c>jgraph.internal.camsim</c>. <c>snapshot</c>, <c>preview</c> and <c>closePreview</c> are the
/// object's methods.
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterWebcamBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        env.Builtins.Register("webcamlist", JgsValue.Function(new BuiltinFunction("webcamlist",
            (args, line, col) => WebcamShared.List(host.Devices, args, line, col))
        {
            AutoCallsBare = true,
        }));
        env.Builtins.Register("webcam", JgsValue.Function(new BuiltinFunction("webcam",
            (args, line, col) => WebcamObject.Create(host.Devices, interpreter, args, line, col))
        {
            KeepsStringArguments = true,
            AutoCallsBare = true,
        }));
    }

    /// <summary>
    /// <c>jgraph.internal.camsim('on')</c> replaces the machine's cameras with <see cref="SimulatedCameras"/>
    /// for the session and <c>'off'</c> puts them back, deleting the webcam objects first;
    /// <c>camsim('unplug', name)</c> and <c>camsim('plug', name)</c> take a simulated camera away and
    /// bring it back, and <c>camsim('open')</c> counts the cameras held open. Test-only and undocumented.
    /// </summary>
    private static JgsValue CameraSimFunction(Interpreter interpreter) =>
        JgsValue.Function(new BuiltinFunction("jgraph.internal.camsim", (args, line, col) =>
        {
            JGraphScriptGlobals host = interpreter.Host
                ?? throw new JgsRuntimeException(line, col, "JGraph:camsim:Arguments", "This session has no host.");
            string verb = args.Count >= 1 && IsTextScalar(args[0]) ? TextOf(args[0]) : "";
            string? name = args.Count == 2 && IsTextScalar(args[1]) ? TextOf(args[1]) : null;
            switch (verb)
            {
                case "on" when args.Count == 1:
                    host.Devices.CameraSimulation ??= new SimulatedCameras();
                    return JgsValue.Null;
                case "off" when args.Count == 1:
                    foreach (DeviceObject device in host.Devices.Live.Where(static d => d is WebcamObject))
                    {
                        device.Delete();
                    }

                    host.Devices.CameraSimulation = null;
                    return JgsValue.Null;
                case "unplug" when name is not null && host.Devices.CameraSimulation is { } sim:
                    sim.Unplug(name);
                    return JgsValue.Null;
                case "plug" when name is not null && host.Devices.CameraSimulation is { } sim:
                    sim.Plug(name);
                    return JgsValue.Null;
                case "open" when args.Count == 1 && host.Devices.CameraSimulation is { } sim:
                    return JgsValue.Number(sim.OpenCount);
                default:
                    throw new JgsRuntimeException(line, col, "JGraph:camsim:Arguments",
                        "jgraph.internal.camsim takes 'on', 'off', 'open', or 'unplug' or 'plug' and a simulated camera's name.");
            }
        })
        {
            BindsAnsAsStatement = false,
        });
}
