% A MATLAB script run from JGS that prints the three struct names the nested crossing left in the
% caller's workspace (V11): JGS has no dot syntax to read them itself.
fprintf('CHK|dc_nested_write_result_f|%s|exact\n', mat2str(r.f));
fprintf('CHK|dc_nested_write_source_f|%s|exact\n', mat2str(q.f));
fprintf('CHK|dc_nested_write_alias_f|%s|exact\n', mat2str(w.f));
