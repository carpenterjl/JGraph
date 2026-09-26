function s = ix_show(v)
% IX_SHOW  A value as one line for the interop fixtures: its class, a colon, then the value.
%   A .NET object shows its ToString, a lib.pointer its DataType, a number %.17g (an array
%   mat2str at 17 digits), a cell its elements in braces. The text never holds | or a newline.
c = class(v);
if isa(v, 'System.Object')
    s = [c ':' char(v.ToString())];
elseif isa(v, 'lib.pointer')
    s = [c ':' v.DataType];
elseif ischar(v)
    s = [c ':' reshape(v', 1, [])];
elseif isstring(v)
    parts = cell(1, numel(v));
    for k = 1:numel(v)
        if ismissing(v(k)), parts{k} = '<missing>'; else, parts{k} = char(v(k)); end
    end
    s = [c ':' strjoin(parts, ',')];
elseif (isnumeric(v) || islogical(v)) && isempty(v)
    s = [c ':[] ' mat2str(size(v))];
elseif (isnumeric(v) || islogical(v)) && isscalar(v)
    if islogical(v) || isinteger(v)
        s = sprintf('%s:%d', c, v);
    else
        s = sprintf('%s:%.17g', c, v);
    end
elseif isnumeric(v) || islogical(v)
    s = [c ':' mat2str(v, 17)];
elseif iscell(v)
    parts = cellfun(@ix_show, v(:)', 'UniformOutput', false);
    s = [c ':{' strjoin(parts, ';') '}'];
elseif isstruct(v)
    s = [c ':' strjoin(fieldnames(v)', ',')];
else
    s = c;
end
s = strrep(strrep(s, '|', '/'), newline, ' / ');
end
