function sp_cbline(src, evt)
% SP_CBLINE  A terminator BytesAvailableFcn that reads one line and logs it.
global CBLOG
CBLOG{end + 1} = readline(src);
end
