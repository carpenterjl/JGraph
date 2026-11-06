% p5_more - the follow-up to p5_analyzer: the display forms over the file-name checks, their
% severities, NOIN's variants, MCFIL's fix with a constructor, a signature line in a plain class
% file, %#ok<FNDEF>, -cyc over a misnamed file, and edit's refusals (never a name with no file:
% R2025b's edit asks to create it, even in -batch).
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name p5_more
here = fileparts(mfilename('fullpath'));
work = fullfile(here, 'rt', 'p5more');
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(work);
plain = fullfile(work, 'plain');
lib = fullfile(work, 'lib');
mkdir(plain);
mkdir(fullfile(lib, '@Thing'));
short = @(s) strrep(strrep(char(s), work, '<work>'), strrep(work, '\', '/'), '<work>');

writeFile(fullfile(plain, 'misnamed.m'), {'function r = othername(x)', 'r = x;', 'end'});
writeFile(fullfile(plain, 'commented.m'), {'% a comment first', '', 'function r = othercommented(x)', 'r = x;', 'end'});
writeFile(fullfile(plain, 'suppressed.m'), {'function r = othersupp(x) %#ok<FNDEF>', 'r = x;', 'end'});
writeFile(fullfile(plain, 'Wrongctor.m'), {'classdef Otherctor', '    methods', '        function o = Otherctor()', '            o;', '        end', '    end', 'end'});
writeFile(fullfile(plain, 'Sig.m'), {'classdef Sig', '    methods', '        r = m(o)', '        r = n(o, k)', '    end', 'end'});
writeFile(fullfile(plain, 'Noin.m'), { ...
    'classdef Noin', ...
    '    methods', ...
    '        function bodied', ...
    '            disp(1)', ...
    '        end', ...
    '        function r = bodiedout', ...
    '            r = 1;', ...
    '        end', ...
    '        sigline', ...
    '        r = sigout', ...
    '        function withtilde(~)', ...
    '            disp(2)', ...
    '        end', ...
    '    end', ...
    '    methods (Static)', ...
    '        function s()', ...
    '            disp(3)', ...
    '        end', ...
    '        t', ...
    '    end', ...
    '    methods (Abstract)', ...
    '        a', ...
    '    end', ...
    '    methods (Access = private)', ...
    '        function p', ...
    '            disp(4)', ...
    '        end', ...
    '    end', ...
    'end'});
writeFile(fullfile(plain, 'good.m'), {'function r = good(x)', 'r = x;', 'end'});
writeFile(fullfile(lib, '@Thing', 'Thing.m'), {'classdef Thing', '    methods', '        r = extent(o)', '    end', 'end'});
writeFile(fullfile(lib, '@Thing', 'extent.m'), {'function r = extent(o)', 'r = o;', 'end'});
writeFile(fullfile(lib, '@Thing', 'misnamed.m'), {'function r = othername(o)', 'r = o;', 'end'});
addpath(lib);

fprintf('== severities\n');
for f = {'misnamed.m', 'commented.m', 'suppressed.m', 'Wrongctor.m', 'Sig.m', 'Noin.m'}
    p = fullfile(plain, f{1});
    m = checkcode(p, '-id', '-struct', '-severity');
    if isempty(m), fprintf('MSG|%s|none\n', f{1}); end
    for i = 1:numel(m)
        fprintf('MSG|%s|%s|%s|%s|%s|%d|%s\n', f{1}, m(i).id, mat2str(m(i).line), mat2str(m(i).column), mat2str(m(i).fix), m(i).severity, m(i).message);
    end
end
m = checkcode(fullfile(lib, '@Thing', 'Thing.m'), '-id', '-struct', '-severity');
fprintf('Thing.m: %d messages\n', numel(m));
m = checkcode(fullfile(plain, 'suppressed.m'), '-id', '-struct', '-notok');
fprintf('suppressed -notok: %s\n', strjoin({m.id}, ','));
fprintf('fields: %s\n', strjoin(fieldnames(checkcode(fullfile(plain, 'misnamed.m'), '-struct', '-severity'))', ','));

fprintf('== display\n');
p = fullfile(plain, 'misnamed.m');
show('checkcode', @() evalc('checkcode(p)'), short);
show('checkcode -id', @() evalc('checkcode(p, ''-id'')'), short);
show('mlint', @() evalc('mlint(p)'), short);
show('checkcode -string', @() checkcode(p, '-string'), short);
show('checkcode -cyc', @() checkcode(p, '-string', '-cyc'), short);
q = fullfile(plain, 'Wrongctor.m');
show('checkcode -id Wrongctor', @() evalc('checkcode(q, ''-id'')'), short);
w = fullfile(plain, 'Noin.m');
show('checkcode -id Noin', @() evalc('checkcode(w, ''-id'')'), short);

fprintf('== name forms from plain\n');
old = cd(plain);
[~, n] = checkcode('Thing/extent'); fprintf('Thing/extent -> %s\n', short(n{1}));
[~, n] = checkcode('Thing'); fprintf('Thing -> %s\n', short(n{1}));
[~, n] = checkcode('extent'); fprintf('extent -> %s\n', short(n{1}));
[~, n] = checkcode('misnamed'); fprintf('misnamed -> %s\n', short(n{1}));
[~, n] = checkcode('@Thing/extent'); fprintf('@Thing/extent -> %s\n', short(n{1}));
[~, n] = checkcode('@Thing/extent.m'); fprintf('@Thing/extent.m -> %s\n', short(n{1}));
refuse('checkcode(''Thing.extent'')', @() checkcode('Thing.extent'), short);
refuse('checkcode(''Thing/nosuch'')', @() checkcode('Thing/nosuch'), short);
refuse('codeIssues(''misnamed'')', @() codeIssues('misnamed'), short);
refuse('codeIssues(''@Thing/extent.m'')', @() codeIssues('@Thing/extent.m'), short);
try
    r = codeIssues('misnamed'); fprintf('codeIssues(misnamed) Files: %s\n', short(strjoin(cellstr(r.Files)', ';')));
catch e
    fprintf('ERR %s\n', e.message);
end

fprintf('== fix\n');
fixDir = fullfile(work, 'fix');
mkdir(fixDir);
copyfile(fullfile(plain, 'Wrongctor.m'), fixDir);
copyfile(fullfile(plain, 'commented.m'), fixDir);
cfx = codeIssues(fullfile(fixDir, 'Wrongctor.m'));
cfx = fix(cfx, 'MCFIL');
L = readlines(fullfile(fixDir, 'Wrongctor.m'));
fprintf('Wrongctor after fix: %s\n', strjoin(cellstr(L(1:3))', ' / '));
cfx = codeIssues(fullfile(fixDir, 'commented.m'));
cfx = fix(cfx, 'FNDEF');
L = readlines(fullfile(fixDir, 'commented.m'));
fprintf('commented after fix line 3: %s\n', L(3));

fprintf('== edit\n');
refuse('edit(1)', @() edit(1), short);
refuse('r = edit(''good'')', @() evalc('r = edit(''good'')'), short);
refuse('edit(''misnamed'')', @() evalc('edit(''misnamed'')'), short);
refuse('edit(''good.m'')', @() evalc('edit(''good.m'')'), short);
refuse('edit(''@Thing/extent'')', @() evalc('edit(''@Thing/extent'')'), short);
refuse('edit(''Thing.extent'')', @() evalc('edit(''Thing.extent'')'), short);
fprintf('nargin edit %d nargout edit %d exist %d\n', nargin('edit'), nargout('edit'), exist('edit'));
cd(old);
rmpath(lib);
fprintf('done\n');

function writeFile(path, lines)
f = fopen(path, 'w');
fprintf(f, '%s\n', lines{:});
fclose(f);
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
