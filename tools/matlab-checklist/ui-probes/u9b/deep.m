function s = deep(v)
% A value on one line, all the way down, with sizes, for the U9b bridge probe.
if isstruct(v)
    f = fieldnames(v);
    parts = cell(1, numel(v));
    for i = 1:numel(v)
        inner = cellfun(@(n) [n '=' deep(v(i).(n))], f', 'UniformOutput', false);
        parts{i} = ['(' strjoin(inner, ',') ')'];
    end
    s = sprintf('struct%s{%s}', mat2str(size(v)), strjoin(parts, ''));
elseif iscell(v)
    parts = cellfun(@deep, v(:)', 'UniformOutput', false);
    s = sprintf('cell%s{%s}', mat2str(size(v)), strjoin(parts, ';'));
elseif ischar(v)
    s = sprintf('char%s''%s''', mat2str(size(v)), strrep(v(:)', newline, '\n'));
elseif isstring(v)
    if isempty(v), s = sprintf('string%s', mat2str(size(v)));
    else, t = cell(1, numel(v)); for i = 1:numel(v), if ismissing(v(i)), t{i} = '<missing>'; else, t{i} = char(v(i)); end, end, s =sprintf('string%s"%s"', mat2str(size(v)), strjoin(t, '","')); end
elseif isnumeric(v) || islogical(v)
    if isempty(v), s = sprintf('%s%s', class(v), mat2str(size(v)));
    elseif ndims(v) > 2, s = sprintf('%s%s nd %s', class(v), mat2str(size(v)), mat2str(v(:)', 17));
    else, s = sprintf('%s%s %s', class(v), mat2str(size(v)), mat2str(v, 17)); end
else
    s = sprintf('<%s%s>', class(v), mat2str(size(v)));
end
end
