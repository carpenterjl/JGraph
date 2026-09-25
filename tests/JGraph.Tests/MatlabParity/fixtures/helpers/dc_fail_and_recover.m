function k = dc_fail_and_recover()
% A MATLAB function that raises and catches its own error (V11): the unwinding leaves it running
% as MATLAB, so the find after the catch is 1-based.
try
    error('dc:fail', 'boom');
catch
end
k = find([0 1 1]);
end
