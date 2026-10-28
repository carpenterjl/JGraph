function acc_tool(varargin)
%ACC_TOOL The acceptance set's console tool (compiler and SDK plan, stage K22): its arguments, the
%data file built in beside it, and the library functions over them. One key<TAB>value line each.
%
%   acc_tool <name> <factor>   greets the name and scales the data by the factor
%   acc_tool -fail             ends on acc_fail's error
k0_quiet;
fprintf('nargin\t%d\n', nargin);
for k = 1:nargin
    fprintf('arg%d\t%s\n', k, varargin{k});
end
fprintf('isdeployed\t%d\n', isdeployed);
text = fileread('acc_data.txt');
v = sscanf(text, '%f')';
fprintf('data\t%s\n', mat2str(v));
fprintf('sum\t%g\n', sum(v));
s = acc_stats(v);
fprintf('stats\tn=%d mean=%g max=%g\n', s.n, s.mean, s.max);
fprintf('count\t%d %d\n', acc_count(), acc_count());
if nargin >= 1 && strcmp(varargin{1}, '-fail')
    acc_fail('asked for');
end
if nargin >= 1
    fprintf('greet\t%s\n', acc_greet(varargin{1}));
end
if nargin >= 2
    k = str2double(varargin{2});
    fprintf('scaled\t%s\n', mat2str(acc_scale(v, k)));
    fprintf('add\t%g\n', acc_add(k, s.max));
end
fprintf('end\n');
end
