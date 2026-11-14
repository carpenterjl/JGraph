function probe_zip(outdir)
% Probes R2025b's zip/unzip encryption: archives written for each method, and the refusals.
if nargin < 1, outdir = fullfile(pwd, 'out_matlab'); end
if exist(outdir, 'dir'), rmdir(outdir, 's'); end
mkdir(outdir);
old = cd(outdir);
cleanup = onCleanup(@() cd(old));
mkdir('src'); mkdir('src/sub'); mkdir('src/emptydir');
fid = fopen('src/a.txt', 'w'); fprintf(fid, '%s\n', repmat('hello world ', 1, 50)); fclose(fid);
fid = fopen('src/sub/c.txt', 'w'); fprintf(fid, 'small'); fclose(fid);
fid = fopen('src/zero.txt', 'w'); fclose(fid);
rng(1); fid = fopen('src/b.bin', 'w'); fwrite(fid, uint8(randi(255, 1, 3000))); fclose(fid);

methods = {'ZipCrypto', 'AES-128', 'AES-256', 'aes128', 'aes256', 'zipcrypto'};
for k = 1:numel(methods)
    m = methods{k};
    f = sprintf('enc_%s.zip', strrep(m, '-', ''));
    run(sprintf('zip_%s', m), @() zip(f, {'src'}, 'Password', 'pw123', 'EncryptionMethod', m));
end
run('zip_string_args', @() zip("enc_str.zip", "src/a.txt", "Password", "pw", "EncryptionMethod", "AES-256"));
run('zip_root_and_pw', @() zip('enc_root.zip', {'a.txt', 'sub'}, 'src', 'Password', 'pw', 'EncryptionMethod', 'ZipCrypto'));
run('zip_unicode_pw', @() zip('enc_uni.zip', 'src/a.txt', 'Password', ['p' char(233) 'w'], 'EncryptionMethod', 'AES-128'));
run('zip_unicode_pw_zc', @() zip('enc_unizc.zip', 'src/a.txt', 'Password', ['p' char(233) 'w'], 'EncryptionMethod', 'ZipCrypto'));
run('zip_long_pw', @() zip('enc_long.zip', 'src/a.txt', 'Password', repmat('x', 1, 200), 'EncryptionMethod', 'AES-256'));
run('zip_space_pw', @() zip('enc_space.zip', 'src/a.txt', 'Password', ' ', 'EncryptionMethod', 'ZipCrypto'));
zip('plain.zip', {'src'});

% Reading them back.
for k = 1:numel(methods)
    m = methods{k};
    f = sprintf('enc_%s.zip', strrep(m, '-', ''));
    d = sprintf('x_%s', strrep(m, '-', ''));
    run(sprintf('unzip_%s', m), @() unzip(f, d, 'Password', 'pw123'));
    if exist(fullfile(d, 'src', 'a.txt'), 'file')
        fprintf('content_%s | %d %d %d\n', m, isequal(fileread(fullfile(d, 'src', 'a.txt')), fileread('src/a.txt')), ...
            isequal(readb(fullfile(d, 'src', 'b.bin')), readb('src/b.bin')), exist(fullfile(d, 'src', 'emptydir'), 'dir'));
    end
end
run('unzip_wrong_zc', @() unzip('enc_ZipCrypto.zip', 'w1', 'Password', 'nope'));
run('unzip_wrong_aes128', @() unzip('enc_AES128.zip', 'w2', 'Password', 'nope'));
run('unzip_wrong_aes256', @() unzip('enc_AES256.zip', 'w3', 'Password', 'nope'));
listing('w1'); listing('w2'); listing('w3');
run('unzip_nopw_zc', @() unzip('enc_ZipCrypto.zip', 'w4'));
run('unzip_nopw_aes', @() unzip('enc_AES256.zip', 'w5'));
listing('w4'); listing('w5');
run('unzip_pw_on_plain', @() unzip('plain.zip', 'w6', 'Password', 'pw'));
run('unzip_uni', @() unzip('enc_uni.zip', 'w7', 'Password', ['p' char(233) 'w']));
run('unzip_unizc', @() unzip('enc_unizc.zip', 'w8', 'Password', ['p' char(233) 'w']));
run('unzip_long', @() unzip('enc_long.zip', 'w9', 'Password', repmat('x', 1, 200)));
run('unzip_space', @() unzip('enc_space.zip', 'w10', 'Password', ' '));
run('unzip_case_pw', @() unzip('enc_AES256.zip', 'w11', 'Password', 'PW123'));
run('unzip_str_pw', @() unzip("enc_str.zip", "w12", "Password", "pw"));
run('unzip_nameonly', @() unzip('enc_ZipCrypto.zip', 'Password', 'pw123'));
run('unzip_root', @() unzip('enc_root.zip', 'w13', 'Password', 'pw'));
if exist('mixed.zip', 'file')
    run('unzip_mixed_pw', @() unzip('mixed.zip', 'w14', 'Password', 'pw'));
    run('unzip_mixed_nopw', @() unzip('mixed.zip', 'w15'));
    listing('w15');
end
end

function b = readb(f)
fid = fopen(f, 'r'); b = fread(fid, inf, 'uint8=>uint8'); fclose(fid);
end

function listing(d)
if ~exist(d, 'dir'), fprintf('listing %s | none\n', d); return; end
l = dir(fullfile(d, '**', '*'));
l = l(~[l.isdir]);
fprintf('listing %s | %s\n', d, strjoin(sort(cellfun(@(f, n) strrep(fullfile(erase(f, [pwd filesep d]), n), '\', '/'), {l.folder}, {l.name}, 'UniformOutput', false)), ' '));
end

function run(key, f)
try
    r = f();
    if iscell(r), r = strjoin(strrep(r, '\', '/'), ' '); end
    fprintf('%s | ok | %s\n', key, r);
catch e
    fprintf('%s | ERR | %s | %s\n', key, e.identifier, strrep(e.message, newline, '\n'));
end
end
