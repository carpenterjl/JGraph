% record: -noFigureWindows
% Open items 171 and 172: gzip, gunzip, tar and untar beside zip and unzip - the names each answers
% for files, folders and wildcards, beside the source or in an output folder, the extensions tar
% adds or compresses by, what untar finds and writes, round trips, and the refusals; then how zip and
% unzip split a positional argument from a name, and the encryption refusals R2025b gives itself.

root = tempname;
src = fullfile(root, 'src');
mkdir(fullfile(src, 'sub', 'deep'));
mkdir(fullfile(src, 'empty'));
writet(fullfile(src, 'a.txt'), 'alpha');
writet(fullfile(src, 'b.txt'), 'beta');
writet(fullfile(src, 'c.m'), 'code');
writet(fullfile(src, 'noext'), 'plain');
writet(fullfile(src, 'sub', 's.txt'), 'inner');
writet(fullfile(src, 'sub', 'deep', 'd.txt'), 'deeper');
mkdir(fullfile(root, 'twin'));
writet(fullfile(root, 'twin', 'a.txt'), 'twin');
mkdir(fullfile(root, 'dir.tar'));
mkdir(fullfile(root, 'gz'));
old = cd(src);

% ---- gzip ----
u2_chk('gz_one', @() names(gzip('a.txt'), root));
u2_chk('gz_one_made', @() isfile('a.txt.gz'));
u2_chk('gz_class', @() sz(gzip('a.txt')));
u2_chk('gz_out', @() names(gzip('a.txt', fullfile(root, 'gz')), root));
u2_chk('gz_out_relative', @() names(gzip('b.txt', 'gzrel'), root));
u2_chk('gz_cell', @() names(gzip({'a.txt', 'c.m'}, fullfile(root, 'gz')), root));
u2_chk('gz_string', @() names(gzip(["a.txt", "b.txt"], string(fullfile(root, 'gz'))), root));
u2_chk('gz_wild', @() names(gzip('*.txt', fullfile(root, 'gz')), root));
u2_chk('gz_folder', @() names(gzip('sub', fullfile(root, 'gzf')), root));
u2_chk('gz_folder_beside', @() names(gzip(fullfile(root, 'twin')), root));
u2_chk('gz_relative_beside', @() names(gzip('sub/s.txt'), root));
u2_chk('gz_absolute', @() names(gzip(fullfile(src, 'c.m'), fullfile(root, 'gz')), root));
u2_chk('gz_noext', @() names(gzip('noext', fullfile(root, 'gz')), root));
u2_chk('gz_of_gz', @() names(gzip('a.txt.gz', fullfile(root, 'gz')), root));
u2_chk('gz_new_nested', @() names(gzip('a.txt', fullfile(root, 'g1', 'g2')), root));
u2_chk('gz_dot_out', @() names(gzip('c.m', '.'), root));
u2_chk('gz_empty_out', @() names(gzip('b.txt', ''), root));
u2_chk('gz_duplicate', @() names(gzip({'a.txt', 'a.txt'}, fullfile(root, 'gz')), root));
u2_chk('gz_overwrite', @() names(gzip('a.txt'), root));
u2_chk('gz_no_output', @() quiet('gzip(''c.m'')'));
u2_chk('gz_empty_folder', @() failure(@() gzip('empty', fullfile(root, 'gz')), root));
u2_chk('gz_missing', @() failure(@() gzip('nosuch.txt', fullfile(root, 'gz')), root));
u2_chk('gz_missing_cleanup', @() missingCleanup(root));
u2_chk('gz_wild_none', @() failure(@() gzip('*.zzz', fullfile(root, 'gz')), root));
u2_chk('gz_number', @() failure(@() gzip(5), root));
u2_chk('gz_cell_number', @() failure(@() gzip({'a.txt', 5}), root));
u2_chk('gz_out_number', @() failure(@() gzip('a.txt', 5), root));
u2_chk('gz_out_file', @() failure(@() gzip('a.txt', 'b.txt'), root));
u2_chk('gz_empty_name', @() failure(@() gzip(''), root));
u2_chk('gz_empty_cell', @() failure(@() gzip({}), root));
u2_chk('gz_wild_path', @() failure(@() gzip('s*/s.txt', fullfile(root, 'gz')), root));
u2_chk('gz_no_args', @() gzip());
u2_chk('gz_three_args', @() gzip('a.txt', 'x', 'y'));
u2_chk('gz_two_outputs', @() twoOut(@() gzip('a.txt')));

% ---- gunzip ----
u2_chk('gun_one', @() names(gunzip(fullfile(root, 'gz', 'a.txt.gz'), fullfile(root, 'gun1')), root));
u2_chk('gun_content', @() fileread(fullfile(root, 'gun1', 'a.txt')));
u2_chk('gun_class', @() sz(gunzip(fullfile(root, 'gz', 'a.txt.gz'), fullfile(root, 'gun1'))));
u2_chk('gun_beside', @() gunBeside(root));
u2_chk('gun_relative_beside', @() names(gunzip('sub/s.txt.gz'), root));
u2_chk('gun_overwrite', @() [names(gunzip('a.txt.gz'), root) ' ; ' fileread('a.txt')]);
u2_chk('gun_folder', @() names(gunzip(fullfile(root, 'gzf'), fullfile(root, 'gun3')), root));
u2_chk('gun_wild', @() names(gunzip(fullfile(root, 'gz', '*.gz'), fullfile(root, 'gun4')), root));
u2_chk('gun_cell', @() names(gunzip({fullfile(root, 'gz', 'a.txt.gz'), fullfile(root, 'gz', 'b.txt.gz')}, fullfile(root, 'gun5')), root));
u2_chk('gun_gz_of_gz', @() names(gunzip(fullfile(root, 'gz', 'a.txt.gz.gz'), fullfile(root, 'gun6')), root));
u2_chk('gun_noext', @() names(gunzip(fullfile(root, 'gz', 'noext.gz'), fullfile(root, 'gun7')), root));
u2_chk('gun_renamed', @() gunRenamed(root));
u2_chk('gun_upper_ext', @() gunUpper(root));
u2_chk('gun_no_output', @() quiet('gunzip(''c.m.gz'')'));
u2_chk('gun_roundtrip', @() gzRoundTrip(root));
u2_chk('gun_without_gz', @() failure(@() gunzip(fullfile(root, 'gz', 'c.m'), fullfile(root, 'gun2')), root));
u2_chk('gun_folder_beside', @() failure(@() gunzip('sub'), root));
u2_chk('gun_not_gz', @() failure(@() gunzip('b.txt', fullfile(root, 'gun8')), root));
u2_chk('gun_not_gz_cleanup', @() isfolder(fullfile(root, 'gun8')));
u2_chk('gun_missing', @() failure(@() gunzip(fullfile(root, 'nosuch.gz'), fullfile(root, 'gun9')), root));
u2_chk('gun_missing_cleanup', @() isfolder(fullfile(root, 'gun9')));
u2_chk('gun_same_beside', @() gunSame(root, ''));
u2_chk('gun_same_dot', @() gunSame(root, '.'));
u2_chk('gun_empty_name', @() failure(@() gunzip(''), root));
u2_chk('gun_number', @() failure(@() gunzip(5), root));
u2_chk('gun_out_number', @() failure(@() gunzip(fullfile(root, 'gz', 'a.txt.gz'), 5), root));
u2_chk('gun_no_args', @() gunzip());
u2_chk('gun_three_args', @() gunzip('a', 'b', 'c'));
u2_chk('gun_two_outputs', @() twoOut(@() gunzip(fullfile(root, 'gz', 'a.txt.gz'), fullfile(root, 'gun1'))));

% ---- tar ----
leftovers = {'a.txt.gz', 'b.txt.gz', 'c.m.gz', fullfile('sub', 's.txt.gz')};
for k = 1:numel(leftovers)
    if isfile(leftovers{k})
        delete(leftovers{k});
    end
end
rmdir(fullfile(src, 'gzrel'), 's');
u2_chk('tar_one', @() names(tar(fullfile(root, 'one.tar'), 'a.txt'), root));
u2_chk('tar_class', @() sz(tar(fullfile(root, 'one.tar'), 'a.txt')));
u2_chk('tar_adds_ext', @() addsExt(root));
u2_chk('tar_cell', @() names(tar(fullfile(root, 'two.tar'), {'b.txt', 'a.txt'}), root));
u2_chk('tar_string', @() names(tar(string(fullfile(root, 's.tar')), ["a.txt", "c.m"]), root));
u2_chk('tar_wild', @() names(tar(fullfile(root, 'w.tar'), '*.txt'), root));
u2_chk('tar_folder', @() names(tar(fullfile(root, 'f.tar'), 'sub'), root));
u2_chk('tar_empty_folder', @() names(tar(fullfile(root, 'e.tar'), 'empty'), root));
u2_chk('tar_dot', @() names(tar(fullfile(root, 'dot.tar'), '.'), root));
u2_chk('tar_root', @() names(tar(fullfile(root, 'r.tar'), {'s.txt', 'deep'}, fullfile(src, 'sub')), root));
u2_chk('tar_absolute', @() names(tar(fullfile(root, 'p.tar'), fullfile(src, 'a.txt')), root));
u2_chk('tar_dotdot', @() names(tar(fullfile(root, 'dd.tar'), '../twin/a.txt'), root));
u2_chk('tar_tgz', @() names(tar(fullfile(root, 'z.tgz'), {'a.txt', 'sub'}), root));
u2_chk('tar_targz', @() names(tar(fullfile(root, 'z2.tar.gz'), 'a.txt'), root));
u2_chk('tar_gz', @() names(tar(fullfile(root, 'z3.gz'), 'a.txt'), root));
u2_chk('tar_TGZ', @() names(tar(fullfile(root, 'z4.TGZ'), 'a.txt'), root));
u2_chk('tar_made', @() made(root));
u2_chk('tar_bytes', @() tarBytes(root));
u2_chk('tar_no_output', @() quiet('tar(''q.tar'', ''a.txt'')'));
u2_chk('tar_self', @() tarSelf(root, src));
u2_chk('tar_duplicate_names', @() failure(@() tar(fullfile(root, 'dn.tar'), {fullfile(src, 'a.txt'), fullfile(root, 'twin', 'a.txt')}), root));
u2_chk('tar_is_dir', @() failure(@() tar(fullfile(root, 'dir.tar'), 'a.txt'), root));
u2_chk('tar_is_dir_noext', @() failure(@() tar(fullfile(root, 'dir'), 'a.txt'), root));
u2_chk('tar_empty_name', @() failure(@() tar('', 'a.txt'), root));
u2_chk('tar_missing', @() failure(@() tar(fullfile(root, 'm.tar'), 'nosuch.txt'), root));
u2_chk('tar_wild_none', @() failure(@() tar(fullfile(root, 'm.tar'), '*.zzz'), root));
u2_chk('tar_empty_cell', @() failure(@() tar(fullfile(root, 'm.tar'), {}), root));
u2_chk('tar_number_name', @() failure(@() tar(5, 'a.txt'), root));
u2_chk('tar_number_files', @() failure(@() tar(fullfile(root, 'm.tar'), 5), root));
u2_chk('tar_bad_root', @() failure(@() tar(fullfile(root, 'm.tar'), 'a.txt', fullfile(root, 'nosuchdir')), root));
u2_chk('tar_root_number', @() failure(@() tar(fullfile(root, 'm.tar'), 'a.txt', 5), root));
u2_chk('tar_one_arg', @() tar(fullfile(root, 'm.tar')));
u2_chk('tar_four_args', @() tar(fullfile(root, 'm.tar'), 'a.txt', src, 'x'));
u2_chk('tar_two_outputs', @() twoOut(@() tar(fullfile(root, 'm.tar'), 'a.txt')));

% ---- untar ----
u2_chk('untar_names', @() names(untar(fullfile(root, 'f.tar'), fullfile(root, 'u1')), root));
u2_chk('untar_class', @() sz(untar(fullfile(root, 'f.tar'), fullfile(root, 'u1'))));
u2_chk('untar_content', @() fileread(fullfile(root, 'u1', 'sub', 'deep', 'd.txt')));
u2_chk('untar_dot', @() names(untar(fullfile(root, 'dot.tar'), fullfile(root, 'u2')), root));
u2_chk('untar_empty_folder', @() untarEmpty(root));
u2_chk('untar_tgz', @() names(untar(fullfile(root, 'z.tgz'), fullfile(root, 'u3')), root));
u2_chk('untar_targz', @() names(untar(fullfile(root, 'z2.tar.gz'), fullfile(root, 'u4')), root));
u2_chk('untar_gz', @() names(untar(fullfile(root, 'z3.gz'), fullfile(root, 'u5')), root));
u2_chk('untar_noext', @() names(untar(fullfile(root, 'one'), fullfile(root, 'u6')), root));
u2_chk('untar_noext_tgz', @() names(untar(fullfile(root, 'z'), fullfile(root, 'u7')), root));
u2_chk('untar_ext_order', @() untarOrder(root));
u2_chk('untar_here', @() untarHere(root));
u2_chk('untar_relative_out', @() names(untar(fullfile(root, 'one.tar'), 'urel'), root));
u2_chk('untar_empty_out', @() names(untar(fullfile(root, 'one.tar'), ''), root));
u2_chk('untar_nested_out', @() names(untar(fullfile(root, 'one.tar'), fullfile(root, 'n1', 'n2')), root));
u2_chk('untar_string', @() names(untar(string(fullfile(root, 'one.tar')), string(fullfile(root, 'u8'))), root));
u2_chk('untar_overwrite', @() untarOverwrite(root));
u2_chk('untar_roundtrip', @() tarRoundTrip(root));
u2_chk('untar_no_output', @() quiet('untar(fullfile(''..'', ''one.tar''), ''uq'')'));
% R2025b writes the ../ entry above the output folder; JGraph refuses the archive, as unzip (ADR 0258).
chkdiv('untar_dotdot', @() untarDotDot(root), 'ADR0340');
u2_chk('untar_missing', @() failure(@() untar(fullfile(root, 'nosuch.tar')), root));
u2_chk('untar_not_tar', @() failure(@() untar('b.txt', fullfile(root, 'u9')), root));
u2_chk('untar_not_tgz', @() failure(@() untar(fullfile(root, 'gz', 'a.txt.gz'), fullfile(root, 'u10')), root));
u2_chk('untar_zip', @() failure(@() untarZip(root), root));
u2_chk('untar_folder', @() failure(@() untar(fullfile(root, 'twin')), root));
u2_chk('untar_empty_name', @() failure(@() untar(''), root));
u2_chk('untar_number', @() failure(@() untar(5), root));
u2_chk('untar_out_number', @() failure(@() untar(fullfile(root, 'one.tar'), 5), root));
u2_chk('untar_no_args', @() untar());
u2_chk('untar_three_args', @() untar('a', 'b', 'c'));
u2_chk('untar_two_outputs', @() twoOut(@() untar(fullfile(root, 'one.tar'), fullfile(root, 'u1'))));
% R2025b reads a file URL; JGraph downloads nothing (ADR 0258).
chkdiv('untar_file_url', @() failure(@() untar(['file:///' strrep(fullfile(root, 'one.tar'), '\', '/')], fullfile(root, 'u11')), root), 'ADR0340');

% ---- zip and unzip: names and values (open item 172) ----
u2_chk('zip_password_only', @() failure(@() zip('z1.zip', 'a.txt', 'Password', 'p'), root));
u2_chk('zip_method_only', @() failure(@() zip('z2.zip', 'a.txt', 'EncryptionMethod', 'AES-256'), root));
u2_chk('zip_method_bad', @() failure(@() zip('z4.zip', 'a.txt', 'Password', 'p', 'EncryptionMethod', 'bogus'), root));
u2_chk('zip_root_password', @() failure(@() zip('z5.zip', 'a.txt', '.', 'Password', 'p'), root));
u2_chk('zip_password_empty', @() failure(@() zip('z7.zip', 'a.txt', 'Password', ''), root));
u2_chk('zip_password_number', @() failure(@() zip('z8.zip', 'a.txt', 'Password', 5), root));
u2_chk('zip_method_number', @() failure(@() zip('z8.zip', 'a.txt', 'EncryptionMethod', 5), root));
u2_chk('zip_password_as_root', @() failure(@() zip('z9.zip', 'a.txt', 'Password'), root));
u2_chk('zip_password_dangling', @() failure(@() zip('z10.zip', 'a.txt', '.', 'Password'), root));
u2_chk('zip_password_partial', @() failure(@() zip('z11.zip', 'a.txt', 'Pass', 'p'), root));
u2_chk('zip_bad_name', @() failure(@() zip('z12.zip', 'a.txt', '.', 'Bogus', 'p'), root));
% R2025b encrypts; .NET's ZIP classes cannot, and JGraph refuses (ADR 0258).
chkdiv('zip_encrypt', @() failure(@() zip('z3.zip', 'a.txt', 'Password', 'p', 'EncryptionMethod', 'zipcrypto'), root), 'ADR0258');
chkdiv('zip_encrypt_lower_names', @() failure(@() zip('z6.zip', 'a.txt', 'password', 'p', 'encryptionmethod', 'AES128'), root), 'ADR0258');
writeEncrypted(fullfile(root, 'enc.zip'));
u2_chk('unzip_encrypted_no_password', @() failure(@() unzip(fullfile(root, 'enc.zip'), fullfile(root, 'unp1')), root));
u2_chk('unzip_password_empty', @() failure(@() unzip(fullfile(root, 'enc.zip'), fullfile(root, 'unp2'), 'Password', ''), root));
u2_chk('unzip_password_number', @() failure(@() unzip(fullfile(root, 'enc.zip'), fullfile(root, 'unp2'), 'Password', 5), root));
u2_chk('unzip_bad_name', @() failure(@() unzip(fullfile(root, 'enc.zip'), fullfile(root, 'unp2'), 'Bogus', 'x'), root));
% R2025b decrypts (or refuses a wrong password); JGraph decrypts nothing (ADR 0258).
chkdiv('unzip_encrypted_password', @() failure(@() unzip(fullfile(root, 'enc.zip'), fullfile(root, 'unp3'), 'Password', 'p'), root), 'ADR0258');
chkdiv('unzip_password_as_name', @() failure(@() unzip(fullfile(root, 'enc.zip'), 'Password', 'x'), root), 'ADR0258');
chkdiv('unzip_file_url', @() failure(@() unzipUrl(root), root), 'ADR0258');

cd(old);
rmdir(root, 's');

function s = names(c, root)
s = strjoin(strrep(c, [root filesep], ''), ' | ');
end

function s = sz(c)
s = sprintf('%s %s', class(c), mat2str(size(c)));
end

function writet(name, text)
fid = fopen(name, 'w');
fprintf(fid, '%s', text);
fclose(fid);
end

function v = twoOut(fn)
[a, b] = fn(); %#ok<ASGLU>
v = 1;
end

function s = quiet(cmd)
s = ['[' evalc(cmd) ']'];
end

function s = failure(fn, root)
try
    s = fn();
    if iscell(s)
        s = names(s, root);
    elseif ~ischar(s)
        s = class(s);
    end
    s = ['ok: ' strrep(s, root, '<root>')];
catch err
    % A URL spells the root with forward slashes.
    s = [err.identifier ' | ' strrep(strrep(err.message, root, '<root>'), strrep(root, '\', '/'), '<root>')];
end
end

function s = missingCleanup(root)
try
    gzip('nosuch.txt', fullfile(root, 'gzx', 'deeper'));
catch
end
s = sprintf('%d %d', isfolder(fullfile(root, 'gzx')), isfolder(fullfile(root, 'gzx', 'deeper')));
end

function s = gunBeside(root)
d = fullfile(root, 'gip');
mkdir(d);
copyfile(fullfile(root, 'gz', 'b.txt.gz'), d);
old = cd(d);
f = gunzip('b.txt.gz');
cd(old);
s = [names(f, root) ' ; ' fileread(fullfile(d, 'b.txt'))];
end

function s = gunRenamed(root)
copyfile(fullfile(root, 'gz', 'a.txt.gz'), fullfile(root, 'renamed.dat'));
s = names(gunzip(fullfile(root, 'renamed.dat'), fullfile(root, 'gunr')), root);
end

function s = gunUpper(root)
copyfile(fullfile(root, 'gz', 'a.txt.gz'), fullfile(root, 'up.GZ'));
s = names(gunzip(fullfile(root, 'up.GZ'), fullfile(root, 'gup')), root);
end

function s = gunSame(root, out)
d = fullfile(root, 'same');
if ~isfolder(d)
    mkdir(d);
end
copyfile(fullfile(root, 'gz', 'noext.gz'), fullfile(d, 'gzplain'));
old = cd(d);
try
    if isempty(out)
        f = gunzip('gzplain');
    else
        f = gunzip('gzplain', out);
    end
    s = ['ok: ' names(f, root)];
catch err
    s = [err.identifier ' | ' err.message];
end
cd(old);
end

function s = gzRoundTrip(root)
bytes = uint8([0:255, 10, 13, 0, 255]);
fid = fopen(fullfile(root, 'bin.dat'), 'w');
fwrite(fid, bytes, 'uint8');
fclose(fid);
gzip(fullfile(root, 'bin.dat'), fullfile(root, 'gzbin'));
gunzip(fullfile(root, 'gzbin', 'bin.dat.gz'), fullfile(root, 'gzbinout'));
fid = fopen(fullfile(root, 'gzbinout', 'bin.dat'), 'r');
back = fread(fid, inf, 'uint8=>uint8')';
fclose(fid);
s = sprintf('%d %d', numel(back), isequal(back, bytes));
end

function s = addsExt(root)
tar(fullfile(root, 'noext'), 'a.txt');
tar(fullfile(root, 'other.ext'), 'a.txt');
tar(fullfile(root, 'up.TAR'), 'a.txt');
s = sprintf('%d %d %d %d %d', isfile(fullfile(root, 'noext.tar')), isfile(fullfile(root, 'other.ext')), ...
    isfile(fullfile(root, 'other.ext.tar')), isfile(fullfile(root, 'up.TAR')), isfile(fullfile(root, 'up.TAR.tar')));
end

function s = made(root)
s = sprintf('%d %d %d %d %d', isfile(fullfile(root, 'z.tgz')), isfile(fullfile(root, 'z.tgz.tar')), ...
    isfile(fullfile(root, 'z2.tar.gz')), isfile(fullfile(root, 'z3.gz')), isfile(fullfile(root, 'z4.TGZ')));
end

function s = tarBytes(root)
% gzip's magic at the front of a .tgz, and the ustar magic in a plain tar's first header.
fid = fopen(fullfile(root, 'z.tgz'), 'r');
b = fread(fid, 2, 'uint8=>uint8')';
fclose(fid);
fid = fopen(fullfile(root, 'one.tar'), 'r');
t = fread(fid, 512, 'uint8=>uint8')';
fclose(fid);
s = sprintf('%s ; %s', mat2str(b), char(t(258:262)));
end

function s = tarSelf(root, src)
tar(fullfile(src, 'self.tar'), 'a.txt');
lastwarn('');
f = tar(fullfile(src, 'self.tar'), '*');
[msg, id] = lastwarn;
delete(fullfile(src, 'self.tar'));
s = [names(f, root) ' ; ' id ' ; ' strrep(msg, root, '<root>')];
end

function s = untarEmpty(root)
f = untar(fullfile(root, 'e.tar'), fullfile(root, 'ue'));
s = sprintf('%d outputs [%s]; folder made %d', numel(f), names(f, root), isfolder(fullfile(root, 'ue', 'empty')));
end

function s = untarOrder(root)
mkdir(fullfile(root, 'uord'));
tar(fullfile(root, 'uord', 'k.tar'), 'a.txt');
tar(fullfile(root, 'uord', 'k.tgz'), 'b.txt');
s = names(untar(fullfile(root, 'uord', 'k'), fullfile(root, 'uord', 'out')), root);
end

function s = untarHere(root)
here = fullfile(root, 'here');
mkdir(here);
old = cd(here);
f = untar(fullfile(root, 'one.tar'));
cd(old);
s = names(f, root);
end

function s = untarOverwrite(root)
out = fullfile(root, 'uo');
mkdir(out);
writet(fullfile(out, 'a.txt'), 'old text');
untar(fullfile(root, 'one.tar'), out);
s = fileread(fullfile(out, 'a.txt'));
end

function s = tarRoundTrip(root)
tar(fullfile(root, 'bin.tgz'), fullfile(root, 'bin.dat'));
untar(fullfile(root, 'bin.tgz'), fullfile(root, 'tbinout'));
fid = fopen(fullfile(root, 'tbinout', 'bin.dat'), 'r');
back = fread(fid, inf, 'uint8=>uint8')';
fclose(fid);
s = sprintf('%d %d', numel(back), isequal(back, uint8([0:255, 10, 13, 0, 255])));
end

function s = untarDotDot(root)
out = fullfile(root, 'udd', 'inner');
mkdir(out);
old = cd(out);
tar('dd.tar', '../../twin/a.txt');
cd(old);
writet(fullfile(root, 'twin', 'a.txt'), 'before');
try
    f = untar(fullfile(out, 'dd.tar'), out);
    s = ['ok: ' names(f, root)];
catch err
    s = [err.identifier ' | ' strrep(err.message, root, '<root>')];
end
s = sprintf('%s ; above %s', s, fileread(fullfile(root, 'twin', 'a.txt')));
end

function f = untarZip(root)
zip(fullfile(root, 'zz.zip'), fullfile(root, 'twin', 'a.txt'));
copyfile(fullfile(root, 'zz.zip'), fullfile(root, 'zz.tar'));
f = untar(fullfile(root, 'zz.tar'), fullfile(root, 'uz'));
end

function f = unzipUrl(root)
zip(fullfile(root, 'plain.zip'), 'a.txt');
f = unzip(['file:///' strrep(fullfile(root, 'plain.zip'), '\', '/')], fullfile(root, 'unp4'));
end

function writeEncrypted(name)
% A ZipCrypto archive R2025b wrote (zip(..., 'Password', 'p', 'EncryptionMethod', 'zipcrypto')) of a.txt holding 'alpha'.
bytes = [80 75 3 4 20 0 9 0 8 0 42 148 108 93 0 0 0 0 0 0 0 0 0 0 0 0 5 0 32 0 97 46 116 120 116 117 120 11 0 1 4 0 0 0 0 4 0 0 0 0 ...
    85 84 13 0 7 224 104 246 106 232 104 246 106 224 104 246 106 80 94 190 26 170 24 123 247 1 108 99 114 129 156 250 53 90 93 75 ...
    80 75 7 8 106 57 224 208 19 0 0 0 5 0 0 0 80 75 1 2 20 3 20 0 9 0 8 0 42 148 108 93 106 57 224 208 19 0 0 0 5 0 0 0 5 0 24 0 ...
    0 0 0 0 0 0 0 0 182 129 0 0 0 0 97 46 116 120 116 117 120 11 0 1 4 0 0 0 0 4 0 0 0 0 85 84 5 0 1 224 104 246 106 80 75 5 6 0 ...
    0 0 0 1 0 1 0 75 0 0 0 102 0 0 0 0 0];
fid = fopen(name, 'w');
fwrite(fid, bytes, 'uint8');
fclose(fid);
end

function chkdiv(name, fn, adr)
% A u2_chk line whose rule is a recorded divergence: the stamp adds what JGraph prints.
fprintf('CHK|%s|%s|div=%s\n', name, u2_text(fn()), adr);
end
