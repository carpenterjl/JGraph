function sp_cblog(src, evt)
% SP_CBLOG  A BytesAvailableFcn that logs NumBytesAvailable (or the tag it is given) in the global CBLOG.
global CBLOG
if isempty(src)
    CBLOG{end + 1} = evt;
else
    CBLOG{end + 1} = src.NumBytesAvailable;
end
end
