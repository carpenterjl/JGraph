function out = dfu_smoke(kind)
% DFU_SMOKE  The cases of dfu_smoke (device classes plan, stage D8, ADR 0191): jgraph.usb.dfulist,
%   jgraph.usb.dfu, dfufile and dfusuffix, JGraph extensions with no MATLAB counterpart, so the answers
%   are written from the rule. The device cases run on jgraph.internal.dfusim's simulated loaders, a
%   fresh one each case; the rest answer the same on any Windows machine with USB. Nothing is sent to
%   a real device. Answers text: a value, or an error's identifier.
flash = 134217728; % 0x08000000
try
    switch kind
        case 'list_none'
            out = sprintf('%d', height(jgraph.usb.dfulist("VendorID", "FFFF")));
        case 'list_columns'
            out = strjoin(jgraph.usb.dfulist("VendorID", "FFFF").Properties.VariableNames, ',');
        case 'open_none'
            jgraph.usb.dfu("VendorID", "FFFF");
        case 'open_hub'
            hubs = jgraph.usb.devices("Class", "Hub");
            jgraph.usb.dfu(hubs(1, :));
        case 'se_props'
            d = jgraph.internal.dfusim("dfuse");
            out = sprintf('%s %s %d %d %d %d %d', d.Mode, d.Version, d.DfuSe, d.Alternate, d.TransferSize, numel(d.AlternateNames), d.WillDetach);
        case 'se_layout'
            d = jgraph.internal.dfusim("dfuse");
            L = d.Layout;
            out = '';
            for i = 1:height(L)
                out = [out sprintf('%s-%s %d*%d %d%d%d; ', L.Start(i), L.End(i), L.Pages(i), L.PageSize(i), L.Readable(i), L.Erasable(i), L.Writeable(i))];
            end
        case 'se_status'
            d = jgraph.internal.dfusim("dfuse");
            s = status(d);
            out = sprintf('%s %s', s.Status, s.State);
        case 'se_download_verify'
            d = jgraph.internal.dfusim("dfuse");
            data = uint8(mod(0:4999, 251));
            download(d, data, Address="0x08003F00", Verify=true);
            back = upload(d, 5000, Address="08003F00");
            out = sprintf('%d %s', isequal(back, data), sprintf('%d ', jgraph.internal.dfusim(d, "peek", flash, 4)));
        case 'se_progress'
            d = jgraph.internal.dfusim("dfuse");
            calls = 0;
            last = '';
            download(d, uint8(zeros(1, 5000)), Address="0x08004000", Progress=@tally);
            out = sprintf('%d %s', calls, last);
        case 'se_default_address'
            d = jgraph.internal.dfusim("dfuse");
            download(d, uint8(1:10));
            out = sprintf('%d ', jgraph.internal.dfusim(d, "peek", flash, 10));
        case 'se_bad_address'
            d = jgraph.internal.dfusim("dfuse");
            download(d, uint8(1:10), Address="0x20000000");
        case 'se_past_end'
            d = jgraph.internal.dfusim("dfuse");
            download(d, uint8([1 2]), Address="0x0801FFFF");
        case 'se_option_bytes'
            d = jgraph.internal.dfusim("dfuse");
            download(d, uint8([18 52]), Alternate=1);
            out = sprintf('%d ', upload(d, 3, Alternate=1));
        case 'se_hex_file'
            d = jgraph.internal.dfusim("dfuse");
            f = [tempname '.hex'];
            fid = fopen(f, 'w');
            fprintf(fid, ':020000040800F2\n:0400000001020304F2\n:00000001FF\n');
            fclose(fid);
            download(d, f);
            out = sprintf('%d ', jgraph.internal.dfusim(d, "peek", flash, 6));
            delete(f);
        case 'se_hex_with_address'
            d = jgraph.internal.dfusim("dfuse");
            f = [tempname '.hex'];
            fid = fopen(f, 'w');
            fprintf(fid, ':020000040800F2\n:0400000001020304F2\n:00000001FF\n');
            fclose(fid);
            cleanup = onCleanup(@() delete(f));
            download(d, f, Address="0x08004000");
        case 'se_wrong_device'
            d = jgraph.internal.dfusim("dfuse");
            f = write_dfu(jgraph.usb.dfusuffix(uint8(1:64), VendorID="1234", ProductID="5678"));
            cleanup = onCleanup(@() delete(f));
            download(d, f, Address="0x08004000");
        case 'se_force'
            d = jgraph.internal.dfusim("dfuse");
            f = write_dfu(jgraph.usb.dfusuffix(uint8(1:64), VendorID="1234", ProductID="5678"));
            cleanup = onCleanup(@() delete(f));
            download(d, f, Address="0x08004000", Force=true);
            out = sprintf('%d ', jgraph.internal.dfusim(d, "peek", flash + 16384, 4));
        case 'se_any_device'
            d = jgraph.internal.dfusim("dfuse");
            f = write_dfu(jgraph.usb.dfusuffix(uint8(5:8)));
            cleanup = onCleanup(@() delete(f));
            download(d, f, Address="0x08004000");
            out = sprintf('%d ', jgraph.internal.dfusim(d, "peek", flash + 16384, 4));
        case 'se_crc'
            d = jgraph.internal.dfusim("dfuse");
            bytes = jgraph.usb.dfusuffix(uint8(1:64), VendorID="0483", ProductID="DF11");
            bytes(3) = bytes(3) + 1;
            f = write_dfu(bytes);
            cleanup = onCleanup(@() delete(f));
            download(d, f, Address="0x08004000");
        case 'se_fail'
            d = jgraph.internal.dfusim("dfuse");
            jgraph.internal.dfusim(d, "fail", 1);
            try
                download(d, uint8(1:10), Address="0x08004000");
                first = 'sent';
            catch e
                first = e.identifier;
            end
            s = status(d);
            download(d, uint8(1:10), Address="0x08004000");
            out = sprintf('%s %s %d', first, s.State, isequal(upload(d, 10, Address="0x08004000"), uint8(1:10)));
        case 'se_erase'
            d = jgraph.internal.dfusim("dfuse");
            download(d, uint8(1:10), Address="0x08004000");
            erase(d, "0x08004000");
            out = sprintf('%d ', jgraph.internal.dfusim(d, "peek", flash + 16384, 3));
        case 'se_erase_options'
            d = jgraph.internal.dfusim("dfuse");
            d.Alternate = 1;
            erase(d, "0x1FFFC000");
        case 'se_mass_erase'
            d = jgraph.internal.dfusim("dfuse");
            before = jgraph.internal.dfusim(d, "peek", flash + 1, 1);
            massErase(d);
            out = sprintf('%d %d', before, jgraph.internal.dfusim(d, "peek", flash + 1, 1));
        case 'se_leave'
            d = jgraph.internal.dfusim("dfuse");
            leave(d);
            gone = jgraph.internal.dfusim(d, "gone");
            try
                status(d);
                after = 'answered';
            catch e
                after = e.identifier;
            end
            out = sprintf('%d %s', gone, after);
        case 'se_detach'
            d = jgraph.internal.dfusim("dfuse");
            detach(d);
        case 'se_alternate_range'
            d = jgraph.internal.dfusim("dfuse");
            d.Alternate = 2;
        case 'se_bad_option'
            d = jgraph.internal.dfusim("dfuse");
            download(d, uint8(1), Bogus=1);
        case 'se_empty'
            d = jgraph.internal.dfusim("dfuse");
            download(d, uint8([]));
        case 'plain_props'
            p = jgraph.internal.dfusim("dfu");
            out = sprintf('%s %s %d %d %d', p.Mode, p.Version, p.DfuSe, p.ManifestationTolerant, height(p.Layout));
        case 'plain_roundtrip'
            p = jgraph.internal.dfusim("dfu");
            before = numel(upload(p));
            download(p, uint8(mod(1:700, 256)), Verify=true);
            out = sprintf('%d %d %s', before, isequal(jgraph.internal.dfusim(p, "image"), uint8(mod(1:700, 256))), ...
                jgraph.internal.dfusim(p, "state"));
        case 'plain_address'
            p = jgraph.internal.dfusim("dfu");
            download(p, uint8(1:10), Address=0);
        case 'plain_mass_erase'
            p = jgraph.internal.dfusim("dfu");
            massErase(p);
        case 'plain_leave'
            p = jgraph.internal.dfusim("dfu");
            leave(p);
        case 'rt_download'
            r = jgraph.internal.dfusim("runtime");
            download(r, uint8(1:10));
        case 'rt_detach'
            r = jgraph.internal.dfusim("runtime");
            d2 = detach(r);
            out = sprintf('%s %s %d', r.Mode, d2.Mode, jgraph.internal.dfusim(r, "gone"));
        case 'file_hex'
            f = [tempname '.hex'];
            fid = fopen(f, 'w');
            fprintf(fid, ':020000040800F2\n:0400000001020304F2\n:020010000506E3\n:00000001FF\n');
            fclose(fid);
            cleanup = onCleanup(@() delete(f));
            img = jgraph.usb.dfufile(f);
            out = sprintf('%s %d %d %d %d', img.Format, img.HasSuffix, numel(img.Targets.Elements), img.Targets.Elements(2).Address, img.Size);
        case 'file_suffix'
            f = write_dfu(jgraph.usb.dfusuffix(uint8(1:64), VendorID="0483", ProductID="DF11", Release="2200"));
            cleanup = onCleanup(@() delete(f));
            img = jgraph.usb.dfufile(f);
            out = sprintf('%s %s:%s %s %s %d %d', img.Format, img.VendorID, img.ProductID, img.Release, img.DfuVersion, img.CrcOk, img.Size);
        case 'file_missing'
            jgraph.usb.dfufile("no_such_firmware.dfu");
        case 'suffix_bytes'
            s = jgraph.usb.dfusuffix(uint8('123456789'));
            out = sprintf('%d %s', numel(s), sprintf('%d ', s(10:21)));
        case 'suffix_bad'
            jgraph.usb.dfusuffix(300);
        otherwise
            error('dfu_smoke:case', 'no case %s', kind);
    end
catch e
    out = e.identifier;
end

    function tally(sent, total)
        % The Progress callback of se_progress: counts its calls, and keeps the last.
        calls = calls + 1;
        last = sprintf('%d/%d', sent, total);
    end
end

function f = write_dfu(bytes)
f = [tempname '.dfu'];
fid = fopen(f, 'w');
fwrite(fid, bytes);
fclose(fid);
end
