function dv_cbread(src, ~)
% DV_CBREAD  A "byte" callback that reads src.BytesAvailableFcnCount bytes and logs them in DVLOG.
global DVLOG
DVLOG{end + 1} = read(src, src.BytesAvailableFcnCount, "uint8");
end
