function out = usb_device_smoke(kind)
% USB_DEVICE_SMOKE  The cases of usb_device_smoke (device classes plan, stage D7, ADR 0190):
%   jgraph.usb.device, a JGraph extension with no MATLAB counterpart, so the answers are written from
%   the rule. Every case answers the same on any Windows machine with USB, whatever is plugged in.
%   Answers text: a value, or an error's identifier.
try
    switch kind
        case 'no_args'
            jgraph.usb.device();
        case 'no_device'
            jgraph.usb.device("VendorID", "FFFF");
        case 'bad_name'
            jgraph.usb.device("Bogus", 1);
        case 'bad_interface'
            jgraph.usb.device("VendorID", "FFFF", "Interface", 1.5);
        case 'interface_too_big'
            jgraph.usb.device("VendorID", "FFFF", "Interface", 300);
        case 'row_and_filters'
            hubs = jgraph.usb.devices("Class", "Hub");
            jgraph.usb.device(hubs(1, :), "VendorID", "FFFF");
        case 'hub_not_winusb'
            hubs = jgraph.usb.devices("Class", "Hub");
            jgraph.usb.device(hubs(1, :));
        case 'hub_not_winusb_interface'
            hubs = jgraph.usb.devices("Class", "Hub");
            jgraph.usb.device(hubs.InstanceID(1), "Interface", 0);
        otherwise
            error('usb_device_smoke:case', 'no case %s', kind);
    end
catch e
    out = e.identifier;
end
end
