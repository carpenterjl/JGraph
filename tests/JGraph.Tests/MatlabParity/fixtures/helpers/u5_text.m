function s = u5_text(v)
% A value as one line for the U5 fixtures: an on/off state as its word, text as itself, a string
% array as its elements, and anything else by class, size and contents. No bars.
if isa(v, 'matlab.lang.OnOffSwitchState')
    s = ['onoff ' char(v)];
elseif ischar(v) && size(v, 1) <= 1 && any(strcmp(v, {'on', 'off'}))
    s = ['onoff ' v];
elseif ischar(v) && size(v, 1) <= 1
    s = ['char ''' regexprep(v, '\s+', ' ') ''''];
elseif isstring(v)
    parts = cell(1, numel(v));
    for k = 1:numel(v)
        parts{k} = char(v(k));
    end
    s = ['string ' mat2str(size(v)) ' [' strjoin(parts, ', ') ']'];
elseif iscell(v)
    parts = cell(1, numel(v));
    for k = 1:numel(v)
        parts{k} = u5_text(v{k});
    end
    s = ['cell ' mat2str(size(v)) ' {' strjoin(parts, ', ') '}'];
elseif isa(v, 'function_handle')
    s = 'function_handle';
elseif isnumeric(v) || islogical(v)
    s = strtrim([class(v) ' ' mat2str(size(v)) ' ' sprintf('%.10g ', v)]);
elseif isstruct(v)
    s = 'struct';
else
    s = 'object';
end
s = strrep(s, '|', '/');
end
