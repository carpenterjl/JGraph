% datetime_forms.m -- the datetime and duration forms of stage V6 (ADR 0167, #123, #127-#129):
% scalar expansion into a mask selection, text written into a datetime or a duration, a number
% written into a duration, an element's component set (d(2).Year = 2000), the refusals in R2025b's
% words, and the display format following what the array holds. The g_ cases are the alias,
% format, timezone, deletion, arithmetic-class and loop lines kept as guards.

run_case('df_mask_nat', @df_mask_nat);
run_case('df_text_char', @df_text_char);
run_case('df_text_string', @df_text_string);
run_case('df_text_string_array', @df_text_string_array);
run_case('df_text_cell', @df_text_cell);
run_case('df_text_garbage', @df_text_garbage);
run_case('df_text_slash_form', @df_text_slash_form);
run_case('df_text_with_time', @df_text_with_time);
run_case('df_number', @df_number);
run_case('df_nan', @df_nan);
run_case('df_delete', @df_delete);
run_case('df_mask_scalar_dt', @df_mask_scalar_dt);
run_case('df_grow_text', @df_grow_text);
run_case('df_grow_gap_text', @df_grow_gap_text);
run_case('df_colon_nat', @df_colon_nat);
run_case('df_rhs_other_format', @df_rhs_other_format);
run_case('df_empty_double_grows', @df_empty_double_grows);
run_case('df_double_target', @df_double_target);
run_case('df_two_subs_nat', @df_two_subs_nat);
run_case('df_two_subs_text', @df_two_subs_text);
run_case('df_struct_field_text', @df_struct_field_text);
run_case('df_cell_held_text', @df_cell_held_text);
run_case('df_elem_component', @df_elem_component);
run_case('df_elem_component_range', @df_elem_component_range);
run_case('df_struct_elem_component', @df_struct_elem_component);
run_case('df_component_elem', @df_component_elem);
run_case('df_dur_text', @df_dur_text);
run_case('df_dur_number', @df_dur_number);
run_case('df_dur_mask_scalar', @df_dur_mask_scalar);
run_case('df_dur_nan', @df_dur_nan);
run_case('df_dur_grow', @df_dur_grow);
run_case('df_dur_into_dt', @df_dur_into_dt);
run_case('df_dt_into_dur', @df_dt_into_dur);
run_case('df_zoned_text', @df_zoned_text);
run_case('df_text_nat', @df_text_nat);
run_case('df_empty_char', @df_empty_char);
run_case('df_overlap', @df_overlap);
run_case('df_char_matrix_one_slot', @df_char_matrix_one_slot);
run_case('df_char_matrix_rows', @df_char_matrix_rows);
run_case('df_format_driven', @df_format_driven);
run_case('df_compact_text', @df_compact_text);
run_case('df_int8', @df_int8);
run_case('df_cell_of_dt', @df_cell_of_dt);
run_case('df_struct_rhs', @df_struct_rhs);
run_case('df_string_expand', @df_string_expand);
run_case('df_char_target', @df_char_target);
run_case('df_dur_string_array', @df_dur_string_array);
run_case('df_logical_rhs', @df_logical_rhs);
run_case('df_string_missing', @df_string_missing);
run_case('df_text_two_into_one', @df_text_two_into_one);
run_case('df_mask_text', @df_mask_text);
run_case('df_dur_text_bad', @df_dur_text_bad);
run_case('df_nd_grow', @df_nd_grow);
run_case('df_end_plus_text_col', @df_end_plus_text_col);
run_case('df_zoned_from_unzoned', @df_zoned_from_unzoned);
run_case('df_unzoned_from_zoned', @df_unzoned_from_zoned);
run_case('df_dur_compound', @df_dur_compound);
run_case('df_text_iso_t', @df_text_iso_t);
run_case('df_year_only', @df_year_only);
run_case('df_format_follows_time', @df_format_follows_time);
run_case('df_format_falls_back', @df_format_falls_back);
run_case('df_format_set_stays', @df_format_set_stays);
run_case('df_nat_format', @df_nat_format);
run_case('df_reshape_keeps_class', @df_reshape_keeps_class);
run_case('df_char_duration_pad', @df_char_duration_pad);
run_case('df_alias_after_text', @df_alias_after_text);
run_case('df_loop_text', @df_loop_text);
run_case('g_dt_alias_elem_write', @g_dt_alias_elem_write);
run_case('g_dt_format_alias', @g_dt_format_alias);
run_case('g_dt_timezone_alias', @g_dt_timezone_alias);
run_case('g_dt_delete_alias', @g_dt_delete_alias);
run_case('g_dt_arith_classes', @g_dt_arith_classes);
run_case('g_dur_diff_alias', @g_dur_diff_alias);
run_case('g_dt_cell_hold', @g_dt_cell_hold);
run_case('g_dt_concat_alias', @g_dt_concat_alias);
run_case('g_dt_plus_rebind_alias', @g_dt_plus_rebind_alias);
run_case('g_dur_minus_in_loop_alias', @g_dur_minus_in_loop_alias);

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

function s = sh(v)
if isdatetime(v) || isduration(v)
    s = sprintf('%s[%s/%s]', class(v), strjoin(cellstr(char(v(:)))', ','), v.Format);
    if isdatetime(v) && ~isempty(v.TimeZone), s = [s '@' v.TimeZone]; end
    s = sprintf('%s %dx%d', s, size(v, 1), size(v, 2));
elseif iscell(v)
    parts = cellfun(@sh, v, 'UniformOutput', false);
    s = ['{' strjoin(parts(:)', ',') '}' sprintf('[%dx%d]', size(v, 1), size(v, 2))];
elseif isstring(v)
    s = ['"' strjoin(cellstr(v(:)'), '","') '"' class(v)];
elseif ischar(v)
    s = ['''' v ''''];
elseif isstruct(v)
    s = ['struct ' strjoin(fieldnames(v)', ',')];
else
    s = [mat2str(v) '/' class(v)];
end
end

function d = three_days()
d = datetime(2020, 1, 1) + days(0:2);
end

% --- the forms ---------------------------------------------------------------------------------

function s = df_mask_nat()
d = three_days(); e = d; e(day(e) > 1) = NaT;
s = sprintf('%d %d %s', sum(isnat(d)), sum(isnat(e)), sh(e));
end

function s = df_text_char()
d = three_days(); e = d; e(2) = '2021-05-05';
s = sprintf('%d %d %s', year(e(2)), year(d(2)), sh(e));
end

function s = df_text_string()
d = three_days(); d(2) = "2021-05-05"; s = sh(d);
end

function s = df_text_string_array()
d = three_days(); d(1:2) = ["2021-01-01" "2021-01-02"]; s = sh(d);
end

function s = df_text_cell()
d = three_days(); d(1:2) = {'2021-01-01', '2021-01-02'}; s = sh(d);
end

function s = df_text_garbage()
d = three_days(); d(2) = 'garbage'; s = sh(d);
end

function s = df_text_slash_form()
d = three_days(); w = warning('off', 'all'); d(2) = '05/05/2021'; warning(w); s = sh(d);
end

function s = df_text_with_time()
d = three_days(); d(2) = '2021-05-05 10:20:30'; s = sh(d);
end

function s = df_number()
d = three_days(); d(2) = 5; s = sh(d);
end

function s = df_nan()
d = three_days(); d(2) = NaN; s = sh(d);
end

function s = df_delete()
d = three_days(); e = d; e(2) = []; s = sprintf('%d %d %s', numel(d), numel(e), sh(e));
end

function s = df_mask_scalar_dt()
d = three_days(); d(day(d) > 1) = datetime(2022, 1, 1); s = sh(d);
end

function s = df_grow_text()
d = three_days(); d(end + 1) = '2022-01-01'; s = sh(d);
end

function s = df_grow_gap_text()
d = three_days(); d(5) = '2022-01-01'; s = sh(d);
end

function s = df_colon_nat()
d = three_days(); d(:) = NaT; s = sh(d);
end

function s = df_rhs_other_format()
d = three_days(); d(2) = datetime(2022, 1, 1, 'Format', 'yyyy'); s = sh(d);
end

function s = df_empty_double_grows()
x = []; x(2) = datetime(2020, 1, 1); s = sh(x);
end

function s = df_double_target()
x = zeros(1, 2); x(2) = datetime(2020, 1, 1); s = sh(x);
end

function s = df_two_subs_nat()
m = reshape(datetime(2020, 1, 1) + days(0:3), 2, 2); m(1, :) = NaT; s = sh(m);
end

function s = df_two_subs_text()
m = reshape(datetime(2020, 1, 1) + days(0:3), 2, 2); m(1, 2) = '2021-05-05'; s = sh(m);
end

function s = df_struct_field_text()
d = three_days(); st.t = d; st.t(2) = '2021-05-05'; s = sprintf('%d %s', year(d(2)), sh(st.t));
end

function s = df_cell_held_text()
d = three_days(); c = {d}; c{1}(2) = '2021-05-05'; s = sprintf('%d %s', year(d(2)), sh(c{1}));
end

function s = df_elem_component()
d = three_days(); e = d; e(2).Year = 2000; s = sprintf('%d %s', year(d(2)), sh(e));
end

function s = df_elem_component_range()
d = three_days(); d(1:2).Year = 2000; s = sh(d);
end

function s = df_struct_elem_component()
d = three_days(); st.t = d; st.t(2).Year = 1999; s = sprintf('%d %s', year(d(2)), sh(st.t));
end

function s = df_component_elem()
d = three_days(); d.Year(2) = 2000; s = sh(d);
end

function s = df_dur_text()
u = hours(1:3); u(2) = '00:30:00'; s = sh(u);
end

function s = df_dur_number()
u = hours(1:3); u(2) = 5; s = sh(u);
end

function s = df_dur_mask_scalar()
u = hours(1:3); u(u > hours(1)) = seconds(5); s = sh(u);
end

function s = df_dur_nan()
u = hours(1:3); u(2) = NaN; s = sh(u);
end

function s = df_dur_grow()
u = hours(1:3); u(5) = minutes(5); s = sh(u);
end

function s = df_dur_into_dt()
d = three_days(); d(2) = seconds(5); s = sh(d);
end

function s = df_dt_into_dur()
u = hours(1:3); u(2) = datetime(2020, 1, 1); s = sh(u);
end

function s = df_zoned_text()
z = datetime(2020, 1, 1, 12, 0, 0, 'TimeZone', 'UTC') + days(0:1); z(2) = '2020-01-02 03:00:00';
s = sprintf('%d %s', hour(z(2)), sh(z));
end

function s = df_text_nat()
d = three_days(); d(2) = "NaT"; s = sh(d);
end

function s = df_empty_char()
d = three_days(); d(2) = ''; s = sh(d);
end

function s = df_overlap()
d = three_days(); d([1 3]) = d([3 1]); s = sh(d);
end

function s = df_char_matrix_one_slot()
d = three_days(); d(2) = ['2021-05-05'; '2021-06-06']; s = sh(d);
end

function s = df_char_matrix_rows()
d = three_days(); d(1:2) = ['2021-05-05'; '2021-06-06']; s = sh(d);
end

function s = df_format_driven()
f = three_days(); f.Format = 'yyyy/MM/dd'; f(2) = '2021/05/05'; g = f; g(3) = '2021-06-06';
s = sprintf('%s / %s', sh(f), sh(g));
end

function s = df_compact_text()
d = three_days(); d(2) = '20210505'; s = sh(d);
end

function s = df_int8()
d = three_days(); d(2) = int8(5); s = sh(d);
end

function s = df_cell_of_dt()
d = three_days(); d(2) = {datetime(2021, 1, 1)}; s = sh(d);
end

function s = df_struct_rhs()
d = three_days(); d(2) = struct('a', 1); s = sh(d);
end

function s = df_string_expand()
d = three_days(); d(1:2) = "2021-05-05"; s = sh(d);
end

function s = df_char_target()
x = 'abc'; x(2) = datetime(2020, 1, 1); s = sh(x);
end

function s = df_dur_string_array()
u = hours(1:3); u(1:2) = ["01:00:00" "02:30:00"]; s = sh(u);
end

function s = df_logical_rhs()
d = three_days(); d(2) = true; s = sh(d);
end

function s = df_string_missing()
d = three_days(); d(2) = string(missing); s = sh(d);
end

function s = df_text_two_into_one()
d = three_days(); d(2) = {'2021-05-05', '2021-06-06'}; s = sh(d);
end

function s = df_mask_text()
d = three_days(); d(day(d) > 1) = '2021-05-05'; s = sh(d);
end

function s = df_dur_text_bad()
u = hours(1:3); u(2) = 'garbage'; s = sh(u);
end

function s = df_nd_grow()
d = three_days(); d(1, 2, 2) = '2021-05-05'; s = sprintf('%s %s', mat2str(size(d)), sh(d(:)'));
end

function s = df_end_plus_text_col()
d = three_days()'; d(end + 1) = "2022-01-01"; s = sh(d);
end

function s = df_zoned_from_unzoned()
z = datetime(2020, 1, 1, 12, 0, 0, 'TimeZone', 'UTC') + days(0:1); z(2) = datetime(2020, 1, 5); s = sh(z);
end

function s = df_unzoned_from_zoned()
d = three_days(); d(2) = datetime(2020, 1, 5, 'TimeZone', 'UTC'); s = sh(d);
end

function s = df_dur_compound()
u = hours(1:3); u(2) = u(2) + minutes(30); s = sh(u);
end

function s = df_text_iso_t()
d = three_days(); d(2) = '2021-05-05T10:20:30'; s = sh(d);
end

function s = df_year_only()
d = three_days(); d(2) = '2021'; s = sh(d);
end

function s = df_format_follows_time()
d = three_days(); a = d.Format; d(2) = datetime(2020, 1, 2, 10, 0, 0);
s = sprintf('%s -> %s', a, d.Format);
end

function s = df_format_falls_back()
x = datetime(2020, 1, 1, 10, 0, 0) + hours(0:1); a = x.Format;
x(1) = datetime(2020, 1, 2); b = x.Format;
x(2) = datetime(2020, 1, 3); c = x.Format;
s = sprintf('%s -> %s -> %s', a, b, c);
end

function s = df_format_set_stays()
d = three_days(); d.Format = 'yyyy'; d(2) = datetime(2020, 1, 2, 10, 0, 0); s = sh(d);
end

function s = df_nat_format()
n = NaT(1, 2); s = sprintf('%s %d', n.Format, sum(isnat(n)));
end

function s = df_reshape_keeps_class()
m = reshape(datetime(2020, 1, 1) + days(0:3), 2, 2);
s = sprintf('%s %s %d', class(m), mat2str(size(m)), day(m(2, 2)));
end

function s = df_char_duration_pad()
c = char(hours([1 0.5])); s = sprintf('%s [%s] [%s]', mat2str(size(c)), c(1, :), c(2, :));
end

function s = df_alias_after_text()
d = three_days(); e = d; e(1) = '2021-05-05'; d(3) = '2022-06-06';
s = sprintf('%s / %s', sh(d), sh(e));
end

function s = df_loop_text()
d = three_days();
for k = 1:3
    d(k) = sprintf('2021-0%d-01', k);
end
s = sh(d);
end

% --- the guards, as in value_isolation_forms --------------------------------------------------

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

function s = g_dt_timezone_alias()
d = datetime(2020, 1, 1, 12, 0, 0, 'TimeZone', 'UTC');
e = d;
e.TimeZone = 'America/New_York';
s = sprintf('%s/%s/%d/%d', d.TimeZone, e.TimeZone, hour(d), hour(e));
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

function s = g_dt_concat_alias()
d = three_days();
e = [d, d];
e(1) = NaT;
s = sprintf('%d %d', isnat(d(1)), numel(e));
end

function s = g_dt_plus_rebind_alias()
d = three_days();
w = d;
d = d + days(1);
s = sprintf('%d %d', day(w(1)), day(d(1)));
end

function s = g_dur_minus_in_loop_alias()
h = hours(1:3);
g = h;
for k = 1:3
    h = h - minutes(30);
end
s = sprintf('%s %s', mat2str(hours(g)), mat2str(hours(h)));
end
