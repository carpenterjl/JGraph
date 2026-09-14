% value_isolation_forms.m -- appendix A of the value-ownership plan: the language forms the probes
% found missing, lossy or refused, which stage V6 builds (a table's brace and paren writes, its
% properties and verbs, sparse indexed assignment, N-D growth of text, error
% records and exception causes, nested auto-creation, char writes into container elements,
% datetime growth and component writes, dictionary brace and vector-key forms, handles held in
% structs and cells), and the datetime forms of M10 and M14 (V3). A case named aNNN is appendix
% row NNN; g_ cases agree on both engines today.

run_case('a025_table_alias_brace', @a025_table_alias_brace);
run_case('a026_sparse_alias', @a026_sparse_alias);
run_case('a038_table_rownames_dot_write', @a038_table_rownames_dot_write);
run_case('a039_timetable_rowtimes_dot_write', @a039_timetable_rowtimes_dot_write);
run_case('a052_nd_growth_string', @a052_nd_growth_string);
run_case('a053_nd_growth_char', @a053_nd_growth_char);
run_case('a054_cellfun_errorhandler_identifier_fn', @a054_cellfun_errorhandler_identifier_fn);
run_case('a054_arrayfun_errorhandler_identifier_fn', @a054_arrayfun_errorhandler_identifier_fn);
run_case('a054_cellfun_errorhandler_record_nonuniform', @a054_cellfun_errorhandler_record_nonuniform);
run_case('g_cellfun_errorhandler_anon', @g_cellfun_errorhandler_anon);
run_case('g_errorhandler_that_errors', @g_errorhandler_that_errors);
run_case('a059_mexception_addcause_alias', @a059_mexception_addcause_alias);
run_case('a066_throw_keeps_cause', @a066_throw_keeps_cause);
run_case('a067_rethrow_keeps_stack_top', @a067_rethrow_keeps_stack_top);
run_case('g_mexception_rethrow_identity', @g_mexception_rethrow_identity);
run_case('a060_sort_cell_alias', @a060_sort_cell_alias);
run_case('a061_complex_promotion_alias', @a061_complex_promotion_alias);
run_case('a063_struct_field_delete_alias', @a063_struct_field_delete_alias);
run_case('g_unique_returns_copy', @g_unique_returns_copy);
run_case('g_single_conversion_alias', @g_single_conversion_alias);
run_case('a082_auto_create_nested_struct_array', @a082_auto_create_nested_struct_array);
run_case('a083_auto_create_cell_element_struct', @a083_auto_create_cell_element_struct);
run_case('a085_struct_empty_ctor', @a085_struct_empty_ctor);
run_case('a085_empty_fieldless_struct_growth', @a085_empty_fieldless_struct_growth);
run_case('a085_empty_fieldless_growth_gap', @a085_empty_fieldless_growth_gap);
run_case('a090_nested_end_cell_growth', @a090_nested_end_cell_growth);
run_case('a091_string_element_char_write', @a091_string_element_char_write);
run_case('a092_strrep_cellstr_alias', @a092_strrep_cellstr_alias);
run_case('g_tbl_column_read_alias', @g_tbl_column_read_alias);
run_case('a117_tbl_alias_row_delete', @a117_tbl_alias_row_delete);
run_case('a119_tbl_cell_var_nested_write', @a119_tbl_cell_var_nested_write);
run_case('a114_tbl_varnames_alias', @a114_tbl_varnames_alias);
run_case('a114_tbl_varnames_brace_on_original', @a114_tbl_varnames_brace_on_original);
run_case('a115_tbl_varnames_whole_set_alias', @a115_tbl_varnames_whole_set_alias);
run_case('a115_tbl_description_alias', @a115_tbl_description_alias);
run_case('a115_tbl_userdata_alias', @a115_tbl_userdata_alias);
run_case('g_tbl_add_var_alias', @g_tbl_add_var_alias);
run_case('a117_tbl_row_append', @a117_tbl_row_append);
run_case('a117_tbl_paren_assign_subtable', @a117_tbl_paren_assign_subtable);
run_case('a117_tbl_row_delete_by_mask', @a117_tbl_row_delete_by_mask);
run_case('a122_tbl_rowfun_writes_global_operand', @a122_tbl_rowfun_writes_global_operand);
run_case('a122_tbl_varfun_alias', @a122_tbl_varfun_alias);
run_case('a122_tbl_addvars_leaves_source', @a122_tbl_addvars_leaves_source);
run_case('a122_tbl_table2array_alias', @a122_tbl_table2array_alias);
run_case('g_tbl_brace_read_then_write', @g_tbl_brace_read_then_write);
run_case('g_tbl_sortrows_leaves_source', @g_tbl_sortrows_leaves_source);
run_case('a121_tbl_vertcat_alias', @a121_tbl_vertcat_alias);
run_case('a120_tbl_struct_var_nested_write', @a120_tbl_struct_var_nested_write);
run_case('g_tbl_subtable_write', @g_tbl_subtable_write);
run_case('a118_tbl_var_growth', @a118_tbl_var_growth);
run_case('g_tbl_dot_self_overlap', @g_tbl_dot_self_overlap);
run_case('a116_tbl_cell_in_container_alias', @a116_tbl_cell_in_container_alias);
run_case('g_table_direct_var_indexed_write', @g_table_direct_var_indexed_write);
run_case('g_dt_alias_elem_write', @g_dt_alias_elem_write);
run_case('g_dt_format_alias', @g_dt_format_alias);
run_case('a127_dt_year_property_alias', @a127_dt_year_property_alias);
run_case('a126_dt_indexed_component_write', @a126_dt_indexed_component_write);
run_case('a123_dt_growth_fill', @a123_dt_growth_fill);
run_case('a123_dt_growth_fill_value', @a123_dt_growth_fill_value);
run_case('g_dur_alias_write', @g_dur_alias_write);
run_case('g_dur_format_alias', @g_dur_format_alias);
run_case('g_dt_timezone_alias', @g_dt_timezone_alias);
run_case('a124_dt_self_overlap', @a124_dt_self_overlap);
run_case('g_dt_delete_alias', @g_dt_delete_alias);
run_case('g_dt_arith_classes', @g_dt_arith_classes);
run_case('g_dur_diff_alias', @g_dur_diff_alias);
run_case('g_dt_cell_hold', @g_dt_cell_hold);
run_case('a127_dt_struct_component_write', @a127_dt_struct_component_write);
run_case('g_dt_concat_alias', @g_dt_concat_alias);
run_case('a128_dt_mask_write_alias', @a128_dt_mask_write_alias);
run_case('g_dt_plus_rebind_alias', @g_dt_plus_rebind_alias);
run_case('a129_dt_text_assign', @a129_dt_text_assign);
run_case('a125_dt_refused_write_atomic', @a125_dt_refused_write_atomic);
run_case('g_dur_minus_in_loop_alias', @g_dur_minus_in_loop_alias);
run_case('g_handle_nested_struct_write', @g_handle_nested_struct_write);
run_case('a137_struct_holding_handle_write', @a137_struct_holding_handle_write);
run_case('a137_struct_copy_nested_write_field_class', @a137_struct_copy_nested_write_field_class);
run_case('a137_struct_copy_whole_prop_write', @a137_struct_copy_whole_prop_write);
run_case('g_struct_handle_direct_nested_write', @g_struct_handle_direct_nested_write);
run_case('a140_struct_copy_keeps_handle_identity', @a140_struct_copy_keeps_handle_identity);
run_case('a138_cell_copy_handle_nested_write', @a138_cell_copy_handle_nested_write);
run_case('a139_struct_handle_method_write', @a139_struct_handle_method_write);
run_case('g_map_struct_value_copy_write', @g_map_struct_value_copy_write);
run_case('a136_dictionary_brace_cell_write', @a136_dictionary_brace_cell_write);
run_case('g_struct_array_datetime_format', @g_struct_array_datetime_format);
run_case('a135_cell_datetime_format', @a135_cell_datetime_format);
run_case('a148_empty_local_field_write', @a148_empty_local_field_write);
run_case('a148_empty_global_field_write', @a148_empty_global_field_write);
run_case('a149_dictionary_vector_keys', @a149_dictionary_vector_keys);

function run_case(name, fn)
global vlog_text
vlog_text = '';
try
    fprintf('CHK|%s|%s|exact\n', name, clean(fn()));
catch err
    fprintf('CHK|%s|ERR %s|exact\n', name, clean(err.message));
end
end

function s = clean(s)
if isnumeric(s) || islogical(s)
    s = mat2str(s);
end
s = char(s);
s = strrep(s, char(13), '');
s = strrep(s, char(10), ' ');
s = strrep(s, '|', '/');
end

% --- table brace and sparse indexed assignment (borrow_probe6.m); Dependent properties are a
% run-stop on JGraph and live in value_isolation_accessors.m ---------------------------------------

function s = a025_table_alias_brace()
T = table([1; 2]);
U = T; U{1, 1} = 9;
s = sprintf('%d %d', T{1, 1}, U{1, 1});
end

function s = a026_sparse_alias()
S = sparse([1 0; 0 1]);
S2 = S; S2(1, 1) = 7;
s = sprintf('%d %d', full(S(1, 1)), full(S2(1, 1)));
end

% --- table metadata through a rebuild (borrow_probe8.m) -------------------------------------------

function s = a038_table_rownames_dot_write()
T = table([1; 2], 'RowNames', {'a'; 'b'});
U = T; U.Var1(1) = 9;
s = sprintf('%d %d', isequal(U.Properties.RowNames, {'a'; 'b'}), isequal(T.Properties.RowNames, {'a'; 'b'}));
end

function s = a039_timetable_rowtimes_dot_write()
TT = timetable(seconds([1; 2]), [1; 2]);
U = TT; U.Var1(1) = 9;
s = sprintf('%d %g', isequal(U.Properties.RowTimes, seconds([1; 2])), U.Var1(1));
end

% --- N-D text growth and ErrorHandler records (borrow_probe10.m, borrow_probe10b.m) ---------------

function s = a052_nd_growth_string()
x = ["a" "b"]; t = x;
x(1, 2, 2) = "c";
s = sprintf('%d %d %s', isstring(x), isstring(t), mat2str(size(x)));
end

function s = a053_nd_growth_char()
x = 'ab'; t = x;
x(1, 2, 2) = 'c';
s = sprintf('%d %d %s', ischar(x), ischar(t), mat2str(size(x)));
end

function y = failing(x)
error('probe:bad', 'bad %d', x);
y = x; %#ok<UNRCH>
end

function s = a054_cellfun_errorhandler_identifier_fn()
r = cellfun(@failing, {1}, 'ErrorHandler', @(e, x) strcmp(e.identifier, 'probe:bad'));
s = num2str(r);
end

function s = a054_arrayfun_errorhandler_identifier_fn()
r = arrayfun(@failing, 1, 'ErrorHandler', @(e, x) strcmp(e.identifier, 'probe:bad'));
s = num2str(r);
end

function s = a054_cellfun_errorhandler_record_nonuniform()
r = cellfun(@failing, {1, 2}, 'UniformOutput', false, ...
    'ErrorHandler', @(e, x) sprintf('%s#%d#%s', e.identifier, e.index, e.message));
s = strjoin(r, ',');
end

function s = g_cellfun_errorhandler_anon()
r = cellfun(@(x) error('probe:bad', 'bad'), {1}, 'ErrorHandler', @(e, x) strcmp(e.identifier, 'probe:bad'));
s = num2str(r);
end

function s = g_errorhandler_that_errors()
try
    cellfun(@(x) error('probe:bad', 'bad'), {1}, 'ErrorHandler', @(e, x) error('probe:handler', 'handler'));
    s = 'no error';
catch e
    s = e.identifier;
end
end

% --- exception values (borrow_probe11.m, borrow_probe12.m) ----------------------------------------

function s = a059_mexception_addcause_alias()
e = MException('a:b', 'outer');
f = e;
f = addCause(f, MException('c:d', 'inner'));
s = sprintf('%d %d', numel(e.cause), numel(f.cause));
end

function s = a066_throw_keeps_cause()
e = addCause(MException('probe:outer', 'outer'), MException('probe:inner', 'inner'));
try
    throw(e);
catch caught
    s = sprintf('%d %s', numel(caught.cause), caught.cause{1}.identifier);
end
end

function thrower()
error('probe:deep', 'deep');
end

function s = a067_rethrow_keeps_stack_top()
try
    try
        thrower();
    catch e1
        rethrow(e1);
    end
catch e2
    s = e2.stack(1).name;
end
end

function s = g_mexception_rethrow_identity()
try
    try
        error('probe:inner', 'inner');
    catch e1
        rethrow(e1);
    end
catch e2
    s = sprintf('%s %s', e2.identifier, e2.message);
end
end

% --- writes into container-held text, complex promotion, field deletion (borrow_probe11.m) --------

function s = a060_sort_cell_alias()
c = {'b', 'a'};
d = sort(c);
d{1}(1) = 'z';
s = sprintf('%s %s', c{2}, d{1});
end

function s = a061_complex_promotion_alias()
x = [1 2 3];
y = x;
y(2) = 1i;
s = sprintf('%d %s', isreal(x), mat2str(y));
end

function s = a063_struct_field_delete_alias()
st.v = [1 2 3];
t = st;
t.v(2) = [];
s = sprintf('%s %s', mat2str(st.v), mat2str(t.v));
end

function s = g_unique_returns_copy()
x = [3 1 2];
u = unique(x);
u(1) = 9;
s = mat2str(x);
end

function s = g_single_conversion_alias()
x = single([1 2 3]);
y = x;
y(1) = 1.5;
s = sprintf('%s %s %s', class(y), mat2str(double(x)), mat2str(double(y)));
end

% --- nested auto-creation, empty structs (borrow_probe15.m, borrow_probe16.m) ---------------------

function s = a082_auto_create_nested_struct_array()
x.y(3).z = 1;
s = sprintf('%d %d %d', numel(x.y), isempty(x.y(1).z), x.y(3).z);
end

function s = a083_auto_create_cell_element_struct()
c = {};
c{3}.f = 1;
s = sprintf('%d %s %s %d', numel(c), class(c{1}), class(c{3}), c{3}.f);
end

function s = a085_struct_empty_ctor()
st = struct([]);
s = sprintf('%s %d %d', class(st), numel(st), numel(fieldnames(st)));
end

function s = a085_empty_fieldless_struct_growth()
st = struct([]);
st(1) = struct('a', 7);
s = sprintf('%d %d', numel(st), st(1).a);
end

function s = a085_empty_fieldless_growth_gap()
st = struct([]);
st(3) = struct('a', 7);
s = sprintf('%d %d %d', numel(st), isempty(st(1).a), st(3).a);
end

% --- growth and char writes through container elements (borrow_probe17.m) ------------------------

function s = a090_nested_end_cell_growth()
c = {[1 2], [3 4]};
c{end}(end + 1) = 5;
s = mat2str(c{2});
end

function s = a091_string_element_char_write()
x = ["abc" "def"];
x{1}(2) = 'Z';
s = char(strjoin(x, ","));
end

function s = a092_strrep_cellstr_alias()
c = {'aa', 'ba'};
d = strrep(c, 'a', 'x');
d{1}(1) = 'Q';
s = sprintf('%s %s', strjoin(c, ','), strjoin(d, ','));
end

% --- tables (borrow_probe23.m, borrow_probe25.m, borrow_probe26.m) --------------------------------

function s = g_tbl_column_read_alias()
T = table([1; 2]);
c = T.Var1;
c(1) = 9;
s = mat2str(T.Var1);
end

function s = a117_tbl_alias_row_delete()
T = table([1; 2; 3]);
U = T;
U(2, :) = [];
s = sprintf('%d %d', height(T), height(U));
end

function s = a119_tbl_cell_var_nested_write()
T = table({[1 2]; [3 4]});
U = T;
U.Var1{1}(1) = 9;
s = sprintf('%s %s', mat2str(T.Var1{1}), mat2str(U.Var1{1}));
end

function s = a114_tbl_varnames_alias()
T = table([1; 2]);
U = T;
U.Properties.VariableNames{1} = 'a';
s = sprintf('%s %s', T.Properties.VariableNames{1}, U.Properties.VariableNames{1});
end

function s = a114_tbl_varnames_brace_on_original()
T = table([1; 2]);
T.Properties.VariableNames{1} = 'a';
s = T.Properties.VariableNames{1};
end

function s = a115_tbl_varnames_whole_set_alias()
T = table([1; 2]);
U = T;
U.Properties.VariableNames = {'a'};
s = sprintf('%s %s', T.Properties.VariableNames{1}, U.Properties.VariableNames{1});
end

function s = a115_tbl_description_alias()
T = table([1; 2]);
U = T;
U.Properties.Description = 'u';
s = sprintf('[%s] [%s]', T.Properties.Description, U.Properties.Description);
end

function s = a115_tbl_userdata_alias()
T = table([1; 2]);
v = [1 2 3];
T.Properties.UserData = v;
v(1) = 7;
s = mat2str(T.Properties.UserData);
end

function s = g_tbl_add_var_alias()
T = table([1; 2]);
U = T;
U.New = [5; 6];
s = sprintf('%d %d', width(T), width(U));
end

function s = a117_tbl_row_append()
T = table([1; 2], {'a'; 'b'});
T(end + 1, :) = {3, 'c'};
s = sprintf('%d %s', height(T), T.Var2{3});
end

function s = a117_tbl_paren_assign_subtable()
T = table([1; 2]);
T(1, :) = table(9);
s = mat2str(T.Var1);
end

function s = a117_tbl_row_delete_by_mask()
T = table([1; 2; 3], {'a'; 'b'; 'c'});
U = T;
U(U.Var1 > 1, :) = [];
s = sprintf('%d %d %s', height(T), height(U), strjoin(U.Var2', ','));
end

function y = bump_gt(x)
global gt_forms
gt_forms(1) = 7;
y = x;
end

function z = rowfun_zero()
out = rowfun(@bump_gt, table(1), 'OutputFormat', 'uniform');
z = out * 0;
end

function s = a122_tbl_rowfun_writes_global_operand()
global gt_forms
gt_forms = [1 2];
r = gt_forms + rowfun_zero();
s = sprintf('%s %s', mat2str(r), mat2str(gt_forms));
end

function s = a122_tbl_varfun_alias()
T = table([1; 2]);
V = varfun(@(x) x, T);
V.Fun_Var1(1) = 9;
s = sprintf('%g %g', T.Var1(1), V.Fun_Var1(1));
end

function s = a122_tbl_addvars_leaves_source()
T = table([1; 2]);
U = addvars(T, [5; 6]);
s = sprintf('%d %d', width(T), width(U));
end

function s = a122_tbl_table2array_alias()
T = table([1; 2]);
A = table2array(T);
A(1) = 9;
s = mat2str(T.Var1);
end

function s = g_tbl_brace_read_then_write()
T = table([1; 2]);
M = T{:, 1};
M(1) = 9;
s = mat2str(T.Var1);
end

function s = g_tbl_sortrows_leaves_source()
T = table([2; 1]);
S = sortrows(T);
s = sprintf('%s %s', mat2str(T.Var1), mat2str(S.Var1));
end

function s = a121_tbl_vertcat_alias()
T = table([1; 2]);
T2 = [T; T];
T2.Var1(1) = 9;
s = sprintf('%g %d', T.Var1(1), height(T2));
end

function s = a120_tbl_struct_var_nested_write()
T = table(struct('a', {1; 2}));
U = T;
U.Var1(1).a = 9;
s = sprintf('%g %g', T.Var1(1).a, U.Var1(1).a);
end

function s = g_tbl_subtable_write()
T = table([1; 2]);
U = T(1, :);
U.Var1 = 9;
s = sprintf('%g %g', T.Var1(1), U.Var1);
end

function s = a118_tbl_var_growth()
T = table([1; 2]);
T.Var1(3) = 5;
s = sprintf('%d %s', height(T), mat2str(T.Var1));
end

function s = g_tbl_dot_self_overlap()
T = table([1; 2; 3]);
T.Var1([2 3 1]) = T.Var1;
s = mat2str(T.Var1);
end

function s = a116_tbl_cell_in_container_alias()
T = table([1; 2]);
c = {T};
d = c;
d{1}.Var1(1) = 9;
s = sprintf('%g %g', c{1}.Var1(1), d{1}.Var1(1));
end

function s = g_table_direct_var_indexed_write()
T = table([1; 2; 3]);
U = T;
U.Var1(2) = 9;
s = sprintf('%s %s', mat2str(T.Var1), mat2str(U.Var1));
end

% --- datetime and duration (borrow_probe24.m, borrow_probe25.m) -----------------------------------

function d = three_days()
d = datetime(2020, 1, 1) + days(0:2);
end

function s = g_dt_alias_elem_write()
d = three_days();
e = d;
e(2) = NaT;
s = sprintf('%d %d', sum(isnat(d)), sum(isnat(e)));
end

function s = g_dt_format_alias()
d = three_days();
e = d;
e.Format = 'yyyy';
s = sprintf('%s/%s', d.Format, e.Format);
end

function s = a127_dt_year_property_alias()
d = three_days();
e = d;
e.Year = 2021;
s = sprintf('%d %d', year(d(1)), year(e(1)));
end

function s = a126_dt_indexed_component_write()
d = three_days();
e = d;
e.Day(2) = 15;
s = sprintf('%d %d %d', day(d(2)), day(e(2)), day(e(1)));
end

function s = a123_dt_growth_fill()
d = three_days();
d(5) = datetime(2020, 2, 1);
s = sprintf('%d %d', numel(d), isnat(d(4)));
end

function s = a123_dt_growth_fill_value()
d = three_days();
d(5) = datetime(2020, 2, 1);
s = sprintf('%s/%d', char(d(4)), isnat(d(4)));
end

function s = g_dur_alias_write()
h = hours(1:3);
g = h;
g(1) = minutes(5);
s = sprintf('%s %s', mat2str(hours(h)), mat2str(minutes(g)));
end

function s = g_dur_format_alias()
h = hours(1:3);
g = h;
g.Format = 'm';
s = sprintf('%s/%s', h.Format, g.Format);
end

function s = g_dt_timezone_alias()
d = datetime(2020, 1, 1, 12, 0, 0, 'TimeZone', 'UTC');
e = d;
e.TimeZone = 'America/New_York';
s = sprintf('%s/%s/%d/%d', d.TimeZone, e.TimeZone, hour(d), hour(e));
end

function s = a124_dt_self_overlap()
d = three_days();
d([2 3 1]) = d;
s = mat2str(day(d));
end

function s = g_dt_delete_alias()
d = three_days();
e = d;
e(2) = [];
s = sprintf('%d %d', numel(d), numel(e));
end

function s = g_dt_arith_classes()
d = three_days();
s = sprintf('%s %s %s %s', class(d - d(1)), class(d + caldays(1)), class(caldays(1) + caldays(2)), ...
    class(d(2) - d));
end

function s = g_dur_diff_alias()
d = three_days();
x = d - d(1);
y = x;
y(1) = hours(1);
s = sprintf('%g %g', hours(x(1)), hours(y(1)));
end

function s = g_dt_cell_hold()
d = three_days();
c = {d};
c{1}(1) = NaT;
s = sprintf('%d %d', isnat(d(1)), isnat(c{1}(1)));
end

function s = a127_dt_struct_component_write()
d = three_days();
st.t = d;
st.t.Year = 1999;
s = sprintf('%d %d', year(d(1)), year(st.t(1)));
end

function s = g_dt_concat_alias()
d = three_days();
e = [d, d];
e(1) = NaT;
s = sprintf('%d %d', isnat(d(1)), numel(e));
end

function s = a128_dt_mask_write_alias()
d = three_days();
e = d;
e(day(e) > 1) = NaT;
s = sprintf('%d %d', sum(isnat(d)), sum(isnat(e)));
end

function s = g_dt_plus_rebind_alias()
d = three_days();
w = d;
d = d + days(1);
s = sprintf('%d %d', day(w(1)), day(d(1)));
end

function s = a129_dt_text_assign()
d = three_days();
e = d;
e(2) = '2021-05-05';
s = sprintf('%d %d', year(e(2)), year(d(2)));
end

function s = a125_dt_refused_write_atomic()
d = three_days();
try
    d(4:5) = [datetime(2021, 1, 1) datetime(2021, 1, 2) datetime(2021, 1, 3)];
catch
end
s = sprintf('%d', numel(d));
end

function s = g_dur_minus_in_loop_alias()
h = hours(1:3);
g = h;
for k = 1:3
    h = h - minutes(30);
end
s = sprintf('%s %s', mat2str(hours(g)), mat2str(hours(h)));
end

% --- handles held in structs and cells, map and dictionary values (borrow_probe26.m, 27) ----------

function s = g_handle_nested_struct_write()
h = HandleHolder();
h.data = struct('a', [1 2]);
g = h;
g.data.a(2) = 9;
s = mat2str(h.data.a);
end

function s = a137_struct_holding_handle_write()
st.h = HandleHolder();
st.h.data = [1 2 3];
t = st;
t.h.data(2) = 9;
s = mat2str(st.h.data);
end

function s = a137_struct_copy_nested_write_field_class()
st.h = HandleHolder();
st.h.data = [1 2 3];
t = st;
t.h.data(2) = 9;
s = sprintf('%s %s', class(t.h), class(st.h));
end

function s = a137_struct_copy_whole_prop_write()
st.h = HandleHolder();
st.h.data = [1 2 3];
t = st;
t.h.data = [4 5 6];
s = mat2str(st.h.data);
end

function s = g_struct_handle_direct_nested_write()
h = HandleHolder();
h.data = [1 2 3];
st.h = h;
st.h.data(2) = 9;
s = mat2str(h.data);
end

function s = a140_struct_copy_keeps_handle_identity()
st.h = HandleHolder();
t = st;
s = sprintf('%d', t.h == st.h);
end

function s = a138_cell_copy_handle_nested_write()
c = {HandleHolder()};
c{1}.data = [1 2 3];
d = c;
d{1}.data(2) = 9;
s = mat2str(c{1}.data);
end

function s = a139_struct_handle_method_write()
st.h = HandleHolder();
st.h.data = [1 2 3];
t = st;
t.h.bump();
s = mat2str(st.h.data);
end

function s = g_map_struct_value_copy_write()
m = containers.Map();
m('k') = struct('a', [1 2]);
t = m('k');
t.a(1) = 9;
u = m('k');
s = sprintf('%s %s', mat2str(u.a), mat2str(t.a));
end

function s = a136_dictionary_brace_cell_write()
d = dictionary("k", {[1 2]});
e = d;
e{"k"}(1) = 9;
a = d{"k"};
b = e{"k"};
s = sprintf('%s %s', mat2str(a), mat2str(b));
end

function s = g_struct_array_datetime_format()
st = struct('t', {datetime(2020, 1, 1), datetime(2021, 1, 1)});
st(2).t.Format = 'yyyy';
s = sprintf('%s/%s', st(1).t.Format, st(2).t.Format);
end

function s = a135_cell_datetime_format()
c = {datetime(2020, 1, 1)};
d = c;
d{1}.Format = 'yyyy';
s = sprintf('%s/%s', c{1}.Format, d{1}.Format);
end

% --- a field written onto an empty double; vector dictionary keys (borrow_probe30.m, #149) --------

function s = a148_empty_local_field_write()
x = [];
x.f = 1;
s = sprintf('%s %g', class(x), x.f);
end

function s = a148_empty_global_field_write()
global ge_forms
ge_forms.f = 1;
s = sprintf('%s %g', class(ge_forms), ge_forms.f);
end

function s = a149_dictionary_vector_keys()
d = dictionary([1 2], [10 20]);
a = d([1 2]);
d(2) = 9;
b = d([1 2]);
s = sprintf('%s %s', mat2str(a), mat2str(b));
end
