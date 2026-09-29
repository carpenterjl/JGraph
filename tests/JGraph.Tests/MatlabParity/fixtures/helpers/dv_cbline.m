function dv_cbline(src, ~)
% DV_CBLINE  A "terminator" callback that reads one line and logs it in DVLOG.
global DVLOG
DVLOG{end + 1} = readline(src);
end
