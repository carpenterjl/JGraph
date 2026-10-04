function s = u1_show(v)
% A value as one line for the U1 fixtures: its class, its size, and what it holds.
if ischar(v)
    if size(v, 1) <= 1
        s = ['char ''' v ''''];
    else
        s = ['char ' mat2str(size(v)) ' ' strjoin(cellstr(v)', ';')];
    end
elseif iscell(v)
    parts = cell(1, numel(v));
    for k = 1:numel(v)
        parts{k} = u1_show(v{k});
    end
    s = ['cell ' mat2str(size(v)) ' {' strjoin(parts, ', ') '}'];
elseif isa(v, 'function_handle')
    s = 'function_handle';
elseif isnumeric(v) || islogical(v)
    s = [class(v) ' ' mat2str(size(v)) ' ' sprintf('%.17g ', v)];
else
    s = class(v);
end
s = strtrim(s);
end
