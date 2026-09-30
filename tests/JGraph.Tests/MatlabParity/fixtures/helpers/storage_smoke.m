function out = storage_smoke(kind)
% STORAGE_SMOKE  The cases of storage_smoke (device classes plan, stage D9, ADR 0192):
%   jgraph.usb.storage and jgraph.usb.eject, JGraph extensions with no MATLAB counterpart, so the
%   answers are written from the rule. Every case answers the same on any Windows machine, whatever is
%   plugged in: the filters match no device, and nothing is ejected. Answers text: a value, or an
%   error's identifier.
try
    switch kind
        case 'disk_columns'
            out = strjoin(jgraph.usb.storage("VendorID", "FFFF").Properties.VariableNames, ',');
        case 'volume_columns'
            [~, V] = jgraph.usb.storage("VendorID", "FFFF");
            out = strjoin(V.Properties.VariableNames, ',');
        case 'none'
            [T, V] = jgraph.usb.storage("VendorID", "FFFF");
            out = sprintf('%d %d %s', height(T), height(V), class(T.Removable));
        case 'bare_two'
            [T, V] = jgraph.usb.storage;
            out = sprintf('%d %d', width(T), width(V));
        case 'bad_filter'
            jgraph.usb.storage("Bogus", 1);
        case 'eject_none'
            jgraph.usb.eject();
        case 'eject_number'
            jgraph.usb.eject(5);
        case 'eject_not_usb_drive'
            jgraph.usb.eject("A:");
        case 'eject_no_device'
            jgraph.usb.eject("USB\VID_FFFF&PID_FFFF\NONE");
        otherwise
            error('storage_smoke:case', 'no case %s', kind);
    end
catch e
    out = e.identifier;
end
end
