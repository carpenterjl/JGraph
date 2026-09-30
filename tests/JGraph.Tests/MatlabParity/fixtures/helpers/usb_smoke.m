function out = usb_smoke(kind)
% USB_SMOKE  The cases of usb_enum_smoke (device classes plan, stage D5, ADR 0188): jgraph.usb, a
%   JGraph extension with no MATLAB counterpart, so the answers are written from the rule. Every
%   case answers the same on any Windows machine, whatever is plugged in: the shapes of the empty
%   and machine-wide answers, and the refusals. Answers text: a value, or an error's identifier.
try
    switch kind
        case 'empty_table'
            T = jgraph.usb.devices("VendorID", "FFFF", "ProductID", 65535);
            out = sprintf('%d %d %s', height(T), width(T), strjoin(T.Properties.VariableNames, ','));
        case 'empty_classes'
            T = jgraph.usb.devices("VendorID", "0xFFFF");
            out = sprintf('%s %s', class(T.VendorID), class(T.HIDCollections));
        case 'partial_name'
            T = jgraph.usb.devices("vendor", "FFFF", "prod", "FFFF");
            out = sprintf('%d', height(T));
        case 'bad_name'
            jgraph.usb.devices("Bogus", 1);
        case 'odd_pairs'
            jgraph.usb.devices("VendorID");
        case 'bad_id'
            jgraph.usb.devices("VendorID", "XYZWV");
        case 'id_too_big'
            jgraph.usb.devices("ProductID", 70000);
        case 'bad_class'
            jgraph.usb.devices("Class", {1});
        case 'bad_driver'
            jgraph.usb.devices("Driver", 5);
        case 'descriptors_none'
            jgraph.usb.descriptors("VendorID", "FFFF");
        case 'descriptors_no_args'
            jgraph.usb.descriptors();
        case 'descriptors_empty_table'
            jgraph.usb.descriptors(jgraph.usb.devices("VendorID", "FFFF"));
        case 'descriptors_unknown_id'
            jgraph.usb.descriptors("USB\VID_FFFF&PID_FFFF\NONE");
        case 'ports_vars'
            P = jgraph.usb.ports;
            out = sprintf('%s %s %s', strjoin(P.Properties.VariableNames, ','), class(P.Port), class(P.Connected));
        case 'ports_args'
            jgraph.usb.ports(1);
        case 'tree_class'
            t = jgraph.usb.tree;
            out = sprintf('%s %d', class(t), size(t, 1) <= 1);
        case 'tree_args'
            jgraph.usb.tree(1);
        case 'watch_bad'
            jgraph.usb.watch(5);
        case 'watch_bad_filter'
            jgraph.usb.watch(@disp, "Bogus", 1);
        case 'watch_object'
            w = jgraph.usb.watch(@(w, e) disp(e.Type), "VendorID", "FFFF");
            before = isvalid(w);
            p = properties(w);
            events = w.Events;
            delete(w);
            out = sprintf('%s %d %s %d %d', class(w), before, strjoin(p', ','), events, isvalid(w));
        otherwise
            error('usb_smoke:case', 'no case %s', kind);
    end
catch e
    out = e.identifier;
end
end
