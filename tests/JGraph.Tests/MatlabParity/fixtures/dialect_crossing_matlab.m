% dialect_crossing_matlab.m -- V11 of the value-ownership plan (ADR 0172): the MATLAB half of
% dialect_crossing.jgs, recorded in R2025b. Every .m helper the JGS fixture reaches is called here
% from MATLAB, so what each helper means in MATLAB is measured, and the hand-written expectations of
% the JGraph-only fixture rest on these lines and on the rule that a .m file means the same thing
% however it was reached.

a = [1 2 3];
r = dc_write_param(a);
fprintf('CHK|dcm_param_write_result|%s|exact\n', mat2str(r));
fprintf('CHK|dcm_param_write_caller_kept|%s|exact\n', mat2str(a));
fprintf('CHK|dcm_first_is_one_based|%g|exact\n', dc_first(a));
r3 = dc_local_alias(a);
fprintf('CHK|dcm_local_alias_result|%s|exact\n', mat2str(r3));
fprintf('CHK|dcm_local_alias_caller_kept|%s|exact\n', mat2str(a));
p = dc_persist();
p(1) = 99;
fprintf('CHK|dcm_persistent_copy_written|%s|exact\n', mat2str(p));
fprintf('CHK|dcm_persistent_slot_kept|%s|exact\n', mat2str(dc_persist()));
c = dc_concat([1 2], [3 4]);
fprintf('CHK|dcm_brackets_concatenate|%s|exact\n', mat2str(c));
fprintf('CHK|dcm_brackets_concatenate_numel|%d|exact\n', numel(c));

run('helpers/dc_script_alias.m');
run('helpers/dc_make_alias.m');
q(1) = 9;
fprintf('CHK|dcm_write_after_alias_q|%s|exact\n', mat2str(q));
fprintf('CHK|dcm_write_after_alias_w_kept|%s|exact\n', mat2str(w));
a = [1 2 3];
run('helpers/dc_write_a.m');
fprintf('CHK|dcm_script_writes_caller_variable|%s|exact\n', mat2str(a));
run('helpers/dc_make_handles.m');
fprintf('CHK|dcm_handle_indexes_one_based|%g|exact\n', first([1 2 3]));
fprintf('CHK|dcm_handle_brackets_join|%d|exact\n', numel(twice([1 2])));

run('helpers/dc_make_struct_alias.m');
r = dc_write_field(q);
run('helpers/dc_show_struct_alias.m');

run('helpers/dc_find.m');
fprintf('CHK|dcm_fn_lexical|%s|exact\n', dc_lexical_fn([0 1 1]));
fprintf('CHK|dcm_fn_recovers_from_own_error|%s|exact\n', mat2str(dc_fail_and_recover()));
fprintf('CHK|dcm_loop|%d|exact\n', dc_loop(1000));

dc_log_reset();
dc_temp_holder();
fprintf('CHK|dcm_release_at_frame_exit|%s|exact\n', dc_log());
dc_log_reset();
h = dc_make_holder('H');
fprintf('CHK|dcm_escaped_holder_alive|%s|exact\n', dc_log());
h = 0;
fprintf('CHK|dcm_escaped_holder_released_by_rebind|%s|exact\n', dc_log());

a = [1 1 1];
b = a;
run('helpers/dc_rebind_minus.m');
fprintf('CHK|dcm_rebind_a|%s|exact\n', mat2str(a));
fprintf('CHK|dcm_rebind_alias_kept|%s|exact\n', mat2str(b));
