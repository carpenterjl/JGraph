using System.Runtime.Versioning;
using JGraph.Devices.Simulation;
using JGraph.Devices.Video;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Devices;
using Xunit;

namespace JGraph.Tests.Devices;

/// <summary>
/// Cameras (device classes plan, stage D11, ADR 0195): the machine's list through Media Foundation,
/// the simulated camera's frames, resolutions and controls, and a frame as snapshot hands it back.
/// No test opens a real camera: the list is read and nothing more.
/// </summary>
public class WebcamTests
{
    [Fact]
    [SupportedOSPlatform("windows")]
    public void TheMachinesCamerasAreListedWithoutOpeningAny()
    {
        IReadOnlyList<CameraInfo> cameras = MediaFoundationCameras.Instance.Cameras();
        Assert.All(cameras, static c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.False(string.IsNullOrWhiteSpace(c.Id));
        });
        Assert.Equal(cameras.Count, cameras.Select(static c => c.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // The list is the same when asked again.
        Assert.Equal(cameras.Select(static c => c.Id), MediaFoundationCameras.Instance.Cameras().Select(static c => c.Id));
    }

    [Fact]
    public void TheSimulatedCameraSendsItsKnownPicture()
    {
        var cameras = new SimulatedCameras();
        IReadOnlyList<CameraInfo> list = cameras.Cameras();
        Assert.Equal([SimulatedCameras.FirstName, SimulatedCameras.SecondName], list.Select(static c => c.Name));
        using ICamera camera = cameras.Open(list[0]);
        Assert.Equal((640, 480), camera.Resolution);
        Assert.Equal([(640, 480), (320, 240), (160, 120), (1280, 720)], camera.Resolutions);

        CameraFrame first = camera.NextFrame(TimeSpan.FromSeconds(1));
        Assert.Equal((640, 480, 1L), (first.Width, first.Height, first.Number));
        Assert.Equal(640 * 480 * 4, first.Bgrx.Length);

        // Blue, green, red: the top-left pixel is black but for the frame's blue, the bottom-right full red and green.
        Assert.Equal([8, 0, 0], first.Bgrx[..3]);
        Assert.Equal([8, 255, 255], first.Bgrx[^4..^1]);
        Assert.Equal(16, camera.NextFrame(TimeSpan.FromSeconds(1)).Bgrx[0]);
        Assert.Equal(2, camera.Latest()!.Number);
    }

    [Fact]
    public void ASimulatedCameraChangesResolutionAndControls()
    {
        var cameras = new SimulatedCameras();
        using ICamera camera = cameras.Open(cameras.Cameras()[0]);
        camera.SetResolution(320, 240);
        CameraFrame small = camera.NextFrame(TimeSpan.FromSeconds(1));
        Assert.Equal((320, 240), (small.Width, small.Height));
        Assert.Throws<CameraException>(() => camera.SetResolution(1, 1));

        Assert.Equal((-5, true), camera.ReadControl("Exposure"));
        camera.WriteControl("Exposure", -3, auto: false);
        Assert.Equal((-3, false), camera.ReadControl("Exposure"));
        camera.WriteControl("Brightness", 138, auto: false);
        Assert.Equal(10, camera.NextFrame(TimeSpan.FromSeconds(1)).Bgrx[1]);
        Assert.Throws<CameraException>(() => camera.ReadControl("Iris"));
    }

    [Fact]
    public void AnUnpluggedCameraLeavesTheListAndStopsSending()
    {
        var cameras = new SimulatedCameras();
        ICamera camera = cameras.Open(cameras.Cameras()[0]);
        Assert.Equal(1, cameras.OpenCount);
        cameras.Unplug(SimulatedCameras.FirstName);
        Assert.Equal([SimulatedCameras.SecondName], cameras.Cameras().Select(static c => c.Name));
        Assert.Throws<CameraException>(() => camera.NextFrame(TimeSpan.FromSeconds(1)));
        Assert.Null(camera.Latest());
        camera.Dispose();
        Assert.Equal(0, cameras.OpenCount);
        cameras.Plug(SimulatedCameras.FirstName);
        Assert.Equal(2, cameras.Cameras().Count);
    }

    [Fact]
    public void AListenerHearsFramesArriveUntilItLetsGo()
    {
        var cameras = new SimulatedCameras();
        using ICamera camera = cameras.Open(cameras.Cameras()[1]);
        int heard = 0;
        void Listener() => Interlocked.Increment(ref heard);
        camera.FrameArrived += Listener;
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref heard) >= 3, TimeSpan.FromSeconds(5)));
        camera.FrameArrived -= Listener;
        Thread.Sleep(100);
        int after = Volatile.Read(ref heard);
        Thread.Sleep(150);
        Assert.Equal(after, Volatile.Read(ref heard));
    }

    [Fact]
    public void AFrameBecomesAHeightByWidthByThreeUint8Array()
    {
        // Two rows of three pixels, each blue-green-red-unused.
        byte[] bgrx =
        [
            1, 2, 3, 0, 4, 5, 6, 0, 7, 8, 9, 0,
            10, 11, 12, 0, 13, 14, 15, 0, 16, 17, 18, 0,
        ];
        var frame = new CameraFrame(3, 2, bgrx, DateTime.Now, 1);
        JgsValue picture = WebcamShared.Picture(frame);
        Assert.Equal([2, 3, 3], picture.Dims);
        Assert.Equal(JgsNumericClass.UInt8, picture.NumericClass);

        // Column-major, red plane first: red of (1,1), (2,1), (1,2) …, then the green plane, then the blue.
        double[] expected = [3, 12, 6, 15, 9, 18, 2, 11, 5, 14, 8, 17, 1, 10, 4, 13, 7, 16];
        Assert.Equal(expected, Enumerable.Range(0, 18).Select(i => picture.ElementAt(i).AsNumber));
        Assert.Equal([0xFF030201u, 0xFF060504u, 0xFF090807u, 0xFF0C0B0Au, 0xFF0F0E0Du, 0xFF121110u], WebcamShared.Argb(frame));
    }
}
