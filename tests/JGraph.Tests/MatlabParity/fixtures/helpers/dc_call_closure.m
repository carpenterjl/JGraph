% A MATLAB script run from JGS that calls two JGS functions the caller defined (V11): jgs_first
% indexes 0-based inside its own body whoever calls it, and jgs_fail's error unwinds the JGS frame
% and leaves this script running as MATLAB - find is 1-based right after the catch.
r = jgs_first([10 20 30]);
fprintf('CHK|dc_script_calls_jgs_fn|%g|exact\n', r);
try
    jgs_fail(1);
    fprintf('CHK|dc_jgs_error_caught|not thrown|exact\n');
catch e
    fprintf('CHK|dc_jgs_error_caught|%s|exact\n', class(e));
end
fprintf('CHK|dc_find_after_jgs_error|%s|exact\n', mat2str(find([0 1 1])));
