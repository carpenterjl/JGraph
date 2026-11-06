% p5_analyzer - what R2025b's Code Analyzer says over files in +package and @Class folders and over
% files whose function or class is not named after the file (packages and class folders plan, stage
% P5): each message's id, line, columns, text and fix through checkcode -id -struct; the name forms
% checkcode and codeIssues take for such files; fix over the file-name check; and edit headless.
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name p5_analyzer
here = fileparts(mfilename('fullpath'));
work = fullfile(here, 'rt', 'p5');
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
plain = fullfile(work, 'plain');
lib = fullfile(work, 'lib');
mkdir(plain);
mkdir(fullfile(lib, '@Thing', 'private'));
mkdir(fullfile(lib, '@Mis'));
mkdir(fullfile(lib, '@Lone'));
mkdir(fullfile(lib, '+pkg', '@D'));
mkdir(fullfile(lib, '+pkg', 'private'));
short = @(s) strrep(strrep(char(s), work, '<work>'), strrep(work, '\', '/'), '<work>');

% --- the files --------------------------------------------------------------------------------------
writeFile(fullfile(plain, 'misnamed.m'), {'function r = othername(x)', 'r = x;', 'end'});
writeFile(fullfile(plain, 'casename.m'), {'function r = Casename(x)', 'r = x;', 'end'});
writeFile(fullfile(plain, 'noargs.m'), {'function othernoargs', 'disp(1)', 'end'});
writeFile(fullfile(plain, 'twoout.m'), {'function [a, b] = othertwo(x)', 'a = x;', 'b = x;', 'end'});
writeFile(fullfile(plain, 'withlocal.m'), {'function r = otherlocal(x)', 'r = helper(x);', 'end', 'function y = helper(z)', 'y = z;', 'end'});
writeFile(fullfile(plain, 'nested.m'), {'function r = othernested(x)', 'r = inner(x);', '    function y = inner(z)', '    y = z;', '    end', 'end'});
writeFile(fullfile(plain, 'dotted.m'), {'function r = pkg.dotted(x)', 'r = x;', 'end'});
writeFile(fullfile(plain, 'Wrongclass.m'), {'classdef Other', 'end'});
writeFile(fullfile(plain, 'Wrongclass2.m'), {'classdef (Sealed) Other2 < handle', '    properties', '        A', '    end', 'end'});
writeFile(fullfile(plain, 'scriptfile.m'), {'x = 1;', 'disp(x)'});
writeFile(fullfile(plain, 'good.m'), {'function r = good(x)', 'r = x;', 'end'});
writeFile(fullfile(plain, 'noend.m'), {'function r = othernoend(x)', 'r = x;'});
writeFile(fullfile(lib, '@Thing', 'Thing.m'), { ...
    'classdef Thing', ...
    '    properties', ...
    '        W = 1', ...
    '    end', ...
    '    methods', ...
    '        function o = Thing(w)', ...
    '            if nargin > 0, o.W = w; end', ...
    '        end', ...
    '        r = extent(o)', ...
    '        r = scale(o, k)', ...
    '        [a, b] = two(o)', ...
    '        noout(o)', ...
    '        bare', ...
    '        function r = inline1(o)', ...
    '            r = o.W;', ...
    '        end', ...
    '    end', ...
    '    methods (Static)', ...
    '        r = staticOne(x)', ...
    '    end', ...
    '    methods (Access = private)', ...
    '        r = secret(o)', ...
    '    end', ...
    '    methods (Abstract)', ...
    '        r = abs1(o)', ...
    '    end', ...
    'end'});
writeFile(fullfile(lib, '@Thing', 'extent.m'), {'function r = extent(o)', 'r = o.W * 2;', 'end'});
writeFile(fullfile(lib, '@Thing', 'scale.m'), {'function r = scale(o, k)', 'r = o.W * k;', 'end'});
writeFile(fullfile(lib, '@Thing', 'misnamed.m'), {'function r = othername(o)', 'r = o.W;', 'end'});
writeFile(fullfile(lib, '@Thing', 'undeclared.m'), {'function r = undeclared(o)', 'r = o.W;', 'end'});
writeFile(fullfile(lib, '@Thing', 'staticOne.m'), {'function r = staticOne(x)', 'r = x;', 'end'});
writeFile(fullfile(lib, '@Thing', 'private', 'p5hidden.m'), {'function r = p5hidden(o)', 'r = o.W;', 'end'});
writeFile(fullfile(lib, '@Thing', 'private', 'misprivate.m'), {'function r = otherprivate(o)', 'r = o.W;', 'end'});
writeFile(fullfile(lib, '@Mis', 'Mis.m'), {'classdef Wrong', 'end'});
writeFile(fullfile(lib, '@Lone', 'method1.m'), {'function r = other1(o)', 'r = o;', 'end'});
writeFile(fullfile(lib, '+pkg', 'f.m'), {'function r = f(x)', 'r = x * 10;', 'end'});
writeFile(fullfile(lib, '+pkg', 'g.m'), {'function r = g(x)', 'r = f(x);', 'end'});
writeFile(fullfile(lib, '+pkg', 'misnamed.m'), {'function r = othername(x)', 'r = x;', 'end'});
writeFile(fullfile(lib, '+pkg', 'C.m'), {'classdef C', '    methods (Static)', '        function r = stat(x)', '            r = x;', '        end', '    end', 'end'});
writeFile(fullfile(lib, '+pkg', 'Wrongc.m'), {'classdef C', 'end'});
writeFile(fullfile(lib, '+pkg', 's.m'), {'disp(1)'});
writeFile(fullfile(lib, '+pkg', '@D', 'D.m'), {'classdef D', '    methods', '        r = dm(o)', '    end', 'end'});
writeFile(fullfile(lib, '+pkg', '@D', 'dm.m'), {'function r = dm(o)', 'r = o;', 'end'});
writeFile(fullfile(lib, '+pkg', '@D', 'wrongdm.m'), {'function r = otherdm(o)', 'r = o;', 'end'});
writeFile(fullfile(lib, '+pkg', 'private', 'ph.m'), {'function r = ph(x)', 'r = x;', 'end'});
writeFile(fullfile(lib, '+pkg', 'private', 'wrongph.m'), {'function r = otherph(x)', 'r = x;', 'end'});
writeFile(fullfile(lib, 'Both.m'), {'function r = Both(x)', 'r = x;', 'end'});

addpath(lib);

% --- every file through checkcode -id -struct -------------------------------------------------------
fprintf('== messages\n');
files = {};
for d = {plain, lib, fullfile(lib, '@Thing'), fullfile(lib, '@Thing', 'private'), fullfile(lib, '@Mis'), fullfile(lib, '@Lone'), fullfile(lib, '+pkg'), fullfile(lib, '+pkg', '@D'), fullfile(lib, '+pkg', 'private')}
    L = dir(fullfile(d{1}, '*.m'));
    for i = 1:numel(L)
        files{end+1} = fullfile(d{1}, L(i).name); %#ok<AGROW>
    end
end
for i = 1:numel(files)
    label = short(files{i});
    try
        m = checkcode(files{i}, '-id', '-struct');
        printMessages(label, m);
    catch e
        fprintf('ERR|%s|%s|%s\n', label, e.identifier, short(e.message));
    end
end

% --- the display forms over the file-name check -----------------------------------------------------
fprintf('== display\n');
show('checkcode plain/misnamed.m', @() evalc('checkcode(fullfile(plain, ''misnamed.m''))'), short);
show('checkcode -id plain/misnamed.m', @() evalc('checkcode(fullfile(plain, ''misnamed.m''), ''-id'')'), short);
show('mlint plain/misnamed.m', @() evalc('mlint(fullfile(plain, ''misnamed.m''))'), short);
show('checkcode -string Wrongclass.m', @() checkcode(fullfile(plain, 'Wrongclass.m'), '-string'), short);
show('checkcode -struct -severity misnamed', @() evalc('disp(checkcode(fullfile(plain, ''misnamed.m''), ''-id'', ''-struct'', ''-severity''))'), short);

% --- the name forms ---------------------------------------------------------------------------------
fprintf('== name forms (lib on the path, cwd = plain)\n');
old = cd(plain);
refuse('checkcode(''pkg.f'')', @() checkcode('pkg.f'), short);
refuse('checkcode(''pkg.C'')', @() checkcode('pkg.C'), short);
refuse('checkcode(''Thing/extent'')', @() checkcode('Thing/extent'), short);
refuse('checkcode(''Thing'')', @() checkcode('Thing'), short);
refuse('checkcode(''extent'')', @() checkcode('extent'), short);
refuse('checkcode(''Both'')', @() checkcode('Both'), short);
refuse('checkcode(''misnamed'')', @() checkcode('misnamed'), short);
refuse('checkcode(''othername'')', @() checkcode('othername'), short);
refuse('checkcode(''+pkg/g'')', @() checkcode('+pkg/g'), short);
refuse('checkcode(''@Thing/extent'')', @() checkcode('@Thing/extent'), short);
refuse('codeIssues(''pkg.f'')', @() codeIssues('pkg.f'), short);
refuse('codeIssues(''Thing/extent'')', @() codeIssues('Thing/extent'), short);
cd(lib);
fprintf('== name forms (cwd = lib)\n');
refuse('checkcode(''+pkg/g'')', @() checkcode('+pkg/g'), short);
refuse('checkcode(''+pkg/g.m'')', @() checkcode('+pkg/g.m'), short);
refuse('checkcode(''@Thing/extent'')', @() checkcode('@Thing/extent'), short);
refuse('checkcode(''@Thing/misnamed'')', @() evalc('checkcode(''@Thing/misnamed'')'), short);
refuse('checkcode(''Thing/extent'')', @() checkcode('Thing/extent'), short);
refuse('checkcode(''pkg/g'')', @() checkcode('pkg/g'), short);
refuse('checkcode(''Both'')', @() checkcode('Both'), short);
[~, names] = checkcode({'+pkg/g.m', '@Thing/extent.m'});
fprintf('two files: %s\n', strjoin(cellfun(short, names', 'UniformOutput', false), ' ; '));
cd(old);

% --- codeIssues over the folders ----------------------------------------------------------------------
fprintf('== codeIssues\n');
try
    q = codeIssues(lib);
    fprintf('Files (%d): %s\n', numel(q.Files), strjoin(cellfun(short, cellstr(q.Files)', 'UniformOutput', false), ' ; '));
    T = q.Issues;
    for i = 1:height(T)
        fprintf('ISSUE|%s|%s|%d|%d|%d|%s\n', short(T.FullFilename(i)), string(T.CheckID(i)), T.LineStart(i), T.ColumnStart(i), T.ColumnEnd(i), T.Description(i));
    end
catch e
    fprintf('ERR|%s|%s\n', e.identifier, short(e.message));
end
try
    q = codeIssues(fullfile(lib, '@Thing'));
    fprintf('@Thing Files (%d): %s\n', numel(q.Files), strjoin(cellfun(short, cellstr(q.Files)', 'UniformOutput', false), ' ; '));
catch e
    fprintf('ERR|%s|%s\n', e.identifier, short(e.message));
end
try
    q = codeIssues(fullfile(lib, '+pkg'));
    fprintf('+pkg Files (%d): %s\n', numel(q.Files), strjoin(cellfun(short, cellstr(q.Files)', 'UniformOutput', false), ' ; '));
catch e
    fprintf('ERR|%s|%s\n', e.identifier, short(e.message));
end

% --- fix over the file-name check ---------------------------------------------------------------------
fprintf('== fix\n');
fixDir = fullfile(work, 'fix');
mkdir(fixDir);
copyfile(fullfile(plain, 'misnamed.m'), fixDir);
copyfile(fullfile(plain, 'Wrongclass.m'), fixDir);
try
    cfx = codeIssues(fullfile(fixDir, 'misnamed.m'));
    fprintf('misnamed Fixability: %s\n', strjoin(cellstr(string(cfx.Issues.Fixability))', ','));
    [cfx, status] = fix(cfx, 'FNDEF');
    disp(status);
    L = readlines(fullfile(fixDir, 'misnamed.m'));
    fprintf('after fix line 1: %s\n', L(1));
catch e
    fprintf('ERR|%s|%s\n', e.identifier, short(e.message));
end
try
    cfx = codeIssues(fullfile(fixDir, 'Wrongclass.m'));
    fprintf('Wrongclass CheckID: %s Fixability: %s\n', strjoin(cellstr(string(cfx.Issues.CheckID))', ','), strjoin(cellstr(string(cfx.Issues.Fixability))', ','));
    cfx = fix(cfx, cellstr(string(cfx.Issues.CheckID)));
    L = readlines(fullfile(fixDir, 'Wrongclass.m'));
    fprintf('after fix line 1: %s\n', L(1));
catch e
    fprintf('ERR|%s|%s\n', e.identifier, short(e.message));
end

% --- edit headless -------------------------------------------------------------------------------------
fprintf('== edit\n');
cd(plain);
refuse('edit(''pkg.f'')', @() evalc('edit(''pkg.f'')'), short);
refuse('edit pkg.f', @() evalc('edit pkg.f'), short);
refuse('edit(''Thing/extent'')', @() evalc('edit(''Thing/extent'')'), short);
refuse('edit(''Thing'')', @() evalc('edit(''Thing'')'), short);
refuse('edit(''pkg.C.stat'')', @() evalc('edit(''pkg.C.stat'')'), short);
refuse('edit(''nosuch_p5_name'')', @() evalc('edit(''nosuch_p5_name'')'), short);
fprintf('nosuch created: %d\n', exist(fullfile(plain, 'nosuch_p5_name.m'), 'file'));
refuse('edit(''misnamed'')', @() evalc('edit(''misnamed'')'), short);
refuse('r = edit(''good'')', @() evalc('r = edit(''good'')'), short);
refuse('edit(1)', @() edit(1), short);
refuse('edit(''a'', ''b'')', @() evalc('edit(''a'', ''b'')'), short);
fprintf('a created: %d b created: %d\n', exist(fullfile(plain, 'a.m'), 'file'), exist(fullfile(plain, 'b.m'), 'file'));
fprintf('nargin edit %d nargout edit %d exist %d\n', nargin('edit'), nargout('edit'), exist('edit'));
cd(old);
rmpath(lib);
fprintf('done\n');

% --- helpers ------------------------------------------------------------------------------------------
function writeFile(path, lines)
f = fopen(path, 'w');
fprintf(f, '%s\n', lines{:});
fclose(f);
end

function printMessages(label, m)
for i = 1:numel(m)
    fprintf('MSG|%s|%s|%s|%s|%s|%s\n', label, m(i).id, mat2str(m(i).line), mat2str(m(i).column), mat2str(m(i).fix), m(i).message);
end
if isempty(m), fprintf('MSG|%s|none\n', label); end
end

function show(label, fn, short)
fprintf('== %s\n', label);
try
    t = fn();
    fprintf('%s<EOS>\n', short(t));
catch e
    fprintf('ERR|%s|%s\n', e.identifier, short(e.message));
end
end

function refuse(label, fn, short)
try
    r = fn();
    if ischar(r), fprintf('ACCEPTED: %s -> %s<EOS>\n', label, short(r)); else, fprintf('ACCEPTED: %s -> %s\n', label, class(r)); end
catch e
    fprintf('REFUSED: %s -> %s: %s\n', label, e.identifier, short(e.message));
end
end
