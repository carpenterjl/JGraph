% A MATLAB script run from JGS (V11): an alias written inside it leaves the other name whole, and
% the script's variables land in the caller's workspace.
q = [1 2 3];
w = q;
w(1) = 5;
fprintf('CHK|dc_run_script_alias_q|%s|exact\n', mat2str(q));
fprintf('CHK|dc_run_script_alias_w|%s|exact\n', mat2str(w));
