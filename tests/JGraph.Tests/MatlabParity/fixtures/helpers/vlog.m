function vlog(tag)
% Appends a tag to the value-isolation fixtures' call log (the global vlog_text), which each case
% resets before it runs and prints afterwards: the order accessors, subscripts, right-hand sides and
% destructors ran in is the recorded value.
global vlog_text
vlog_text = [vlog_text tag ';'];
end
