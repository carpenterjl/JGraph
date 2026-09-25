function s = dc_log()
% The value-isolation fixtures' call log (vlog's global), for a caller that has no global statement
% of its own (V11: the JGS fixture reads it through this).
global vlog_text
s = vlog_text;
end
