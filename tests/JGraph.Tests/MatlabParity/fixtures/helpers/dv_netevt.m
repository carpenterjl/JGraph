function dv_netevt(src, evt)
% DV_NETEVT  A callback for the network fixtures: logs in the global DVLOG the source's class, the
%   event's class, its property names and each property's value (a number, logical or text as it is,
%   anything else as its class); a port only as whether it is empty, since the OS chooses it.
global DVLOG
DVLOG{end + 1} = class(src);
DVLOG{end + 1} = class(evt);
p = properties(evt);
DVLOG{end + 1} = strjoin(p', ',');
for k = 1:numel(p)
    v = evt.(p{k});
    if endsWith(p{k}, 'Port')
        DVLOG{end + 1} = isempty(v);
    elseif isnumeric(v) || islogical(v)
        DVLOG{end + 1} = v;
    elseif isstring(v) || ischar(v)
        DVLOG{end + 1} = char(v);
    else
        DVLOG{end + 1} = class(v);
    end
end
end
