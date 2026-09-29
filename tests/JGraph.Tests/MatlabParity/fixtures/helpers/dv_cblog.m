function dv_cblog(src, evt)
% DV_CBLOG  A callback for the device fixtures: logs src.NumBytesAvailable in the global DVLOG, or,
%   called with an empty src, the tag it is given as evt.
global DVLOG
if isempty(src)
    DVLOG{end + 1} = evt;
else
    DVLOG{end + 1} = src.NumBytesAvailable;
end
end
