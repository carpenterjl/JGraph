function cross_write(outdir)
% Writes encrypted archives with whichever engine runs this, for the other engine to read.
if exist(outdir, 'dir'), rmdir(outdir, 's'); end
mkdir(outdir);
old = cd(outdir);
c = onCleanup(@() cd(old));
mkdir('src'); mkdir('src/sub'); mkdir('src/emptydir');
fid = fopen('src/a.txt', 'w'); fprintf(fid, '%s\n', repmat('hello world ', 1, 50)); fclose(fid);
fid = fopen('src/sub/c.txt', 'w'); fprintf(fid, 'small'); fclose(fid);
fid = fopen('src/zero.txt', 'w'); fclose(fid);
fid = fopen('src/b.bin', 'w'); fwrite(fid, uint8(mod((1:3000) * 7919, 251))); fclose(fid);
methods = {'ZipCrypto', 'AES-128', 'AES-256'};
for k = 1:numel(methods)
    z = zip(sprintf('x_%s.zip', strrep(methods{k}, '-', '')), {'src'}, 'Password', ['pw ' char(233)], 'EncryptionMethod', methods{k});
    fprintf('wrote %s | %s\n', methods{k}, strjoin(z, ' '));
end
end
