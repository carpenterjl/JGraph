function s = ip_describe(v)
% IP_DESCRIBE  One line saying what a value is: class, size, then the value.
c = class(v);
sz = mat2str(size(v));
try
    if isa(v, 'System.Object')
        s = char(v.ToString());
    elseif isa(v, 'lib.pointer')
        try
            val = ip_describe(v.Value);
        catch e
            val = ['<Value: ' e.identifier '>'];
        end
        s = sprintf('DataType=%s isNull=%d Value=%s', v.DataType, isNull(v), val);
    elseif strncmp(c, 'lib.', 4)
        s = ['libstruct ' ip_describe(get(v))];
    elseif isnumeric(v) || islogical(v)
        if isempty(v)
            s = '[]';
        else
            s = mat2str(v, 17);
        end
    elseif ischar(v)
        s = ['''' reshape(v', 1, []) ''''];
    elseif isstring(v)
        parts = cell(1, numel(v));
        for k = 1:numel(v)
            if ismissing(v(k)), parts{k} = '<missing>'; else, parts{k} = ['"' char(v(k)) '"']; end
        end
        s = strjoin(parts, ' ');
    elseif iscell(v)
        parts = cellfun(@ip_describe, v(:)', 'UniformOutput', false);
        s = ['{' strjoin(parts, '; ') '}'];
    elseif isstruct(v)
        s = ['struct(' strjoin(fieldnames(v)', ',') ')'];
    else
        s = evalc('disp(v)');
    end
catch e
    s = ['<describe failed: ' e.identifier ' ' e.message '>'];
end
s = sprintf('%s %s %s', c, sz, ip_flat(s));
end

function t = ip_flat(t)
t = strtrim(regexprep(t, '\s*\n\s*', ' | '));
end
