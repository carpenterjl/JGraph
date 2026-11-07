function p7_tool(file, factor)
%P7_TOOL The acceptance library's console program (packages and class folders plan, stage P7),
%built by mcc -m in both engines: reads a trace through the sub-package, scales it, sums it as a
%Series. One key<TAB>value line each.
%
%   p7_tool <file> [<factor>]
beep('off');
import signal.io.readTrace
if nargin < 2, factor = '1'; end
t = readTrace(file);
scale(t, str2double(factor));
s = Series.ofTrace(t);
fprintf('name\t%s\n', t.Name);
fprintf('data\t%s\n', mat2str(t.Data));
fprintf('total\t%g\n', total(s));
fprintf('describe\t%s | %s\n', describe(s), describe(t));
fprintf('deployed\t%d\n', isdeployed);
fprintf('which\t%s %s\n', regexprep(which('signal.Trace'), '^.*[\\/]', ''), regexprep(which('Series/total'), '^.*[\\/]', ''));
fprintf('end\n');
end
