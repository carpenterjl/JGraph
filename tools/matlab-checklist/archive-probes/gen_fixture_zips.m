function gen_fixture_zips(outdir)
% Writes the small archives the fixture embeds, with R2025b's zip: AES-256 of a short file (AE-2),
% AES-128 of a longer one (AE-1), and the two halves of a mixed archive.
if exist(outdir, 'dir'), rmdir(outdir, 's'); end
mkdir(outdir);
old = cd(outdir);
c = onCleanup(@() cd(old));
w('a.txt', 'alpha');
w('long.txt', 'alpha beta gamma delta epsilon');
w('b.txt', 'beta');
zip('aes256.zip', 'a.txt', 'Password', 'p', 'EncryptionMethod', 'AES-256');
zip('aes128.zip', 'long.txt', 'Password', 'p', 'EncryptionMethod', 'AES-128');
zip('half_plain.zip', 'b.txt');
zip('half_enc.zip', 'a.txt', 'Password', 'p', 'EncryptionMethod', 'ZipCrypto');
end

function w(name, text)
fid = fopen(name, 'w'); fwrite(fid, text); fclose(fid);
end
