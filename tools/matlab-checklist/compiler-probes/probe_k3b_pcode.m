function probe_k3b_pcode(work)
%PROBE_K3B_PCODE What R2025b's pcode writes and how a P-file runs beside and without its source
%(compiler and SDK plan, stage K3b). Run under matlab -batch -noFigureWindows with a scratch
%folder outside the repo; it is emptied first.
beep('off');
here = fileparts(mfilename('fullpath'));
addpath(fullfile(here, 'src'));
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
src = fullfile(work, 'src');
out = fullfile(work, 'out');
mkdir(src); mkdir(out);
put(src, 'k3pf.m', {
    'function [a, b] = k3pf(x, y)'
    '%K3PF The help line of k3pf.'
    '%   More help.'
    'if x < 0'
    '    error(''k3b:neg'', ''negative %d'', x);'
    'end'
    'a = x + 1; % a comment'
    'b = mfilename;'
    'if nargin > 1, b = mfilename(''fullpath''); end'
    'end'
    });
put(src, 'k3ps.m', {'% a script', 'k3ps_v = 42;', 'disp(k3ps_v)'});
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

cd(out);
k0_do('pcode.one', @() pcode(fullfile(src, 'k3pf.m')));
k0_try('pcode.one.written', @() names(out));
k0_try('pcode.one.src', @() names(src));
k0_do('pcode.noext', @() pcode(fullfile(src, 'k3ps')));
k0_try('pcode.noext.written', @() names(out));
k0_do('pcode.inplace', @() pcode(fullfile(src, 'k3pc.m'), '-inplace'));
k0_try('pcode.inplace.src', @() names(src));
k0_try('pcode.inplace.out', @() names(out));
k0_do('pcode.syntax', @() pcode(fullfile(src, 'k3pbad.m')));
k0_try('pcode.syntax.written', @() names(out));
k0_do('pcode.missing', @() pcode(fullfile(src, 'nosuch.m')));
k0_do('pcode.missing.noext', @() pcode('k3nosuchfile'));
k0_do('pcode.noargs', @() pcode());
k0_try('pcode.nargout', @() pcode(fullfile(src, 'k3pf.m')));
k0_do('pcode.number', @() pcode(5));
k0_do('pcode.string', @() pcode(string(fullfile(src, 'k3plocal.m'))));
k0_do('pcode.two', @() pcode(fullfile(src, 'k3pdeep.m'), fullfile(src, 'k3pboth.m')));
k0_try('pcode.two.written', @() names(out));
k0_do('pcode.badopt', @() pcode(fullfile(src, 'k3pf.m'), '-nosuch'));
k0_do('pcode.r2022a', @() pcode(fullfile(src, 'k3pf.m'), '-R2022a'));
k0_do('pcode.r2007b', @() pcode(fullfile(src, 'k3pf.m'), '-R2007b'));
k0_do('pcode.folder', @() pcode(src));
k0_try('pcode.folder.written', @() names(out));
k0_do('pcode.wild', @() pcode(fullfile(src, 'k3pl*.m')));
k0_do('pcode.txt', @() pcode(fullfile(src, 'notes.txt')));
fclose(fopen(fullfile(src, 'notes.txt'), 'w'));
k0_do('pcode.txt.exists', @() pcode(fullfile(src, 'notes.txt')));
k0_do('pcode.cellarg', @() pcode({fullfile(src, 'k3pf.m')}));

% The P-file's bytes: the header, its length against the source's.
k0_try('pfile.head', @() head(fullfile(out, 'k3pf.p'), 16));
k0_try('pfile.length.vs.source', @() [len(fullfile(out, 'k3pf.p')) len(fullfile(src, 'k3pf.m'))]);
pcode(fullfile(src, 'k3pf.m'), '-R2007b');
k0_try('pfile.r2007b.head', @() head(fullfile(out, 'k3pf.p'), 16));
k0_try('pfile.r2007b.length', @() len(fullfile(out, 'k3pf.p')));
pcode(fullfile(src, 'k3pf.m'));
a1 = bytes(fullfile(out, 'k3pf.p'));
pcode(fullfile(src, 'k3pf.m'));
k0_try('pfile.same.twice', @() isequal(a1, bytes(fullfile(out, 'k3pf.p'))));

% Running a P-file with no source beside it (out holds only P-files).
k0_try('exist.pfile', @() exist('k3pf'));
k0_try('exist.pfile.file', @() exist('k3pf', 'file'));
k0_try('which.pfile', @() strrep(which('k3pf'), work, '<W>'));
k0_try('call.pfile', @() k3pf(1));
k0_try('call.pfile.mfilename', @() nthout(2, @() k3pf(1)));
k0_try('call.pfile.fullpath', @() strrep(nthout(2, @() k3pf(1, 2)), work, '<W>'));
k0_try('nargin.pfile', @() nargin('k3pf'));
k0_try('nargout.pfile', @() nargout('k3pf'));
k0_try('error.pfile', @() errinfo(@() k3pf(-2), work));
k0_try('error.pfile.deep', @() errinfo(@() k3pdeep, work));
k0_try('help.pfile.nosource', @() strtrim(evalc('help k3pf')));
k0_try('type.pfile', @() strtrim(evalc('type k3pf')));
k0_try('type.pfile.p', @() strtrim(evalc('type k3pf.p')));
k0_try('fileread.pfile.head', @() double(subsref(fileread(fullfile(out, 'k3pf.p')), substruct('()', {1:8}))));
k0_try('script.pfile', @() strtrim(evalc('k3ps')));
k0_try('local.pfile', @() k3plocal);
k0_try('func2str.pfile', @() func2str(@k3pf));
k0_try('functions.pfile', @() strrep(getfield(functions(@k3pf), 'file'), work, '<W>'));
k0_try('dbtype.pfile', @() strtrim(evalc('dbtype k3pf')));
k0_try('which.all.pfile', @() strrep(strjoin(which('k3pf', '-all'), ';'), work, '<W>'));
k0_try('exist.pfile.withext', @() exist('k3pf.p'));
k0_try('exist.pfile.withext.file', @() exist('k3pf.p', 'file'));
k0_try('run.pfile', @() strtrim(evalc('run(''k3ps.p'')')));
k0_try('run.pfile.noext', @() strtrim(evalc('run(fullfile(pwd, ''k3ps''))')));
k0_try('feval.pfile.name', @() feval('k3pf', 4));
k0_try('str2func.pfile', @() feval(str2func('k3pf'), 5));
k0_try('builtin.pfile', @() builtin('k3pf', 1));
k0_try('inmem.pfile', @() any(strcmp(inmem, 'k3pf')));
k0_try('getReport.pfile', @() strrep(rep(@() k3pf(-3)), work, '<W>'));

% The class's P-file sits beside its source (-inplace).
cd(src);
k0_try('class.pfile.both', @() twice(k3pc));
k0_try('which.class.both', @() strrep(which('k3pc'), work, '<W>'));
cd(out);

% A P-file beside its source: which wins, and what help and a stale source do.
both = fullfile(work, 'both');
mkdir(both);
copyfile(fullfile(src, 'k3pboth.m'), both);
cd(both);
pcode('k3pboth.m');
k0_try('both.names', @() names(both));
k0_try('both.call', @() k3pboth);
k0_try('both.which', @() strrep(which('k3pboth'), work, '<W>'));
k0_try('both.which.all', @() strrep(strjoin(which('k3pboth', '-all'), ';'), work, '<W>'));
k0_try('both.exist', @() exist('k3pboth'));
put(both, 'k3pboth.m', {'function r = k3pboth', 'r = ''from newer m'';', 'end'});
setmtime(fullfile(both, 'k3pboth.m'));
clear k3pboth
rehash
k0_try('both.stale.call', @() k3pboth);
k0_try('both.stale.again', @() k3pboth);
copyfile(fullfile(src, 'k3pf.m'), both);
pcode('k3pf.m');
clear k3pf
k0_try('both.help', @() strtrim(evalc('help k3pf')));
k0_try('both.which.pf', @() strrep(which('k3pf'), work, '<W>'));
delete(fullfile(both, 'k3pf.m'));
clear k3pf
rehash
k0_try('both.help.after.delete', @() strtrim(evalc('help k3pf')));

% A P-file whose bytes were changed.
cd(out);
b = bytes(fullfile(out, 'k3plocal.p'));
b(end - 3) = bitxor(b(end - 3), 1);
w(fullfile(out, 'k3plocal.p'), b);
clear k3plocal
rehash
k0_try('tampered.call', @() k3plocal);
w(fullfile(out, 'k3plocal.p'), b(1:20));
clear k3plocal
rehash
k0_try('truncated.call', @() k3plocal);
w(fullfile(out, 'k3pjunk.p'), uint8('not a pfile at all'));
rehash
k0_try('junk.call', @() k3pjunk);
k0_try('junk.exist', @() exist('k3pjunk'));

% A script's P-file run in this workspace, outside an anonymous function.
t = evalc('k3ps');
fprintf('script.pfile.direct\t%s\t%d\n', k0_flat(strtrim(t)), k3ps_v);
k0_do('pcode.both.flags', @() pcode(fullfile(src, 'k3pf.m'), '-R2022a', '-R2007b'));
k0_do('pcode.nodebug.alone', @() pcode(fullfile(src, 'k3pf.m'), '-nodebug'));
k0_do('pcode.nodebug', @() pcode(fullfile(src, 'k3pf.m'), '-R2022a', '-nodebug'));
k0_do('pcode.nocheck.syntax', @() pcode(fullfile(src, 'k3pbad.m'), '-nocheck'));
k0_try('pcode.nocheck.written', @() names(out));
k0_try('nocheck.call', @() k3pbad);
k0_do('pcode.dotdot', @() pcode('..\src\k3pf.m'));
k0_do('pcode.qmark', @() pcode('k3p?.m'));
k0_do('pcode.notpath', @() pcode(fullfile(work, 'nosuchdir', 'k3pf.m')));
k0_do('pcode.dironly', @() pcode([src filesep]));
k0_do('pcode.upper.M', @() pcode(fullfile(src, 'k3pf.M')));
k0_do('pcode.p.ext', @() pcode(fullfile(src, 'k3pf.p')));
mkdir(fullfile(src, 'private'));
put(fullfile(src, 'private'), 'k3ppriv.m', {'function r = k3ppriv', 'r = 1;', 'end'});
k0_do('pcode.private', @() pcode(fullfile(src, 'private', 'k3ppriv.m')));
k0_try('pcode.private.written', @() names(fullfile(out, 'private')));
ids = {
    {'MATLAB:pcode:BadPath', 'ARG'}
    {'MATLAB:pcode:Ambiguous', 'ARG'}
    {'MATLAB:pcode:NotPath', 'ARG'}
    {'MATLAB:pcode:NotFileName', 'ARG'}
    {'MATLAB:pcode:IncompatibleFlags'}
    {'MATLAB:pcode:R2022aRequired'}
    {'MATLAB:pcode:FileNotFound', 'ARG'}
    {'MATLAB:pcode:FileUnsupported', 'ARG'}
    {'MATLAB:pcode:UnknownFlag', 'ARG'}
    {'MATLAB:pcode:ArgMustBeString'}
    {'MATLAB:corruptPfile_3'}
    {'MATLAB:shortPfileFormat', 'ARG'}
    {'MATLAB:pfileOlderThanMfile', 'P', 'M', 'P'}
    };
for i = 1:numel(ids)
    a = ids{i};
    k0_try(['msg.' a{1}], @() getString(message(a{:})));
end
cd(here);
end

function put(folder, name, lines)
fid = fopen(fullfile(folder, name), 'w');
fprintf(fid, '%s\n', lines{:});
fclose(fid);
end

function s = names(folder)
d = dir(folder);
d = d(~[d.isdir]);
s = strjoin(sort({d.name}), ' ');
end

function n = len(f)
d = dir(f);
n = d.bytes;
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

function s = head(f, n)
b = bytes(f);
b = b(1:min(n, numel(b)));
s = sprintf('%02x', b);
printable = char(b);
printable(b < 32 | b > 126) = '.';
s = [s ' ' printable];
end

function v = nthout(k, f)
outs = cell(1, k);
[outs{:}] = f();
v = outs{k};
end

function s = errinfo(f, work)
try
    f();
    s = 'no error';
catch e
    st = e.stack;
    parts = arrayfun(@(t) sprintf('%s:%s:%d', strrep(t.file, work, '<W>'), t.name, t.line), st(1:min(3, end)), 'UniformOutput', false);
    s = sprintf('%s | %s | %s', e.identifier, e.message, strjoin(parts, ' ; '));
end
end

function s = rep(f)
try
    f();
    s = 'no error';
catch e
    s = strrep(e.getReport('extended', 'hyperlinks', 'off'), newline, '\n');
end
end

function setmtime(f)
% Make the source newer than its P-file by a few seconds.
pause(2.1);
fid = fopen(f, 'a');
fprintf(fid, '%% touched\n');
fclose(fid);
end
