using System.Runtime.Versioning;
using JGraph.Devices.Usb;
using JGraph.Devices.WinUsb;
using Xunit;

namespace JGraph.Tests.Devices;

/// <summary>
/// WinUSB (device classes plan, stage D7, ADR 0190): the driver-binding refusal's words, and — on a
/// machine where some interface is bound to WinUSB — opening it and reading its device descriptor over
/// the default pipe, a standard request that changes nothing.
/// </summary>
[SupportedOSPlatform("windows")]
public class WinUsbTests
{
    [Fact]
    public void TheRefusalNamesEachInterfacesDriver()
    {
        var composite = new UsbDeviceInfo
        {
            InstanceId = "USB\\VID_0B05&PID_19B6\\X",
            VendorId = 0x0B05,
            ProductId = 0x19B6,
            Driver = "usbccgp",
            InterfaceDrivers = new Dictionary<int, string> { [0] = "HidUsb", [1] = "HidUsb", [2] = "usbvideo", [3] = "HidUsb" },
        };
        Assert.Equal("No WinUSB driver is bound to 0B05:19B6 (interfaces 0, 1 and 3 are bound to HidUsb; interface 2 is bound to usbvideo). "
            + "Bind WinUSB with Zadig or an INF, or give the firmware MS OS 2.0 descriptors, which make Windows bind it by itself.",
            WinUsbDevice.NotBoundSentence(composite));
        Assert.StartsWith("No WinUSB driver is bound to 0B05:19B6 interface 2 (interface 2 is bound to usbvideo).",
            WinUsbDevice.NotBoundSentence(composite, 2), StringComparison.Ordinal);
        Assert.StartsWith("No WinUSB driver is bound to 0B05:19B6 interface 9 (the device has no interface 9).",
            WinUsbDevice.NotBoundSentence(composite, 9), StringComparison.Ordinal);

        var single = new UsbDeviceInfo { InstanceId = "USB\\VID_1234&PID_5678\\1", VendorId = 0x1234, ProductId = 0x5678, Driver = "usbser" };
        Assert.StartsWith("No WinUSB driver is bound to 1234:5678 (it is bound to usbser).", WinUsbDevice.NotBoundSentence(single), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownDeviceHasNoWinUsbPaths() => Assert.Empty(WinUsbDevice.PathsOf("USB\\VID_FFFF&PID_FFFF\\NONE"));

    [Fact]
    public void AWinUsbInterfaceAnswersItsDeviceDescriptor()
    {
        foreach (UsbDeviceInfo device in UsbEnumerator.Devices())
        {
            IReadOnlyList<(int Interface, string Path)> paths = WinUsbDevice.PathsOf(device.InstanceId);
            if (paths.Count == 0 || device.DeviceDescriptor.Length != 18)
            {
                continue;
            }

            WinUsbDevice winusb;
            try
            {
                winusb = WinUsbDevice.Open(paths[0].Path);
            }
            catch (JGraph.Devices.DeviceOpenException)
            {
                continue; // open in another program
            }

            using (winusb)
            {
                Assert.NotEmpty(winusb.Interfaces);

                // GET_DESCRIPTOR(DEVICE): standard, device recipient, IN.
                byte[] descriptor = winusb.Control(0x80, 6, 0x0100, 0, [], 18, TimeSpan.FromSeconds(2));
                Assert.Equal(device.DeviceDescriptor, descriptor);
            }

            return;
        }
    }
}
