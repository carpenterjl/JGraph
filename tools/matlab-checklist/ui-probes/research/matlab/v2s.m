function s = v2s(v)
%V2S compact one-line text form of an arbitrary value for dumps
try
    if isa(v, 'function_handle')
        s = ['@' char(func2str(v))];
        if s(2) == '@', s = s(2:end); end
    elseif isa(v, 'matlab.lang.OnOffSwitchState')
        s = ['''' char(v) ''' (OnOffSwitchState)'];
    elseif ischar(v)
        if size(v,1) <= 1
            s = ['''' v ''''];
        else
            s = sprintf('char[%dx%d] %s', size(v,1), size(v,2), mat2str(v));
        end
    elseif isstring(v)
        if isempty(v)
            s = sprintf('string.empty(%s)', num2str(size(v)));
        elseif isscalar(v)
            if ismissing(v), s = 'string(missing)'; else, s = ['"' char(v) '"']; end
        else
            s = ['string[' num2str(size(v)) '] ' strjoin(cellfun(@(x) ['"' x '"'], cellstr(v(:)'), 'UniformOutput', false), ' ')];
        end
    elseif islogical(v)
        if isempty(v), s = sprintf('logical.empty[%s]', num2str(size(v)));
        else, s = ['logical ' mat2str(v)]; end
    elseif isnumeric(v)
        if isempty(v)
            s = sprintf('%s[] size %s', class(v), num2str(size(v)));
        elseif numel(v) > 40
            s = sprintf('%s[%s] (large)', class(v), num2str(size(v)));
        else
            s = mat2str(v, 6);
            if ~isa(v, 'double'), s = [class(v) ' ' s]; end
        end
    elseif iscell(v)
        if isempty(v)
            s = sprintf('{} size %s', num2str(size(v)));
        elseif numel(v) > 30
            s = sprintf('cell[%s] (large)', num2str(size(v)));
        else
            parts = cellfun(@v2s, v(:)', 'UniformOutput', false);
            s = sprintf('{%s} size %s', strjoin(parts, ', '), num2str(size(v)));
        end
    elseif isstruct(v)
        if isempty(v), s = 'struct.empty'; else
            s = ['struct(' strjoin(fieldnames(v)', ',') ')']; end
    elseif isdatetime(v)
        if isempty(v), s = 'datetime.empty'; else, s = ['datetime ' char(string(v(1)))]; end
    elseif istable(v)
        s = sprintf('table[%s]', num2str(size(v)));
    elseif isenum(v)
        s = sprintf('%s.%s', class(v), char(v));
    elseif isobject(v) || ishandle(v)
        if isempty(v)
            s = sprintf('%s.empty', class(v));
        else
            s = sprintf('<%s> size %s', class(v), num2str(size(v)));
        end
    else
        s = sprintf('<%s>', class(v));
    end
catch e
    s = sprintf('<%s: unprintable %s>', class(v), e.message);
end
s = strrep(s, newline, '\n');
if numel(s) > 400, s = [s(1:400) ' ...']; end
end
