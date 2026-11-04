% w11_debug - the debugger's words R2025b prints headless (App Designer window plan, stage W11), past
% what w0_debug recorded for an .mlapp: dbstop, dbclear and dbstatus over a function file with a local
% function, a script, a class file and the plain-text acceptance app; where a breakpoint on a line
% without code lands; the functional forms; the refusals; dbstop if's identifiers; and what dbcont,
% dbstep, dbquit, dbstack, dbup, dbdown and dbtype say at the prompt. Pausing itself needs a window
% session and is not recorded here.
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name w11_debug
here = fileparts(mfilename('fullpath'));
src = fullfile(fileparts(here), 'ds8', 'rt', 'accept');
work = fullfile(here, 'rt', 'debug');
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
copyfile(fullfile(src, 'AcceptForm.mlapp'), work);
copyfile(fullfile(src, 'AcceptFormPlain.m'), work);
writeFile(fullfile(work, 'w11fun.m'), sprintf(['function y = w11fun(x)\n%% a comment\n\ny = helper(x) + 1;\n\n' ...
    'z = y * 2;\ny = z;\nend\n\nfunction r = helper(x)\n%% help\nr = x + 1;\nend\n']));
writeFile(fullfile(work, 'w11script.m'), sprintf('%% script\na = 1;\n\nb = a + 1;\ndisp(b)\n'));
writeFile(fullfile(work, 'W11Class.m'), sprintf(['classdef W11Class\n    properties\n        X = 1\n    end\n' ...
    '    methods\n        function obj = W11Class(v)\n            obj.X = v;\n        end\n' ...
    '        function r = twice(obj)\n            r = obj.X * 2;\n        end\n    end\nend\n']));
addpath(work);
cd(work);
short = @(s) strrep(char(s), work, '<debug>');

fprintf('== function file: where breakpoints land\n');
tryStop('in', 'w11fun');                      % no line: the first executable line
status(short);
dbclear all
tryStop('in', 'w11fun', 'at', '2');           % a comment
tryStop('in', 'w11fun', 'at', '3');           % blank
tryStop('in', 'w11fun', 'at', '5');           % blank before z =
tryStop('in', 'w11fun', 'at', '8');           % end
tryStop('in', 'w11fun', 'at', '9');           % blank between functions
tryStop('in', 'w11fun', 'at', '11');          % a comment in helper
status(short);
dbclear all
tryStop('in', 'w11fun', 'at', '4');
tryStop('in', 'w11fun', 'at', '4');           % twice: one entry?
tryStop('in', 'w11fun.m', 'at', '6');         % with the extension
tryStop('in', 'w11fun>helper');               % the local function
tryStop('in', 'w11fun>helper', 'at', '12');
tryStop('in', fullfile(work, 'w11fun.m'), 'at', '7');
status(short);
fprintf('== dbstatus w11fun, dbstatus(''w11fun''), -completenames\n');
fprintf('%s', short(evalc('dbstatus w11fun')));
s = dbstatus('w11fun'); fprintf('struct: %s\n', short(describe(s)));
fprintf('%s', short(evalc('dbstatus -completenames')));
s = dbstatus('-completenames'); fprintf('struct: %s\n', short(describe(s)));
fprintf('== dbclear forms\n');
tryClear('in', 'w11fun', 'at', '4');
status(short);
tryClear('in', 'w11fun>helper');
status(short);
tryClear('in', 'w11fun');
status(short);
tryClear('in', 'w11fun');                      % nothing left to clear
tryClear('all', 'in', 'w11fun');
tryClear('in', 'NoSuchW11');
tryClear('in', 'w11fun', 'at', '100');
tryClear('if', 'error');                        % none set
dbclear all

fprintf('== conditions on a line\n');
tryStop('in', 'w11fun', 'at', '4', 'if', 'x > 1');
status(short);
tryStop('in', 'w11fun', 'at', '4', 'if', 'x > 2');   % replaced?
status(short);
tryStop('in', 'w11fun', 'at', '4');                   % plain again: condition kept or dropped?
status(short);
tryStop('in', 'w11fun', 'at', '6', 'if', 'y > 1 && x < 3');
s = dbstatus; fprintf('struct: %s\n', short(describe(s)));
dbclear all

fprintf('== refusals\n');
tryStop();
tryStop('in');
tryStop('at');
tryStop('at', '3');
tryStop('if');
tryStop('in', 'w11fun', 'at');
tryStop('in', 'w11fun', 'at', '0');
tryStop('in', 'w11fun', 'at', '-1');
tryStop('in', 'w11fun', 'at', 'abc');
tryStop('in', 'w11fun', 'at', '4', 'if');
tryStop('in', 'w11fun', 'at', '100');
tryStop('in', 'w11fun', 'at', '13');           % the last end
tryStop('in', 'w11fun', 'at', '12', 'extra');
tryStop('in', 'NoSuchW11', 'at', '3');
tryStop('in', 'w11fun>nosuch');
tryStop('in', 'w11fun', 'at', '4', 'if', '');
tryStop('bogus', 'w11fun');
tryStop('in', 42);
tryStop('in', 'w11fun', 'at', 4);
tryStop('if', 'error', 'MATLAB:a', 'MATLAB:b');
tryStop('if', 'warning', 'nocolon');
dbclear all

fprintf('== functional forms\n');
tryStop('w11fun', '4');
status(short);
dbclear all
tryStop('w11fun', 4);
tryStop('w11fun');
status(short);
dbclear all
tryStop("in", "w11fun", "at", "6");
status(short);
dbclear all

fprintf('== a script and a class\n');
tryStop('in', 'w11script', 'at', '1');         % a comment
tryStop('in', 'w11script', 'at', '2');
tryStop('in', 'w11script');
tryStop('in', 'W11Class', 'at', '7');
tryStop('in', 'W11Class>W11Class.twice');
tryStop('in', 'W11Class', 'at', '3');          % a property default
tryStop('in', 'W11Class', 'at', '1');          % classdef line
tryStop('in', 'AcceptFormPlain', 'at', '29');  % startupFcn's body
tryStop('in', 'AcceptFormPlain>AcceptFormPlain.ComputeButtonPushed');
status(short);
s = dbstatus; fprintf('struct: %s\n', short(describe(s)));
dbclear all

fprintf('== dbstop if\n');
tryStop('if', 'error');
tryStop('if', 'error', 'MATLAB:badsubscript');
status(short);
tryStop('if', 'error', 'MATLAB:nomem');
tryStop('if', 'caught', 'error', 'MATLAB:badsubscript');
tryStop('if', 'warning', 'MATLAB:singularMatrix');
tryStop('if', 'warning');
tryStop('if', 'infnan');
tryStop('if', 'naninf');
status(short);
s = dbstatus; fprintf('struct: %s\n', short(describe(s)));
tryClear('if', 'error', 'MATLAB:nomem');
status(short);
tryClear('if', 'error');
status(short);
tryClear('if', 'warning', 'MATLAB:singularMatrix');
status(short);
tryClear('if', 'infnan');
status(short);
dbclear all
fprintf('== empty dbstatus\n');
s = dbstatus; fprintf('size %s class %s fields %s\n', mat2str(size(s)), class(s), strjoin(fieldnames(s)', ','));
fprintf('printed: [%s]\n', evalc('dbstatus'));
fprintf('dbstatus w11fun printed: [%s]\n', evalc('dbstatus w11fun'));
tryStop('if', 'error');
s = dbstatus; fprintf('one condition: %s\n', describe(s));
dbclear all

fprintf('== the prompt words outside the debugger\n');
for w = {'dbcont', 'dbstep', 'dbstep in', 'dbstep out', 'dbstep 3', 'dbquit', 'dbquit all', 'dbstack', 'dbup', 'dbdown', 'dbstack -completenames'}
    lastwarn('');
    try
        t = evalc(w{1});
        [wm, wid] = lastwarn;
        fprintf('%s -> [%s]%s\n', w{1}, strtrim(t), iff(isempty(wm), '', [' warning ' wid ': ' wm]));
    catch e
        fprintf('%s -> %s: %s\n', w{1}, e.identifier, e.message);
    end
end
try
    s = dbstack;
    fprintf('s = dbstack: size %s class %s fields %s\n', mat2str(size(s)), class(s), strjoin(fieldnames(s)', ','));
    [s, i] = dbstack;
    fprintf('[s, i] = dbstack: i = %s\n', mat2str(i));
catch e
    fprintf('s = dbstack -> %s: %s\n', e.identifier, e.message);
end
fprintf('== dbtype\n');
fprintf('%s', evalc('dbtype w11fun'));
fprintf('---\n%s', evalc('dbtype w11fun 4:6'));
fprintf('---\n%s', evalc('dbtype(''w11fun'', ''10:12'')'));
try
    evalc('dbtype NoSuchW11');
catch e
    fprintf('dbtype NoSuchW11 -> %s: %s\n', e.identifier, e.message);
end
try
    evalc('dbtype w11fun 50:60');
    fprintf('dbtype w11fun 50:60 -> [%s]\n', strtrim(evalc('dbtype w11fun 50:60')));
catch e
    fprintf('dbtype w11fun 50:60 -> %s: %s\n', e.identifier, e.message);
end
fprintf('%s', evalc('dbtype AcceptFormPlain 28:30'));
try
    fprintf('%s', evalc('dbtype AcceptForm 33:35'));
catch e
    fprintf('dbtype AcceptForm -> %s: %s\n', e.identifier, e.message);
end
fprintf('== nargout/exist\n');
for n = {'dbstop', 'dbclear', 'dbstatus', 'dbcont', 'dbstep', 'dbquit', 'dbstack', 'dbup', 'dbdown', 'dbtype'}
    try
        fprintf('%s: nargout %d exist %d which %s\n', n{1}, nargout(n{1}), exist(n{1}), short(which(n{1}))); %#ok<EXIST>
    catch e
        fprintf('%s: nargout -> %s | exist %d\n', n{1}, e.message, exist(n{1})); %#ok<EXIST>
    end
end
cd(here);
rmpath(work);
fprintf('done\n');

function tryStop(varargin)
lastwarn('');
try
    dbstop(varargin{:});
    [w, id] = lastwarn;
    fprintf('dbstop %s -> ok%s\n', words(varargin), iff(isempty(w), '', [' (warning ' id ': ' w ')']));
catch e
    fprintf('dbstop %s -> %s: %s\n', words(varargin), e.identifier, strrep(e.message, newline, ' | '));
end
end

function tryClear(varargin)
lastwarn('');
try
    dbclear(varargin{:});
    [w, id] = lastwarn;
    fprintf('dbclear %s -> ok%s\n', words(varargin), iff(isempty(w), '', [' (warning ' id ': ' w ')']));
catch e
    fprintf('dbclear %s -> %s: %s\n', words(varargin), e.identifier, strrep(e.message, newline, ' | '));
end
end

function t = words(args)
parts = cell(1, numel(args));
for k = 1:numel(args)
    a = args{k};
    if ischar(a) || isstring(a)
        parts{k} = char(a);
    else
        parts{k} = ['<' class(a) ' ' mat2str(a) '>'];
    end
end
t = strjoin(parts, ' ');
if isempty(t), t = '(nothing)'; end
end

function status(short)
t = evalc('dbstatus');
t = regexprep(t, '<a href="matlab: opentoline\(''([^'']*)'',(\d+),1\)">\d+</a>', '{$1:$2}');
fprintf('%s', short(t));
if isempty(t), fprintf('(dbstatus prints nothing)\n'); end
end

function t = describe(s)
if isempty(s)
    t = sprintf('empty %s', mat2str(size(s)));
    return
end
rows = cell(1, numel(s));
for k = 1:numel(s)
    e = s(k);
    expr = '';
    if isfield(e, 'expression') && ~isempty(e.expression)
        expr = strjoin(cellfun(@(c) ['"' char(c) '"'], e.expression, 'UniformOutput', false), ';');
    end
    ident = '';
    if isfield(e, 'identifier') && ~isempty(e.identifier)
        ident = strjoin(cellfun(@char, e.identifier, 'UniformOutput', false), ';');
    end
    rows{k} = sprintf('{name=%s file=%s line=%s anonymous=%s expression=[%s] cond=%s identifier=[%s]}', ...
        e.name, e.file, mat2str(e.line), mat2str(e.anonymous), expr, e.cond, ident);
end
t = strjoin(rows, ' ');
end

function writeFile(path, text)
f = fopen(path, 'w');
fprintf(f, '%s', text);
fclose(f);
end

function v = iff(c, a, b)
if c, v = a; else, v = b; end
end
