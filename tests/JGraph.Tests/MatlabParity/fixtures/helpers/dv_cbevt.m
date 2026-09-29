function dv_cbevt(src, evt)
% DV_CBEVT  A callback that logs what its arguments are: the source's class, the event's class, its
%   properties, its count, whether AbsTime is a datetime of now, and whether it is a handle.
global DVLOG
DVLOG{end + 1} = class(src);
DVLOG{end + 1} = class(evt);
DVLOG{end + 1} = strjoin(properties(evt)', ',');
DVLOG{end + 1} = evt.BytesAvailableFcnCount;
DVLOG{end + 1} = class(evt.AbsTime);
DVLOG{end + 1} = abs(seconds(datetime('now') - evt.AbsTime)) < 5;
DVLOG{end + 1} = isa(evt, 'handle');
end
