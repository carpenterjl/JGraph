% w10_checkcode.m -- the Code Analyzer's first set over helpers\w10_analyzed.m (App Designer window plan W10,
% ADR 0304): checkcode's struct with -id and -severity, its -string text with -cyc, and codeIssues' tables, as
% R2025b reports them. The texts name no variables, so every line compares exact; the file's path is cut out.
file = which('w10_analyzed');
m = checkcode(file, '-id', '-severity');
fprintf('CHK|count|%d|exact\n', numel(m));
for k = 1:numel(m)
    fprintf('CHK|m%02d|%s L%d C%d-%d fix%d sev%d|exact\n', k, m(k).id, m(k).line, m(k).column(1), m(k).column(2), m(k).fix, m(k).severity);
end
s = checkcode(file, '-string', '-id', '-cyc');
s = strrep(s, newline, ' / ');
fprintf('CHK|string|%s|exact\n', s);
n = checkcode(file, '-notok');
fprintf('CHK|notok|%d|exact\n', numel(n));
c = codeIssues(file);
t = c.Issues;
fprintf('CHK|issues|%d %s|exact\n', height(t), strjoin(t.Properties.VariableNames, ','));
fprintf('CHK|severities|%s|exact\n', strjoin(cellstr(string(t.Severity))', ','));
fprintf('CHK|fixability|%s|exact\n', strjoin(cellstr(string(t.Fixability))', ','));
fprintf('CHK|checks|%s|exact\n', strjoin(cellstr(string(t.CheckID))', ','));
fprintf('CHK|lines|%s|exact\n', mat2str(t.LineStart'));
fprintf('CHK|location|%s|exact\n', t.Location(1));
u = c.SuppressedIssues;
fprintf('CHK|suppressed|%d %s|exact\n', height(u), strjoin(u.Properties.VariableNames, ','));
fprintf('CHK|suppression|%s|exact\n', strjoin(cellstr(string(u.Suppression))', ','));
