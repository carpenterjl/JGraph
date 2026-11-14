function cross_read(indir, srcdir)
% Reads the archives the other engine wrote (cross_write), and holds every file against the source.
old = cd(indir);
c = onCleanup(@() cd(old));
methods = {'ZipCrypto', 'AES-128', 'AES-256'};
for k = 1:numel(methods)
    f = sprintf('x_%s.zip', strrep(methods{k}, '-', ''));
    out = ['read_' strrep(methods{k}, '-', '')];
    if exist(out, 'dir'), rmdir(out, 's'); end
    try
        u = unzip(f, out, 'Password', ['pw ' char(233)]);
        same = true;
        for name = {'a.txt', 'b.bin', 'zero.txt', 'sub/c.txt'}
            same = same && isequal(readb(fullfile(out, 'src', name{1})), readb(fullfile(srcdir, 'src', name{1})));
        end
        fprintf('read %s | %d entries | same %d | emptydir %d\n', methods{k}, numel(u), same, isfolder(fullfile(out, 'src', 'emptydir')));
    catch e
        fprintf('read %s | ERR %s | %s\n', methods{k}, e.identifier, strrep(e.message, newline, ' '));
    end
    try
        unzip(f, [out '_wrong'], 'Password', 'nope');
        fprintf('wrong %s | accepted\n', methods{k});
    catch e
        fprintf('wrong %s | %s\n', methods{k}, e.identifier);
    end
end
end

function b = readb(f)
fid = fopen(f, 'r'); b = fread(fid, inf, 'uint8=>uint8'); fclose(fid);
end
