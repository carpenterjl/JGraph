% w0_debug - how R2025b's debugger names an App Designer app (stage W0 of the App Designer window
% plan), headless:
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w0_debug
% A -batch session cannot pause, so this records what dbstop accepts for an .mlapp, how dbstatus
% reports it, the code lines those numbers count, and the dbstop if conditions' words. Pausing in an
% app (dbstack, K>>) needs a window session and is left to W11.
here = fileparts(mfilename('fullpath'));
src = fullfile(fileparts(here), 'ds8', 'rt', 'accept');
work = fullfile(here, 'rt', 'debug');
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
copyfile(fullfile(src, 'AcceptForm.mlapp'), work);
copyfile(fullfile(src, 'AcceptFormPlain.m'), work);
addpath(work);
short = @(s) strrep(char(s), work, '<debug>');

code = appdesigner.internal.codegeneration.getAppFileCode(fullfile(work, 'AcceptForm.mlapp'));
lines = splitlines(string(code));
fprintf('== AcceptForm.mlapp code: %d lines\n', numel(lines));
cb = find(contains(lines, 'function ComputeButtonPushed'), 1);
fprintf('callback at line %d: %s\n', cb, strtrim(lines(cb)));
for k = cb:cb + 4, fprintf('%4d  %s\n', k, lines(k)); end
fprintf('which AcceptForm: %s\n', short(which('AcceptForm')));
fprintf('exist AcceptForm: %d\n', exist('AcceptForm')); %#ok<EXIST>

tries = {
    {'in', 'AcceptForm', 'at', num2str(cb + 1)}
    {'in', 'AcceptForm.mlapp', 'at', num2str(cb + 2)}
    {'in', fullfile(work, 'AcceptForm.mlapp'), 'at', num2str(cb + 3)}
    {'in', 'AcceptForm>AcceptForm.ComputeButtonPushed'}
    {'in', 'AcceptForm', 'at', '1'}
    {'in', 'AcceptForm', 'at', num2str(cb + 1), 'if', 'app.EditField.Value > 3'}
    {'in', 'AcceptForm', 'at', num2str(numel(lines) + 50)}
    {'in', 'AcceptFormPlain', 'at', '3'}
    };
for k = 1:numel(tries)
    t = tries{k};
    lastwarn('');
    try
        dbstop(t{:});
        [w, id] = lastwarn;
        fprintf('dbstop %s -> ok%s\n', short(strjoin(t, ' ')), iff(isempty(w), '', [' (warning ' id ': ' short(w) ')']));
    catch e
        fprintf('dbstop %s -> %s: %s\n', short(strjoin(t, ' ')), e.identifier, short(e.message));
    end
end
fprintf('== dbstatus struct\n');
s = dbstatus;
for k = 1:numel(s)
    fprintf('  name %s | file %s | line %s | anonymous %s | expression %s | cond %s | identifier %s\n', ...
        s(k).name, short(s(k).file), mat2str(s(k).line), mat2str(s(k).anonymous), ...
        strjoin(cellfun(@char, s(k).expression, 'UniformOutput', false), ';'), s(k).cond, ...
        strjoin(cellfun(@char, s(k).identifier, 'UniformOutput', false), ';'));
end
fprintf('== dbstatus printed\n');
fprintf('%s', short(evalc('dbstatus')));
fprintf('== dbstatus AcceptForm printed\n');
fprintf('%s', short(evalc('dbstatus AcceptForm')));
dbclear all

for c = {{'if', 'error'}, {'if', 'warning'}, {'if', 'naninf'}, {'if', 'caught', 'error'}, ...
        {'if', 'error', 'MATLAB:badsubscript'}, {'if', 'warning', 'MATLAB:singularMatrix'}}
    dbstop(c{1}{:});
end
fprintf('== dbstatus after the conditions\n');
fprintf('%s', evalc('dbstatus'));
s = dbstatus;
for k = 1:numel(s)
    fprintf('  cond %s | identifier %s\n', s(k).cond, strjoin(cellfun(@char, s(k).identifier, 'UniformOutput', false), ';'));
end
dbclear if error
fprintf('== after dbclear if error\n');
fprintf('%s', evalc('dbstatus'));
dbclear all
fprintf('== after dbclear all: %d entries\n', numel(dbstatus));
for c = {{'if', 'bogus'}, {'in', 'NoSuchFunctionW0', 'at', '3'}, {'at', '3'}}
    try
        dbstop(c{1}{:});
        fprintf('dbstop %s -> ok\n', strjoin(c{1}, ' '));
    catch e
        fprintf('dbstop %s -> %s: %s\n', strjoin(c{1}, ' '), e.identifier, e.message);
    end
end
try
    dbclear('in', 'NoSuchFunctionW0');
    fprintf('dbclear in NoSuchFunctionW0 -> ok\n');
catch e
    fprintf('dbclear in NoSuchFunctionW0 -> %s: %s\n', e.identifier, e.message);
end
rmpath(work);
fprintf('done\n');

function v = iff(c, a, b)
if c, v = a; else, v = b; end
end
