function out = hid_smoke(kind)
% HID_SMOKE  The cases of hid_smoke (device classes plan, stage D6, ADR 0189): jgraph.usb.hidlist and
%   jgraph.usb.hid, a JGraph extension with no MATLAB counterpart, so the answers are written from the
%   rule. Every case answers the same on any Windows machine, whatever is plugged in: the empty
%   list's shape, and the refusals. Answers text: a value, or an error's identifier.
try
    switch kind
        case 'empty_list'
            T = jgraph.usb.hidlist("VendorID", "FFFF");
            out = sprintf('%d %d %s', height(T), width(T), strjoin(T.Properties.VariableNames, ','));
        case 'empty_classes'
            T = jgraph.usb.hidlist("VendorID", 65535, "ProductID", "0xFFFF");
            out = sprintf('%s %s %s', class(T.VendorID), class(T.UsagePage), class(T.SystemOwned));
        case 'usage_hex'
            T = jgraph.usb.hidlist("UsagePage", "FFFF", "Usage", "0xFFFF");
            out = sprintf('%d', height(T));
        case 'list_bad_name'
            jgraph.usb.hidlist("Bogus", 1);
        case 'list_odd'
            jgraph.usb.hidlist("VendorID");
        case 'list_bad_usage'
            jgraph.usb.hidlist("UsagePage", "XYZWV");
        case 'list_bad_serial'
            jgraph.usb.hidlist("SerialNumber", 5);
        case 'open_none'
            jgraph.usb.hid("VendorID", "FFFF");
        case 'open_bad_name'
            jgraph.usb.hid("Bogus", 1);
        case 'open_odd'
            jgraph.usb.hid("Path");
        case 'open_bad_path'
            jgraph.usb.hid("Path", 5);
        case 'open_bad_buffers'
            jgraph.usb.hid("VendorID", "FFFF", "InputBuffers", "x");
        otherwise
            error('hid_smoke:case', 'no case %s', kind);
    end
catch e
    out = e.identifier;
end
end
