function sp_cbevtlog(src, evt)
% SP_CBEVTLOG  Logs a callback's source class, its event's class and properties and their values.
global CBLOG
CBLOG{end + 1} = class(src);
CBLOG{end + 1} = class(evt);
try
    p = properties(evt);
    CBLOG{end + 1} = strjoin(p', ',');
    for k = 1:numel(p)
        v = evt.(p{k});
        if isnumeric(v) || islogical(v)
            CBLOG{end + 1} = v;
        elseif isstring(v) || ischar(v)
            CBLOG{end + 1} = char(v);
        else
            CBLOG{end + 1} = class(v);
        end
    end
catch e
    CBLOG{end + 1} = e.identifier;
end
end
