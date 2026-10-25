% record: -noFigureWindows
% Compiler and SDK plan, stage K11: C shared libraries - compiler.build.CSharedLibraryOptions, its
% defaults, derived names and refusals; mcc -W lib and -l, how they name a library and what they
% refuse; and the wrappers -T codegen generates for a function of each kind of signature, read from
% the generated header and source. Nothing is compiled here, so neither engine needs a C compiler.
beep('off');
root = tempname;
src = fullfile(root, 'src');
mkdir(src);
put(src, 'k11f.m', {'function [a, b] = k11f(x, varargin)', '%K11F A probe function.', 'a = x; b = nargin;', 'end'});
put(src, 'k11g.m', {'function k11g()', '%K11G Nothing out.', 'end'});
put(src, 'k11same.m', {'function x = k11same(x)', 'x = 2 * x;', 'end'});
put(src, 'k11tilde.m', {'function y = k11tilde(~, b, ~)', 'y = b;', 'end'});
put(src, 'k11words.m', {'function [int, long] = k11words(char, NULL, class, bool)', 'int = 1; long = 2;', 'end'});
put(src, 'k11cpp.m', {'function [new, this] = k11cpp(delete, nargout)', 'new = 1; this = 2;', 'end'});
put(src, 'k11script.m', {'disp(1);'});
put(src, 'k11cls.m', {'classdef k11cls', 'end'});
put(src, 'k11notes.txt', {'notes'});
old = cd(src);

% The options from one file: the defaults and the names derived from it.
o = compiler.build.CSharedLibraryOptions('k11f.m');
u2_chk('class', @() class(o));
u2_chk('props', @() strjoin(properties(o)', ' '));
u2_chk('name', @() o.LibraryName);
u2_chk('getname', @() o.getName);
u2_chk('outdir', @() o.OutputDir);
u2_chk('version', @() o.LibraryVersion);
u2_chk('embed', @() char(o.EmbedArchive));
u2_chk('debug', @() char(o.DebugBuild));
u2_chk('files', @() rel(o.FunctionFiles, src));
u2_chk('files_size', @() size(o.FunctionFiles));
u2_chk('packages', @() strjoin(o.SupportPackages, ' '));
u2_chk('two_files', @() rel(compiler.build.CSharedLibraryOptions({'k11g.m', 'k11f.m'}).FunctionFiles, src));
u2_chk('two_name', @() compiler.build.CSharedLibraryOptions({'k11g.m', 'k11f.m'}).LibraryName);
u2_chk('given_name', @() compiler.build.CSharedLibraryOptions('k11f.m', 'LibraryName', 'K11Named').OutputDir);
u2_chk('partial_name', @() compiler.build.CSharedLibraryOptions('k11f.m', 'libraryn', 'P').LibraryName);

% The constructor's refusals.
u2_chk('none', @() err(@() compiler.build.CSharedLibraryOptions()));
u2_chk('missing', @() err(@() compiler.build.CSharedLibraryOptions('k11nosuch.m')));
u2_chk('not_code', @() err(@() compiler.build.CSharedLibraryOptions('k11notes.txt')));
u2_chk('number', @() err(@() compiler.build.CSharedLibraryOptions(5)));
u2_chk('empty_cell', @() err(@() compiler.build.CSharedLibraryOptions({})));
u2_chk('unknown_nv', @() err(@() compiler.build.CSharedLibraryOptions('k11f.m', 'Nonsense', 1)));
u2_chk('ambiguous_nv', @() err(@() compiler.build.CSharedLibraryOptions('k11f.m', 'Library', 'P')));
u2_chk('odd_nv', @() err(@() compiler.build.CSharedLibraryOptions('k11f.m', 'LibraryName')));

% Each property's checks.
o = compiler.build.CSharedLibraryOptions('k11f.m');
u2_chk('set_name_spaced', @() setp(o, 'LibraryName', 'a b'));
u2_chk('set_name_digit', @() setp(o, 'LibraryName', '1abc'));
u2_chk('set_name_dash', @() setp(o, 'LibraryName', 'a-b'));
u2_chk('set_name_string', @() class(setp(o, 'LibraryName', "str")));
u2_chk('set_name_slash', @() err(@() setp(o, 'LibraryName', 'a/b')));
u2_chk('set_name_colon', @() err(@() setp(o, 'LibraryName', 'a:b')));
u2_chk('set_name_empty', @() err(@() setp(o, 'LibraryName', '')));
u2_chk('set_name_number', @() err(@() setp(o, 'LibraryName', 5)));
u2_chk('set_version_text', @() setp(o, 'LibraryVersion', 'abc'));
u2_chk('set_version_empty', @() err(@() setp(o, 'LibraryVersion', '')));
u2_chk('set_version_number', @() err(@() setp(o, 'LibraryVersion', 5)));
u2_chk('set_embed_zero', @() char(setp(o, 'EmbedArchive', 0)));
u2_chk('set_embed_bad', @() err(@() setp(o, 'EmbedArchive', 'maybe')));
u2_chk('set_debug_two', @() char(setp(o, 'DebugBuild', 2)));
u2_chk('set_files_missing', @() err(@() setp(o, 'FunctionFiles', 'k11nosuch.m')));
u2_chk('set_files_text', @() err(@() setp(o, 'FunctionFiles', 'k11notes.txt')));
u2_chk('set_files_empty', @() err(@() setp(o, 'FunctionFiles', {})));
u2_chk('set_outdir_bad', @() err(@() setp(o, 'OutputDir', 'out<1>')));

% mcc's names for a library, and its refusals.
u2_chk('mcc_badname', @() build(root, 'badname', {'-W', 'lib:k11-bad', '-T', 'codegen', 'k11f.m'}));
u2_chk('mcc_digitname', @() build(root, 'digitname', {'-W', 'lib:1k11', '-T', 'codegen', 'k11f.m'}));
u2_chk('mcc_emptyname', @() build(root, 'emptyname', {'-W', 'lib:', '-T', 'codegen', 'k11f.m'}));
u2_chk('mcc_badversion', @() build(root, 'badversion', {'-W', 'lib:k11b,version=x.y', '-T', 'codegen', 'k11f.m'}));
u2_chk('mcc_lib_o', @() build(root, 'lib_o', {'-W', 'lib:k11w', '-o', 'k11other', '-T', 'codegen', 'k11f.m'}));
u2_chk('mcc_o_lib', @() build(root, 'o_lib', {'-o', 'k11other', '-W', 'lib:k11w', '-T', 'codegen', 'k11f.m'}));
u2_chk('mcc_exe', @() build(root, 'exe', {'-W', 'lib:k11x', '-T', 'link:exe', 'k11f.m'}));
u2_chk('mcc_nofiles', @() build(root, 'nofiles', {'-W', 'lib:k11n', '-T', 'codegen'}));
u2_chk('mcc_ell', @() build(root, 'ell', {'-l', '-T', 'codegen', 'k11f.m'}));
u2_chk('mcc_ell_o', @() build(root, 'ell_o', {'-l', '-o', 'k11named', '-T', 'codegen', 'k11f.m'}));
u2_chk('mcc_W_W', @() build(root, 'W_W', {'-W', 'lib:k11a', '-W', 'lib:k11b', '-T', 'codegen', 'k11f.m'}));
u2_chk('mcc_ell_W', @() build(root, 'ell_W', {'-l', '-W', 'lib:k11lw', '-T', 'codegen', 'k11f.m'}));
u2_chk('mcc_W_extra', @() build(root, 'W_extra', {'-W', 'lib:k11x,bogus', '-T', 'codegen', 'k11f.m'}));
u2_chk('mcc_dash_c', @() build(root, 'dash_c', {'-W', 'lib:k11c', '-c', 'k11f.m'}));
u2_chk('mcc_codegen_C', @() build(root, 'codegen_C', {'-W', 'lib:k11s', '-T', 'codegen', '-C', 'k11f.m'}));

% The wrappers generated for each kind of signature.
u2_chk('gen_main', @() build(root, 'gen', {'-W', 'lib:k11gen', '-T', 'codegen', 'k11f.m', 'k11g.m', 'k11same.m', 'k11tilde.m', 'k11words.m', 'k11script.m', 'k11cls.m'}));
gen = fullfile(root, 'gen');
u2_chk('gen_h_mlx', @() grab(fullfile(gen, 'k11gen.h'), '^bool MW_CALL_CONV mlx'));
u2_chk('gen_h_mlf', @() grab(fullfile(gen, 'k11gen.h'), '^extern LIB_k11gen_C_API bool MW_CALL_CONV mlf'));
u2_chk('gen_h_general', @() grab(fullfile(gen, 'k11gen.h'), '^(bool|void) MW_CALL_CONV k11gen'));
u2_chk('gen_c_calls', @() grab(fullfile(gen, 'k11gen.c'), 'return mcl'));
u2_chk('gen_c_init', @() grab(fullfile(gen, 'k11gen.c'), 'mclInitializeComponentInstance|mclGetEmbeddedCtfStream'));
u2_chk('gen_exports', @() grab(fullfile(gen, 'k11gen.exports'), '.'));
u2_chk('gen_c_separate', @() grab(fullfile(root, 'codegen_C', 'k11s.c'), 'mclInitializeComponentInstance|mclGetEmbeddedCtfStream|LibTarget|"k11s"'));
chkdiv('gen_cpp_words', @() lastline(build(root, 'cpp', {'-W', 'lib:k11cppl', '-T', 'codegen', 'k11cpp.m'}), fullfile(root, 'cpp', 'k11cppl.h')), 'ADR0267');

% compiler.build.cSharedLibrary's own refusals, before any build.
u2_chk('api_wrong_options', @() err(@() compiler.build.cSharedLibrary(compiler.build.StandaloneApplicationOptions('k11f.m'))));
u2_chk('api_no_args', @() err(@() compiler.build.cSharedLibrary()));
u2_chk('api_two_out', @() err(@() twoOut()));
u2_chk('api_extra', @() err(@() compiler.build.cSharedLibrary(o, 'Verbose', 'on')));
u2_chk('api_badname', @() err(@() compiler.build.cSharedLibrary('k11f.m', 'LibraryName', 'a b', 'OutputDir', fullfile(root, 'api_bad'))));
cd(old);
rmdir(root, 's');

function v = setp(o, name, value)
% Sets a property on a copy of the options and answers what it then reads.
o.(name) = value;
v = o.(name);
end

function twoOut()
[a, b] = compiler.build.cSharedLibrary('k11f.m'); %#ok<ASGLU>
end

function s = build(root, name, args)
% A build's outcome: ok or its refusal, the warning it raised, and the files it wrote.
folder = fullfile(root, name);
lastwarn('', '');
s = err(@() quiet(args, folder));
[~, id] = lastwarn;
s = sprintf('%s | warn %s | files %s', s, id, names(folder));
end

function quiet(args, folder)
% A build with what it prints left out: a codegen build prints nothing, a refusal is the error.
evalc('mcc(args{:}, ''-d'', folder)');
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

function s = grab(file, pattern)
% The lines of a generated file that match, joined; the file's header (its first comment) is left out.
text = strrep(fileread(file), sprintf('\r\n'), newline);
text = regexprep(text, '^/\*.*?\*/\n', '', 'once');
lines = splitlines(text);
keep = lines(~cellfun(@isempty, regexp(lines, pattern, 'once')));
s = strjoin(strtrim(keep)', ' / ');
end

function s = lastline(outcome, header)
% A build's outcome, then its header's last mlf declaration.
s = [outcome ' | ' grab(header, '^extern LIB_\w+ bool MW_CALL_CONV mlf')];
end

function s = rel(files, folder)
files = cellfun(@(f) strrep(f, [folder filesep], ''), cellstr(files), 'UniformOutput', false);
s = strjoin(files(:)', ' ');
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
