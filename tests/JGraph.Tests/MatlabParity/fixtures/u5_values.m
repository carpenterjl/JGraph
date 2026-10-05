% record: -noFigureWindows
% U5 of the app-building plan (ADR 0202): how the value properties of a uifigure's components play
% together - a list's Items, ItemsData, Value and ValueIndex; an edit field's limits and input
% type; a numeric field's limits, rounding and empties; a slider's limits; a button group's
% selection - in a uifigure that is never shown.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);

% --- uidropdown ---------------------------------------------------------------------------------
d = uidropdown(uf);
fprintf('CHK|dd_default|%s|exact\n', u5_list(d));
d.Items = {'x', 'y', 'z'}; fprintf('CHK|dd_items|%s|exact\n', u5_list(d));
d.Value = 'y'; fprintf('CHK|dd_value|%s|exact\n', u5_list(d));
d.Items = {'y', 'z', 'w'}; fprintf('CHK|dd_items_keep_value|%s|exact\n', u5_list(d));
d.Items = {'a', 'b'}; fprintf('CHK|dd_items_value_gone|%s|exact\n', u5_list(d));
d.ValueIndex = 2; fprintf('CHK|dd_index|%s|exact\n', u5_list(d));
indices = {3, 0, [], 1.5, [1 2]};
for k = 1:numel(indices), u2_chk(sprintf('dd_index_%d', k), @() u5_setget(d, 'ValueIndex', indices{k})); end
fprintf('CHK|dd_after_bad_indices|%s|exact\n', u5_list(d));
values = {"a", 'A', {'a'}, 1, [], ''};
for k = 1:numel(values), u2_chk(sprintf('dd_value_%d', k), @() u5_setget(d, 'Value', values{k})); end
fprintf('CHK|dd_after_values|%s|exact\n', u5_list(d));
d.Items = {'a', 'b', 'c'}; d.ItemsData = [10 20 30]; fprintf('CHK|dd_data|%s|exact\n', u5_list(d));
u2_chk('dd_data_value', @() u5_setget(d, 'Value', 20));
fprintf('CHK|dd_after_data_value|%s|exact\n', u5_list(d));
u2_chk('dd_data_value_by_text', @() u5_setget(d, 'Value', 'b'));
u2_chk('dd_data_value_int8', @() u5_setget(d, 'Value', int8(30)));
fprintf('CHK|dd_after_int8|%s|exact\n', u5_list(d));
d.ItemsData = {'p', 'q', 'r'}; fprintf('CHK|dd_data_cell|%s|exact\n', u5_list(d));
u2_chk('dd_data_cell_value', @() u5_setget(d, 'Value', 'q'));
d.ItemsData = [1 2]; fprintf('CHK|dd_data_shorter|%s|exact\n', u5_list(d));
u2_chk('dd_data_shorter_value', @() u5_setget(d, 'Value', 'c'));
u2_chk('dd_data_shorter_index', @() u5_setget(d, 'ValueIndex', 3));
d.ItemsData = [1 2 3 4 5]; fprintf('CHK|dd_data_longer|%s|exact\n', u5_list(d));
u2_chk('dd_data_longer_value', @() u5_setget(d, 'Value', 5));
d.ItemsData = {1, 'two', [3 3]}; fprintf('CHK|dd_data_mixed|%s|exact\n', u5_list(d));
u2_chk('dd_data_mixed_value', @() u5_setget(d, 'Value', [3 3]));
fprintf('CHK|dd_after_mixed|%s|exact\n', u5_list(d));
d.ItemsData = []; fprintf('CHK|dd_data_cleared|%s|exact\n', u5_list(d));
d.Items = {'a', 'a', 'b'}; d.ValueIndex = 2; fprintf('CHK|dd_duplicates|%s|exact\n', u5_list(d));
d.Items = {}; fprintf('CHK|dd_no_items|%s|exact\n', u5_list(d));
u2_chk('dd_no_items_value', @() u5_setget(d, 'Value', 'x'));
items = {'abc', [1 2 3], {1, 2}, {'a'; 'b'}, {'a', 'b'; 'c', 'd'}};
for k = 1:numel(items), u2_chk(sprintf('dd_items_%d', k), @() u5_setget(d, 'Items', items{k})); end
delete(d);
d = uidropdown(uf, 'Editable', 'on', 'Items', {'a', 'b'});
fprintf('CHK|dd_editable|%s|exact\n', u5_list(d));
d.Value = 'free'; fprintf('CHK|dd_editable_free|%s|exact\n', u5_list(d));
u2_chk('dd_editable_number', @() u5_setget(d, 'Value', 5));
d.Value = 'b'; fprintf('CHK|dd_editable_back|%s|exact\n', u5_list(d));
d.Value = 'free'; d.Editable = 'off'; fprintf('CHK|dd_editable_off|%s|exact\n', u5_list(d));
u2_chk('dd_made_value_then_items', @() get(uidropdown(uf, 'Value', 'q', 'Items', {'p', 'q'}), 'Value'));
u2_chk('dd_made_value_not_an_item', @() get(uidropdown(uf, 'Value', 'zz'), 'Value'));
u2_chk('dd_made_with_data', @() get(uidropdown(uf, 'Items', {'p', 'q'}, 'ItemsData', [7 8], 'Value', 8), 'ValueIndex'));
delete(allchild(uf));

% --- uilistbox ----------------------------------------------------------------------------------
l = uilistbox(uf);
fprintf('CHK|lb_default|%s|exact\n', u5_list(l));
l.Value = 'Item 3'; fprintf('CHK|lb_value|%s|exact\n', u5_list(l));
values = {{}, [], {'Item 1'}, {'Item 1', 'Item 2'}, 'bogus'};
for k = 1:numel(values), u2_chk(sprintf('lb_value_%d', k), @() u5_setget(l, 'Value', values{k})); end
u2_chk('lb_index_two', @() u5_setget(l, 'ValueIndex', [1 2]));
u2_chk('lb_index_past', @() u5_setget(l, 'ValueIndex', 9));
l.Value = 'Item 1'; l.Multiselect = 'on'; fprintf('CHK|lb_multi|%s|exact\n', u5_list(l));
l.Value = {'Item 3', 'Item 1'}; fprintf('CHK|lb_multi_value|%s|exact\n', u5_list(l));
l.Value = 'Item 2'; fprintf('CHK|lb_multi_char|%s|exact\n', u5_list(l));
l.ValueIndex = [4 2]; fprintf('CHK|lb_multi_index|%s|exact\n', u5_list(l));
u2_chk('lb_multi_value_twice', @() u5_setget(l, 'Value', {'Item 1', 'Item 1'}));
u2_chk('lb_multi_value_none', @() u5_setget(l, 'Value', {}));
fprintf('CHK|lb_multi_none|%s|exact\n', u5_list(l));
u2_chk('lb_multi_strings', @() u5_setget(l, 'Value', ["Item 1"; "Item 4"]));
fprintf('CHK|lb_multi_after_strings|%s|exact\n', u5_list(l));
l.Multiselect = 'off'; fprintf('CHK|lb_multi_off|%s|exact\n', u5_list(l));
l.Items = {'Item 4', 'new'}; fprintf('CHK|lb_items|%s|exact\n', u5_list(l));
l.Multiselect = 'on'; l.Value = {'Item 4', 'new'}; l.ItemsData = [5 6]; fprintf('CHK|lb_multi_data|%s|exact\n', u5_list(l));
u2_chk('lb_multi_data_value', @() u5_setget(l, 'Value', [6 5]));
u2_chk('lb_multi_data_cell', @() u5_setget(l, 'Value', {5}));
u2_chk('lb_multi_data_missing', @() u5_setget(l, 'Value', 7));
l.Items = {}; fprintf('CHK|lb_no_items|%s|exact\n', u5_list(l));
delete(l);
l = uilistbox(uf, 'Items', {'a', 'b', 'c'}, 'ItemsData', [1 2 3], 'Value', 2); fprintf('CHK|lb_made_with_data|%s|exact\n', u5_list(l));
u2_chk('lb_made_multi', @() get(uilistbox(uf, 'Multiselect', 'on', 'Value', {'Item 2', 'Item 4'}), 'ValueIndex'));
delete(allchild(uf));

% --- uieditfield --------------------------------------------------------------------------------
e = uieditfield(uf, 'CharacterLimits', [2 4], 'Value', 'abc');
texts = {'a', 'abcde', '', 'ab', 'abcd'};
for k = 1:numel(texts), u2_chk(sprintf('ef_limited_%d', k), @() u5_setget(e, 'Value', texts{k})); end
u2_chk('ef_limits_keep', @() get(uieditfield(uf, 'Value', 'abc', 'CharacterLimits', [2 4]), 'Value'));
u2_chk('ef_limits_empty_short', @() get(uieditfield(uf, 'Value', 'a', 'CharacterLimits', [2 4]), 'Value'));
u2_chk('ef_limits_empty_long', @() get(uieditfield(uf, 'Value', 'abcdef', 'CharacterLimits', [2 4]), 'Value'));
limits = {[0 0], [3 3], [5 1], [-1 3], [0 Inf], [1.5 3], [Inf Inf], 5, [1 2 3], 'a', [NaN 3]};
for k = 1:numel(limits), u2_chk(sprintf('ef_limits_%02d', k), @() u5_setget(uieditfield(uf), 'CharacterLimits', limits{k})); end
types = {'text', 'digits', 'letters', 'alphanumerics', 'bogus', 'DIGITS', 'dig', 5};
for k = 1:numel(types), u2_chk(sprintf('ef_inputtype_%d', k), @() u5_setget(uieditfield(uf), 'InputType', types{k})); end
u2_chk('ef_digits_over_letters', @() get(uieditfield(uf, 'Value', 'abc', 'InputType', 'digits'), 'Value'));
kinds = {'digits', 'letters', 'alphanumerics'};
texts = {'123', '12a', '1.5', '-1', 'abc', 'ab c', 'ab1', 'ab_1', ''};
for i = 1:numel(kinds)
    e = uieditfield(uf, 'InputType', kinds{i});
    for k = 1:numel(texts), u2_chk(sprintf('ef_%s_%d', kinds{i}, k), @() u5_setget(e, 'Value', texts{k})); end
end
delete(allchild(uf));

% --- the numeric field and the spinner ----------------------------------------------------------
both = {'NumericEditField', 'Spinner'};
for i = 1:2
    t = both{i};
    n = u5_make(t, uf); n.Limits = [0 10];
    inside = {0, 10, -1, 11, 5.5};
    for k = 1:numel(inside), u2_chk(sprintf('%s_closed_%d', t, k), @() u5_setget(n, 'Value', inside{k})); end
    n.LowerLimitInclusive = 'off'; n.UpperLimitInclusive = 'off'; n.Value = 5;
    inside = {0, 10, 0.001};
    for k = 1:numel(inside), u2_chk(sprintf('%s_open_%d', t, k), @() u5_setget(n, 'Value', inside{k})); end
    n = u5_make(t, uf); n.Value = 50; n.Limits = [0 10]; fprintf('CHK|%s_limits_under_value|%s|exact\n', t, u5_text(n.Value));
    n = u5_make(t, uf); n.Value = -50; n.Limits = [0 10]; fprintf('CHK|%s_limits_over_value|%s|exact\n', t, u5_text(n.Value));
    n = u5_make(t, uf); n.Limits = [0 10]; n.Value = 10; n.UpperLimitInclusive = 'off'; fprintf('CHK|%s_upper_opened|%s|exact\n', t, u5_text(n.Value));
    n = u5_make(t, uf); n.Limits = [0 10]; n.Value = 0; n.LowerLimitInclusive = 'off'; fprintf('CHK|%s_lower_opened|%s|exact\n', t, u5_text(n.Value));
    limits = {[0 10], [10 0], [5 5], [-Inf Inf], [0 Inf], [-Inf 0], [Inf Inf], [NaN 1], 5, [1 2 3], 'a', [1; 2], int8([1 5]), [], {1, 2}};
    for k = 1:numel(limits), u2_chk(sprintf('%s_limits_%02d', t, k), @() u5_setget(u5_make(t, uf), 'Limits', limits{k})); end
    n = u5_make(t, uf); n.Value = 2.6; n.RoundFractionalValues = 'on'; fprintf('CHK|%s_rounded|%s|exact\n', t, u5_text(n.Value));
    rounds = {2.5, 3.5, -2.5, 2.4999, 1e10};
    for k = 1:numel(rounds), u2_chk(sprintf('%s_rounding_%d', t, k), @() u5_setget(n, 'Value', rounds{k})); end
    n = u5_make(t, uf); n.Limits = [0 2.5]; n.RoundFractionalValues = 'on';
    u2_chk([t '_rounding_at_the_limit'], @() u5_setget(n, 'Value', 2.5));
    u2_chk([t '_rounding_inside'], @() u5_setget(n, 'Value', 2.2));
    n = u5_make(t, uf); n.Value = 2.6; n.Limits = [0 2.9]; n.RoundFractionalValues = 'on'; fprintf('CHK|%s_rounded_inside_limits|%s|exact\n', t, u5_text(n.Value));
    n = u5_make(t, uf);
    u2_chk([t '_empty_refused'], @() u5_setget(n, 'Value', []));
    n.AllowEmpty = 'on';
    u2_chk([t '_empty_allowed'], @() u5_setget(n, 'Value', []));
    n.AllowEmpty = 'off'; fprintf('CHK|%s_empty_filled|%s|exact\n', t, u5_text(n.Value));
    formats = {'%d', '%.2f', '%11.4g', '%s', 'abc', '%d units', '%5.1f%%', '', 5, '%d %d', '%x', '%e', '%i', '%c', '$%.2f', "%.3f", '%bx', '%*d', '%u', '%o', '%G', '%+d', '%ld'};
    for k = 1:numel(formats), u2_chk(sprintf('%s_format_%02d', t, k), @() u5_setget(u5_make(t, uf), 'ValueDisplayFormat', formats{k})); end
    delete(allchild(uf));
end
steps = {1, 0.5, 0, -1, [1 2], 'a', Inf, NaN, int8(2), [], 1e-3, true};
for k = 1:numel(steps), u2_chk(sprintf('spinner_step_%02d', k), @() u5_setget(uispinner(uf), 'Step', steps{k})); end
u2_chk('spinner_step_fraction_while_rounding', @() u5_setget(uispinner(uf, 'RoundFractionalValues', 'on'), 'Step', 0.5));
u2_chk('spinner_rounding_with_fraction_step', @() u5_setget(uispinner(uf, 'Step', 0.5), 'RoundFractionalValues', 'on'));
delete(allchild(uf));

% --- uislider -----------------------------------------------------------------------------------
s = uislider(uf);
values = {50, 0, 100, -1, 101, [1 2], 'a', NaN, int8(5), true, [], 33.333};
for k = 1:numel(values), u2_chk(sprintf('slider_value_%02d', k), @() u5_setget(s, 'Value', values{k})); end
s = uislider(uf); s.Value = 80; s.Limits = [0 50]; fprintf('CHK|slider_limits_under_value|%s %s|exact\n', u5_text(s.Value), u5_text(s.Step));
s = uislider(uf); s.Value = 20; s.Limits = [30 50]; fprintf('CHK|slider_limits_over_value|%s %s|exact\n', u5_text(s.Value), u5_text(s.Step));
limits = {[10 0], [5 5], [-Inf Inf], [0 Inf], [NaN 1], 5, [1 2 3], 'a', int8([1 5]), [], [0 1]};
for k = 1:numel(limits), u2_chk(sprintf('slider_limits_%02d', k), @() u5_setget(uislider(uf), 'Limits', limits{k})); end
s = uislider(uf); s.MajorTicks = [0 50 100];
fprintf('CHK|slider_ticks_set|%s %s %s %s|exact\n', u5_text(s.MajorTicks), s.MajorTicksMode, u5_text(s.MajorTickLabels), s.MajorTickLabelsMode);
s = uislider(uf); s.MajorTickLabels = {'lo', 'hi'};
fprintf('CHK|slider_labels_set|%s %s %s|exact\n', u5_text(s.MajorTicks), u5_text(s.MajorTickLabels), s.MajorTickLabelsMode);
s = uislider(uf); s.MinorTicks = 0:25:100; fprintf('CHK|slider_minor_set|%s %s|exact\n', u5_text(s.MinorTicks), s.MinorTicksMode);
s = uislider(uf); s.MajorTicks = []; fprintf('CHK|slider_no_ticks|%s %s|exact\n', u5_text(s.MajorTicks), u5_text(s.MajorTickLabels));
s = uislider(uf); s.MajorTicks = [200 300]; fprintf('CHK|slider_ticks_outside|%s %s|exact\n', u5_text(s.MajorTicks), u5_text(s.MajorTickLabels));
ticks = {[3 1 2], [1 1 2], 'a', {1, 2}, NaN, [1 Inf], [1; 2], int8([1 2])};
for k = 1:numel(ticks), u2_chk(sprintf('slider_major_%d', k), @() u5_setget(uislider(uf), 'MajorTicks', ticks{k})); end
for k = 1:numel(ticks), u2_chk(sprintf('slider_minor_%d', k), @() u5_setget(uislider(uf), 'MinorTicks', ticks{k})); end
labels = {{'a', 'b'}, ["a" "b"], 'ab', [1 2], {1, 2}, {}, {'a'; 'b'}};
for k = 1:numel(labels), u2_chk(sprintf('slider_labels_%d', k), @() u5_setget(uislider(uf), 'MajorTickLabels', labels{k})); end
s = uislider(uf); s.Step = 5; fprintf('CHK|slider_step_set|%s %s|exact\n', u5_text(s.Step), s.StepMode);
u2_chk('slider_step_value_off_it', @() u5_setget(s, 'Value', 13));
s.StepMode = 'auto'; fprintf('CHK|slider_step_auto|%s %s|exact\n', u5_text(s.Step), s.StepMode);
steps = {0, -1, 200, [1 2], 'a', NaN, Inf, 0.001, {1}};
for k = 1:numel(steps), u2_chk(sprintf('slider_step_%d', k), @() u5_setget(uislider(uf), 'Step', steps{k})); end
s = uislider(uf); s.Orientation = 'vertical';
fprintf('CHK|slider_upright|%s %s|exact\n', mat2str(s.Position), mat2str(s.OuterPosition));
u2_chk('slider_made_upright', @() get(uislider(uf, 'Orientation', 'vertical'), 'Position'));
u2_chk('slider_position', @() u5_setget(uislider(uf), 'Position', [10 10 300 3]));
u2_chk('slider_outer', @() get(uislider(uf, 'Position', [10 10 300 3]), 'OuterPosition'));
u2_err('slider_outer_set', @() set(uislider(uf), 'OuterPosition', [10 10 300 60]));
r = uislider(uf, 'range');
values = {[10 20], [20 10], [10 10], 50, [0 100], [-1 50], [50 101], [1 2 3], 'a', [NaN 5], [10; 20], int8([10 20]), []};
for k = 1:numel(values), u2_chk(sprintf('range_value_%02d', k), @() u5_setget(r, 'Value', values{k})); end
r = uislider(uf, 'range'); r.Value = [60 80]; r.Limits = [0 50]; fprintf('CHK|range_limits_under|%s|exact\n', u5_text(r.Value));
r = uislider(uf, 'range'); r.Value = [10 80]; r.Limits = [20 50]; fprintf('CHK|range_limits_inside|%s|exact\n', u5_text(r.Value));
delete(allchild(uf));

% --- text ---------------------------------------------------------------------------------------
texts = {'one', "str", {'a', 'b'}, {'a'; 'b'}, ["p" "q"], ["p"; "q"], sprintf('x\ny'), ['ab'; 'cd'], '', {}, {''}, 5, {1}, [], ...
    {'a', 'b'; 'c', 'd'}, {sprintf('m\nn'), 'o'}};
for k = 1:numel(texts), u2_chk(sprintf('textarea_%02d', k), @() u5_setget(uitextarea(uf), 'Value', texts{k})); end
for k = 1:numel(texts), u2_chk(sprintf('label_text_%02d', k), @() u5_setget(uilabel(uf), 'Text', texts{k})); end
for k = 1:numel(texts), u2_chk(sprintf('checkbox_text_%02d', k), @() u5_setget(uicheckbox(uf), 'Text', texts{k})); end
pictures = {'', 5, rand(4, 4, 3), uint8(255 * rand(4, 4, 3)), rand(4, 4), 'info', 'success', 'error', 'warning', 'question', 'none', "", ...
    rand(4, 4, 4), rand(3, 3, 3) > 2, 'nosuch', 'nosuch.txt'};
for k = 1:numel(pictures)
    u2_chk(sprintf('icon_%02d', k), @() size(u5_setgetraw(uibutton(uf), 'Icon', pictures{k})));
    u2_chk(sprintf('imagesource_%02d', k), @() size(u5_setgetraw(uiimage(uf), 'ImageSource', pictures{k})));
end
delete(allchild(uf));

% --- the button group of a uifigure -------------------------------------------------------------
bg = uibuttongroup(uf);
fprintf('CHK|group_empty|%d|exact\n', isempty(bg.SelectedObject));
r1 = uiradiobutton(bg, 'Text', 'r1'); fprintf('CHK|group_first|%s %s|exact\n', u5_group(bg), mat2str(r1.Position));
r2 = uiradiobutton(bg, 'Text', 'r2'); fprintf('CHK|group_second|%s %s|exact\n', u5_group(bg), mat2str(r2.Position));
r3 = uiradiobutton(bg, 'Text', 'r3', 'Value', true); fprintf('CHK|group_third_made_on|%s|exact\n', u5_group(bg));
r2.Value = true; fprintf('CHK|group_value_on|%s|exact\n', u5_group(bg));
u2_err('group_selected_off', @() set(r2, 'Value', false)); fprintf('CHK|group_after_selected_off|%s|exact\n', u5_group(bg));
u2_err('group_unselected_off', @() set(r1, 'Value', false)); fprintf('CHK|group_after_unselected_off|%s|exact\n', u5_group(bg));
u2_err('group_value_one', @() set(r1, 'Value', 1)); fprintf('CHK|group_after_one|%s|exact\n', u5_group(bg));
u2_err('group_value_two', @() set(r1, 'Value', 2));
u2_err('group_value_word', @() set(r1, 'Value', 'on'));
bg.SelectedObject = r3; fprintf('CHK|group_selectedobject|%s|exact\n', u5_group(bg));
u2_err('group_select_none', @() set(bg, 'SelectedObject', [])); fprintf('CHK|group_after_none|%s|exact\n', u5_group(bg));
u2_err('group_select_figure', @() set(bg, 'SelectedObject', uf));
u2_err('group_select_outsider', @() set(bg, 'SelectedObject', uiradiobutton(uibuttongroup(uf))));
delete(r3); fprintf('CHK|group_selected_deleted|%s|exact\n', u5_group(bg));
r1.Value = true; delete(r1); fprintf('CHK|group_first_deleted|%s|exact\n', u5_group(bg));
delete(r2); fprintf('CHK|group_all_deleted|%d|exact\n', isempty(bg.SelectedObject));
r4 = uiradiobutton(bg, 'Text', 'r4', 'Value', false); fprintf('CHK|group_first_made_off|%s|exact\n', u5_group(bg));
u2_chk('group_toggle_among_radios', @() u5_kind(uitogglebutton(bg)));
u2_chk('group_uicontrol_among_radios', @() get(uicontrol(bg, 'Style', 'radiobutton'), 'Type'));
u2_chk('group_button_among_radios', @() u5_kind(uibutton(bg)));
fprintf('CHK|group_after_strangers|%s|exact\n', u5_group(bg));
delete(bg);
bg = uibuttongroup(uf);
t1 = uitogglebutton(bg, 'Text', 't1'); fprintf('CHK|group_first_toggle|%s %s|exact\n', u5_group(bg), mat2str(t1.Position));
t2 = uitogglebutton(bg, 'Text', 't2'); fprintf('CHK|group_second_toggle|%s|exact\n', u5_group(bg));
t2.Value = true; fprintf('CHK|group_toggle_on|%s|exact\n', u5_group(bg));
u2_err('group_toggle_off', @() set(t2, 'Value', false)); fprintf('CHK|group_after_toggle_off|%s|exact\n', u5_group(bg));
u2_chk('group_radio_among_toggles', @() u5_kind(uiradiobutton(bg)));
bg2 = uibuttongroup(uf); q = uitogglebutton(bg2, 'Text', 'q');
u2_chk('group_move_selected_in', @() u5_reparent(q, bg));
fprintf('CHK|group_after_move_in|%s|exact\n', u5_group(bg));
u2_chk('group_radio_to_figure', @() u5_reparent(uiradiobutton(bg2), uf));
u2_chk('group_radio_to_panel', @() u5_reparent(uiradiobutton(bg2), uipanel(uf)));
u2_chk('group_callback', @() class(u5_setgetraw(bg, 'SelectionChangedFcn', @(s, e) disp(1))));
u2_chk('group_radio_has_no_callback', @() get(uiradiobutton(bg2), 'ValueChangedFcn'));
delete(uf);
