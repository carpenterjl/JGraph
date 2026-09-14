% value_isolation_writes.m -- appendix A of the value-ownership plan: what an indexed write reads
% and in what order. Self-overlap (M10: the right-hand side, a subscript, or both are the target's
% own storage, with and without growth), a multiple assignment's outputs (M11), a refused write
% leaving nothing behind (M14, including write masks of every length and whole-element struct
% assignment), each subscript evaluated once (M15), and the order of right-hand side, subscripts,
% end and target by the target's shape (M16, #151 on today's roads and #153 on the composite roads
% V6 builds). A case named aNNN is appendix row NNN; g_ cases agree on both engines today.

run_case('a031_overlap_permute_real', @a031_overlap_permute_real);
run_case('g_overlap_repeat_real', @g_overlap_repeat_real);
run_case('a032_overlap_permute_complex', @a032_overlap_permute_complex);
run_case('a033_overlap_permute_2d', @a033_overlap_permute_2d);
run_case('a034_overlap_permute_cell', @a034_overlap_permute_cell);
run_case('g_overlap_shift_real', @g_overlap_shift_real);
run_case('a035_multi_assign_deal_order', @a035_multi_assign_deal_order);
run_case('g_multi_assign_cslist_order', @g_multi_assign_cslist_order);
run_case('g_multi_assign_varargout_order', @g_multi_assign_varargout_order);
run_case('a040_grow_from_self_range', @a040_grow_from_self_range);
run_case('a041_grow_from_self_end', @a041_grow_from_self_end);
run_case('a042_index_by_self', @a042_index_by_self);
run_case('g_mask_from_self', @g_mask_from_self);
run_case('g_colon_from_self_reversed', @g_colon_from_self_reversed);
run_case('g_cell_contains_self', @g_cell_contains_self);
run_case('g_struct_contains_self', @g_struct_contains_self);
run_case('g_struct_array_delete_alias', @g_struct_array_delete_alias);
run_case('a043_string_array_self_permute', @a043_string_array_self_permute);
run_case('g_char_row_self_permute', @g_char_row_self_permute);
run_case('a044_nd_self_permute', @a044_nd_self_permute);
run_case('a045_logical_self_permute', @a045_logical_self_permute);
run_case('g_int_self_permute', @g_int_self_permute);
run_case('g_cell_brace_cslist_self', @g_cell_brace_cslist_self);
run_case('a046_field_self_permute', @a046_field_self_permute);
run_case('a047_rejected_growth_linear', @a047_rejected_growth_linear);
run_case('g_rejected_growth_linear_alias', @g_rejected_growth_linear_alias);
run_case('a048_rejected_growth_2d', @a048_rejected_growth_2d);
run_case('a049_rejected_growth_cell', @a049_rejected_growth_cell);
run_case('a050_brace_logical_subscript_row', @a050_brace_logical_subscript_row);
run_case('a051_brace_logical_subscript_col', @a051_brace_logical_subscript_col);
run_case('g_paren_scalar_subscript_once', @g_paren_scalar_subscript_once);
run_case('g_end_vs_rhs_growth', @g_end_vs_rhs_growth);
run_case('a056_subscript_shrinks_target', @a056_subscript_shrinks_target);
run_case('a062_logical_mask_growth', @a062_logical_mask_growth);
run_case('a062_mask_growth_alias', @a062_mask_growth_alias);
run_case('a068_mask_short_write', @a068_mask_short_write);
run_case('a069_mask_trailing_false_write', @a069_mask_trailing_false_write);
run_case('a070_mask_all_false_write', @a070_mask_all_false_write);
run_case('a071_mask_growth_2d_rows', @a071_mask_growth_2d_rows);
run_case('g_varargin_write_isolated', @g_varargin_write_isolated);
run_case('a073_grow_rows_from_self', @a073_grow_rows_from_self);
run_case('a074_grow_cols_from_self', @a074_grow_cols_from_self);
run_case('g_struct_array_self_element', @g_struct_array_self_element);
run_case('a075_struct_array_grow_from_self', @a075_struct_array_grow_from_self);
run_case('g_string_plus_alias', @g_string_plus_alias);
run_case('g_cell_grow_with_self', @g_cell_grow_with_self);
run_case('g_for_deletes_iterated', @g_for_deletes_iterated);
run_case('g_while_condition_rebound', @g_while_condition_rebound);
run_case('a076_struct_element_incompatible', @a076_struct_element_incompatible);
run_case('g_struct_element_field_order', @g_struct_element_field_order);
run_case('a086_empty_struct_with_fields_mismatch', @a086_empty_struct_with_fields_mismatch);
run_case('a087_empty_struct_with_fields_match', @a087_empty_struct_with_fields_match);
run_case('g_empty_double_to_struct', @g_empty_double_to_struct);
run_case('g_struct_array_growth_fill', @g_struct_array_growth_fill);
run_case('g_row_delete_alias', @g_row_delete_alias);
run_case('g_cell_row_delete_alias', @g_cell_row_delete_alias);
run_case('g_colon_delete_all', @g_colon_delete_all);
run_case('g_growth_from_empty', @g_growth_from_empty);
run_case('g_num2cell_then_write', @g_num2cell_then_write);
run_case('g_isequal_after_alias_write', @g_isequal_after_alias_write);
run_case('g_order_var_subscript_vs_rhs', @g_order_var_subscript_vs_rhs);
run_case('g_order_var_paren_2d', @g_order_var_paren_2d);
run_case('g_order_dictionary_key_vs_rhs', @g_order_dictionary_key_vs_rhs);
run_case('g_order_var_end_vs_rhs', @g_order_var_end_vs_rhs);
run_case('a151_order_one_level_brace', @a151_order_one_level_brace);
run_case('a151_order_field_then_paren', @a151_order_field_then_paren);
run_case('a151_order_brace_then_paren', @a151_order_brace_then_paren);
run_case('a151_order_paren_then_field', @a151_order_paren_then_field);
run_case('a151_order_dynamic_field', @a151_order_dynamic_field);
run_case('a151_order_field_field_paren', @a151_order_field_field_paren);
run_case('a153_order_cell_elem_end_vs_rhs', @a153_order_cell_elem_end_vs_rhs);
run_case('a153_order_struct_field_end_vs_rhs', @a153_order_struct_field_end_vs_rhs);
run_case('a153_order_table_var_end_vs_rhs', @a153_order_table_var_end_vs_rhs);
run_case('a153_order_table_varnames_sub_vs_rhs', @a153_order_table_varnames_sub_vs_rhs);
run_case('a153_order_datetime_day_sub_vs_rhs', @a153_order_datetime_day_sub_vs_rhs);

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

function s = logged(v)
global vlog_text
if isnumeric(v) || islogical(v)
    v = mat2str(v);
end
s = sprintf('%s / %s', vlog_text, v);
end

% --- self-overlap and multiple assignment (borrow_probe8.m) -----------------------------------------

function s = a031_overlap_permute_real()
a = [1 2 3]; a([2 3 1]) = a;
s = mat2str(a);
end

function s = g_overlap_repeat_real()
a = [1 2 3 4]; a([1 2]) = a([2 1]);
s = mat2str(a);
end

function s = a032_overlap_permute_complex()
a = [1+1i 2 3]; a([2 3 1]) = a;
s = mat2str(a);
end

function s = a033_overlap_permute_2d()
a = [1 2; 3 4]; a([2 1], :) = a;
s = mat2str(a);
end

function s = a034_overlap_permute_cell()
a = {1, 'b', 3}; a([2 3 1]) = a;
s = sprintf('%s %s %s', class(a{1}), class(a{2}), class(a{3}));
end

function s = g_overlap_shift_real()
a = 1:6; a(2:6) = a(1:5);
s = mat2str(a);
end

function s = a035_multi_assign_deal_order()
v = [1 2 3]; [v(1), b] = deal(7, v);
s = mat2str(b);
end

function s = g_multi_assign_cslist_order()
v = [1 2 3]; C = {7, v};
[v(1), b] = C{:};
s = mat2str(b);
end

function varargout = two_out(varargin)
varargout = varargin;
end

function s = g_multi_assign_varargout_order()
v = [1 2 3];
[v(1), b] = two_out(7, v);
s = mat2str(b);
end

% --- growth from self, subscript from self, every element type (borrow_probe9.m) -------------------

function s = a040_grow_from_self_range()
a = [1 2 3]; a(4:6) = a;
s = mat2str(a);
end

function s = a041_grow_from_self_end()
a = [1 2 3]; a(end+1:end+3) = a;
s = mat2str(a);
end

function s = a042_index_by_self()
a = [3 1 2]; a(a) = a;
s = mat2str(a);
end

function s = g_mask_from_self()
a = [5 1 7 2]; a(a > 2) = a(1:2);
s = mat2str(a);
end

function s = g_colon_from_self_reversed()
a = 1:5; a(:) = a(end:-1:1);
s = mat2str(a);
end

function s = g_cell_contains_self()
c = {1, 2}; c{end+1} = c;
s = sprintf('%d %d', numel(c), numel(c{3}));
end

function s = g_struct_contains_self()
st.a = 1; st.f = st;
s = sprintf('%d %d', isfield(st.f, 'f'), st.f.a);
end

function s = g_struct_array_delete_alias()
st = struct('f', {1, 2, 3});
t = st; t(2) = [];
s = sprintf('%d %d', numel(st), numel(t));
end

function s = a043_string_array_self_permute()
x = ["a" "b" "c"]; x([2 3 1]) = x;
s = char(strjoin(x, ","));
end

function s = g_char_row_self_permute()
x = 'abc'; x([2 3 1]) = x;
s = x;
end

function s = a044_nd_self_permute()
x = reshape(1:8, 2, 2, 2); x(:, :, [2 1]) = x;
s = mat2str(x(:)');
end

function s = a045_logical_self_permute()
x = [true false false]; x([2 3 1]) = x;
s = mat2str(x);
end

function s = g_int_self_permute()
x = int8([1 2 3]); x([2 3 1]) = x;
s = mat2str(x);
end

function s = g_cell_brace_cslist_self()
c = {1, 2, 3}; [c{[2 3 1]}] = c{:};
s = mat2str([c{:}]);
end

function s = a046_field_self_permute()
st.v = [1 2 3]; st.v([2 3 1]) = st.v;
s = mat2str(st.v);
end

% --- refused writes and subscripts evaluated twice (borrow_probe10.m) -----------------------------

function s = a047_rejected_growth_linear()
a = [1 2];
try
    a(4:5) = [7 8 9];
catch
end
s = mat2str(a);
end

function s = g_rejected_growth_linear_alias()
a = [1 2]; b = a;
try
    a(4:5) = [7 8 9];
catch
end
s = mat2str(b);
end

function s = a048_rejected_growth_2d()
a = [1 2; 3 4];
try
    a(3:4, 1) = [7 8 9];
catch
end
s = mat2str(a);
end

function s = a049_rejected_growth_cell()
c = {1, 2};
try
    c(4:5) = {7, 8, 9};
catch
end
s = num2str(numel(c));
end

function k = pick_logical_rows()
vlog('pick');
k = [true false];
end

function k = pick_scalar()
vlog('pick');
k = 2;
end

function s = a050_brace_logical_subscript_row()
C = {10; 20};
tail = '';
try
    C{pick_logical_rows(), 1} = 99;
catch e
    tail = [' ERR ' e.message];
end
s = [logged(C{1}) tail];
end

function s = a051_brace_logical_subscript_col()
C = {10, 20};
tail = '';
try
    C{1, pick_logical_rows()} = 99;
catch e
    tail = [' ERR ' e.message];
end
s = [logged(C{1}) tail];
end

function s = g_paren_scalar_subscript_once()
v = [10 20 30];
v(pick_scalar()) = 5;
s = logged(v);
end

% --- the order of end, subscripts and the right-hand side on a variable (borrow_probe11.m) --------

function v = grow_gx()
global gx
gx(end + 1) = 50;
v = 99;
end

function k = shrink_gx()
global gx
gx(end) = [];
k = 3;
end

function s = g_end_vs_rhs_growth()
global gx
gx = [1 2];
gx(end + 1) = grow_gx();
s = mat2str(gx);
end

function s = a056_subscript_shrinks_target()
global gx
gx = [1 2 3];
gx(shrink_gx()) = 9;
s = mat2str(gx);
end

function s = a062_logical_mask_growth()
x = [1 2 3];
x(logical([0 0 0 1])) = 9;
s = mat2str(x);
end

% --- write masks of every length (borrow_probe12.m) -------------------------------------------------

function s = a062_mask_growth_alias()
x = [1 2 3]; alias = x; x(logical([0 0 0 1])) = 9;
s = sprintf('%s %s', mat2str(x), mat2str(alias));
end

function s = a068_mask_short_write()
x = [1 2 3]; x(logical([0 1])) = 9;
s = mat2str(x);
end

function s = a069_mask_trailing_false_write()
x = [1 2 3]; x(logical([1 0 0 0 0])) = 9;
s = mat2str(x);
end

function s = a070_mask_all_false_write()
x = [1 2 3]; x(false(1, 5)) = 9;
s = mat2str(x);
end

function s = a071_mask_growth_2d_rows()
x = [1 2; 3 4]; x(logical([0 0 1]), :) = 9;
s = mat2str(x);
end

% --- varargin, 2-D and struct-array growth from self, loops over what they mutate (borrow_probe13.m)

function r = write_first(varargin)
varargin{1}(1) = 7;
r = varargin{1};
end

function s = g_varargin_write_isolated()
x = [1 2 3];
y = write_first(x);
s = sprintf('%s %s', mat2str(x), mat2str(y));
end

function s = a073_grow_rows_from_self()
x = [1 2; 3 4]; x(end+1:end+2, :) = x;
s = mat2str(x);
end

function s = a074_grow_cols_from_self()
x = [1 2; 3 4]; x(:, end+1:end+2) = x;
s = mat2str(x);
end

function s = g_struct_array_self_element()
st = struct('f', {1, 2}); st(2) = st(1);
s = sprintf('%g %g', st(1).f, st(2).f);
end

function s = a075_struct_array_grow_from_self()
st = struct('f', {1, 2}); st(end+1:end+2) = st;
s = mat2str([st.f]);
end

function s = g_string_plus_alias()
a = ["x" "y"]; b = a; b = b + "!"; %#ok<NASGU>
s = char(strjoin(a, ","));
end

function s = g_cell_grow_with_self()
c = {1}; c(end+1) = {c};
s = sprintf('%d %d', numel(c), numel(c{2}));
end

function s = g_for_deletes_iterated()
x = [10 20 30];
seen = [];
for v = x
    seen(end+1) = v; %#ok<AGROW>
    x(end) = [];
end
s = sprintf('%s %s', mat2str(seen), mat2str(x));
end

function s = g_while_condition_rebound()
n = 0; lim = 3;
while n < lim
    n = n + 1;
    lim = 2;
end
s = num2str(n);
end

% --- whole-element struct assignment (borrow_probe14.m, borrow_probe16.m) ------------------------

function s = a076_struct_element_incompatible()
st = struct('a', {1, 2}); t = struct('b', 3);
rejected = false;
try
    st(2) = t;
catch
    rejected = true;
end
s = sprintf('%d %d %d', rejected, isfield(st, 'b'), numel(st));
end

function s = g_struct_element_field_order()
st = struct('a', {1, 2}, 'b', {3, 4}); t = struct('b', 30, 'a', 10);
st(2) = t;
s = sprintf('%g %g', st(2).a, st(2).b);
end

function s = a086_empty_struct_with_fields_mismatch()
st = struct('a', {});
try
    st(1) = struct('b', 7);
    s = sprintf('accepted %s', strjoin(fieldnames(st)', ','));
catch e
    s = ['refused ' e.identifier];
end
end

function s = a087_empty_struct_with_fields_match()
st = struct('a', {});
st(1) = struct('a', 7);
s = sprintf('%d %d', numel(st), st(1).a);
end

function s = g_empty_double_to_struct()
st = [];
st(1).a = 7;
s = sprintf('%s %d', class(st), st(1).a);
end

% --- growth fill, deletion through aliases, empties (borrow_probe15.m, borrow_probe17.m) -----------

function s = g_struct_array_growth_fill()
st = struct('f', 1, 'g', 2);
st(3).f = 5;
s = sprintf('%d %d %d', numel(st), isempty(st(2).f), isempty(st(3).g));
end

function s = g_row_delete_alias()
x = [1 2; 3 4; 5 6]; y = x;
y(2, :) = [];
s = sprintf('%s %s', mat2str(x), mat2str(y));
end

function s = g_cell_row_delete_alias()
c = {1, 2; 3, 4}; d = c;
d(1, :) = [];
s = sprintf('%s %s', mat2str(size(c)), mat2str(size(d)));
end

function s = g_colon_delete_all()
x = [1 2 3]; y = x;
x(:) = [];
s = sprintf('%s %s', mat2str(size(x)), mat2str(y));
end

function s = g_growth_from_empty()
x = [];
x(3) = 1;
s = mat2str(x);
end

function s = g_num2cell_then_write()
x = [1 2 3];
c = num2cell(x);
c{1} = 9;
s = sprintf('%s %g', mat2str(x), c{1});
end

function s = g_isequal_after_alias_write()
x = {1, [2 3]};
y = x;
y{2}(1) = 7;
s = sprintf('%d %s', isequal(x, y), mat2str(x{2}));
end

% --- the order of subscripts, end and the right-hand side by target shape (borrow_probe29.m, 30) --

function k = idx_l()
vlog('idx');
k = 2;
end

function v = rhs_l()
vlog('rhs');
v = 9;
end

function n = name_l()
vlog('name');
n = 'g';
end

function s = g_order_var_subscript_vs_rhs()
x = [1 2 3];
x(idx_l()) = rhs_l();
s = logged(x);
end

function s = g_order_var_paren_2d()
x = [1 2; 3 4];
x(idx_l(), 1) = rhs_l();
s = logged(x);
end

function k = key_l()
vlog('key');
k = 2;
end

function s = g_order_dictionary_key_vs_rhs()
d = dictionary([1 2], [10 20]);
d(key_l()) = rhs_l();
s = logged([d(1) d(2)]);
end

function v = grow_g()
global g_order
vlog('rhs');
g_order(end + 1) = 50;
v = 9;
end

function s = g_order_var_end_vs_rhs()
global g_order
g_order = [1 2 3];
g_order(end) = grow_g();
s = logged(g_order);
end

function s = a151_order_one_level_brace()
c = {1, 2, 3};
c{idx_l()} = rhs_l();
s = logged(mat2str(cell2mat(c)));
end

function s = a151_order_field_then_paren()
st.f = [1 2 3];
st.f(idx_l()) = rhs_l();
s = logged(st.f);
end

function s = a151_order_brace_then_paren()
c = {[1 2 3]};
c{1}(idx_l()) = rhs_l();
s = logged(c{1});
end

function s = a151_order_paren_then_field()
st = struct('f', {1, 2, 3});
st(idx_l()).f = rhs_l();
s = logged(mat2str([st.f]));
end

function s = a151_order_dynamic_field()
st.f = 1;
st.(name_l()) = rhs_l();
s = logged(st.g);
end

function s = a151_order_field_field_paren()
st.a.b = [1 2 3];
st.a.b(idx_l()) = rhs_l();
s = logged(st.a.b);
end

function v = growc_g()
global gc_order
vlog('rhs');
gc_order{1}(end + 1) = 50;
v = 9;
end

function s = a153_order_cell_elem_end_vs_rhs()
global gc_order
gc_order = {[1 2 3]};
gc_order{1}(end) = growc_g();
s = logged(gc_order{1});
end

function v = grows_g()
global gs_order
vlog('rhs');
gs_order.f(end + 1) = 50;
v = 9;
end

function s = a153_order_struct_field_end_vs_rhs()
global gs_order
gs_order.f = [1 2 3];
gs_order.f(end) = grows_g();
s = logged(gs_order.f);
end

function v = growt_g()
global gt_order
vlog('rhs');
gt_order.Var1(end) = 50;
v = 9;
end

function s = a153_order_table_var_end_vs_rhs()
global gt_order
gt_order = table([1; 2; 3]);
gt_order.Var1(end) = growt_g();
s = logged(gt_order.Var1);
end

function n = rhsname_l()
vlog('rhs');
n = 'b';
end

function s = a153_order_table_varnames_sub_vs_rhs()
T = table([1; 2], [3; 4]);
T.Properties.VariableNames{idx_l()} = rhsname_l();
s = logged(strjoin(T.Properties.VariableNames, ','));
end

function s = a153_order_datetime_day_sub_vs_rhs()
e = datetime(2020, 1, 1) + days(0:2);
e.Day(idx_l()) = rhs_l();
s = logged(mat2str(day(e)));
end
