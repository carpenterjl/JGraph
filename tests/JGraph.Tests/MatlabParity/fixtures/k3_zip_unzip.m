% record: -noFigureWindows
% Compiler and SDK plan, stage K3: zip and unzip, which users package a program's files with -
% the entry names zip answers for files, folders, wildcards and a root folder, the .zip it adds,
% what unzip writes and answers, a round trip, and both functions' refusals.

root = tempname;
src = fullfile(root, 'src');
mkdir(fullfile(src, 'sub', 'deep'));
mkdir(fullfile(src, 'empty'));
writelines('alpha', fullfile(src, 'a.txt'));
writelines('beta', fullfile(src, 'b.txt'));
writelines('code', fullfile(src, 'c.m'));
writelines('inner', fullfile(src, 'sub', 's.txt'));
writelines('deeper', fullfile(src, 'sub', 'deep', 'd.txt'));
mkdir(fullfile(root, 'mix', 'aa'));
writelines('z', fullfile(root, 'mix', 'z.txt'));
writelines('m', fullfile(root, 'mix', 'm.txt'));
writelines('x', fullfile(root, 'mix', 'aa', 'x.txt'));
writelines('q', fullfile(root, 'mix', 'Q.txt'));
mkdir(fullfile(root, 'twin'));
writelines('twin', fullfile(root, 'twin', 'a.txt'));
mkdir(fullfile(root, 'dir.zip'));
writelines('outside', fullfile(root, 'outside.txt'));
old = cd(src);

u2_chk('one_file', @() names(zip(fullfile(root, 'one.zip'), 'a.txt')));
u2_chk('one_file_class', @() class(zip(fullfile(root, 'one.zip'), 'a.txt')));
u2_chk('one_file_size', @() mat2str(size(zip(fullfile(root, 'one.zip'), 'a.txt'))));
u2_chk('two_files_size', @() mat2str(size(zip(fullfile(root, 'two.zip'), {'a.txt', 'b.txt'}))));
u2_chk('adds_zip', @() addsZip(root));
u2_chk('cell_files', @() names(zip(fullfile(root, 'two.zip'), {'b.txt', 'a.txt'})));
u2_chk('string_files', @() names(zip(fullfile(root, 'two.zip'), ["a.txt", "c.m"])));
u2_chk('string_zipname', @() names(zip(string(fullfile(root, 'two.zip')), 'a.txt')));
u2_chk('wildcard', @() names(zip(fullfile(root, 'w.zip'), '*.txt')));
u2_chk('wildcard_sub', @() names(zip(fullfile(root, 'w.zip'), 'sub/*.txt')));
u2_chk('folder', @() names(zip(fullfile(root, 'f.zip'), 'sub')));
u2_chk('empty_folder', @() names(zip(fullfile(root, 'e.zip'), 'empty')));
u2_chk('empty_folder_list', @() listing(fullfile(root, 'e.zip'), root));
u2_chk('dot', @() names(zip(fullfile(root, 'dot.zip'), '.')));
u2_chk('folder_and_file', @() names(zip(fullfile(root, 'ff.zip'), {'sub', 'a.txt'})));
u2_chk('duplicate', @() names(zip(fullfile(root, 'dup.zip'), {'a.txt', 'a.txt'})));
u2_chk('rootfolder', @() names(zip(fullfile(root, 'r.zip'), {'s.txt', 'deep'}, fullfile(src, 'sub'))));
u2_chk('rootfolder_rel', @() names(zip(fullfile(root, 'r.zip'), 's.txt', 'sub')));
u2_chk('fullpath_inside', @() names(zip(fullfile(root, 'p.zip'), fullfile(src, 'a.txt'))));
u2_chk('fullpath_rootfolder', @() names(zip(fullfile(root, 'p.zip'), fullfile(src, 'sub', 's.txt'), src)));
u2_chk('order_mixed', @() names(zip(fullfile(root, 'mix.zip'), fullfile(root, 'mix'))));
u2_chk('order_mixed_root', @() names(zip(fullfile(root, 'mix.zip'), '.', fullfile(root, 'mix'))));
u2_chk('order_given', @() names(zip(fullfile(root, 'mix.zip'), {'sub', 'c.m', 'a.txt'})));
u2_chk('star', @() names(zip(fullfile(root, 'star.zip'), '*')));
u2_chk('relative_subpath', @() names(zip(fullfile(root, 'rs.zip'), 'sub/s.txt')));
u2_chk('dotdot', @() failure(@() names(zip(fullfile(root, 'dd.zip'), '../outside.txt')), root));
u2_chk('fullpath_outside', @() failure(@() names(zip(fullfile(root, 'po.zip'), fullfile(root, 'outside.txt'))), root));
u2_chk('fullpath_outside_unzip', @() failure(@() outsideUnzip(root), root));
u2_chk('relative_zipname', @() relativeZip(root));
u2_chk('duplicate_names', @() failure(@() names(zip(fullfile(root, 'dn.zip'), {fullfile(src, 'a.txt'), fullfile(root, 'twin', 'a.txt')})), root));
u2_chk('self_zip', @() selfZip(root, src));
u2_chk('zip_is_dir', @() failure(@() names(zip(fullfile(root, 'dir.zip'), 'a.txt')), root));
u2_chk('zip_is_dir_noext', @() failure(@() names(zip(fullfile(root, 'dir'), 'a.txt')), root));
u2_chk('zip_empty_name', @() failure(@() names(zip('', 'a.txt')), root));
u2_chk('zip_empty_file', @() failure(@() names(zip(fullfile(root, 'm.zip'), '')), root));
u2_chk('zip_cell_number', @() failure(@() names(zip(fullfile(root, 'm.zip'), {'a.txt', 5})), root));
u2_chk('zip_root_number', @() failure(@() names(zip(fullfile(root, 'm.zip'), 'a.txt', 5)), root));
u2_chk('zip_wild_folder', @() failure(@() names(zip(fullfile(root, 'm.zip'), 's*/s.txt')), root));
u2_chk('overwrite', @() overwrite(root));
u2_chk('no_output', @() quietZip(root));

u2_chk('unzip_names', @() names(unzip(fullfile(root, 'f.zip'), fullfile(root, 'out1')), root));
u2_chk('unzip_class', @() class(unzip(fullfile(root, 'f.zip'), fullfile(root, 'out1'))));
u2_chk('unzip_size', @() mat2str(size(unzip(fullfile(root, 'f.zip'), fullfile(root, 'out1')))));
u2_chk('unzip_content', @() fileread(fullfile(root, 'out1', 'sub', 'deep', 'd.txt')));
u2_chk('unzip_dot', @() names(unzip(fullfile(root, 'dot.zip'), fullfile(root, 'out2')), root));
u2_chk('unzip_empty_folder', @() unzipEmpty(root));
u2_chk('unzip_noext', @() names(unzip(fullfile(root, 'one'), fullfile(root, 'out3')), root));
u2_chk('unzip_cwd', @() unzipHere(root));
u2_chk('unzip_new_nested', @() names(unzip(fullfile(root, 'one.zip'), fullfile(root, 'n1', 'n2')), root));
u2_chk('unzip_string', @() names(unzip(string(fullfile(root, 'one.zip')), string(fullfile(root, 'out4'))), root));
u2_chk('unzip_overwrite', @() unzipOverwrite(root));
u2_chk('roundtrip_bytes', @() roundTrip(root));
u2_chk('unzip_no_output', @() quietUnzip(root));
u2_chk('unzip_relative_out', @() names(unzip(fullfile(root, 'one.zip'), 'relout')));
u2_chk('unzip_dot_out', @() names(unzip(fullfile(root, 'one.zip'), '.')));
u2_chk('unzip_empty_out', @() names(unzip(fullfile(root, 'one.zip'), '')));
% R2025b writes the ../ entry above the output folder; JGraph refuses the archive (ADR 0258).
chkdiv('unzip_dotdot', @() unzipDotDot(root), 'ADR0258');
u2_chk('unzip_empty_name', @() failure(@() names(unzip('')), root));
u2_chk('unzip_out_number', @() failure(@() names(unzip(fullfile(root, 'one.zip'), 5)), root));
u2_chk('unzip_folder', @() failure(@() names(unzip(fullfile(root, 'twin'))), root));
u2_chk('unzip_password', @() failure(@() names(unzip(fullfile(root, 'one.zip'), fullfile(root, 'pw'), 'Password', 'x')), root));

u2_chk('zip_missing', @() zip(fullfile(root, 'm.zip'), 'nosuch.txt'));
u2_chk('zip_missing_wild', @() zip(fullfile(root, 'm.zip'), '*.nosuch'));
u2_chk('zip_one_missing', @() zip(fullfile(root, 'm.zip'), {'a.txt', 'nosuch.txt'}));
u2_chk('zip_no_args', @() zip());
u2_chk('zip_one_arg', @() zip(fullfile(root, 'm.zip')));
u2_chk('zip_number', @() zip(fullfile(root, 'm.zip'), 5));
u2_chk('zip_number_name', @() zip(5, 'a.txt'));
u2_chk('zip_empty_cell', @() zip(fullfile(root, 'm.zip'), {}));
u2_chk('zip_bad_root', @() failure(@() zip(fullfile(root, 'm.zip'), 'a.txt', fullfile(root, 'nosuchdir')), root));
u2_chk('zip_four_args', @() zip(fullfile(root, 'm.zip'), 'a.txt', src, 'x'));
u2_chk('zip_two_outputs', @() twoZipOutputs(root));
u2_chk('unzip_missing', @() failure(@() unzip(fullfile(root, 'nosuch.zip')), root));
u2_chk('unzip_not_zip', @() failure(@() unzip(fullfile(src, 'a.txt'), fullfile(root, 'out6')), root));
u2_chk('unzip_no_args', @() unzip());
u2_chk('unzip_number', @() unzip(5));
u2_chk('unzip_three_args', @() unzip(fullfile(root, 'one.zip'), fullfile(root, 'out7'), 'x'));

cd(old);
rmdir(root, 's');

function s = names(c, root)
if nargin > 1
    c = strrep(c, [root filesep], '');
end
s = strjoin(c, ' | ');
end

function s = listing(zipfile, root)
out = fullfile(root, 'listing');
if isfolder(out)
    rmdir(out, 's');
end
f = unzip(zipfile, out);
d = dir(fullfile(out, '**'));
d = d(~ismember({d.name}, {'.', '..'}));
s = sprintf('%d outputs; on disk: %s', numel(f), strjoin(strrep(fullfile({d.folder}, {d.name}), [out filesep], ''), ' | '));
end

function s = addsZip(root)
zip(fullfile(root, 'noext'), 'a.txt');
zip(fullfile(root, 'other.ext'), 'a.txt');
s = sprintf('%d %d %d %d', isfile(fullfile(root, 'noext.zip')), isfile(fullfile(root, 'noext')), ...
    isfile(fullfile(root, 'other.ext')), isfile(fullfile(root, 'other.ext.zip')));
end

function s = relativeZip(root)
zip('rel', 'a.txt');
s = sprintf('%d', isfile('rel.zip'));
movefile('rel.zip', fullfile(root, 'rel.zip'));
end

function s = overwrite(root)
zip(fullfile(root, 'ow.zip'), {'a.txt', 'b.txt'});
zip(fullfile(root, 'ow.zip'), 'c.m');
s = names(unzip(fullfile(root, 'ow.zip'), fullfile(root, 'owout')), root);
end

function s = unzipEmpty(root)
f = unzip(fullfile(root, 'e.zip'), fullfile(root, 'eout'));
s = sprintf('%d outputs [%s]; folder made %d', numel(f), strjoin(strrep(f, [root filesep], ''), ' | '), ...
    isfolder(fullfile(root, 'eout', 'empty')));
end

function s = unzipHere(root)
here = fullfile(root, 'here');
mkdir(here);
old = cd(here);
f = unzip(fullfile(root, 'one.zip'));
cd(old);
s = names(f, root);
end

function s = unzipOverwrite(root)
out = fullfile(root, 'uo');
mkdir(out);
writelines('old text', fullfile(out, 'a.txt'));
unzip(fullfile(root, 'one.zip'), out);
s = strtrim(fileread(fullfile(out, 'a.txt')));
end

function s = roundTrip(root)
bytes = uint8([0:255, 10, 13, 0, 255]);
fid = fopen(fullfile(root, 'bin.dat'), 'w');
fwrite(fid, bytes, 'uint8');
fclose(fid);
zip(fullfile(root, 'bin.zip'), fullfile(root, 'bin.dat'), root);
unzip(fullfile(root, 'bin.zip'), fullfile(root, 'binout'));
fid = fopen(fullfile(root, 'binout', 'bin.dat'), 'r');
back = fread(fid, inf, 'uint8=>uint8')';
fclose(fid);
s = sprintf('%d %d', numel(back), isequal(back, bytes));
end

function v = twoZipOutputs(root)
[a, b] = zip(fullfile(root, 'm.zip'), 'a.txt'); %#ok<ASGLU>
v = 1;
end

function s = quietZip(root)
s = evalc('zip(fullfile(root, ''q.zip''), ''a.txt'')');
s = sprintf('[%s]', s);
end

function s = quietUnzip(root)
s = evalc('unzip(fullfile(root, ''one.zip''), fullfile(root, ''out5''))');
s = sprintf('[%s]', s);
end

function s = failure(fn, root)
try
    s = fn();
    if ~ischar(s)
        s = class(s);
    end
    s = ['ok: ' strrep(s, root, '<root>')];
catch err
    s = [err.identifier ' | ' strrep(err.message, root, '<root>')];
end
end

function s = outsideUnzip(root)
zip(fullfile(root, 'po2.zip'), fullfile(root, 'outside.txt'));
s = names(unzip(fullfile(root, 'po2.zip'), fullfile(root, 'poout')), root);
end

function s = selfZip(root, src)
zip(fullfile(src, 'self.zip'), 'a.txt');
lastwarn('');
f = zip(fullfile(src, 'self.zip'), '*');
[msg, id] = lastwarn;
delete(fullfile(src, 'self.zip'));
s = [names(f) ' ; ' id ' ; ' strrep(msg, root, '<root>')];
end

function s = unzipDotDot(root)
out = fullfile(root, 'dd', 'inner');
mkdir(out);
zip(fullfile(root, 'dd', 'inner', 'dd.zip'), '../outside.txt');
try
    f = unzip(fullfile(out, 'dd.zip'), out);
    s = ['ok: ' strrep(strjoin(f, ' | '), root, '<root>')];
catch err
    s = [err.identifier ' | ' strrep(err.message, root, '<root>')];
end
s = sprintf('%s ; in dd %d ; in inner %d', s, isfile(fullfile(root, 'dd', 'outside.txt')), isfile(fullfile(out, 'outside.txt')));
end

function chkdiv(name, fn, adr)
% A u2_chk line whose rule is a recorded divergence: the stamp adds what JGraph prints.
fprintf('CHK|%s|%s|div=%s\n', name, u2_text(fn()), adr);
end
