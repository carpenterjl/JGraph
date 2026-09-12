% ADR 0152 — the eight gaps found writing head2head_GUI's launcher, measured against R2025b.
% One section per gap: a comma-separated list reached through a field, datestr's own format
% language, growing a cell by brace assignment, the grouped bar layout, bar's name/value form
% after a width, and categorical's value set.

% --- 1. a comma-separated list through a struct field ------------------------------------------
s = struct('a', {1, 2, 3}, 'b', {'x', 'y', 'z'});
h.list = s;
q.inner.list = s;
k = {s};

fprintf('CHK|list_plain|%s|exact\n', strjoin({s.b}, ','));
fprintf('CHK|list_through_field|%s|exact\n', strjoin({h.list.b}, ','));
fprintf('CHK|list_through_field_size|%s|shape\n', mat2str(size({h.list.b})));
fprintf('CHK|list_two_fields_deep|%s|exact\n', strjoin({q.inner.list.b}, ','));
fprintf('CHK|list_through_cell|%s|exact\n', strjoin({k{1}.b}, ','));
fprintf('CHK|list_field_sliced|%s|exact\n', strjoin({h.list(2:3).b}, ','));
fprintf('CHK|list_numeric_through_field|%s|exact\n', mat2str([h.list.a]));
fprintf('CHK|list_count|%.17g|exact\n', numel({h.list.b}));
fprintf('CHK|list_as_arguments|%s|exact\n', sprintf('%s%s%s', h.list.b));
sorted = sort({h.list.b});
fprintf('CHK|list_sorted|%s|exact\n', strjoin(sorted, ','));

% --- 2. datestr reads its own format language --------------------------------------------------
d = datenum(2026, 9, 12, 14, 35, 7.25);
morning = datenum(2026, 1, 5, 9, 5, 3);
fprintf('CHK|datestr_default|%s|exact\n', datestr(d));
fprintf('CHK|datestr_iso|%s|exact\n', datestr(d, 'yyyy-mm-dd HH:MM:SS'));
fprintf('CHK|datestr_day_month_year|%s|exact\n', datestr(d, 'dd-mmm-yyyy'));
fprintf('CHK|datestr_month_name|%s|exact\n', datestr(d, 'mmmm dd, yyyy'));
fprintf('CHK|datestr_compact|%s|exact\n', datestr(d, 'yyyymmddTHHMMSS'));
fprintf('CHK|datestr_fraction|%s|exact\n', datestr(d, 'HH:MM:SS.FFF'));
fprintf('CHK|datestr_quarter|%s|exact\n', datestr(d, 'QQ yy'));
fprintf('CHK|datestr_weekday|%s|exact\n', datestr(d, 'ddd mmm dd'));
fprintf('CHK|datestr_weekday_full|%s|exact\n', datestr(d, 'dddd'));
fprintf('CHK|datestr_initials|%s|exact\n', datestr(d, 'd m'));
fprintf('CHK|datestr_meridiem|%s|exact\n', datestr(d, 'HH:MM PM'));
fprintf('CHK|datestr_meridiem_am|%s|exact\n', datestr(morning, 'HH:MM PM'));
fprintf('CHK|datestr_hour_24|%s|exact\n', datestr(morning, 'HH:MM:SS'));
fprintf('CHK|datestr_literal|%s|exact\n', datestr(d, 'yyyy at HH:MM'));
for id = [0 1 2 3 5 12 13 14 16 17 18 21 23 26 29 30 31]
    fprintf('CHK|datestr_id_%d|%s|exact\n', id, datestr(d, id));
end
fprintf('CHK|datestr_midnight_default|%s|exact\n', datestr(datenum(2026, 1, 5)));
stacked = datestr([d; morning], 'yyyy-mm-dd HH:MM:SS');
fprintf('CHK|datestr_vector_size|%s|shape\n', mat2str(size(stacked)));
fprintf('CHK|datestr_vector_second|%s|exact\n', strtrim(stacked(2, :)));

% --- 3. growing a cell by brace assignment -----------------------------------------------------
C = {}; C{2, 1} = 'a';
fprintf('CHK|cell_grow_two_subscripts|%s|shape\n', mat2str(size(C)));
fprintf('CHK|cell_grow_wrote|%s|exact\n', C{2, 1});
D = cell(0, 1); D{2, 1} = 'b';
fprintf('CHK|cell_grow_from_column|%s|shape\n', mat2str(size(D)));
E = {}; E{3} = 'c';
fprintf('CHK|cell_grow_linear_empty|%s|shape\n', mat2str(size(E)));
F = {'p'; 'q'}; F{4} = 'd';
fprintf('CHK|cell_grow_column_stays_column|%s|shape\n', mat2str(size(F)));
fprintf('CHK|cell_grow_column_kept|%s|exact\n', F{2});
G = {'p', 'q'}; G{4} = 'e';
fprintf('CHK|cell_grow_row_stays_row|%s|shape\n', mat2str(size(G)));
H = {}; H{2, 3} = 'f';
fprintf('CHK|cell_grow_rectangle|%s|shape\n', mat2str(size(H)));
K = cell(2, 2); K{3, 3} = 'g';
fprintf('CHK|cell_grow_both_ways|%s|shape\n', mat2str(size(K)));
L = cell(1, 0); L{3} = 'h';
fprintf('CHK|cell_grow_from_empty_row|%s|shape\n', mat2str(size(L)));
N = {'p'; 'q'}; N{1, 4} = 'i';
fprintf('CHK|cell_grow_column_widened|%s|shape\n', mat2str(size(N)));
fprintf('CHK|cell_grow_fill_is_empty|%.17g|exact\n', numel(C{1, 1}));
fprintf('CHK|cell_grow_fill_shape|%s|div=ADR0152\n', mat2str(size(C{1, 1})));

% --- 4. the grouped bar layout and bar's name/value form ---------------------------------------
figure;
b2 = bar(1:3, [1 2; 3 4; 5 6], 0.8);
fprintf('CHK|bar_group_two_first|%s|exact\n', mat2str(b2(1).XEndPoints, 17));
fprintf('CHK|bar_group_two_second|%s|exact\n', mat2str(b2(2).XEndPoints, 17));
close all

figure;
b3 = bar(1:3, [1 2 3; 3 4 5; 5 6 7]);
fprintf('CHK|bar_group_three_first|%s|exact\n', mat2str(b3(1).XEndPoints, 17));
fprintf('CHK|bar_group_three_middle|%s|exact\n', mat2str(b3(2).XEndPoints, 17));
close all

figure;
b6 = bar(1:2, ones(2, 6));
fprintf('CHK|bar_group_six_first|%s|exact\n', mat2str(b6(1).XEndPoints, 17));
close all

figure;
bs = bar([1 3 5], [1 2; 3 4; 5 6]);
fprintf('CHK|bar_group_spacing_two|%s|exact\n', mat2str(bs(1).XEndPoints, 17));
close all

figure;
b1 = bar(1:3, [1; 3; 5], 0.8);
fprintf('CHK|bar_single_series|%s|exact\n', mat2str(b1.XEndPoints, 17));
close all

figure;
bn = bar(1, 0.5, 0.6, 'FaceColor', [0 0 1], 'EdgeColor', 'none');
fprintf('CHK|bar_width_then_pairs_x|%s|exact\n', mat2str(bn.XEndPoints, 17));
fprintf('CHK|bar_width_then_pairs_width|%.17g|exact\n', bn.BarWidth);
fprintf('CHK|bar_width_then_pairs_color|%s|exact\n', mat2str(bn.FaceColor, 17));
close all

figure;
bw = bar(1:3, [1 2 3], 0.4, 'FaceColor', [1 0 0]);
fprintf('CHK|bar_vector_width_then_pairs|%.17g|exact\n', bw.BarWidth);
close all

figure;
bt = bar(1, 0.5);
fprintf('CHK|bar_two_scalars_is_x_and_y|%.17g|exact\n', bt.BarWidth);
fprintf('CHK|bar_two_scalars_position|%.17g|exact\n', bt.XEndPoints(1));
close all

% --- 5. categorical over a value set -----------------------------------------------------------
c = categorical([1 1 2]', [1 2], {'A', 'B'});
fprintf('CHK|categorical_size|%s|shape\n', mat2str(size(c)));
fprintf('CHK|categorical_names|%s|exact\n', strjoin(cellstr(c)', ','));
extra = categorical([1 1 2]', [1 2 3], {'A', 'B', 'C'});
fprintf('CHK|categorical_unused_level|%s|exact\n', strjoin(cellstr(extra)', ','));
text = categorical({'x', 'y', 'x'}, {'y', 'x'}, {'Why', 'Ex'});
fprintf('CHK|categorical_text_values|%s|exact\n', strjoin(cellstr(text), ','));
outside = categorical([1 3 2], [1 2], {'A', 'B'});
fprintf('CHK|categorical_value_outside_set|%s|exact\n', strjoin(cellstr(outside), ','));
bare = categorical([1 2], [1 2]);
fprintf('CHK|categorical_value_set_names_itself|%s|exact\n', strjoin(cellstr(bare), ','));
