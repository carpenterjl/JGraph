function s = v2s(v)
% A value as one line, for the U8 probes.
if ischar(v)
    if size(v, 1) <= 1, s = ['''' v '''']; else, s = sprintf('char %s %s', mat2str(size(v)), mat2str(v)); end
elseif isstring(v)
    if isscalar(v), if ismissing(v), s = 'string <missing>'; else, s = ['"' char(v) '"']; end, else, s = sprintf('string %s [%s]', mat2str(size(v)), strjoin(v, ',')); end
elseif iscell(v)
    parts = cellfun(@v2s, v, 'UniformOutput', false);
    s = sprintf('cell %s {%s}', mat2str(size(v)), strjoin(parts(:)', ','));
elseif isa(v, 'function_handle')
    s = ['@' func2str(v)];
elseif isa(v, 'matlab.lang.OnOffSwitchState')
    s = sprintf('OnOff %s', char(v));
elseif isnumeric(v) || islogical(v)
    if isempty(v), s = sprintf('%s %s', class(v), mat2str(size(v)));
    elseif numel(v) > 40 || ndims(v) > 2, s = sprintf('%s size %s', class(v), mat2str(size(v)));
    else, s = sprintf('%s %s', class(v), mat2str(v, 8)); end
elseif istable(v)
    s = sprintf('table %s vars=%s', mat2str(size(v)), strjoin(v.Properties.VariableNames, ','));
elseif isstruct(v)
    s = sprintf('struct %s', strjoin(fieldnames(v)', ','));
elseif isa(v, 'matlab.graphics.Graphics') || isa(v, 'handle')
    if isempty(v), s = sprintf('%s empty %s', class(v), mat2str(size(v))); else, s = sprintf('<%s %s>', class(v), mat2str(size(v))); end
else
    s = sprintf('<%s>', class(v));
end
end
