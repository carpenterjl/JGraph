% record: -noFigureWindows
% Open items 190 and 204: mkdir, copyfile and movefile as R2025b answers them - the warning for a
% folder already there and the three outputs instead, a file in the way, a folder it cannot make,
% folder and wildcard sources onto a new or an existing destination, a wildcard matching nothing,
% the 'f' mode over a read-only file, and each refusal with and without outputs.

root = tempname;
mkdir(root);
old = cd(root);

% ---- mkdir ----
u2_chk('mk_new', @() quietly(@() mkdir('m1'), root));
u2_chk('mk_new_made', @() isfolder('m1'));
u2_chk('mk_exist', @() quietly(@() mkdir('m1'), root));
u2_chk('mk_exist_one_out', @() outs(1, @mkdir, {'m1'}, root));
u2_chk('mk_exist_two_out', @() outs(2, @mkdir, {'m1'}, root));
u2_chk('mk_exist_three_out', @() outs(3, @mkdir, {'m1'}, root));
u2_chk('mk_new_three_out', @() outs(3, @mkdir, {'m2'}, root));
u2_chk('mk_new_one_out', @() outs(1, @mkdir, {'m3'}, root));
u2_chk('mk_parent_exist', @() outs(3, @mkdir, {'.', 'm1'}, root));
u2_chk('mk_parent_exist_warns', @() quietly(@() mkdir('.', 'm1'), root));
u2_chk('mk_parent_new', @() outs(3, @mkdir, {'m1', 'inner'}, root));
u2_chk('mk_parent_missing', @() outs(3, @mkdir, {'nop', 'q'}, root));
u2_chk('mk_parent_missing_made', @() isfolder(fullfile('nop', 'q')));
u2_chk('mk_nested', @() outs(3, @mkdir, {fullfile('n1', 'n2', 'n3')}, root));
writet('afile', 'x');
u2_chk('mk_file', @() quietly(@() mkdir('afile'), root));
u2_chk('mk_file_three_out', @() outs(3, @mkdir, {'afile'}, root));
u2_chk('mk_file_one_out', @() outs(1, @mkdir, {'afile'}, root));
u2_chk('mk_file_parent', @() outs(3, @mkdir, {'.', 'afile'}, root));
u2_chk('mk_under_file', @() quietly(@() mkdir(fullfile('afile', 'sub')), root));
u2_chk('mk_under_file_three_out', @() outs(3, @mkdir, {fullfile('afile', 'sub')}, root));
u2_chk('mk_bad_name', @() quietly(@() mkdir('a<b'), root));
u2_chk('mk_bad_name_three_out', @() outs(3, @mkdir, {'a<b'}, root));
u2_chk('mk_empty', @() quietly(@() mkdir(''), root));
u2_chk('mk_empty_three_out', @() outs(3, @mkdir, {''}, root));
u2_chk('mk_number', @() mkdir(5));
u2_chk('mk_string', @() outs(3, @mkdir, {"m4"}, root));
u2_chk('mk_string_exist', @() outs(3, @mkdir, {"m4"}, root));
u2_chk('mk_no_args', @() mkdir());
u2_chk('mk_three_args', @() mkdir('a', 'b', 'c'));
u2_chk('mk_four_out', @() outs(4, @mkdir, {'m5'}, root));
u2_chk('mk_trailing_sep', @() outs(3, @mkdir, {['m1' filesep]}, root));
u2_chk('mk_dot', @() outs(3, @mkdir, {'.'}, root));
u2_chk('mk_status_class', @() class(mkdir('m1')));

% ---- copyfile ----
mkdir('cs');
writet(fullfile('cs', 'a.txt'), 'A');
writet(fullfile('cs', 'b.txt'), 'B');
writet(fullfile('cs', 'c.m'), 'C');
mkdir(fullfile('cs', 'sub'));
writet(fullfile('cs', 'sub', 's.txt'), 'S');
u2_chk('cp_file_new', @() [outs(3, @copyfile, {fullfile('cs', 'a.txt'), 'c1.txt'}, root) ' ; ' tree('c1.txt')]);
u2_chk('cp_file_over', @() [outs(3, @copyfile, {fullfile('cs', 'b.txt'), 'c1.txt'}, root) ' ; ' tree('c1.txt')]);
mkdir('cd1');
u2_chk('cp_file_to_folder', @() [outs(3, @copyfile, {fullfile('cs', 'a.txt'), 'cd1'}, root) ' ; ' tree('cd1')]);
u2_chk('cp_file_no_parent', @() [outs(3, @copyfile, {fullfile('cs', 'a.txt'), fullfile('cnew', 'deep', 'x.txt')}, root) ' ; ' tree('cnew')]);
u2_chk('cp_file_to_new_sep', @() [outs(3, @copyfile, {fullfile('cs', 'a.txt'), ['cnew2' filesep]}, root) ' ; ' tree('cnew2')]);
u2_chk('cp_folder_new', @() [outs(3, @copyfile, {'cs', 'cf1'}, root) ' ; ' tree('cf1')]);
mkdir('cf2');
writet(fullfile('cf2', 'keep.txt'), 'K');
u2_chk('cp_folder_exist', @() [outs(3, @copyfile, {'cs', 'cf2'}, root) ' ; ' tree('cf2')]);
u2_chk('cp_folder_onto_file', @() [outs(3, @copyfile, {'cs', 'c1.txt'}, root) ' ; ' tree('c1.txt')]);
u2_chk('cp_folder_nested_new', @() [outs(3, @copyfile, {'cs', fullfile('cnp', 'a', 'b')}, root) ' ; ' tree('cnp')]);
u2_chk('cp_wild_new', @() [outs(3, @copyfile, {fullfile('cs', '*.txt'), 'cw1'}, root) ' ; ' tree('cw1')]);
mkdir('cw2');
u2_chk('cp_wild_exist', @() [outs(3, @copyfile, {fullfile('cs', '*.txt'), 'cw2'}, root) ' ; ' tree('cw2')]);
u2_chk('cp_wild_one', @() [outs(3, @copyfile, {fullfile('cs', '*.m'), 'cw3'}, root) ' ; ' tree('cw3')]);
u2_chk('cp_wild_folders', @() [outs(3, @copyfile, {fullfile('cs', '*'), 'cw4'}, root) ' ; ' tree('cw4')]);
u2_chk('cp_wild_none', @() [outs(3, @copyfile, {fullfile('cs', '*.zzz'), 'cw5'}, root) ' ; ' tree('cw5')]);
u2_chk('cp_wild_none_no_out', @() quietly(@() copyfile(fullfile('cs', '*.zzz'), 'cw5'), root));
u2_chk('cp_wild_onto_file', @() [outs(3, @copyfile, {fullfile('cs', '*.txt'), 'c1.txt'}, root) ' ; ' tree('c1.txt')]);
u2_chk('cp_wild_question', @() [outs(3, @copyfile, {fullfile('cs', '?.txt'), 'cw6'}, root) ' ; ' tree('cw6')]);
u2_chk('cp_missing', @() outs(3, @copyfile, {'nosuch.txt', 'x.txt'}, root));
u2_chk('cp_missing_no_out', @() quietly(@() copyfile('nosuch.txt', 'x.txt'), root));
u2_chk('cp_missing_one_out', @() outs(1, @copyfile, {'nosuch.txt', 'x.txt'}, root));
u2_chk('cp_missing_folder', @() outs(3, @copyfile, {fullfile('nodir', 'x.txt'), 'y.txt'}, root));
u2_chk('cp_self', @() outs(3, @copyfile, {'c1.txt', 'c1.txt'}, root));
mkdir('ci');
writet(fullfile('ci', 'a.txt'), 'A');
mkdir(fullfile('ci', 'sub'));
writet(fullfile('ci', 'sub', 's.txt'), 'S');
u2_chk('cp_folder_into_itself', @() [outs(3, @copyfile, {'ci', fullfile('ci', 'sub', 'inside')}, root) ' ; ' tree('ci')]);
mkdir('cj');
writet(fullfile('cj', 'a.txt'), 'A');
mkdir(fullfile('cj', 'sub'));
writet(fullfile('cj', 'sub', 's.txt'), 'S');
u2_chk('cp_wild_into_match', @() [outs(3, @copyfile, {fullfile('cj', '*'), fullfile('cj', 'sub')}, root) ' ; ' tree('cj')]);
writet('cfile.txt', 'F');
mkdir('cfa');
u2_chk('cp_file_into_folder', @() [outs(3, @copyfile, {'cfile.txt', 'cfa'}, root) ' ; ' tree('cfa')]);
writet('ro.txt', 'old');
fileattrib('ro.txt', '-w');
u2_chk('cp_read_only', @() [outs(3, @copyfile, {fullfile('cs', 'a.txt'), 'ro.txt'}, root) ' ; ' tree('ro.txt')]);
u2_chk('cp_read_only_f', @() [outs(3, @copyfile, {fullfile('cs', 'b.txt'), 'ro.txt', 'f'}, root) ' ; ' tree('ro.txt')]);
writet('ro2.txt', 'old');
fileattrib('ro2.txt', '-w');
mkdir('wro');
writet(fullfile('wro', 'ro2.txt'), 'new');
u2_chk('cp_wild_read_only', @() [outs(3, @copyfile, {fullfile('wro', '*.txt'), '.'}, root) ' ; ' tree('ro2.txt')]);
u2_chk('cp_wild_read_only_F', @() [outs(3, @copyfile, {fullfile('wro', '*.txt'), '.', 'F'}, root) ' ; ' tree('ro2.txt')]);
fileattrib('ro.txt', '+w');
fileattrib('ro2.txt', '+w');
u2_chk('cp_mode_bad', @() outs(3, @copyfile, {fullfile('cs', 'a.txt'), 'cx.txt', 'x'}, root));
u2_chk('cp_mode_number', @() outs(3, @copyfile, {fullfile('cs', 'a.txt'), 'cx.txt', 1}, root));
u2_chk('cp_four_args', @() copyfile('a', 'b', 'f', 'g'));
u2_chk('cp_one_arg', @() [outs(3, @copyfile, {fullfile('cs', 'c.m')}, root) ' ; ' tree('c.m')]);
u2_chk('cp_number', @() outs(3, @copyfile, {5, 'x'}, root));
u2_chk('cp_string', @() [outs(3, @copyfile, {string(fullfile('cs', 'a.txt')), "cstr.txt"}, root) ' ; ' tree('cstr.txt')]);
u2_chk('cp_four_out', @() outs(4, @copyfile, {fullfile('cs', 'a.txt'), 'c4.txt'}, root));
u2_chk('cp_empty_source', @() outs(3, @copyfile, {'', 'x'}, root));
u2_chk('cp_empty_destination', @() outs(3, @copyfile, {'cfile.txt', ''}, root));
u2_chk('cp_status_class', @() class(copyfile(fullfile('cs', 'a.txt'), 'c5.txt')));
u2_chk('cp_statement', @() quietly(@() copyfile(fullfile('cs', 'a.txt'), 'c6.txt'), root));

% ---- movefile ----
mkdir('ms');
writet(fullfile('ms', 'a.txt'), 'A');
writet(fullfile('ms', 'b.txt'), 'B');
writet(fullfile('ms', 'c.m'), 'C');
mkdir(fullfile('ms', 'sub'));
writet(fullfile('ms', 'sub', 's.txt'), 'S');
writet('mv1.txt', 'one');
u2_chk('mv_file_new', @() [outs(3, @movefile, {'mv1.txt', 'mv2.txt'}, root) ' ; ' tree('mv1.txt') ' ; ' tree('mv2.txt')]);
writet('mv3.txt', 'three');
u2_chk('mv_file_over', @() [outs(3, @movefile, {'mv3.txt', 'mv2.txt'}, root) ' ; ' tree('mv2.txt')]);
mkdir('md1');
u2_chk('mv_file_to_folder', @() [outs(3, @movefile, {'mv2.txt', 'md1'}, root) ' ; ' tree('md1')]);
writet('mv4.txt', 'four');
u2_chk('mv_file_no_parent', @() [outs(3, @movefile, {'mv4.txt', fullfile('mnew', 'deep', 'x.txt')}, root) ' ; ' tree('mnew')]);
u2_chk('mv_file_to_new_sep', @() outs(3, @movefile, {'mv4.txt', ['mnew2' filesep]}, root));
copyfile('ms', 'mf');
u2_chk('mv_folder_new', @() [outs(3, @movefile, {'mf', 'mf1'}, root) ' ; ' tree('mf') ' ; ' tree('mf1')]);
mkdir('mf2');
writet(fullfile('mf2', 'keep.txt'), 'K');
u2_chk('mv_folder_exist', @() [outs(3, @movefile, {'mf1', 'mf2'}, root) ' ; ' tree('mf1') ' ; ' tree('mf2')]);
mkdir('mfa');
writet(fullfile('mfa', 'x.txt'), 'X');
mkdir(fullfile('mfb', 'mfa'));
writet(fullfile('mfb', 'mfa', 'y.txt'), 'Y');
u2_chk('mv_folder_merge', @() [outs(3, @movefile, {'mfa', 'mfb'}, root) ' ; ' tree('mfa') ' ; ' tree('mfb')]);
copyfile('ms', 'mg');
u2_chk('mv_wild_new', @() [outs(3, @movefile, {fullfile('mg', '*.txt'), 'mw1'}, root) ' ; ' tree('mg') ' ; ' tree('mw1')]);
copyfile('ms', 'mh');
mkdir('mw2');
writet(fullfile('mw2', 'a.txt'), 'old');
u2_chk('mv_wild_exist', @() [outs(3, @movefile, {fullfile('mh', '*.txt'), 'mw2'}, root) ' ; ' tree('mh') ' ; ' tree('mw2')]);
copyfile('ms', 'mi');
u2_chk('mv_wild_one', @() [outs(3, @movefile, {fullfile('mi', '*.m'), 'mw3'}, root) ' ; ' tree('mi') ' ; ' tree('mw3')]);
u2_chk('mv_wild_folders', @() [outs(3, @movefile, {fullfile('mi', '*'), 'mw4'}, root) ' ; ' tree('mi') ' ; ' tree('mw4')]);
u2_chk('mv_wild_none', @() outs(3, @movefile, {fullfile('ms', '*.zzz'), 'mw5'}, root));
u2_chk('mv_wild_none_no_out', @() quietly(@() movefile(fullfile('ms', '*.zzz'), 'mw5'), root));
u2_chk('mv_wild_onto_file', @() outs(3, @movefile, {fullfile('ms', '*.txt'), 'cfile.txt'}, root));
u2_chk('mv_missing', @() outs(3, @movefile, {'nosuch.txt', 'x.txt'}, root));
u2_chk('mv_missing_no_out', @() quietly(@() movefile('nosuch.txt', 'x.txt'), root));
u2_chk('mv_self', @() outs(3, @movefile, {fullfile('md1', 'mv2.txt'), fullfile('md1', 'mv2.txt')}, root));
u2_chk('mv_into_own_folder', @() outs(3, @movefile, {fullfile('md1', 'mv2.txt'), 'md1'}, root));
writet('mro.txt', 'old');
writet('msrc.txt', 'new');
fileattrib('mro.txt', '-w');
u2_chk('mv_read_only', @() [outs(3, @movefile, {'msrc.txt', 'mro.txt'}, root) ' ; ' tree('mro.txt') ' ; ' tree('msrc.txt')]);
u2_chk('mv_read_only_f', @() [outs(3, @movefile, {'msrc.txt', 'mro.txt', 'f'}, root) ' ; ' tree('mro.txt') ' ; ' tree('msrc.txt')]);
fileattrib('mro.txt', '+w');
writet('mx.txt', 'x');
u2_chk('mv_mode_bad', @() outs(3, @movefile, {'mx.txt', 'mx2.txt', 'x'}, root));
u2_chk('mv_one_arg_here', @() outs(3, @movefile, {'mx.txt'}, root));
u2_chk('mv_number', @() outs(3, @movefile, {5, 'x'}, root));
u2_chk('mv_folder_onto_file', @() outs(3, @movefile, {'ms', 'mx.txt'}, root));
u2_chk('mv_empty_destination', @() outs(3, @movefile, {'mx.txt', ''}, root));
u2_chk('mv_status_class', @() class(movefile('mx.txt', 'mx3.txt')));

cd(old);
rmdir(root, 's');

function s = quietly(fn, root)
% A call with no outputs: what it raised, or ok and the warning it left.
lastwarn('');
try
    fn();
    s = 'ok';
catch err
    s = [err.identifier ' / ' clean(err.message, root)];
end
[m, id] = lastwarn;
s = sprintf('%s ; warn %s / %s', s, id, clean(m, root));
end

function s = outs(n, f, args, root)
% The n outputs a call answers (class, size and value each), and the warning it left.
lastwarn('');
o = cell(1, n);
try
    [o{:}] = f(args{:});
    parts = cell(1, n);
    for k = 1:n
        v = o{k};
        if ischar(v)
            v = strrep(v, root, '<root>'); % measured after the root goes, which is machine-specific
            parts{k} = sprintf('%s %s [%s]', class(v), mat2str(size(v)), clean(v, root));
        else
            parts{k} = sprintf('%s %s %s', class(v), mat2str(size(v)), mat2str(v));
        end
    end
    s = strjoin(parts, ' ; ');
catch err
    s = [err.identifier ' / ' clean(err.message, root)];
end
[m, id] = lastwarn;
s = sprintf('%s ; warn %s / %s', s, id, clean(m, root));
end

function t = clean(t, root)
t = strtrim(regexprep(strrep(t, root, '<root>'), '\s+', ' '));
end

function writet(name, text)
fid = fopen(name, 'w');
fprintf(fid, '%s', text);
fclose(fid);
end

function s = tree(p)
% A file's text, or a folder's every entry (sorted, a folder ending in /) with each file's text.
if isfile(p)
    s = sprintf('file(%s)', fileread(p));
    return
end
if ~isfolder(p)
    s = 'absent';
    return
end
d = dir(fullfile(pwd, p, '**'));
d = d(~ismember({d.name}, {'.', '..'}));
items = cell(1, numel(d));
for k = 1:numel(d)
    rel = strrep(fullfile(d(k).folder, d(k).name), [fullfile(pwd, p) filesep], '');
    rel = strrep(rel, '\', '/');
    if d(k).isdir
        items{k} = [rel '/'];
    else
        items{k} = [rel '=' fileread(fullfile(d(k).folder, d(k).name))];
    end
end
items = sort(items);
s = ['{' strjoin(items, ' ') '}'];
end
