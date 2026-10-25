% record: -noFigureWindows
% Compiler and SDK plan, stage K7: the rest of mcc's command line - the obsolete switches, the ones a
% standalone build ignores, letters read as switches, bundles, -T, every -W target's checks, -w and
% its warning table, -K - as R2025b's mcc answers them from a session. The programs are built and
% never run here.
beep('off');
root = tempname;
src = fullfile(root, 'src');
mkdir(fullfile(src, 'startup'));
put(src, 'k7f.m', {'function k7f(varargin)', 'fprintf(''k7f %d\n'', nargin);', 'end'});
put(fullfile(src, 'startup'), 'k7s.m', {'function k7s()', 'disp(''k7s'');', 'end'});
put(fullfile(src, 'startup'), 'startup.m', {'disp(''startup'');'});
put(src, 'k7_data.txt', {'data'});
put(src, 'k7_bad.ico', {'not an icon'});
put(src, 'k7.bundle', {'-m -o k7_bundled'});
put(src, 'k7p2.bundle', {'-W main:%1%,version=%2%', '-T link:exe'});
old = cd(src);

% Refusals: the obsolete switches, letters read as switches, the syntax, -A, -T and -W.
u2_chk('obsolete_V', @() err(@() mcc('-V', '-m', 'k7f.m')));
u2_chk('obsolete_win32', @() err(@() mcc('-win32', '-m', 'k7f.m')));
u2_chk('obsolete_win64', @() err(@() mcc('-win64', '-m', 'k7f.m')));
u2_chk('obsolete_y', @() err(@() mcc('-y', '-m', 'k7f.m')));
u2_chk('obsolete_F_alone', @() err(@() mcc('-F', 'k7.prj')));
u2_chk('letters_unknown', @() err(@() mcc('-ok7ns', '-m', 'k7f.m')));
u2_chk('letters_help', @() err(@() mcc('-help')));
u2_chk('help_takes_next', @() err(@() mcc('-m', 'k7f.m', '-h', '-d', 'out')));
u2_chk('whitespace', @() err(@() mcc('-m k7f.m')));
u2_chk('empty_value', @() err(@() mcc('-o', '', '-m', 'k7f.m')));
u2_chk('o_twice', @() err(@() mcc('-o', 'k7a', '-o', 'k7b', '-m', 'k7f.m')));
u2_chk('A_bogus', @() err(@() mcc('-A', 'none', '-m', 'k7f.m')));
u2_chk('D_unknown', @() err(@() mcc('-D', 'x', '-m', 'k7f.m')));
u2_chk('c_alone', @() err(@() mcc('-c', 'k7f.m')));
u2_chk('T_lib_m', @() err(@() mcc('-m', '-T', 'link:lib', 'k7f.m')));
u2_chk('T_without_W', @() err(@() mcc('-T', 'codegen', 'k7f.m')));
u2_chk('W_lib_empty', @() err(@() mcc('-W', 'lib:', 'k7f.m')));
u2_chk('W_cpplib_empty', @() err(@() mcc('-W', 'cpplib:', 'k7f.m')));
u2_chk('W_cpplib_bad', @() err(@() mcc('-W', 'cpplib:k7c,bogus', 'k7f.m')));
u2_chk('W_cpplib_named', @() err(@() mcc('-W', 'cpplib:k7c,named', 'k7f.m')));
u2_chk('W_dotnet_api', @() err(@() mcc('-W', 'dotnet:k7asm,api=bogus', 'k7f.m')));
u2_chk('W_dotnet_api_late', @() err(@() mcc('-W', 'dotnet:k7asm,k7cls,api=mwarray', 'k7f.m')));
u2_chk('W_dotnet_version', @() err(@() mcc('-W', 'dotnet:k7asm,k7cls,3.5', 'k7f.m')));
u2_chk('W_python_empty', @() err(@() mcc('-W', 'python:', 'k7f.m')));
u2_chk('W_ctf_without_U', @() err(@() mcc('-W', 'CTF:k7ctf', 'k7f.m')));
u2_chk('W_spark_without_C', @() err(@() mcc('-W', 'spark:k7sp,2', 'k7f.m')));
u2_chk('B_missing', @() err(@() mcc('-B', 'k7_nosuch.bundle', 'k7f.m')));
u2_chk('B_too_few', @() err(@() mcc('-B', 'k7p2.bundle:k7few', 'k7f.m')));
u2_chk('w_bogus', @() err(@() mcc('-w', 'bogus', '-m', 'k7f.m')));
u2_chk('w_short_com_id', @() err(@() mcc('-w', 'disable:COM_WARN_STARTUP_FILE_INCLUDED', '-m', 'k7f.m')));
u2_chk('Z_bogus', @() err(@() mcc('-Z', 'k7 nothing', '-m', 'k7f.m', '-d', fullfile(root, 'z'))));
u2_chk('non_code', @() strrep(err(@() mcc('-m', 'k7f.m', 'k7_data.txt', '-d', fullfile(root, 'nc'))), root, '<root>'));
u2_chk('I_missing', @() strrep(err(@() mcc('-I', 'k7_nosuchdir', '-m', 'k7f.m', '-d', fullfile(root, 'inc'))), root, '<root>'));
u2_chk('file_named', @() err(@() mcc('-m', 'k7f.m', '-M', '-d', fullfile(root, 'named'))));

% The warning table, and what -w sets in it.
t_w_list = evalc('mcc(''-w'', ''list'')');
u2_chk('w_list', @() joined(t_w_list));
t_w_list_disabled = evalc('mcc(''-w'', ''disable:repeated_file'', ''-w'', ''list'')');
u2_chk('w_list_disabled', @() joined(t_w_list_disabled));
t_w_list_error = evalc('mcc(''-w'', ''error'', ''-w'', ''list'')');
u2_chk('w_list_error', @() joined(t_w_list_error));
t_help_first_line = evalc('mcc(''-?'')');
chkdiv('help_first_line', @() firstline(t_help_first_line), 'ADR0263');

% Builds: what each wrote, its note or warning, and its refusal.
u2_chk('codegen_files', @() build(root, 'codegen', {'-m', '-T', 'codegen', 'k7f.m'}));
u2_chk('compile_files', @() build(root, 'compile', {'-W', 'main:k7tc', '-T', 'compile:exe', 'k7f.m'}));
u2_chk('U_files', @() build(root, 'mps', {'-U', '-m', 'k7f.m'}));
u2_chk('mC_files', @() build(root, 'mC', {'-mC', 'k7f.m'}));
u2_chk('bundle_files', @() build(root, 'bundle', {'-B', 'k7.bundle', 'k7f.m'}));
u2_chk('bundle_param_files', @() build(root, 'bparam', {'-B', 'k7p2.bundle:k7bp,1.2', 'k7f.m'}));
u2_chk('icon_files', @() build(root, 'icon', {'-m', 'k7f.m', '-r', 'k7_bad.ico'}));
u2_chk('icon_K_files', @() build(root, 'iconK', {'-K', '-m', 'k7f.m', '-r', 'k7_bad.ico'}));
u2_chk('repeated', @() build(root, 'rep', {'-m', 'k7f.m', 'k7f.m'}));
u2_chk('repeated_disabled', @() build(root, 'repoff', {'-w', 'disable:repeated_file', '-m', 'k7f.m', 'k7f.m'}));
u2_chk('repeated_error', @() build(root, 'reperr', {'-w', 'error:repeated_file', '-m', 'k7f.m', 'k7f.m'}));
u2_chk('startup', @() build(root, 'start', {'-m', fullfile(src, 'startup', 'k7s.m')}));
u2_chk('startup_disabled', @() build(root, 'startoff', {'-w', 'disable', '-m', fullfile(src, 'startup', 'k7s.m')}));
u2_chk('startup_error', @() build(root, 'starterr', {'-w', 'error', '-m', fullfile(src, 'startup', 'k7s.m')}));
u2_chk('startup_error_quiet', @() build(root, 'starteq', {'-w', 'error', '-w', 'disable:Compiler:compiler:COM_WARN_STARTUP_FILE_INCLUDED', '-m', fullfile(src, 'startup', 'k7s.m')}));
u2_chk('ignored_g', @() build(root, 'g', {'-G', '-m', 'k7f.m'}));
u2_chk('ignored_M', @() build(root, 'M', {'-M', '-DK7=1', '-m', 'k7f.m'}));
u2_chk('quiet_switches', @() build(root, 'quiet', {'-b', '-S', '-u', '-H', '-Y', 'k7.lic', '-A', 'all', '-m', 'k7f.m'}));
t_note_O = evalc('mcc(''-O'', ''-m'', ''k7f.m'', ''-d'', fullfile(root, ''O''))');
u2_chk('note_O', @() joined(t_note_O));
t_note_t = evalc('mcc(''-t'', ''-m'', ''k7f.m'', ''-d'', fullfile(root, ''t''))');
u2_chk('note_t', @() joined(t_note_t));
cd(old);
rmdir(root, 's');

function s = build(root, name, args)
% A build's outcome: ok or its refusal, the warning it raised, and the files it wrote.
folder = fullfile(root, name);
lastwarn('', '');
s = err(@() mcc(args{:}, '-d', folder));
[~, id] = lastwarn;
s = sprintf('%s | warn %s | files %s', s, id, names(folder));
end

function s = err(f)
try
    f();
    s = 'no error';
catch e
    s = [e.identifier ' / ' strrep(e.message, newline, ' / ')];
end
end

function s = names(folder)
if ~exist(folder, 'dir')
    s = '';
    return
end
d = dir(folder);
d = d(~[d.isdir]);
s = strjoin(sort({d.name}), ' ');
end

function s = firstline(text)
t = splitlines(text);
s = t{1};
end

function s = joined(text)
t = regexprep(text, '<a [^>]*>|</a>', '');
s = strrep(strrep(t, sprintf('\r'), ''), newline, ' / ');
end

function put(folder, name, lines)
fid = fopen(fullfile(folder, name), 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end

function chkdiv(name, fn, adr)
% A u2_chk line whose rule is a recorded divergence: the stamp adds what JGraph prints.
fprintf('CHK|%s|%s|div=%s\n', name, u2_text(fn()), adr);
end
