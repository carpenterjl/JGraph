% record: -noFigureWindows
% Compiler and SDK plan, stage K3b: pcode and P-files - what pcode writes and where, its flags and
% warnings, a P-file found before its source, run, reported at line 0, warned about when older than
% its source, refused when damaged - and the secret functions in a session with no vault.

root = tempname;
src = fullfile(root, 'src');
out = fullfile(root, 'out');
mkdir(src); mkdir(out);
put(src, 'k3pf.m', {
    'function [a, b] = k3pf(x, y)'
    '%K3PF The help line of k3pf.'
    'if x < 0'
    '    error(''k3b:neg'', ''negative %d'', x);'
    'end'
    'a = x + 1; % a comment'
    'b = mfilename;'
    'if nargin > 1, b = mfilename(''fullpath''); end'
    'end'
    });
put(src, 'k3ps.m', {'% a script', 'k3ps_v = 42;'});
put(src, 'k3pc.m', {
    'classdef k3pc'
    '    properties'
    '        V = 3'
    '    end'
    '    methods'
    '        function r = twice(o), r = 2 * o.V; end'
    '    end'
    'end'
    });
put(src, 'k3pbad.m', {'function k3pbad', 'x = (1;', 'end'});
put(src, 'k3pboth.m', {'function r = k3pboth', 'r = ''from m'';', 'end'});
put(src, 'k3plocal.m', {'function r = k3plocal', 'r = inner(2);', 'end', 'function r = inner(x)', 'r = x * 10;', 'end'});
put(src, 'k3pdeep.m', {'function k3pdeep', 'k3pdeep2;', 'end', 'function k3pdeep2', 'error(''k3b:deep'', ''deep failure'');', 'end'});
old = cd(out);

% What pcode writes, and where.
u2_chk('one', @() done(@() pcode(fullfile(src, 'k3pf.m'))));
u2_chk('one_written', @() names(out));
u2_chk('one_src_untouched', @() names(src));
u2_chk('noext', @() done(@() pcode(fullfile(src, 'k3ps'))));
u2_chk('noext_written', @() names(out));
u2_chk('inplace', @() done(@() pcode(fullfile(src, 'k3pc.m'), '-inplace')));
u2_chk('inplace_src', @() names(src));
u2_chk('inplace_out', @() names(out));
chkdiv('syntax', @() strrep(errtext(@() pcode(fullfile(src, 'k3pbad.m'))), root, '<root>'), 'ADR0259');
u2_chk('syntax_written', @() names(out));
u2_chk('string', @() done(@() pcode(string(fullfile(src, 'k3plocal.m')))));
u2_chk('two', @() done(@() pcode(fullfile(src, 'k3pdeep.m'), fullfile(src, 'k3pboth.m'))));
u2_chk('two_written', @() names(out));
u2_chk('r2022a', @() done(@() pcode(fullfile(src, 'k3pf.m'), '-R2022a')));
u2_chk('r2007b', @() done(@() pcode(fullfile(src, 'k3pf.m'), '-r2007b')));
u2_chk('nodebug', @() done(@() pcode(fullfile(src, 'k3pf.m'), '-R2022a', '-nodebug')));
chkdiv('folder', @() strrep(errtext(@() pcode(src)), root, '<root>'), 'ADR0259');
u2_chk('folder_written', @() names(out));
u2_chk('wild', @() done(@() pcode(fullfile(src, 'k3pl*.m'))));
u2_chk('p_ext', @() done(@() pcode(fullfile(src, 'k3pf.p'))));
u2_chk('noargs', @() done(@() pcode()));
mkdir(fullfile(src, 'private'));
put(fullfile(src, 'private'), 'k3ppriv.m', {'function r = k3ppriv', 'r = 1;', 'end'});
u2_chk('private', @() done(@() pcode(fullfile(src, 'private', 'k3ppriv.m'))));
u2_chk('private_written', @() names(fullfile(out, 'private')));

% Refusals, and the warnings that skip an argument.
u2_chk('nargout', @() pcode(fullfile(src, 'k3pf.m')));
u2_chk('number', @() done(@() pcode(5)));
u2_chk('cell', @() done(@() pcode({fullfile(src, 'k3pf.m')})));
u2_chk('badopt', @() done(@() pcode(fullfile(src, 'k3pf.m'), '-nosuch')));
u2_chk('both_flags', @() done(@() pcode(fullfile(src, 'k3pf.m'), '-R2022a', '-R2007b')));
u2_chk('nodebug_alone', @() done(@() pcode(fullfile(src, 'k3pf.m'), '-nodebug')));
u2_chk('warn_missing', @() warned(@() pcode(fullfile(src, 'nosuch.m')), root));
u2_chk('warn_missing_noext', @() warned(@() pcode('k3nosuchfile'), root));
u2_chk('warn_txt', @() warned(@() pcode(fullfile(src, 'notes.txt')), root));
u2_chk('warn_dotdot', @() warned(@() pcode('..\src\k3pf.m'), root));
u2_chk('warn_qmark', @() warned(@() pcode('k3p?.m'), root));
u2_chk('warn_notpath', @() warned(@() pcode(fullfile(root, 'nosuchdir', 'k3pf.m')), root));

% The bytes: the same source gives the same P-file twice; the header and length are each engine's own.
a1 = bytes(fullfile(out, 'k3pf.p'));
pcode(fullfile(src, 'k3pf.m'));
u2_chk('same_twice', @() isequal(a1, bytes(fullfile(out, 'k3pf.p'))));
chkdiv('header', @() char(a1(1:12)), 'ADR0259');

% A P-file with no source beside it.
u2_chk('exist', @() exist('k3pf'));
u2_chk('exist_file', @() exist('k3pf', 'file'));
u2_chk('exist_withext', @() exist('k3pf.p'));
u2_chk('which', @() strrep(which('k3pf'), root, '<root>'));
u2_chk('which_all', @() strrep(strjoin(which('k3pf', '-all'), ';'), root, '<root>'));
u2_chk('call', @() k3pf(1));
u2_chk('call_mfilename', @() second(@() k3pf(1)));
u2_chk('call_fullpath', @() strrep(second(@() k3pf(1, 2)), root, '<root>'));
u2_chk('nargin', @() nargin('k3pf'));
u2_chk('nargout_of', @() nargout('k3pf'));
u2_chk('error', @() errinfo(@() k3pf(-2), root, 1));
u2_chk('error_deep', @() errinfo(@() k3pdeep, root, 2));
u2_chk('local', @() k3plocal);
u2_chk('func2str', @() func2str(@k3pf));
u2_chk('functions_file', @() strrep(getfield(functions(@k3pf), 'file'), root, '<root>'));
u2_chk('feval', @() feval('k3pf', 4));
u2_chk('str2func', @() feval(str2func('k3pf'), 5));
u2_chk('type', @() strtrim(evalc('type k3pf')));
run('k3ps.p');
u2_chk('script_run', @() k3ps_v);
clear k3ps_v
run(fullfile(out, 'k3ps'));
u2_chk('script_run_noext', @() k3ps_v);
u2_chk('class_beside', @() classbeside(src));

% A P-file beside its source: the P-file wins, and a newer source is warned about once per read.
both = fullfile(root, 'both');
mkdir(both);
copyfile(fullfile(src, 'k3pboth.m'), both);
cd(both);
pcode('k3pboth.m');
u2_chk('both_names', @() names(both));
u2_chk('both_call', @() k3pboth);
u2_chk('both_which', @() strrep(which('k3pboth'), root, '<root>'));
u2_chk('both_which_all', @() strrep(strjoin(which('k3pboth', '-all'), ';'), root, '<root>'));
u2_chk('both_exist', @() exist('k3pboth'));
pause(2.1);
put(both, 'k3pboth.m', {'function r = k3pboth', 'r = ''from newer m'';', 'end'});
clear k3pboth
rehash
u2_chk('stale_call', @() warnedcall(@() k3pboth, root));
u2_chk('stale_again', @() warnedcall(@() k3pboth, root));

% Damaged P-files.
cd(out);
b = bytes(fullfile(out, 'k3plocal.p'));
b(end - 3) = bitxor(b(end - 3), 1);
w(fullfile(out, 'k3plocal.p'), b);
clear k3plocal
rehash
u2_chk('tampered', @() k3plocal);
w(fullfile(out, 'k3plocal.p'), b(1:20));
clear k3plocal
rehash
u2_chk('truncated', @() strrep(errtext(@() k3plocal), root, '<root>'));
w(fullfile(out, 'k3pjunk.p'), uint8('not a pfile at all'));
rehash
u2_chk('junk', @() strrep(errtext(@() k3pjunk), root, '<root>'));
u2_chk('junk_exist', @() exist('k3pjunk'));
cd(old);

% The secret functions, where there is no vault: arguments first, then the vault's refusal.
u2_chk('isSecret', @() isSecret('k3bnosuch'));
u2_chk('isSecret_string', @() isSecret("k3bnosuch"));
u2_chk('isSecret_empty', @() isSecret(''));
u2_chk('isSecret_number', @() isSecret(5));
u2_chk('isSecret_none', @() isSecret());
u2_chk('isSecret_cell', @() isSecret({'a'}));
u2_chk('getSecret', @() getSecret('k3bnosuch'));
u2_chk('getSecret_empty', @() getSecret(''));
u2_chk('getSecret_emptystring', @() getSecret(""));
u2_chk('getSecret_number', @() getSecret(5));
u2_chk('getSecret_none', @() getSecret());
u2_chk('getSecret_two', @() getSecret('a', 'b'));
u2_chk('getSecret_strings', @() getSecret(["a" "b"]));
u2_chk('getSecret_path', @() getSecret('PATH'));
u2_chk('getSecretMetadata', @() getSecretMetadata('k3bnosuch'));
u2_chk('removeSecret', @() done(@() removeSecret('k3bnosuch')));
u2_chk('removeSecret_empty', @() done(@() removeSecret('')));
u2_chk('setSecretMetadata_strings', @() done(@() setSecretMetadata('k3bnosuch', dictionary("a", "b"))));
u2_chk('setSecretMetadata_cells', @() done(@() setSecretMetadata('k3bnosuch', dictionary("a", {1}))));
u2_chk('setSecretMetadata_unconfigured', @() done(@() setSecretMetadata('k3bnosuch', dictionary())));
u2_chk('setSecretMetadata_numkeys', @() done(@() setSecretMetadata('k3bnosuch', dictionary(1, {1}))));
u2_chk('setSecretMetadata_number', @() done(@() setSecretMetadata('k3bnosuch', 5)));
u2_chk('setSecretMetadata_one', @() done(@() setSecretMetadata('k3bnosuch')));
u2_chk('setSecret_empty', @() done(@() setSecret('')));
u2_chk('setSecret_number', @() done(@() setSecret(5)));
u2_chk('setSecret_badopt', @() done(@() setSecret('k3bnosuch', 'Nosuch', 1)));
u2_chk('listSecrets', @() done(@() listSecrets));
u2_chk('listSecrets_arg', @() done(@() listSecrets(1)));
u2_chk('getSecret_noout', @() strtrim(evalc('getSecret(''k3bnosuch'')')));

function s = done(f)
% Runs an output-less call for its effect.
f();
s = 'ok';
end

function s = warned(f, root)
% The warning a call gave: its identifier and message.
lastwarn('');
f();
[m, id] = lastwarn;
s = strrep([id ' / ' m], root, '<root>');
end

function s = warnedcall(f, root)
% What a call answered and the warning it gave, newlines as ' / '.
lastwarn('');
v = f();
[m, id] = lastwarn;
s = strrep(strrep([v ' ; ' id ' / ' m], newline, ' / '), root, '<root>');
end

function s = errinfo(f, root, n)
% An error's identifier, message and first n frames (file, name, line).
try
    f();
    s = 'no error';
catch e
    st = e.stack;
    parts = arrayfun(@(t) sprintf('%s:%s:%d', strrep(t.file, root, '<root>'), t.name, t.line), st(1:min(n, end)), 'UniformOutput', false);
    s = sprintf('%s | %s | %s', e.identifier, e.message, strjoin(parts, ' ; '));
end
end

function s = errtext(f)
try
    f();
    s = 'no error';
catch e
    s = [e.identifier ' / ' e.message];
end
end

function v = second(f)
[~, v] = f();
end

function s = classbeside(src)
% A class whose P-file sits beside its source.
was = cd(src);
s = sprintf('%d %s', twice(k3pc), strrep(which('k3pc'), fileparts(src), '<root>'));
cd(was);
end

function s = names(folder)
d = dir(folder);
d = d(~[d.isdir]);
s = strjoin(sort({d.name}), ' ');
end

function b = bytes(f)
fid = fopen(f, 'r');
b = fread(fid, Inf, '*uint8')';
fclose(fid);
end

function w(f, b)
fid = fopen(f, 'w');
fwrite(fid, b, 'uint8');
fclose(fid);
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
