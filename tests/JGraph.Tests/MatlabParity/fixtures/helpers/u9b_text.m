function s = u9b_text(v)
% A value as one line for the U9b fixtures, all the way down: class, size and contents, a struct's
% fields by name, a cell's elements in order. An on/off state is its word, whether it is held as an
% OnOffSwitchState or as text (as U5's text has it), and a handle to a named function its name. No
% bars.
if isa(v, 'matlab.lang.OnOffSwitchState')
    s = ['onoff ' char(v)];
elseif ischar(v) && isrow(v) && any(strcmp(v, {'on', 'off'}))
    s = ['onoff ' v];
elseif isstruct(v)
    f = fieldnames(v);
    parts = cell(1, numel(v));
    for i = 1:numel(v)
        inner = cell(1, numel(f));
        for j = 1:numel(f)
            inner{j} = [f{j} '=' u9b_text(v(i).(f{j}))];
        end
        parts{i} = ['(' strjoin(inner, ',') ')'];
    end
    s = ['struct' mat2str(size(v)) '{' strjoin(parts, '') '}'];
elseif iscell(v)
    parts = cell(1, numel(v));
    for i = 1:numel(v)
        parts{i} = u9b_text(v{i});
    end
    s = ['cell' mat2str(size(v)) '{' strjoin(parts, ';') '}'];
elseif ischar(v)
    rows = cell(1, size(v, 1));
    for i = 1:size(v, 1)
        rows{i} = v(i, :);
    end
    s = ['char' mat2str(size(v)) '''' strrep(strjoin(rows, ';'), newline, '\n') ''''];
elseif isstring(v)
    parts = cell(1, numel(v));
    for i = 1:numel(v)
        if ismissing(v(i)), parts{i} = '<missing>'; else, parts{i} = char(v(i)); end
    end
    s = ['string' mat2str(size(v)) '"' strjoin(parts, '","') '"'];
elseif isdatetime(v)
    s = ['datetime' mat2str(size(v)) '[' strjoin(cellstr(v(:))', ',') ']'];
elseif isa(v, 'function_handle')
    t = func2str(v);
    if numel(t) > 1 && t(1) == '@' && t(2) ~= '('
        t = t(2:end);
    end
    s = ['function_handle ' t];
elseif isnumeric(v) || islogical(v)
    if isempty(v)
        s = [class(v) mat2str(size(v))];
    elseif ndims(v) > 2
        s = [class(v) mat2str(size(v)) ' nd ' mat2str(double(v(:))', 17)];
    else
        s = [class(v) mat2str(size(v)) ' ' mat2str(double(v), 17)];
    end
else
    s = ['<' class(v) mat2str(size(v)) '>'];
end
s = strrep(s, '|', '/');
end
