function dc_log_reset()
% Empties the value-isolation fixtures' call log (vlog's global) before a case runs (V11).
global vlog_text
vlog_text = '';
end
