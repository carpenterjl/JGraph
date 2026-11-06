% p5_display - the display forms over the file-name checks and the severities p5_analyzer did not print.
%   powershell -ExecutionPolicy Bypass -File .\run-probe.ps1 -Name p5_display
here = fileparts(mfilename('fullpath'));
work = fullfile(here, 'rt', 'p5display');
if exist(work, 'dir'), rmdir(work, 's'); end
mkdir(fullfile(work, '@Mis'));
short = @(s) strrep(strrep(char(s), work, '<work>'), strrep(work, '\', '/'), '<work>');
writeFile(fullfile(work, 'misnamed.m'), {'function r = othername(x)', 'r = x;', 'end'});
writeFile(fullfile(work, 'dotted.m'), {'function r = pkg.dotted(x)', 'r = x;', 'end'});
writeFile(fullfile(work, 'Wrongctor.m'), {'classdef Otherctor', '    methods', '        function o = Otherctor()', '            disp(o)', '        end', '    end', 'end'});
writeFile(fullfile(work, 'Noin.m'), {'classdef Noin', '    methods', '        function bodied', '            disp(1)', '        end', '        sigline', '    end', 'end'});
writeFile(fullfile(work, '@Mis', 'Mis.m'), {'classdef Wrong', 'end'});
for f = {'misnamed.m', 'dotted.m', 'Wrongctor.m', 'Noin.m', '@Mis\Mis.m'}
    p = fullfile(work, f{1});
    m = checkcode(p, '-id', '-struct', '-severity');
    for i = 1:numel(m)
        fprintf('MSG|%s|%s|%s|%s|%s|%d|%s\n', f{1}, m(i).id, mat2str(m(i).line), mat2str(m(i).column), mat2str(m(i).fix), m(i).severity, short(m(i).message));
    end
    fprintf('== checkcode %s\n%s<EOS>\n', f{1}, short(evalc(['checkcode(''' p ''')'])));
    fprintf('== checkcode -id %s\n%s<EOS>\n', f{1}, short(evalc(['checkcode(''' p ''', ''-id'')'])));
    fprintf('== mlint %s\n%s<EOS>\n', f{1}, short(evalc(['mlint(''' p ''')'])));
end
fprintf('== codeIssues display\n');
q = codeIssues(fullfile(work, 'misnamed.m'));
fprintf('%s<EOS>\n', short(evalc('disp(q)')));
fprintf('%s<EOS>\n', short(evalc('disp(q.Issues)')));
fprintf('done\n');

function writeFile(path, lines)
f = fopen(path, 'w');
fprintf(f, '%s\n', lines{:});
fclose(f);
end
