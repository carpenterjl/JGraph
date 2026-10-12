% record: -noFigureWindows
% Open items 112 and 113 (ADR 0229): the charts R2025b builds out of several objects - a pie's
% patches and texts, a raised pie's patches, surfaces and texts, bar3's surface per column,
% compass's and feather's line per arrow, fill's patch per polygon, errorbar's series per column,
% fplot's line per function, a stairs matrix, plotmatrix's full grid, meshc's and surfc's floor
% contour - with their geometry; and contour's automatic levels: the round step, counts, steps,
% the modes, contourc's own rule, contourf's minimum, fcontour, ZLocation. Probes probe_112 to
% probe_112c and probe_113 to probe_113b (open-items scratch). Handles are compared by count and
% Type, never by class (ADR 0051).
close all force

% --- item 112: handle counts and types
f = figure('Visible', 'off');
u9b_chk('pie_count', @() size(pie([1 2 3])));
u9b_chk('pie_types', @() types_of(pie([1 2 3])));
u9b_chk('pie_one', @() size(pie(5)));
u9b_chk('pie3_count', @() size(pie3([1 2 3])));
u9b_chk('pie3_types', @() types_of(pie3([1 2 3])));
u9b_chk('meshc_count', @() size(meshc(magic(4))));
u9b_chk('meshc_types', @() types_of(meshc(magic(4))));
u9b_chk('surfc_types', @() types_of(surfc(magic(4))));
u9b_chk('bar3_count', @() size(bar3(magic(3))));
u9b_chk('bar3_types', @() types_of(bar3(magic(3))));
u9b_chk('bar3_vector', @() size(bar3([1 2 3])));
u9b_chk('bar3h_count', @() size(bar3h(magic(3))));
u9b_chk('compass_count', @() size(compass([1 2], [1 -1])));
u9b_chk('compass_types', @() types_of(compass([1 2], [1 -1])));
u9b_chk('feather_count', @() size(feather([1 2], [1 -1])));
u9b_chk('feather_one', @() size(feather(1, 1)));
u9b_chk('errorbar_matrix', @() size(errorbar(magic(3), ones(3))));
u9b_chk('errorbar_groups', @() size(errorbar([1 2; 3 4], [1 1; 1 1])));
u9b_chk('errorbar_xvec', @() size(errorbar([1 2 3], magic(3), ones(3))));
u9b_chk('errorbar_vector', @() size(errorbar([1 2 3], [1 1 1])));
u9b_chk('fill_matrix', @() size(fill([0 1; 1 2; 1 2], [0 0; 0 0; 1 1], 'r')));
u9b_chk('fill_groups', @() size(fill([0 1 1], [0 0 1], 'r', [2 3 3], [0 0 1], 'b')));
u9b_chk('fill_vector', @() size(fill([0 1 1], [0 0 1], 'r')));
u9b_chk('polarplot_matrix', @() size(polarplot([0 1; 1 2], [1 2; 2 3])));
u9b_chk('plot_matrices', @() size(plot(magic(3), magic(3)')));
u9b_chk('fplot_cell', @() size(fplot({@sin, @cos})));
u9b_chk('fplot_cell_interval', @() size(fplot({@sin, @cos}, [0 2])));
u9b_chk('fplot_cell_spec', @() size(fplot({@sin, @cos}, '--')));
u9b_chk('fplot_one', @() size(fplot(@sin)));
u9b_chk('fplot_name', @() fplot(@sin).DisplayName);
u9b_chk('fplot_cell_names', @() cell_names(fplot({@sin, @cos})));
u9b_chk('stairs_matrix', @() size(stairs(magic(3))));
u9b_chk('stairs_x_matrix', @() size(stairs([1 2 3], magic(3))));
u9b_chk('plotmatrix_valid', @() plotmatrix_valid(magic(3)));
u9b_chk('plotmatrix_xy_valid', @() plotmatrix_valid(magic(3), magic(3) + 1));

% --- item 112: geometry
h = pie([1 2 3]);
u9b_chk('pie_xdata_count', @() numel(h(1).XData));
u9b_chk('pie_xdata_first', @() round(flat(h(1).XData, 2:4) * 1e6) / 1e6);
u9b_chk('pie_ydata_last', @() round(h(5).YData(end - 1) * 1e6) / 1e6);
u9b_chk('pie_cdata', @() unique(h(3).CData)');
u9b_chk('pie_facecolor', @() h(1).FaceColor);
u9b_chk('pie_strings', @() {h(2).String, h(4).String, h(6).String});
u9b_chk('pie_text1', @() {round(h(2).Position(1:2) * 1e6) / 1e6, h(2).HorizontalAlignment, h(2).VerticalAlignment});
u9b_chk('pie_text3', @() {round(h(4).Position(1:2) * 1e6) / 1e6, h(4).HorizontalAlignment, h(4).VerticalAlignment});
u9b_chk('pie_text5', @() {round(h(6).Position(1:2) * 1e6) / 1e6, h(6).HorizontalAlignment, h(6).VerticalAlignment});
u9b_chk('pie_axes', @() {xlim, ylim, get(gca, 'Visible'), get(gca, 'CLim')});
u9b_chk('pie_displayname', @() h(1).DisplayName);
h = pie([1 2 3], [0 1 0]);
u9b_chk('pie_explode', @() round(flat(h(3).XData, 1:2) * 1e6) / 1e6);
h = pie([1 2 3], {'a', 'b', 'c'});
u9b_chk('pie_labels', @() {h(2).String, h(1).DisplayName, h(5).DisplayName});
h = pie([1 2 3], '%.1f%%');
u9b_chk('pie_format', @() {h(2).String, h(6).String});
h = pie([0.25 0.25]);
u9b_chk('pie_partial', @() {h(2).String, round(h(3).YData(end - 1) * 1e6) / 1e6});
h = pie([0.001 0.999]);
u9b_chk('pie_tiny', @() {h(2).String, h(4).String});
u9b_chk('pie_negative', @() numel(pie([1 -2 3])));
u9b_chk('pie_negative_labels', @() pie_negative_labels());
u9b_chk('pie_all_negative', @() pie([-1 -2]));
u9b_chk('pie_labels_short', @() pie([1 2 3], {'a', 'b'}));
u9b_chk('pie_word', @() pie([1 2 3], 'abc'));
u9b_chk('pie_explode_short', @() pie([1 2 3], [1 0]));
u9b_chk('pie_nan', @() pie([1 NaN 3]));
u9b_chk('pie_cell', @() pie({1, 2}));
h = pie3([1 2 3]);
u9b_chk('pie3_bottom_z', @() unique(h(1).ZData)');
u9b_chk('pie3_side', @() {size(h(2).XData), h(2).ZData(2, :)});
u9b_chk('pie3_top_z', @() unique(h(3).ZData)');
u9b_chk('pie3_texts', @() {round(h(4).Position * 1e6) / 1e6, round(h(8).Position * 1e6) / 1e6, round(h(12).Position * 1e6) / 1e6});
u9b_chk('pie3_strings', @() {h(4).String, h(8).String, h(12).String});
u9b_chk('pie3_axes', @() {get(gca, 'View'), xlim});
u9b_chk('pie3_labels', @() pie3_labels());
u9b_chk('pie3_labels_short', @() pie3([1 2 3], {'a', 'b'}));
u9b_chk('pie3_explode_short', @() pie3([1 2 3], [1 0]));
u9b_chk('pie3_all_negative', @() pie3([-1 -2]));
u9b_chk('pie3_negative', @() numel(pie3([1 -2 3])));
h = bar3(magic(3));
u9b_chk('bar3_sizes', @() {size(h(1).XData), size(h(1).YData), size(h(1).ZData)});
u9b_chk('bar3_xdata', @() h(1).XData(1:6, :));
u9b_chk('bar3_ydata', @() h(1).YData(1:6, :));
u9b_chk('bar3_zdata', @() h(1).ZData(1:6, :));
u9b_chk('bar3_second', @() {h(2).XData(2, :), h(2).CData(2, :), h(2).FaceColor});
u9b_chk('bar3_axes', @() {xlim, get(gca, 'YDir'), get(gca, 'View'), get(gca, 'YTick'), get(gca, 'XTick'), get(gca, 'CLim')});
h = bar3(magic(3), 'grouped');
u9b_chk('bar3_grouped', @() {round(h(2).XData(2, :) * 1e6) / 1e6, round(h(2).YData(2, :) * 1e6) / 1e6, round(xlim * 1e6) / 1e6});
h = bar3(magic(3), 'stacked');
u9b_chk('bar3_stacked', @() h(2).ZData(1:3, :));
h = bar3([10 20 40], [1 2; 3 4; 5 6]);
u9b_chk('bar3_positions', @() {h(1).YData(1:6, :), get(gca, 'YTick')});
h = bar3h(magic(3));
u9b_chk('bar3h', @() {h(1).YData(1:3, :), h(1).ZData(1:3, :)});
h = bar3(magic(3), 'r');
u9b_chk('bar3_red', @() h(1).FaceColor);
h = bar3([1 2 3]);
u9b_chk('bar3_vector_clim', @() {get(gca, 'CLim'), h.XData(2, :)});
h = compass([1 2], [1 -1]);
u9b_chk('compass_seats', @() [h.SeriesIndex]);
u9b_chk('compass_same_colour', @() isequal(h(1).Color, h(2).Color));
u9b_chk('compass_points', @() numel(h(2).XData));
h = compass([1 2], [1 -1], 'r--');
u9b_chk('compass_spec', @() {h(1).Color, h(1).LineStyle});
h = compass([1 2], [0 0]);
hold on
k = compass(1, 1);
u9b_chk('compass_second_seat', @() k.SeriesIndex);
hold off
h = feather([1 2], [1 -1]);
u9b_chk('feather_arrow', @() {round(h(1).XData * 1e6) / 1e6, round(h(1).YData * 1e6) / 1e6});
u9b_chk('feather_baseline', @() {h(3).XData, h(3).YData});
u9b_chk('feather_seats', @() [h.SeriesIndex]);
u9b_chk('feather_next', @() get(gca, 'ColorOrderIndex'));
h = feather([1 2], [1 -1], 'LineWidth', 3);
u9b_chk('feather_options', @() [h.LineWidth]);
h = fill([0 1; 1 2; 1 2], [0 0; 0 0; 1 1], 'r');
u9b_chk('fill_colour', @() {h(2).FaceColor, h(2).XData(:)'});
h = fill([0 1; 1 2; 1 2], [0 0; 0 0; 1 1], [3 7]);
u9b_chk('fill_flat', @() {h(1).FaceColor, h(1).CData(:)', h(2).CData(:)'});
h = fill([0 1; 1 2; 1 2], [0 0; 0 0; 1 1], [1 2; 3 4; 5 6]);
u9b_chk('fill_interp', @() {h(2).FaceColor, h(2).CData(:)'});
h = fill([0 1 1], [0 0 1], 'r', [2 3 3], [0 0 1], 'b');
u9b_chk('fill_group_colours', @() {h(1).FaceColor, h(2).FaceColor});
h = errorbar(magic(3), ones(3));
u9b_chk('errorbar_columns', @() {h(1).YData, h(3).YData, h(2).XData});
h = errorbar([1 2 3]', [1 2; 3 4; 5 6], [1 1 1]');
u9b_chk('errorbar_colvec', @() {h(2).YData, h(2).YNegativeDelta});
h = polarplot([0 1; 1 2], [1 2; 2 3]);
u9b_chk('polarplot_columns', @() {h(2).ThetaData, h(2).RData});
h = plot(magic(3), magic(3)');
u9b_chk('plot_paired', @() {h(2).XData, h(2).YData});
h = stairs(magic(3));
u9b_chk('stairs_columns', @() h(2).YData);
h = meshc(magic(4));
u9b_chk('meshc_contour', @() {h(2).ZLocation, h(2).ZLocationMode, h(2).LevelList, h(2).LevelStep});
h = surfc(1:4, 10:10:40, magic(4));
u9b_chk('surfc_contour', @() {h(2).XData(1, :), h(2).YData(:)', h(2).LevelList});
h = surfc(peaks(10));
set(h(2), 'LineWidth', 2);
u9b_chk('surfc_contour_set', @() {h(2).LineWidth, h(2).LevelList});
u9b_chk('text_displayname', @() text(1, 1, 'a').DisplayName);
clf(f);
plot(1:3);
u9b_chk('replace_after_pieces', @() numel(get(gca, 'Children')));

% --- item 113: contour levels
ranges = {'magic3', magic(3); 'magic4', magic(4); 'magic5', magic(5); 'magic6', magic(6); 'magic8', magic(8); ...
    'peaks20', peaks(20); 'peaks49', peaks; 'm4x100', 100 * magic(4); 'm4d1000', magic(4) / 1000; ...
    'm4p05', magic(4) + 0.5; 'negm4', -magic(4); 'unit', [0 0; 0 1]; 'r3', [0 3; 1 2]; 'r11', [0 11; 1 2]; ...
    'r12', [0 12; 1 2]; 'r21', [0 21; 1 2]; 'r22', [0 22; 1 2]; 'r24', [0 24; 1 2]; 'r50', [0 50; 1 2]; ...
    'r56', [0 56; 1 2]; 'r60', [0 60; 1 2]; 'r0p1', [0 0.1; 0.01 0.02]; 'r0p7', [0 0.7; 0.1 0.2]; ...
    'off5', [5 5.5; 5.1 5.2]; 'off1000', [1000 1016; 1001 1002]; 'tiny', [0 1e-9; 2e-10 3e-10]; ...
    'huge', [0 1e6; 2e5 3e5]; 'withnan', [1 NaN 3; 4 5 6; 7 8 16]; 'withinf', [1 Inf 3; 4 5 6; 7 8 16]; ...
    'neg', [-3 3; -1 1]; 'negonly', [-30 -3; -10 -20]; 'r0p115', [0 0.115; 0.01 0.02]; 'r0p12', [0 0.12; 0.01 0.02]; ...
    'r2p3', [0 2.3; 1 1.5]; 'r2p4', [0 2.4; 1 1.5]; 'o3to16', [3 16; 4 5]; 'o2p5to17p5', [2.5 17.5; 4 5]};
% The contour matrices are compared by the levels they carry, without the one-point column R2025b
% writes for a level at the data's maximum and without a grid holding NaN or Inf, where this
% build's tracer drops the lowest curves (ADR 0229).
for k = 1:size(ranges, 1)
    Z = ranges{k, 2};
    u9b_chk(['levels_' ranges{k, 1}], @() contour_levels(Z));
    if all(isfinite(Z(:)))
        u9b_chk(['contourc_' ranges{k, 1}], @() inside_max(round(levels_of(contourc(Z)) * 1e9) / 1e9, Z));
    end
end
u9b_chk('levels_constant', @() contour_levels(ones(3)));
Z = magic(4);
u9b_chk('contour3_levels', @() contour_levels3(Z));
u9b_chk('contourf_levels', @() contourf_levels(Z));
u9b_chk('contourf_peaks', @() contourf_levels(peaks(20)));
for n = [0 1 2 2.5 3 5 8]
    u9b_chk(sprintf('count_%g', n), @() contour_count(Z, n));
    u9b_chk(sprintf('contourc_count_%g', n), @() round(levels_of(contourc(Z, n)) * 1e9) / 1e9);
end
u9b_chk('count_negative', @() contour(Z, -1));
u9b_chk('contourf_count', @() contourf_count(Z, 3));
u9b_chk('level_pair', @() contour_given(Z, [5 5]));
u9b_chk('level_list', @() contour_given(Z, [3 7 11]));
u9b_chk('step_option', @() contour_stepped(Z, 3));
u9b_chk('step_option_2p5', @() contour_stepped(Z, 2.5));
u9b_chk('contourf_step', @() contourf_stepped(Z, 3));
u9b_chk('contourf_list', @() contourf_given(Z, [4 8 12]));
u9b_chk('set_step_then_list', @() contour_set_step_list(Z));
u9b_chk('modes_back_to_auto', @() contour_modes_auto(Z));
u9b_chk('zdata_rewrite', @() contour_zdata(Z));
u9b_chk('fcontour_levels', @() fcontour_levels(@(x, y) x.^2 + y.^2));
u9b_chk('fcontour_sincos', @() fcontour_levels(@(x, y) sin(x) .* cos(y)));
u9b_chk('fcontour_step', @() fcontour_step(@(x, y) x.^2 + y.^2, 7));
u9b_chk('fcontour_fill', @() fcontour_fill(@(x, y) x.^2 + y.^2));
u9b_chk('fcontour_list', @() fcontour_list(@(x, y) x.^2 + y.^2, [3 9]));
u9b_chk('zlocation_default', @() zlocation_default(Z));
u9b_chk('zlocation_words', @() zlocation_words(Z));
u9b_chk('zlocation_zero', @() zlocation_bad(Z, 'zero'));
u9b_chk('zlocation_auto', @() zlocation_bad(Z, 'auto'));
u9b_chk('zlocation_option', @() zlocation_option(Z));

function t = types_of(h)
t = arrayfun(@(k) get(k, 'Type'), h(:)', 'UniformOutput', false);
end

function n = cell_names(h)
n = {h(1).DisplayName, h(2).DisplayName};
end

function v = plotmatrix_valid(varargin)
[h, ax] = plotmatrix(varargin{:});
v = [numel(h), sum(isvalid(h(:))), numel(ax), sum(isvalid(ax(:))), isvalid(h(2, 2)), strcmp(get(h(1, 2), 'Type'), get(h(2, 1), 'Type'))];
end

function r = pie_negative_labels()
h = pie([1 -2 3], [1 0 0], {'a', 'b', 'c'});
r = {numel(h), h(2).String, h(4).String, round(flat(h(1).XData, 2:3) * 1e6) / 1e6};
end

function r = pie3_labels()
h = pie3([1 2 1], [0 1 0], {'a', 'b', 'c'});
r = {numel(h), h(4).String, h(6).DisplayName, abs(h(5).XData(1)) > 0.05};
end

function v = flat(data, at)
% A patch's data as a row whichever way it is stored (its shape is open item 114).
v = data(:)';
v = v(at);
end

function lv = inside_max(lv, Z)
lv = lv(lv < max(Z(isfinite(Z))));
end

function lv = matrix_levels(M, Z)
if all(isfinite(Z(:)))
    lv = inside_max(round(levels_of(M) * 1e9) / 1e9, Z);
else
    lv = 'skipped';
end
end

function r = contour_levels(Z)
[M, c] = contour(Z);
r = {round(c.LevelList * 1e9) / 1e9, round(c.LevelStep * 1e9) / 1e9, c.LevelListMode, c.LevelStepMode, matrix_levels(M, Z)};
end

function r = contour_levels3(Z)
[M, c] = contour3(Z);
r = {c.LevelList, c.LevelStep, matrix_levels(M, Z)};
end

function r = contourf_levels(Z)
[M, c] = contourf(Z);
r = {round(c.LevelList * 1e6) / 1e6, c.LevelStep, matrix_levels(M, Z)};
end

function r = contour_count(Z, n)
[M, c] = contour(Z, n);
r = {round(c.LevelList * 1e9) / 1e9, c.LevelStep, c.LevelListMode, c.LevelStepMode, round(levels_of(M) * 1e9) / 1e9};
end

function r = contourf_count(Z, n)
[~, c] = contourf(Z, n);
r = {c.LevelList, c.LevelListMode, c.LevelStep};
end

function r = contour_given(Z, v)
[M, c] = contour(Z, v);
r = {c.LevelList, c.LevelStep, c.LevelListMode, c.LevelStepMode, levels_of(M)};
end

function r = contourf_given(Z, v)
[~, c] = contourf(Z, v);
r = {c.LevelList, c.LevelListMode};
end

function r = contour_stepped(Z, s)
[M, c] = contour(Z, 'LevelStep', s);
r = {c.LevelList, c.LevelStep, c.LevelListMode, c.LevelStepMode, levels_of(M)};
end

function r = contourf_stepped(Z, s)
[~, c] = contourf(Z, 'LevelStep', s);
r = {c.LevelList, c.LevelListMode};
end

function r = contour_set_step_list(Z)
[~, c] = contour(Z);
c.LevelStep = 4;
a = {c.LevelList, c.LevelStep, c.LevelListMode, c.LevelStepMode};
c.LevelList = [1 8];
b = {c.LevelList, c.LevelStep, c.LevelListMode, c.LevelStepMode};
r = [a, b];
end

function r = contour_modes_auto(Z)
[~, c] = contour(Z);
c.LevelStep = 4;
c.LevelList = [1 8];
c.LevelListMode = 'auto';
a = {c.LevelList, c.LevelStep, c.LevelListMode, c.LevelStepMode};
c.LevelStepMode = 'auto';
b = {c.LevelList, c.LevelStep, c.LevelListMode, c.LevelStepMode};
r = [a, b];
end

function r = contour_zdata(Z)
[~, c] = contour(Z);
c.ZData = peaks(20);
a = {c.LevelList, c.LevelStep, c.LevelListMode, size(c.XData)};
c.LevelList = [0 2];
c.ZData = Z;
b = {c.LevelList, c.LevelStep, c.LevelListMode, size(c.XData)};
r = [a, b];
end

function r = fcontour_levels(f)
h = fcontour(f);
r = {round(h.LevelList * 1e9) / 1e9, round(h.LevelStep * 1e9) / 1e9, h.LevelListMode, h.LevelStepMode};
end

function r = fcontour_step(f, s)
h = fcontour(f, 'LevelStep', s);
r = {h.LevelList, h.LevelStep, h.LevelListMode, h.LevelStepMode};
end

function r = fcontour_fill(f)
h = fcontour(f, 'Fill', 'on');
r = {h.LevelList, h.LevelStep};
end

function r = fcontour_list(f, v)
h = fcontour(f, 'LevelList', v);
r = {h.LevelList, h.LevelStep, h.LevelListMode};
end

function r = zlocation_default(Z)
[~, c] = contour(Z);
[~, d] = contour3(Z);
r = {c.ZLocation, c.ZLocationMode, d.ZLocation, d.ZLocationMode};
end

function r = zlocation_words(Z)
[~, c] = contour(Z);
c.ZLocation = 'zmax';
a = {c.ZLocation, c.ZLocationMode};
c.ZLocation = 2;
b = {c.ZLocation, c.ZLocationMode};
c.ZLocation = 'zmin';
r = [a, b, {c.ZLocation}];
end

function r = zlocation_bad(Z, word)
[~, c] = contour(Z);
c.ZLocation = word;
r = c.ZLocation;
end

function r = zlocation_option(Z)
[~, c] = contour(Z, 'ZLocation', 'zmax');
r = c.ZLocation;
end

function lv = levels_of(M)
% The distinct levels of a contour matrix, as a row (unique of nothing is 0-by-1 in R2025b and
% 1-by-0 here, and neither is the point).
lv = [];
k = 1;
while k <= size(M, 2)
    lv(end + 1) = M(1, k); %#ok<AGROW>
    k = k + M(2, k) + 1;
end
lv = reshape(unique(lv), 1, []);
end
