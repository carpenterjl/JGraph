function out = printers_sim_case(kind)
% PRINTERS_SIM_CASE  The cases of printers_sim (device classes plan, stage D12, ADR 0196):
%   jgraph.usb.printers and jgraph.usb.printraw on jgraph.internal.printsim's queues, and the shape
%   of jgraph.usb.netadapter on the machine's own adapters. "JGraph Test Printer" is the default
%   queue, a USB printer (1209:7001) on USB001 whose IEEE 1284 device ID is
%   "MFG:JGraph;MDL:Test Printer;CMD:ESC/POS;CLS:PRINTER;"; "JGraph Document Writer" has no USB
%   device behind it and is listed first. A job is kept as it was sent. The answers are written from
%   that rule. Answers text.
jgraph.internal.printsim('on');
usb = 'JGraph Test Printer';
other = 'JGraph Document Writer';
work = tempname;
mkdir(work);
try
    switch kind
        case 'table'
            T = jgraph.usb.printers;
            out = sprintf('%s %d %d; %s; %s; %s %s', class(T), size(T), strjoin(T.Properties.VariableNames, ' '), strjoin(T.Name', ', '), ...
                class(T.Default), class(T.Jobs));
        case 'usb_row'
            T = jgraph.usb.printers;
            r = T(2, :);
            out = sprintf('%s; %s; %s; %d; %s; %d; %s; %s; %s; %s; %s; %s; %s', r.Name, r.Port, r.Driver, r.Default, r.Status, r.Jobs, r.VendorID, r.ProductID, ...
                r.Manufacturer, r.Model, r.CommandSet, r.DeviceID, r.InstanceID);
        case 'other_row'
            T = jgraph.usb.printers;
            r = T(1, :);
            out = sprintf('%s; %s; %s; %d; %s; [%s%s%s%s%s%s%s]', r.Name, r.Port, r.Driver, r.Default, r.Status, r.VendorID, r.ProductID, ...
                r.Manufacturer, r.Model, r.CommandSet, r.DeviceID, r.InstanceID);
        case 'table_args'
            out = dv_err(@() jgraph.usb.printers(1));
        case 'print_bytes'
            job = jgraph.usb.printraw(usb, [27 64 72 105 10]);
            j = jgraph.internal.printsim('job', 1);
            out = sprintf('%s %d; %s; %s; %s [%s]; [%s]', class(job), job, j.Printer, j.DocumentName, class(j.Data), sprintf('%d ', j.Data), j.OutputFile);
        case 'print_forms'
            % The data as uint8, as a matrix (sent in column order), as text, and as nothing; the printer in any case, and as a row.
            T = jgraph.usb.printers;
            a = jgraph.usb.printraw('jgraph test printer', uint8([1 2 255]));
            b = jgraph.usb.printraw(T(2, :), [1 2; 3 4]);
            d = jgraph.usb.printraw(other, 'Hi');
            e = jgraph.usb.printraw(usb, "ZPL^XA");
            f = jgraph.usb.printraw(usb, []);
            out = sprintf('%d %d %d %d %d %d', a, b, d, e, f, jgraph.internal.printsim('jobs'));
            for k = 1:5
                j = jgraph.internal.printsim('job', k);
                out = [out sprintf('; %s [%s]', extractAfter(j.Printer, 'JGraph '), sprintf('%d ', j.Data))]; %#ok<AGROW>
            end
        case 'document_name'
            jgraph.usb.printraw(usb, 1, DocumentName="receipt 42");
            jgraph.usb.printraw(usb, 1, 'doc', 'label');
            a = jgraph.internal.printsim('job', 1);
            b = jgraph.internal.printsim('job', 2);
            out = sprintf('%s; %s', a.DocumentName, b.DocumentName);
        case 'output_file'
            f = fullfile(work, 'job.bin');
            job = jgraph.usb.printraw(usb, [29 86 0], OutputFile=f, DocumentName="cut");
            fid = fopen(f, 'r'); bytes = fread(fid); fclose(fid);
            j = jgraph.internal.printsim('job', job);
            out = sprintf('%d [%s] %d %s', job, sprintf('%d ', bytes), strcmp(j.OutputFile, f), j.DocumentName);
        case 'bad_printer'
            T = jgraph.usb.printers;
            out = [dv_err(@() jgraph.usb.printraw('nope', 1)) ' // ' dv_err(@() jgraph.usb.printraw('JGraph', 1)) ' // ' dv_err(@() jgraph.usb.printraw(1, 1)) ...
                ' // ' dv_err(@() jgraph.usb.printraw(T, 1)) sprintf(' // %d', jgraph.internal.printsim('jobs'))];
        case 'bad_data'
            out = [dv_err(@() jgraph.usb.printraw(usb, 256)) ' // ' dv_err(@() jgraph.usb.printraw(usb, -1)) ' // ' dv_err(@() jgraph.usb.printraw(usb, 1.5)) ...
                ' // ' dv_err(@() jgraph.usb.printraw(usb, {1})) ' // ' dv_err(@() jgraph.usb.printraw(usb, char([72 8364]))) ...
                sprintf(' // %d', jgraph.internal.printsim('jobs'))];
        case 'bad_pairs'
            out = [dv_err(@() jgraph.usb.printraw(usb)) ' // ' dv_err(@() jgraph.usb.printraw(usb, 1, 'DocumentName')) ' // ' dv_err(@() jgraph.usb.printraw(usb, 1, 'Copies', 2)) ...
                ' // ' dv_err(@() jgraph.usb.printraw(usb, 1, 'DocumentName', 5)) ' // ' dv_err(@() jgraph.usb.printraw(usb, 1, 'OutputFile', '')) ...
                ' // ' dv_err(@() jgraph.usb.printraw()) sprintf(' // %d', jgraph.internal.printsim('jobs'))];
        case 'offline'
            jgraph.internal.printsim('offline', usb, true);
            T = jgraph.usb.printers;
            refused = dv_err(@() jgraph.usb.printraw(usb, 1));
            fine = jgraph.usb.printraw(other, 1);
            jgraph.internal.printsim('offline', usb, false);
            T2 = jgraph.usb.printers;
            out = sprintf('%s %s %s // %s // %d %d', T.Status(2), T.Status(1), T2.Status(2), refused, fine, jgraph.usb.printraw(usb, 1));
        case 'output_file_bad'
            out = [strrep(dv_err(@() jgraph.usb.printraw(usb, 1, OutputFile=fullfile(work, 'no', 'x.bin'))), work, '<work>') sprintf(' // %d', jgraph.internal.printsim('jobs'))];
        case 'netadapter'
            % The machine's own adapters: only the shape is pinned.
            N = jgraph.usb.netadapter;
            out = sprintf('%s %d; %s; %s %s %s %s; %d', class(N), width(N), strjoin(N.Properties.VariableNames, ' '), class(N.Name), class(N.Speed), class(N.Kind), ...
                class(N.InstanceID), all(startsWith(N.InstanceID, "USB\")));
        case 'netadapter_filter'
            % A vendor no device has keeps no adapter; the filters are jgraph.usb.devices'.
            N = jgraph.usb.netadapter(VendorID="FFFE", ProductID="FFFD");
            out = [sprintf('%d %d // ', size(N)) dv_err(@() jgraph.usb.netadapter('Nope', 1)) ' // ' dv_err(@() jgraph.usb.netadapter('SIM\NO\SUCH'))];
        otherwise
            error('printers_sim_case:unknown', 'unknown case %s', kind);
    end
catch e
    out = ['ERR ' e.identifier ' ' e.message];
end
jgraph.internal.printsim('off');
rmdir(work, 's');
out = strrep(out, '|', '/'); % a fixture value holds no |
end
