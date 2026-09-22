% struct_field_cslist_assign.m -- V6 of the value-ownership plan: comma-list assignment to a struct
% array's field, [st.f] = deal(v), [st.f] = C{:}, [st(2:3).f] = deal(a, b) (appendix A #30), and the
% same over an array of graphics handles, [h.LineWidth] = deal(3) (#134). Each element's field is an
% entry of its own: it takes a share of the value (M2) and detaches on a later write (M3); count
% mismatches are refused in MATLAB's words.

run_case('d_deal_one_to_all', @d_deal_one_to_all);
run_case('d_deal_shares_then_detaches', @d_deal_shares_then_detaches);
run_case('d_deal_one_each', @d_deal_one_each);
run_case('d_cell_spread', @d_cell_spread);
run_case('d_spread_extra_values', @d_spread_extra_values);
run_case('d_spread_too_few', @d_spread_too_few);
run_case('d_deal_count_mismatch', @d_deal_count_mismatch);
run_case('d_range_subset', @d_range_subset);
run_case('d_range_grows', @d_range_grows);
run_case('d_range_end', @d_range_end);
run_case('d_logical_mask_subset', @d_logical_mask_subset);
run_case('d_new_field', @d_new_field);
run_case('d_new_variable', @d_new_variable);
run_case('d_2d_struct_array', @d_2d_struct_array);
run_case('d_mixed_targets', @d_mixed_targets);
run_case('d_tilde_target', @d_tilde_target);
run_case('d_size_outputs', @d_size_outputs);
run_case('d_user_function_outputs', @d_user_function_outputs);
run_case('d_nargout_in_callee', @d_nargout_in_callee);
run_case('d_dynamic_field', @d_dynamic_field);
run_case('d_nested_path', @d_nested_path);
run_case('d_cell_held_struct_array', @d_cell_held_struct_array);
run_case('d_scalar_struct', @d_scalar_struct);
run_case('d_empty_struct_array', @d_empty_struct_array);
run_case('d_read_back_list', @d_read_back_list);
run_case('d_string_values', @d_string_values);
run_case('d_cell_values', @d_cell_values);
run_case('d_alias_isolation', @d_alias_isolation);
run_case('d_subscript_evaluates_once', @d_subscript_evaluates_once);
run_case('d_single_assign_refused', @d_single_assign_refused);
run_case('d_deal_string_into_cell', @d_deal_string_into_cell);
run_case('g_handle_array_one_to_all', @g_handle_array_one_to_all);
run_case('g_handle_array_one_each', @g_handle_array_one_each);
run_case('g_handle_array_subset', @g_handle_array_subset);
run_case('g_handle_array_read_list', @g_handle_array_read_list);

function run_case(name, fn)
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

function [a, b] = two_out(x)
a = x; b = -x;
end

function varargout = count_out()
varargout = num2cell(1:nargout);
end

function k = bump()
global cslist_bumps
cslist_bumps = cslist_bumps + 1;
k = 2;
end

function s = d_deal_one_to_all()
st = struct('f', {0, 0});
[st.f] = deal(5);
s = sprintf('%d %d', st(1).f, st(2).f);
end

function s = d_deal_shares_then_detaches()
v = [1 2 3];
st = struct('f', {0, 0});
[st.f] = deal(v);
v(1) = 7;
st(1).f(2) = 9;
s = sprintf('%s %s %s', mat2str(v), mat2str(st(1).f), mat2str(st(2).f));
end

function s = d_deal_one_each()
st = struct('f', {0, 0});
[st.f] = deal(1, 2);
s = sprintf('%d %d', st(1).f, st(2).f);
end

function s = d_cell_spread()
st = struct('f', {0, 0});
C = {10, 20};
[st.f] = C{:};
s = sprintf('%d %d', st(1).f, st(2).f);
end

function s = d_spread_extra_values()
st = struct('f', {0, 0});
C = {1, 2, 3};
[st.f] = C{:};
s = sprintf('%d %d %d', st(1).f, st(2).f, numel(st));
end

function s = d_spread_too_few()
st = struct('f', {0, 0});
C = {1};
[st.f] = C{:};
s = sprintf('%d %d', st(1).f, st(2).f);
end

function s = d_deal_count_mismatch()
st = struct('f', {0, 0});
[st.f] = deal(1, 2, 3);
s = sprintf('%d %d', st(1).f, st(2).f);
end

function s = d_range_subset()
st = struct('f', {0, 0, 0});
[st(2:3).f] = deal(20, 30);
s = sprintf('%d %d %d', st(1).f, st(2).f, st(3).f);
end

function s = d_range_grows()
st = struct('f', {1, 2});
[st(3:4).f] = deal(3, 4);
s = sprintf('%d %s', numel(st), mat2str([st.f]));
end

function s = d_range_end()
st = struct('f', {0, 0, 0});
[st(end - 1:end).f] = deal(7, 8);
s = mat2str([st.f]);
end

function s = d_logical_mask_subset()
st = struct('f', {0, 0, 0});
[st([true false true]).f] = deal(1, 3);
s = mat2str([st.f]);
end

function s = d_new_field()
st = struct('f', {1, 2});
[st.g] = deal('x');
s = sprintf('%s %s %s', strjoin(fieldnames(st)', ','), st(1).g, st(2).g);
end

function s = d_new_variable()
[q(1:2).f] = deal(1, 2);
s = sprintf('%s %s %s', class(q), mat2str(size(q)), mat2str([q.f]));
end

function s = d_2d_struct_array()
st = struct('f', {0 0; 0 0});
[st.f] = deal(1, 2, 3, 4);
s = sprintf('%d %d %d %d %s', st(1, 1).f, st(2, 1).f, st(1, 2).f, st(2, 2).f, mat2str(size(st)));
end

function s = d_mixed_targets()
st = struct('f', {0, 0});
[a, st.f, b] = deal(1, 2, 3, 4);
s = sprintf('%d %d %d %d', a, st(1).f, st(2).f, b);
end

function s = d_tilde_target()
st = struct('f', {0, 0});
[~, st.f] = deal(1, 2, 3);
s = sprintf('%d %d', st(1).f, st(2).f);
end

function s = d_size_outputs()
st = struct('f', {0, 0});
[st.f] = size(ones(2, 3));
s = sprintf('%d %d', st(1).f, st(2).f);
end

function s = d_user_function_outputs()
st = struct('f', {0, 0});
[st.f] = two_out(5);
s = sprintf('%d %d', st(1).f, st(2).f);
end

function s = d_nargout_in_callee()
st = struct('f', {0, 0, 0});
[st.f] = count_out();
s = mat2str([st.f]);
end

function s = d_dynamic_field()
st = struct('f', {0, 0});
name = 'f';
[st.(name)] = deal(8);
s = sprintf('%d %d', st(1).f, st(2).f);
end

function s = d_nested_path()
h.list = struct('f', {0, 0});
[h.list.f] = deal(1, 2);
s = sprintf('%d %d', h.list(1).f, h.list(2).f);
end

function s = d_cell_held_struct_array()
k = {struct('f', {0, 0})};
[k{1}.f] = deal(1, 2);
s = sprintf('%d %d', k{1}(1).f, k{1}(2).f);
end

function s = d_scalar_struct()
st = struct('f', 0);
[st.f] = deal(3);
s = sprintf('%d %s', st.f, mat2str(size(st)));
end

function s = d_empty_struct_array()
st = struct('f', {});
[st.f] = deal(1);
s = sprintf('%d', numel(st));
end

function s = d_read_back_list()
st = struct('f', {0, 0, 0});
[st.f] = deal(4, 5, 6);
x = [st.f];
c = {st.f};
s = sprintf('%s %d %d', mat2str(x), numel(c), c{3});
end

function s = d_string_values()
st = struct('f', {0, 0});
[st.f] = deal("a", "b");
s = sprintf('%s %s %s', class(st(1).f), st(1).f, st(2).f);
end

function s = d_cell_values()
st = struct('f', {0, 0});
[st.f] = deal({1, 2});
st(1).f{1} = 9;
s = sprintf('%d %d %d %d', iscell(st(2).f), st(1).f{1}, st(2).f{1}, st(2).f{2});
end

function s = d_alias_isolation()
st = struct('f', {0, 0});
t = st;
[st.f] = deal(9);
s = sprintf('%d %d %d %d', st(1).f, st(2).f, t(1).f, t(2).f);
end

function s = d_subscript_evaluates_once()
global cslist_bumps
cslist_bumps = 0;
st = struct('f', {0, 0, 0});
[st(bump()).f] = deal(9);
s = sprintf('%d %s', cslist_bumps, mat2str([st.f]));
end

function s = d_single_assign_refused()
st = struct('f', {0, 0});
st.f = 5;
s = sprintf('%d %d', st(1).f, st(2).f);
end

function s = d_deal_string_into_cell()
c = {["a" "b"]};
[c{1}, b] = deal("z", c{1});
s = sprintf('%s %s %s %d', class(c{1}), c{1}, class(b), numel(b));
end

function s = g_handle_array_one_to_all()
f = figure('Visible', 'off');
p1 = plot([1 2 3]);
hold on
p2 = plot([4 5 6]);
h = [p1 p2];
[h.LineWidth] = deal(3);
s = sprintf('%g %g', p1.LineWidth, p2.LineWidth);
close(f);
end

function s = g_handle_array_one_each()
f = figure('Visible', 'off');
p1 = plot([1 2 3]);
hold on
p2 = plot([4 5 6]);
h = [p1 p2];
[h.LineWidth] = deal(1, 2);
s = sprintf('%g %g', p1.LineWidth, p2.LineWidth);
close(f);
end

function s = g_handle_array_subset()
f = figure('Visible', 'off');
p1 = plot([1 2 3]);
hold on
p2 = plot([4 5 6]);
p3 = plot([7 8 9]);
h = [p1 p2 p3];
[h.LineWidth] = deal(1);
[h(2:3).LineWidth] = deal(4);
s = sprintf('%g %g %g', p1.LineWidth, p2.LineWidth, p3.LineWidth);
close(f);
end

function s = g_handle_array_read_list()
f = figure('Visible', 'off');
p1 = plot([1 2 3]);
hold on
p2 = plot([4 5 6]);
h = [p1 p2];
[h.LineWidth] = deal(1, 2);
w = [h.LineWidth];
s = mat2str(w);
close(f);
end
