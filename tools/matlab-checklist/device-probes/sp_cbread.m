function sp_cbread(src, evt)
% SP_CBREAD  A BytesAvailableFcn that reads evt.BytesAvailableFcnCount bytes and logs them.
global CBLOG
CBLOG{end + 1} = read(src, src.BytesAvailableFcnCount, "uint8");
end
