function out = mtp_sim_case(kind)
% MTP_SIM_CASE  The cases of mtp_sim (device classes plan, stage D12, ADR 0196): jgraph.usb.mtplist,
%   jgraph.usb.mtp, dir, download, upload, mkdir, deleteObject and capture on jgraph.internal.mtpsim's
%   devices. "JGraph Test Phone" (MTP) has "Internal storage" (DCIM/Camera with IMG_0001.jpg of 2048
%   bytes and IMG_0002.jpg of 4096, Documents/notes.txt of 34, an empty Music) and "SD card"
%   (readme.txt of 8); "JGraph Test Camera" (PTP) has "Memory card" with DCIM/100JGRPH/DSC_0001.JPG of
%   1024, and takes DSC_0002.JPG when asked. Byte i of a picture is its number plus i, modulo 256
%   (IMG_0001 is 1, DSC_0001 is 101). The answers are written from that rule. Answers text.
jgraph.internal.mtpsim('on');
phone = 'JGraph Test Phone';
camera = 'JGraph Test Camera';
work = tempname;
mkdir(work);
try
    switch kind
        case 'list'
            T = jgraph.usb.mtplist;
            out = sprintf('%s %d %d; %s; %s; %s; %s; %s', class(T), size(T), strjoin(T.Properties.VariableNames, ' '), strjoin(T.Name', ', '), ...
                strjoin(T.Manufacturer', ' '), strjoin(T.Description', ', '), class(T.DeviceID));
        case 'list_args'
            out = dv_err(@() jgraph.usb.mtplist(1));
        case 'open'
            m = jgraph.usb.mtp(phone);
            out = sprintf('%s; %s; %s; %s; %s; %s; %s; %s; %d %d', class(m), m.Name, m.Manufacturer, m.Model, m.SerialNumber, m.FirmwareVersion, ...
                m.Protocol, m.Type, isvalid(m), isa(m, 'handle'));
        case 'disp'
            m = jgraph.usb.mtp(2);
            out = ix_flat(evalc('disp(m)'));
        case 'properties'
            m = jgraph.usb.mtp(1);
            out = [strjoin(properties(m)', ' ') ' // ' strjoin(methods(m)', ' ')];
        case 'by_name'
            a = jgraph.usb.mtp('camera');
            b = jgraph.usb.mtp("JGRAPH TEST PHONE");
            d = jgraph.usb.mtp(2);
            T = jgraph.usb.mtplist;
            e = jgraph.usb.mtp(T(1, :));
            out = sprintf('%s; %s; %s; %s; %d', a.Name, b.Name, d.Name, e.Name, jgraph.internal.mtpsim('open'));
        case 'bad_device'
            T = jgraph.usb.mtplist;
            out = [dv_err(@() jgraph.usb.mtp) ' // ' dv_err(@() jgraph.usb.mtp('nope')) ' // ' dv_err(@() jgraph.usb.mtp('JGraph')) ...
                ' // ' dv_err(@() jgraph.usb.mtp(3)) ' // ' dv_err(@() jgraph.usb.mtp({1})) ' // ' dv_err(@() jgraph.usb.mtp(T)) ...
                ' // ' dv_err(@() jgraph.usb.mtp(1, 2)) ' // ' dv_err(@() jgraph.usb.mtp(''))];
        case 'storage'
            m = jgraph.usb.mtp(phone);
            S = m.Storage;
            out = sprintf('%s %d %d; %s; %s; %.0f %.0f; %.0f %.0f', class(S), size(S), strjoin(S.Properties.VariableNames, ' '), strjoin(S.Name', ', '), ...
                S.Capacity, S.Capacity - S.Free);
        case 'dir_root'
            m = jgraph.usb.mtp(phone);
            d = dir(m);
            e = dir(m, '/');
            out = [sprintf('%s %d %d; %s; %s, %s; %d %d; %d; ', class(d), size(d), strjoin(fieldnames(d)', ' '), d(1).name, d(2).name, ...
                d(1).isdir, d(2).isdir, d(1).bytes) '[' d(1).folder '] [' d(1).date ']' sprintf(' %d %d', isempty(d(1).datenum), isequal(d, e))];
        case 'dir_folder'
            m = jgraph.usb.mtp(phone);
            d = dir(m, 'Internal storage');
            p = dir(m, "Internal storage/DCIM/Camera");
            out = sprintf('%d %d: %s; %d %d %d // %d %d: %s; %d %d; %d %d; %s; %s; %s; %.6f', size(d), strjoin({d.name}, ' '), d.isdir, size(p), strjoin({p.name}, ' '), ...
                p.bytes, p.isdir, p(1).folder, p(1).date, p(2).date, p(1).datenum);
        case 'dir_file'
            % A file's path lists the one file, as dir does; names match in any case, and either slash parts them.
            m = jgraph.usb.mtp(phone);
            d = dir(m, 'internal STORAGE\documents/NOTES.TXT');
            e = dir(m, 'Internal storage/Music');
            out = sprintf('%d %d %s %d %d [%s] %s // %d %d %s', size(d), d.name, d.bytes, d.isdir, d.folder, d.date, size(e), class(e));
        case 'dir_print'
            m = jgraph.usb.mtp(phone);
            out = [ix_flat(evalc('dir(m)')) ' // ' ix_flat(evalc('dir(m, ''Internal storage'')')) ' // ' ix_flat(evalc('dir(m, ''SD card/readme.txt'')')) ...
                ' // [' ix_flat(evalc('m.dir(''Internal storage/Music'')')) ']'];
        case 'dir_bad'
            m = jgraph.usb.mtp(phone);
            out = [dv_err(@() dir(m, 'Nope')) ' // ' dv_err(@() dir(m, 'Internal storage/DCIM/Nope/x.jpg')) ' // ' dv_err(@() dir(m, 'SD card/readme.txt/x')) ...
                ' // ' dv_err(@() dir(m, 1)) ' // ' dv_err(@() dir(m, 'a', 'b'))];
        case 'download'
            m = jgraph.usb.mtp(phone);
            f = download(m, 'Internal storage/DCIM/Camera/IMG_0002.jpg', fullfile(work, 'two.bin'));
            b = mtp_bytes(f);
            g = download(m, 'SD card/readme.txt', work);
            [~, name, ext] = fileparts(g);
            out = sprintf('%s %d %d %d %d %d %d // %s%s %d [%s]', class(f), strcmp(f, fullfile(work, 'two.bin')), numel(b), b(1), b(2), b(255), b(4096), ...
                name, ext, strcmp(fileparts(g), work), sprintf('%d ', mtp_bytes(g)));
        case 'download_replace'
            % A download replaces a local file of that name, and leaves no part file behind.
            m = jgraph.usb.mtp(phone);
            f = fullfile(work, 'notes.txt');
            fid = fopen(f, 'w'); fwrite(fid, 1:100); fclose(fid);
            download(m, 'Internal storage/Documents/notes.txt', f);
            l = dir(work);
            out = sprintf('%d %s // %s', numel(mtp_bytes(f)), strtrim(char(mtp_bytes(f)')), strjoin({l(~[l.isdir]).name}, ' '));
        case 'download_bad'
            m = jgraph.usb.mtp(phone);
            out = [dv_err(@() download(m, 'Internal storage/DCIM')) ' // ' dv_err(@() download(m, 'SD card/nope.txt', work)) ' // ' dv_err(@() download(m)) ...
                ' // ' dv_err(@() download(m, 1)) ' // ' dv_err(@() download(m, 'SD card/readme.txt', fullfile(work, 'no', 'such', 'x.txt'))) ...
                sprintf(' // %d', numel(dir(work)) - 2)];
        case 'upload'
            m = jgraph.usb.mtp(phone);
            f = fullfile(work, 'data.bin');
            fid = fopen(f, 'w'); fwrite(fid, [9 8 7 6 5]); fclose(fid);
            p = upload(m, f, 'internal storage/music');
            d = dir(m, 'Internal storage/Music');
            g = download(m, p, fullfile(work, 'back.bin'));
            S = m.Storage;
            out = sprintf('%s %s; %s %d %d; [%s]; %.0f', class(p), p, d.name, d.bytes, d.isdir, sprintf('%d ', mtp_bytes(g)), S.Capacity(1) - S.Free(1));
        case 'upload_bad'
            m = jgraph.usb.mtp(phone);
            f = fullfile(work, 'README.TXT');
            fid = fopen(f, 'w'); fwrite(fid, 1); fclose(fid);
            out = [dv_err(@() upload(m, f, 'SD card')) ' // ' dv_err(@() upload(m, f, '/')) ' // ' dv_err(@() upload(m, f, 'SD card/readme.txt')) ...
                ' // ' dv_err(@() upload(m, f, 'SD card/nope')) ' // ' dv_err(@() upload(m, fullfile(work, 'absent.bin'), 'SD card')) ...
                ' // ' dv_err(@() upload(m, f)) ' // ' dv_err(@() upload(m, 1, 'SD card'))];
        case 'mkdir'
            m = jgraph.usb.mtp(phone);
            mkdir(m, 'SD card/a/b/c');
            mkdir(m, 'sd card\A\b');
            mkdir(m, 'SD card/a/d');
            x = dir(m, 'SD card');
            y = dir(m, 'SD card/a');
            z = dir(m, 'SD card/a/b');
            out = sprintf('%s; %s; %s %d', strjoin({x.name}, ' '), strjoin({y.name}, ' '), z.name, z.isdir);
        case 'mkdir_bad'
            m = jgraph.usb.mtp(phone);
            out = [dv_err(@() mkdir(m, 'New storage')) ' // ' dv_err(@() mkdir(m, 'Nope/a')) ' // ' dv_err(@() mkdir(m, 'SD card/readme.txt/a')) ...
                ' // ' dv_err(@() mkdir(m)) ' // ' dv_err(@() mkdir(m, 1)) ' // ' mtp_out(@() mkdir(m, 'SD card/q'))];
        case 'delete'
            m = jgraph.usb.mtp(phone);
            mkdir(m, 'SD card/a/b');
            f = fullfile(work, 'x.bin');
            fid = fopen(f, 'w'); fwrite(fid, [1 2 3]); fclose(fid);
            upload(m, f, 'SD card/a/b');
            kept = dv_err(@() deleteObject(m, 'SD card/a'));
            deleteObject(m, 'SD card/a/b/x.bin');
            deleteObject(m, 'sd card/A/B');
            mkdir(m, 'SD card/a/c');
            deleteObject(m, 'SD card/a', Recursive=true);
            deleteObject(m, 'SD card/readme.txt', 'Recursive', false);
            d = dir(m, 'SD card');
            out = sprintf('%s // %d %d %s', kept, size(d), dv_err(@() dir(m, 'SD card/a')));
        case 'delete_bad'
            m = jgraph.usb.mtp(phone);
            out = [dv_err(@() deleteObject(m, 'SD card')) ' // ' dv_err(@() deleteObject(m, '')) ' // ' dv_err(@() deleteObject(m, 'SD card/nope')) ...
                ' // ' dv_err(@() deleteObject(m)) ' // ' dv_err(@() deleteObject(m, 'SD card/readme.txt', 'Force', true)) ...
                ' // ' dv_err(@() deleteObject(m, 'SD card/readme.txt', 'Recursive', 'yes')) ' // ' mtp_out(@() deleteObject(m, 'SD card/readme.txt'))];
        case 'capture'
            m = jgraph.usb.mtp(camera);
            capture(m);
            m.capture();
            d = dir(m, 'Memory card/DCIM/100JGRPH');
            f = download(m, 'Memory card/DCIM/100JGRPH/DSC_0003.JPG', work);
            b = mtp_bytes(f);
            p = jgraph.usb.mtp(phone);
            out = sprintf('%s; %d %d %d // %s // %s // %s', strjoin({d.name}, ' '), numel(b), b(1), b(2), dv_err(@() capture(p)), dv_err(@() capture(m, 1)), mtp_out(@() capture(m)));
        case 'unplug'
            m = jgraph.usb.mtp(phone);
            jgraph.internal.mtpsim('unplug', phone);
            T = jgraph.usb.mtplist;
            lost = dv_err(@() dir(m));
            store = dv_err(@() m.Storage);
            name = m.Name;
            gone = dv_err(@() jgraph.usb.mtp(phone));
            only = jgraph.usb.mtp;
            jgraph.internal.mtpsim('unplug', camera);
            none = dv_err(@() jgraph.usb.mtp);
            L = jgraph.usb.mtplist;
            out = sprintf('%d %s // %s // %s // %s // %s // %s // %s // %d %d', height(T), T.Name(1), lost, store, name, gone, only.Name, none, size(L));
        case 'clear'
            m = jgraph.usb.mtp(phone);
            other = jgraph.usb.mtp(camera);
            n2 = jgraph.internal.mtpsim('open');
            clear m
            n1 = jgraph.internal.mtpsim('open');
            delete(other);
            out = [sprintf('%d %d %d %d // ', n2, n1, jgraph.internal.mtpsim('open'), isvalid(other)) dv_err(@() dir(other)) ' // ' ix_flat(evalc('disp(other)'))];
        case 'set_get'
            m = jgraph.usb.mtp(phone);
            m.UserData = 'mine';
            out = sprintf('%s %s // %s // %s', m.UserData, get(m, 'MODEL'), mtp_set(m, 'Name', 'x'), dv_err(@() m.NoSuch));
        otherwise
            error('mtp_sim_case:unknown', 'unknown case %s', kind);
    end
catch e
    out = ['ERR ' e.identifier ' ' e.message];
end
clear m a b d e p other only
jgraph.internal.mtpsim('off');
rmdir(work, 's');
out = strrep(out, work, '<work>');
out = strrep(out, '|', '/'); % a fixture value holds no |
end

function b = mtp_bytes(file)
% A local file's bytes, as a column of doubles.
fid = fopen(file, 'r');
b = fread(fid);
fclose(fid);
end

function out = mtp_set(m, name, value)
% m.(name) = value, answering 'ok' or the refusal.
try
    m.(name) = value;
    out = 'ok';
catch e
    out = [e.identifier ' ## ' ix_flat(e.message)];
end
end

function out = mtp_out(f)
% One output asked of a method that has none.
try
    x = f(); %#ok<NASGU>
    out = 'none';
catch e
    out = [e.identifier ' ## ' ix_flat(e.message)];
end
end
