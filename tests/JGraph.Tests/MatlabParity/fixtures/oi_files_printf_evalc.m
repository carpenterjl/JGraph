% record: -noFigureWindows
% Open items 300, 303, 306 and 308 (ADR 0352). 300: dir of a relative folder after cd, a ** pattern
% among them, and delete's patterns and warnings. 303: fileread of bytes that are not all UTF-8,
% decided for the whole file by ICU's UTF-8 recogniser, and the encodings fileread names. 306: a
% string under a printf conversion, an identifier with no message, the warning table's layout,
% getReport of a warning raised as an error, an engine warning's line, and the reports that cannot
% raise in the error state. 308: evalc's NUL and U+FFFF, and MException's Correction.

root = tempname;
mkdir(root);
old = cd(root);
s0 = warning;

% ---- 300: dir after cd, relative patterns
mkdir(fullfile('rel', 'sub', 'deep'));
mkdir(fullfile('rel', 'empty'));
ofp_write(fullfile('rel', 'a.txt'));
ofp_write(fullfile('rel', 'sub', 'b.txt'));
ofp_write(fullfile('rel', 'sub', 'deep', 'c.txt'));
ofp_write(fullfile('rel', 'sub', 'z.m'));
ofp_dir('dir_rel', 'rel', root);
ofp_dir('dir_rel_pattern', fullfile('rel', 'sub', '*.txt'), root);
ofp_dir('dir_rel_starstar', fullfile('rel', '**'), root);
ofp_dir('dir_rel_starstar_txt', fullfile('rel', '**', '*.txt'), root);
ofp_dir('dir_slash_starstar', 'rel/**/*.txt', root);
ofp_dir('dir_cwd_starstar', '**/*.m', root);
ofp_dir('dir_dot_starstar', fullfile('.', 'rel', '**', '*.txt'), root);
ofp_dir('dir_mid_starstar', fullfile('rel', '**', 'deep'), root);
ofp_dir('dir_sub_starstar', fullfile('rel', 'sub', '**', '*'), root);
ofp_dir('dir_missing_starstar', fullfile('nope', '**'), root);
ofp_dir('dir_abs_starstar', fullfile(root, 'rel', '**', '*.txt'), root);

% ---- 300: delete's patterns and warnings
mkdir('d');
cd('d');
ofp_write('x1.tmp'); ofp_write('x2.tmp'); ofp_write('y.txt'); mkdir('fold.tmp'); mkdir('sub');
ofp_write(fullfile('sub', 'a1.txt')); ofp_write(fullfile('sub', 'a2.txt')); ofp_write(fullfile('sub', 'b.txt'));
ofp_write('q1.dat'); ofp_write('q22.dat');
ofp_del('del_star', @() delete('*.tmp'), root);
ofp_files('after_star', '.');
ofp_del('del_sub_wild', @() delete('sub/a*.txt'), root);
ofp_files('after_sub_wild', 'sub');
ofp_del('del_question', @() delete('q?.dat'), root);
ofp_files('after_question', '.');
ofp_del('del_nomatch', @() delete('nomatch*.tmp'), root);
ofp_del('del_missing', @() delete('missing.txt'), root);
ofp_del('del_missing_folder', @() delete(fullfile('nodir', 'missing.txt')), root);
ofp_del('del_folder', @() delete('sub'), root);
ofp_del('del_empty', @() delete(''), root);
ofp_del('del_dot', @() delete('.'), root);
ofp_del('del_bracket', @() delete('[ab].tmp'), root);
ofp_write('s1.tmp'); ofp_write('s2.tmp');
ofp_del('del_two', @() delete('s1.tmp', 's2.tmp'), root);
ofp_write('m1.tmp');
ofp_del('del_two_one_missing', @() delete('nothere.tmp', 'm1.tmp'), root);
ofp_files('after_two', '.');
ofp_write('s3.tmp'); ofp_write('s4.tmp');
ofp_del('del_string_wild', @() delete("s*.tmp"), root);
ofp_files('after_string_wild', '.');
ofp_del('del_string_missing', @() delete("gone.txt"), root);
mkdir('sa'); ofp_write(fullfile('sa', 'b.txt'));
ofp_del('del_wild_folder_part', @() delete(fullfile('s*', 'b.txt')), root);
fprintf('CHK|after_wild_folder_part|%d|exact\n', isfile(fullfile('sa', 'b.txt')));
ofp_write('Up.TMP');
ofp_del('del_case', @() delete('*.tmp'), root);
ofp_files('after_case', '.');
ofp_write('ro.tmp');
fileattrib('ro.tmp', '-w');
ofp_del('del_readonly', @() delete('ro.tmp'), root);
fprintf('CHK|after_readonly|%d|exact\n', isfile('ro.tmp'));
ofp_write('ab1.tmp');
ofp_del('del_abs_wild', @() delete(fullfile(pwd, 'ab*.tmp')), root);
fprintf('CHK|after_abs_wild|%d|exact\n', isfile('ab1.tmp'));
ofp_del('del_cell', @() delete({'y.txt'}), root);
ofp_del('del_string_array', @() delete(["a.tmp" "b.tmp"]), root);
ofp_del('del_none', @() delete(), root);
warning('error', 'MATLAB:DELETE:FileNotFound');
ofp_del('del_missing_error', @() delete('missing.txt'), root);
warning('on', 'MATLAB:DELETE:FileNotFound');
cd(root);

% ---- 303: fileread decides UTF-8 or Windows-1252 for the whole file
ca9 = [195 169];
cases = {
    'utf8',        [195 169 226 130 172]
    'cp1252',      [233 128]
    'mixed_end',   [ca9 233]
    'mixed_mid',   [ca9 233 65]
    'undefined',   [129 141 143 144 157 65]
    'high',        128:159
    'bom_ascii',   [239 187 191 97 98]
    'bom_only',    [239 187 191]
    'bom_bad',     [239 187 191 233 97]
    'bom_v10_i1',  [239 187 191 repmat(ca9, 1, 10) 128]
    'bom_v9_i1',   [239 187 191 repmat(ca9, 1, 9) 128]
    'v11_i1',      [repmat(ca9, 1, 11) 128]
    'v4',          repmat(ca9, 1, 4)
    'lone_cont',   [97 128 98]
    'overlong',    [192 175]
    'surrogate',   [237 160 128]
    'astral',      [240 159 152 128 97 240 159 152 129]
    'truncated',   [97 226 130]
    'trail_cut',   [ca9 226 130]
    'c3c3',        [195 195 169]
    'e98080',      [233 128 128]
    'f8',          [248 136 128 128 128]
    'sjis',        [130 160 130 162]
    'utf16le_bom', [255 254 97 0 98 0]
    'utf16be_bom', [254 255 0 97 0 98]
    'latin_a0ff',  [160 255]
    'nul',         [97 0 98]
    'crlf',        [97 13 10 98]
    'late_valid',  [repmat(97, 1, 9000) ca9]
    'late_broken', [ca9 32 repmat(97, 1, 9000) 233 32]
    };
for k = 1:size(cases, 1)
    name = ['ofp_' cases{k, 1} '.bin'];
    f = fopen(name, 'w'); fwrite(f, cases{k, 2}, 'uint8'); fclose(f);
    % R2025b writes U+001A for a character past the basic plane, losing it; here it is its
    % surrogate pair (ADR 0352's divergence).
    rule = 'exact';
    if strcmp(cases{k, 1}, 'astral')
        rule = 'div=ADR0352';
    end
    fprintf('CHK|fr_%s|%s|%s\n', cases{k, 1}, ofp_codes(fileread(name)), rule);
end
fprintf('CHK|enc_utf8_bad|%s|exact\n', ofp_codes(fileread('ofp_cp1252.bin', 'Encoding', 'UTF-8')));
fprintf('CHK|enc_utf8_mixed|%s|exact\n', ofp_codes(fileread('ofp_mixed_mid.bin', 'Encoding', 'UTF-8')));
fprintf('CHK|enc_utf8_bom|%s|exact\n', ofp_codes(fileread('ofp_bom_ascii.bin', 'Encoding', 'UTF-8')));
fprintf('CHK|enc_1252_utf8|%s|exact\n', ofp_codes(fileread('ofp_utf8.bin', 'Encoding', 'windows-1252')));
fprintf('CHK|enc_1252_high|%s|exact\n', ofp_codes(fileread('ofp_high.bin', 'Encoding', 'windows-1252')));
fprintf('CHK|enc_1252_bom|%s|exact\n', ofp_codes(fileread('ofp_bom_ascii.bin', 'Encoding', 'windows-1252')));
fprintf('CHK|enc_latin1_high|%s|exact\n', ofp_codes(fileread('ofp_high.bin', 'Encoding', 'ISO-8859-1')));
fprintf('CHK|enc_ascii_utf8|%s|exact\n', ofp_codes(fileread('ofp_utf8.bin', 'Encoding', 'US-ASCII')));
fprintf('CHK|enc_utf16le|%s|exact\n', ofp_codes(fileread('ofp_utf16le_bom.bin', 'Encoding', 'UTF-16LE')));
fprintf('CHK|enc_string|%s|exact\n', ofp_codes(fileread("ofp_cp1252.bin", "Encoding", "UTF-8")));
u9b_chk('enc_bogus', @() fileread('ofp_cp1252.bin', 'Encoding', 'bogus-enc'));

% ---- 306: a string under a printf conversion
u9b_chk('pf_d_str', @() sprintf('%d', "5"));
u9b_chk('pf_d_str_text', @() sprintf('%d;', "abc"));
u9b_chk('pf_i_str', @() sprintf('%i', "a"));
u9b_chk('pf_u_str', @() sprintf('%u', "a"));
u9b_chk('pf_x_str', @() sprintf('%x', "a"));
u9b_chk('pf_o_str', @() sprintf('%o', "a"));
u9b_chk('pf_f_str_text', @() sprintf('%f', "ab"));
u9b_chk('pf_e_str', @() sprintf('%e', "1"));
u9b_chk('pf_g_str', @() sprintf('%g', "1.5"));
u9b_chk('pf_g_str_spaces', @() sprintf('%g;', " 2 "));
u9b_chk('pf_g_str_hex', @() sprintf('%g;', "0x10"));
u9b_chk('pf_g_str_complex', @() sprintf('%g;', "1+2i"));
u9b_chk('pf_e_str_empty', @() sprintf('%e;', ""));
u9b_chk('pf_f_strarr', @() sprintf('%f;', ["1" "x"]));
u9b_chk('pf_c_str1', @() sprintf('%c;', "a"));
u9b_chk('pf_c_str2', @() sprintf('%c;', "ab"));
u9b_chk('pf_c_str0', @() sprintf('%c;', ""));
u9b_chk('pf_c_strarr', @() sprintf('%c;', ["a" "b"]));
u9b_chk('pf_c_strarr2', @() sprintf('%c;', ["a" "bc"]));
u9b_chk('pf_c_str_num', @() sprintf('%c%c;', "a", 66));
u9b_chk('pf_s_str', @() sprintf('%s;', "abc"));
u9b_chk('pf_s_width', @() sprintf('%5s;', "ab"));
u9b_chk('pf_s_strarr', @() sprintf('%s %s;', ["a" "b"]));
u9b_chk('pf_s_num_after', @() sprintf('%s;', "ab", 65));
u9b_chk('pf_d_s_mixed', @() sprintf('%d %s;', 1, "x"));
u9b_chk('pf_d_s_pairs', @() sprintf('%d %s;', 1, "ab", 2, "c"));
u9b_chk('pf_d_strarr', @() sprintf('%d;', ["a" "b"]));
u9b_chk('pf_s_d_two', @() sprintf('%s %d;', "a", "b"));
u9b_chk('pf_d_after_num', @() sprintf('%d;', 5, "x"));
u9b_chk('pf_d_empty_str', @() sprintf('%d;', ""));
u9b_chk('pf_d_missing', @() sprintf('%d;', string(missing)));
u9b_chk('pf_s_missing', @() sprintf('%s;', string(missing)));
u9b_chk('pf_c_missing', @() sprintf('%c;', string(missing)));
u9b_chk('pf_f_missing', @() sprintf('%f;', string(missing)));
u9b_chk('pf_star_str', @() sprintf('%*d;', "3", 5));
u9b_chk('pf_star_str_bad', @() sprintf('%*d;', "x", 5));
u9b_chk('pf_prec_star_str', @() sprintf('%.*f;', "2", pi));
u9b_chk('pf_width_d_str', @() sprintf('%5.1d;', "a"));
u9b_chk('pf_no_conversion', @() sprintf('abc', "x"));
u9b_chk('pf_format_string', @() sprintf("%d;", 5));
u9b_chk('pf_format_string_escape', @() sprintf("a\tb%d", 5));
u9b_chk('pf_fprintf_d_str', @() evalc('fprintf(''%d\n'', "5")'));
u9b_chk('pf_fprintf_c_str2', @() evalc('fprintf(''%c\n'', "ab")'));
u9b_chk('pf_fprintf_s_str', @() evalc('fprintf(''%s;'', "ab")'));
u9b_chk('pf_fprintf_format_string', @() evalc('fprintf("%d;", 7)'));

% ---- 306: an identifier with no message
ids = {'pkg:id', 'pkg:id:sub', 'A:b', 'a1:b2', 'a_b:c_d', 'a:b:c:d', 'pkg:', ':pkg', 'a::b', 'a: b', 'a:%d', 'a:1b', '_a:b', 'a:_b', 'a-b:c', 'a.b:c', 'plain text', 'http://x.y'};
for k = 1:numel(ids)
    ofp_lone(sprintf('lone_%d', k), ids{k});
end
ofp_lone('lone_string', "pkg:id");
u9b_chk('warn_id_number', @() warning('a:b', 5));
u9b_chk('warn_struct_message', @() warning(struct('message', 'm', 'identifier', 'a:b')));

% ---- 306: the table's layout
warning('on', 'all');
fprintf('CHK|table_all_on|%s|exact\n', ofp_lines(evalc('warning')));
warning('off', 'backtrace');
fprintf('CHK|table_backtrace_off|%s|exact\n', ofp_lines(evalc('warning')));
warning('on', 'backtrace');
warning('on', 'ofp:on');
fprintf('CHK|table_explicit_on|%s|exact\n', ofp_lines(evalc('warning')));
warning('off', 'ofp:a');
warning('error', 'ofp:b');
fprintf('CHK|table_two|%s|exact\n', ofp_lines(evalc('warning')));
fprintf('CHK|table_query_all|%s|exact\n', ofp_lines(evalc('warning(''query'', ''all'')')));
warning('off', 'all');
fprintf('CHK|table_all_off|%s|exact\n', ofp_lines(evalc('warning')));
warning('on', 'ofp:c');
fprintf('CHK|table_default_off|%s|exact\n', ofp_lines(evalc('warning')));
warning('on', 'all');

% ---- 306: a warning raised as an error, reported
warning('error', 'ofp:r');
try
    warning('ofp:r', 'raised %d', 4);
catch e
    fprintf('CHK|report_basic|%s|exact\n', strrep(getReport(e, 'basic'), newline, '/'));
end
try
    ofp_raise();
catch e
    fprintf('CHK|report_fn_basic|%s|exact\n', strrep(getReport(e, 'basic'), newline, '/'));
    fprintf('CHK|report_fn_top_line|%d|exact\n', e.stack(1).line);
end
warning('on', 'ofp:r');
warning('error', 'MATLAB:colon:nonIntegerIndex');
try
    ofp_colon(1:10);
catch e
    fprintf('CHK|engine_warn_id|%s|exact\n', e.identifier);
    fprintf('CHK|engine_warn_line|%d|exact\n', e.stack(1).line);
    fprintf('CHK|engine_warn_basic|%s|exact\n', strrep(getReport(e, 'basic'), newline, '/'));
end
warning('on', 'MATLAB:colon:nonIntegerIndex');

% ---- 306: what cannot raise, in the error state
src = EventBox;
addlistener(src, 'Changed', @(~, ~) error('ofp:cb', 'callback failed'));
warning('error', 'MATLAB:callback:error');
lastwarn('', '');
out = evalc('fire(src)');
[m, id] = lastwarn;
fprintf('CHK|listener_error_state|out=%d lastwarn=%s %d|exact\n', numel(out), id, numel(m));
warning('on', 'MATLAB:callback:error');
lastwarn('', '');
evalc('fire(src)');
[~, id] = lastwarn;
fprintf('CHK|listener_on|%s|exact\n', id);
warning('error', 'MATLAB:class:DestructorError');
lastwarn('', '');
out = evalc('dt = DeleteThrower; clear dt');
[m, id] = lastwarn;
fprintf('CHK|destructor_error_state|out=%d lastwarn=%s %d|exact\n', numel(out), id, numel(m));
warning('on', 'MATLAB:class:DestructorError');
tm = timer('TimerFcn', @(~, ~) [], 'Period', 10, 'ExecutionMode', 'fixedRate');
start(tm);
warning('error', 'MATLAB:timer:deleterunning');
lastwarn('', '');
evalc('delete(tm)');
[m, id] = lastwarn;
fprintf('CHK|timer_delete_error_state|%s %s valid=%d|exact\n', id, strtok(m, newline), isvalid(tm));
warning('on', 'MATLAB:timer:deleterunning');

% ---- 308: evalc's NUL and U+FFFF
ofp_evalc('ev_fprintf_nul', 'fprintf(''a%sb'', char(0))');
ofp_evalc('ev_disp_nul', 'disp(char([97 0 98]))');
ofp_evalc('ev_disp_two_nul', 'disp(char([0 0]))');
ofp_evalc('ev_format_nul', 'fprintf([''a'' 0 ''b''])');
ofp_evalc('ev_escape_nul', 'fprintf(''a\0b'')');
ofp_evalc('ev_disp_string_nul', 'disp(string(char([97 0 98])))');
ofp_evalc('ev_error_message_nul', 'try, error(''a%sb'', char(0)); catch e, disp(e.message); end');
ofp_evalc('ev_ffff_alone', 'fprintf(''%s'', char(65535))');
ofp_evalc('ev_ffff_two', 'fprintf(''%s'', char([65535 65535]))');
ofp_evalc('ev_a_ffff_b', 'fprintf(''a%sb'', char(65535))');
ofp_evalc('ev_ffff_then_a', 'fprintf(''%sa'', char(65535))');
ofp_evalc('ev_a_then_ffff', 'fprintf(''a%s'', char(65535))');
ofp_evalc('ev_two_writes', 'fprintf(''a''); fprintf(''%s'', char(65535))');
ofp_evalc('ev_ffff_then_write', 'fprintf(''%s'', char(65535)); fprintf(''b'')');
ofp_evalc('ev_disp_ffff', 'disp(char(65535))');
ofp_evalc('ev_ffff_nl', 'fprintf(''%s\n'', char(65535))');
ofp_evalc('ev_nul_ffff', 'fprintf(''%s'', char([0 65535]))');
ofp_evalc('ev_ffff_nul', 'fprintf(''%s'', char([65535 0]))');
ofp_evalc('ev_fffe', 'fprintf(''%s'', char(65534))');
ofp_evalc('ev_controls', 'fprintf(''%s'', char([1 7 8 9 27 127 128 159 160]))');
fprintf('CHK|sprintf_keeps_nul|%s|exact\n', mat2str(double(sprintf('a%sb', char(0)))));
f = fopen('ofp_nul.txt', 'w'); fprintf(f, 'a%sb%sc', char(0), char(65535)); fclose(f);
f = fopen('ofp_nul.txt', 'r'); b = fread(f, Inf, 'uint8')'; fclose(f);
fprintf('CHK|file_keeps_nul|%s|exact\n', mat2str(b));

% ---- 308: MException's Correction
try
    error('ofp:plain', 'plain');
catch e
    fprintf('CHK|corr_error|%s %s|exact\n', class(e.Correction), mat2str(size(e.Correction)));
end
try
    ofp_no_such_function_xyz(1);
catch e
    fprintf('CHK|corr_undefined|%s %s|exact\n', class(e.Correction), mat2str(size(e.Correction)));
end
try
    x = [1 2 3];
    x(5);
catch e
    fprintf('CHK|corr_index|%s %s|exact\n', class(e.Correction), mat2str(size(e.Correction)));
end
me = MException('ofp:built', 'built');
fprintf('CHK|corr_built|%s %s|exact\n', class(me.Correction), mat2str(size(me.Correction)));
fprintf('CHK|corr_properties|%s|exact\n', strjoin(properties(me)', ','));
fprintf('CHK|corr_isprop|%d|exact\n', isprop(me, 'Correction'));
try
    throw(me);
catch e
    fprintf('CHK|corr_thrown|%s %s|exact\n', class(e.Correction), mat2str(size(e.Correction)));
end

warning(s0);
cd(old);
rmdir(root, 's');

function ofp_write(name)
f = fopen(name, 'w');
fprintf(f, 'x');
fclose(f);
end

function ofp_dir(key, pattern, root)
d = dir(pattern);
parts = cell(1, numel(d));
for i = 1:numel(d)
    parts{i} = [strrep(strrep(d(i).folder, root, '~'), '\', '/') '/' d(i).name sprintf(':%d', d(i).isdir)];
end
fprintf('CHK|%s|%s %d [%s]|exact\n', key, mat2str(size(d)), numel(d), strjoin(parts, ' '));
end

function ofp_del(key, fn, root)
lastwarn('', '');
try
    fn();
    [m, id] = lastwarn;
    fprintf('CHK|%s|ok %s %s|exact\n', key, id, strrep(strrep(m, root, '~'), '\', '/'));
catch err
    fprintf('CHK|%s|ERR %s %s|exact\n', key, err.identifier, strrep(strrep(err.message, root, '~'), '\', '/'));
end
end

function ofp_files(key, folder)
d = dir(folder);
names = sort({d(~[d.isdir]).name});
fprintf('CHK|%s|%s|exact\n', key, strjoin(names, ','));
end

function s = ofp_codes(t)
d = double(t);
if numel(d) > 24
    s = sprintf('%s n=%d [%s .. %s]', class(t), numel(d), strtrim(sprintf('%d ', d(1:4))), strtrim(sprintf('%d ', d(end-3:end))));
else
    s = sprintf('%s n=%d [%s]', class(t), numel(d), strtrim(sprintf('%d ', d)));
end
end

function ofp_lone(key, id)
lastwarn('', '');
try
    evalc('warning(id)');
    [m, wid] = lastwarn;
    fprintf('CHK|%s_warning|ok %s %s|exact\n', key, wid, m);
catch err
    fprintf('CHK|%s_warning|ERR %s|exact\n', key, err.identifier);
end
try
    error(id);
catch err
    fprintf('CHK|%s_error|%s %s|exact\n', key, err.identifier, err.message);
end
end

function s = ofp_lines(t)
s = strrep(t, newline, '/');
end

function ofp_raise()
warning('ofp:r', 'from a function');
end

function y = ofp_colon(x)
y = 0;
y = x(1.5:3);
end

function ofp_evalc(key, code)
t = evalc(code);
fprintf('CHK|%s|%s|exact\n', key, mat2str(double(t)));
end
