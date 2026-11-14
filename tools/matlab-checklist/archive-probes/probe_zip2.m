function probe_zip2(outdir)
% Mixed archives, and the AE version against the file size.
old = cd(outdir);
cleanup = onCleanup(@() cd(old));
run('mixed_zc_pw', @() unzip('mixed_zc.zip', 'm1', 'Password', 'pw')); listing('m1');
run('mixed_zc_nopw', @() unzip('mixed_zc.zip', 'm2')); listing('m2');
run('mixed_zc_wrong', @() unzip('mixed_zc.zip', 'm3', 'Password', 'bad')); listing('m3');
run('mixed_aes_pw', @() unzip('mixed_aes.zip', 'm4', 'Password', 'pw')); listing('m4');
run('mixed_aes_wrong', @() unzip('mixed_aes.zip', 'm5', 'Password', 'bad')); listing('m5');
run('mixed_twopw_pw', @() unzip('mixed_twopw.zip', 'm6', 'Password', 'pw')); listing('m6');
run('mixed_encfirst_nopw', @() unzip('mixed_encfirst.zip', 'm7')); listing('m7');
run('mixed_encfirst_wrong', @() unzip('mixed_encfirst.zip', 'm8', 'Password', 'bad')); listing('m8');
% the output folder: made before the refusal?
run('wrong_newfolder', @() unzip('enc_ZipCrypto.zip', 'fresh/deeper', 'Password', 'bad'));
fprintf('fresh exists | %d\n', exist('fresh/deeper', 'dir'));
% sizes around WinZip's 20-byte line
mkdir('sz');
for n = [0 1 19 20 21 100]
    fid = fopen(sprintf('sz/f%03d.txt', n), 'w'); fwrite(fid, repmat('a', 1, n)); fclose(fid);
end
zip('sizes_aes.zip', 'sz', 'Password', 'pw', 'EncryptionMethod', 'AES-256');
zip('sizes_zc.zip', 'sz', 'Password', 'pw', 'EncryptionMethod', 'ZipCrypto');
fprintf('sizes written\n');
% incompressible tiny and large: is anything stored rather than deflated?
fid = fopen('sz/rand.bin', 'w'); rng(2); fwrite(fid, uint8(randi(255, 1, 64))); fclose(fid);
zip('rand_aes.zip', 'sz/rand.bin', 'Password', 'pw', 'EncryptionMethod', 'AES-128');
zip('rand_plain.zip', 'sz/rand.bin');
fprintf('rand written\n');
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
