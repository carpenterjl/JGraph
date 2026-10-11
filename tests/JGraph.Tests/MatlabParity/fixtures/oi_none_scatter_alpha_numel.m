% record: -noFigureWindows
% Open items 100 to 104 (ADR 0226): the colour 'none' and its prefixes; a marker chart's CData, its
% two marker colours and their words; transparencies kept as written; a heatmap's CellLabelFormat as
% R2025b keeps and refuses it; the legacy numel(A, idx...), a ruler's SecondaryLabel, and a polar r
% ruler's labels with their powers. Probes probe_100 to probe_104 (open-items scratch). The colour
% order is a separate open item, so an automatic colour is read as the word 'auto'; R2025b fills in a
% SecondaryLabel's String when it draws, so each change is drawn first. The heatmap's drawn cell labels are checked in OpenItemsBatch13Tests against R2025b's.
close all force

% --- item 100: 'none'
f = figure('Visible', 'off');
ax = axes(f);
hold(ax, 'on');
u9b_chk('none_line', @() get(line(ax, 1:2, 1:2, 'Color', 'none'), 'Color'));
u9b_chk('none_line_get_all', @() getfield(get(line(ax, 1:2, 1:2, 'Color', 'none')), 'Color'));
u9b_chk('none_prefixes', @() cellfun(@(w) set_get(line(ax, 1:2, 1:2), 'Color', w), {'none', 'None', 'no', 'n'}, 'UniformOutput', false));
u9b_chk('none_string', @() set_get(line(ax, 1:2, 1:2), 'Color', "none"));
u9b_chk('none_then_red', @() set_get(line(ax, 1:2, 1:2, 'Color', 'none'), 'Color', 'r'));
u9b_chk('none_isequal', @() isequal(get(line(ax, 1:2, 1:2, 'Color', 'none'), 'Color'), 'none'));
u9b_chk('none_plot', @() get(plot(ax, 1:2, 'Color', 'none'), 'Color'));
u9b_chk('none_plot3', @() get(plot3(ax, 1:2, 1:2, 1:2, 'Color', 'none'), 'Color'));
u9b_chk('none_stem', @() get(stem(ax, 1:2, 'Color', 'none'), 'Color'));
u9b_chk('none_stairs', @() get(stairs(ax, 1:2, 'Color', 'none'), 'Color'));
u9b_chk('none_errorbar', @() get(errorbar(ax, 1:2, 1:2, [1 1], 'Color', 'none'), 'Color'));
u9b_chk('none_xline', @() get(xline(ax, 1, 'Color', 'none'), 'Color'));
u9b_chk('none_animatedline', @() get(animatedline(ax, 'Color', 'none'), 'Color'));
u9b_chk('none_marker_edge', @() get(plot(ax, 1:2, 'o', 'MarkerEdgeColor', 'none'), 'MarkerEdgeColor'));
u9b_chk('marker_edge_auto', @() {get(plot(ax, 1:2, 'o'), 'MarkerEdgeColor'), get(plot(ax, 1:2, 'o'), 'MarkerFaceColor')});
u9b_chk('none_text', @() get(text(ax, 0, 0, 'x', 'Color', 'none'), 'Color'));
u9b_chk('none_scatter_edge', @() get(scatter(ax, 1:2, 1:2, 'MarkerEdgeColor', 'none'), 'MarkerEdgeColor'));
u9b_chk('rgba_line', @() get(plot(ax, 1:2, 'Color', [1 0 0 0.3]), 'Color'));
u9b_chk('rgba_rectangle', @() get(rectangle(ax, 'Position', [0 0 1 1], 'FaceColor', [0 0 1 0.5]), 'FaceColor'));
[~, hc] = contour(ax, peaks(5), 'LineColor', 'none');
u9b_chk('none_contour', @() hc.LineColor);
ax.Color = 'none';
u9b_chk('none_axes', @() ax.Color);
ax.XColor = 'none';
u9b_chk('none_xcolor', @() ax.XColor);
close all force

% --- item 101: a marker chart's colours
f = figure('Visible', 'off');
ax = axes(f);
x = 1:3;
y = [2 1 3];
forms = {
    'g', @() scatter(ax, x, y, 'g')
    'og', @() scatter(ax, x, y, 'og')
    'star', @() scatter(ax, x, y, '*')
    'plain', @() scatter(ax, x, y)
    'filled', @() scatter(ax, x, y, 'filled')
    'r_filled', @() scatter(ax, x, y, 'r', 'filled')
    'empty_size', @() scatter(ax, x, y, [], 'g')
    'size_r', @() scatter(ax, x, y, 20, 'r')
    'size_red', @() scatter(ax, x, y, 20, 'red')
    'size_string', @() scatter(ax, x, y, 20, "green")
    'size_rgb', @() scatter(ax, x, y, 20, [1 0 0])
    'size_values', @() scatter(ax, x, y, 20, [1 2 3])
    'size_values_filled', @() scatter(ax, x, y, 20, [1 2 3], 'filled')
    'edge_g', @() scatter(ax, x, y, 'MarkerEdgeColor', 'g')
    'face_g', @() scatter(ax, x, y, 'MarkerFaceColor', 'g')
    'face_flat', @() scatter(ax, x, y, 'MarkerFaceColor', 'flat')
    'spec_and_face', @() scatter(ax, x, y, 'gs', 'MarkerFaceColor', 'r')
    'cdata_option', @() scatter(ax, x, y, 'CData', [0 0 1])
    'swarm_g', @() swarmchart(ax, x, y, 'g')
    'scatter3_g', @() scatter3(ax, x, y, x, 'g')
    'scatter3_size_m', @() scatter3(ax, x, y, x, 10, 'm')
    'scatter3_plain', @() scatter3(ax, x, y, x)
    'scatter3_filled_k', @() scatter3(ax, x, y, x, 'k', 'filled')
    };
for k = 1:size(forms, 1)
    cla(ax, 'reset');
    u9b_chk(['scatter_' forms{k, 1}], @() marker_words(forms{k, 2}()));
end
cla(ax, 'reset');
s = scatter(ax, x, y, 'g');
s.CData = [1 0 0];
u9b_chk('scatter_set_rgb', @() marker_words(s));
s.MarkerEdgeColor = 'k';
u9b_chk('scatter_set_edge', @() marker_words(s));
s.MarkerEdgeColor = 'flat';
s.MarkerFaceColor = 'flat';
u9b_chk('scatter_set_flat', @() marker_words(s));
s.MarkerFaceColor = 'none';
s.CDataMode = 'auto';
u9b_chk('scatter_set_auto', @() marker_words(s));
s.CData = [3 2 1];
u9b_chk('scatter_set_values', @() marker_words(s));
u9b_chk('scatter_set_word_refused', @() id_of(@() set(s, 'CData', 'r')));
u9b_chk('scatter_option_word_refused', @() id_of(@() scatter(ax, x, y, 'CData', 'r')));
close all force

% --- item 102: transparencies as written
f = figure('Visible', 'off');
ax = axes(f);
b = bubblechart(ax, 1:3, 1:3, [1 2 3]);
u9b_chk('bubble_defaults', @() {b.MarkerFaceAlpha, b.MarkerEdgeAlpha, b.MarkerFaceColor, b.MarkerEdgeColor});
b.MarkerFaceAlpha = 0.25;
b.MarkerEdgeAlpha = 0.3;
u9b_chk('bubble_written', @() {b.MarkerFaceAlpha, b.MarkerEdgeAlpha});
b.MarkerFaceAlpha = 1/3;
u9b_chk('bubble_third', @() b.MarkerFaceAlpha);
b.MarkerFaceAlpha = 'flat';
u9b_chk('bubble_flat', @() b.MarkerFaceAlpha);
u9b_chk('bubble_option', @() get(bubblechart(ax, 1:3, 1:3, [1 2 3], 'MarkerFaceAlpha', 0.25), 'MarkerFaceAlpha'));
cla(ax);
u9b_chk('scatter_alphas', @() get(scatter(ax, 1:3, 1:3, 'MarkerFaceAlpha', 0.3, 'MarkerEdgeAlpha', 0.7), {'MarkerFaceAlpha', 'MarkerEdgeAlpha'}));
u9b_chk('scatter_alpha_default', @() get(scatter(ax, 1:3, 1:3), {'MarkerFaceAlpha', 'MarkerEdgeAlpha'}));
u9b_chk('swarm_alpha', @() get(swarmchart(ax, 1:3, 1:3, 'MarkerFaceAlpha', 0.3), 'MarkerFaceAlpha'));
u9b_chk('patch_alphas', @() get(patch(ax, [0 1 1], [0 0 1], 'r', 'FaceAlpha', 0.3, 'EdgeAlpha', 0.45), {'FaceAlpha', 'EdgeAlpha'}));
u9b_chk('fill_alpha', @() get(fill(ax, [0 1 1], [0 0 1], 'r', 'FaceAlpha', 0.3), 'FaceAlpha'));
cla(ax);
u9b_chk('area_alphas', @() get(area(ax, 1:3, 'FaceAlpha', 0.35, 'EdgeAlpha', 0.15), {'FaceAlpha', 'EdgeAlpha'}));
cla(ax);
u9b_chk('bar_alphas', @() get(bar(ax, 1:3, 'FaceAlpha', 0.33, 'EdgeAlpha', 0.66), {'FaceAlpha', 'EdgeAlpha'}));
cla(ax);
sf = surf(ax, peaks(4), 'FaceAlpha', 0.3, 'EdgeAlpha', 0.4);
u9b_chk('surf_alphas', @() {sf.FaceAlpha, sf.EdgeAlpha});
alpha(sf, 0.55);
u9b_chk('alpha_of_object', @() sf.FaceAlpha);
close all force

% --- item 103: a heatmap's CellLabelFormat
f = figure('Visible', 'off');
h = heatmap(f, [1 2.5 -3; 1234567.89 0.000123 NaN; 0 0.5 42]);
u9b_chk('cell_format_default', @() h.CellLabelFormat);
taken = {'%.2f', '%g%%', '%+g', '%5.1f', '%d', '%e', '%.0e', '%g V', '%.3g', '%i', '%G', '%E', '%f%%', '%-8.2f|', '%08.2f', '% g', '%#g', '\x00B0%g'};
for k = 1:numel(taken)
    h.CellLabelFormat = taken{k};
    u9b_chk(sprintf('cell_format_%d', k), @() h.CellLabelFormat);
end
refused = {'abc', '%x', '%o', '%u', '%c', '%s', 'percentage', 'usd', 'auto', '', '%', '%q', '%g%g', '%*d'};
for k = 1:numel(refused)
    u9b_chk(sprintf('cell_format_refused_%d', k), @() as_statement(@() set(h, 'CellLabelFormat', refused{k})));
end
set(h, 'CellLabelFormat', "%.3f");
u9b_chk('cell_format_string', @() {class(h.CellLabelFormat), h.CellLabelFormat});
u9b_chk('cell_format_option', @() get(heatmap(f, [1 2], 'CellLabelFormat', '%.2f'), 'CellLabelFormat'));
ax = axes(figure('Visible', 'off'));
u9b_chk('ruler_format_q', @() as_statement(@() set(ax.XAxis, 'TickLabelFormat', '%q')));
u9b_chk('ruler_format_star', @() as_statement(@() set(ax.XAxis, 'TickLabelFormat', '%*d')));
close all force

% --- item 104: numel(A, idx...), a ruler's SecondaryLabel, a polar r ruler's labels
A = ones(3, 4);
subscripts = {
    {1, 2}, {A, ':', 1}, {A, 1:2, ':'}, {A, 1, 1}, {A, [1 2 3]}, {A, ':', ':'}, {A, ':'}, {A, true(1, 4), 2}, ...
    {A, [], 1}, {A, 'a'}, {A, 'ab'}, {A, 1, 2, 3}, {A, 99}, {A, 0}, {A, -1}, {A, 1.5}, {A, int8(2), 1:3}, ...
    {'abc', 1:2}, {{1, 2, 3}, ':'}, {struct('f', {1, 2, 3}), 2:3}, {A, zeros(2, 3)}, {[], 1}, {A, NaN}, ...
    {A, 1, ':', 1}, {A, logical([1 0 1])}};
for k = 1:numel(subscripts)
    u9b_chk(sprintf('numel_%d', k), @() numel(subscripts{k}{:}));
end
u9b_chk('numel_cell_subscript', @() numel(A, {1, 2}));
u9b_chk('numel_string_subscript', @() numel(A, "a"));
u = struct('f', {1, 2, 3});
u9b_chk('numel_list', @() numel(u.f));
c = {5, 1};
u9b_chk('numel_brace_list', @() numel(c{:}));

f = figure('Visible', 'off');
ax = axes(f);
plot(ax, [0 50000], [0 2e-5]);
ax.XLim = [0 50000];
ax.XTick = 0:10000:50000;
ax.YLim = [0 2e-5];
ax.YTick = (0:4) * 5e-6;
drawnow;
sx = ax.XAxis.SecondaryLabel;
u9b_chk('secondary_strings', @() {sx.String, ax.YAxis.SecondaryLabel.String, sx.Visible, sx.StringMode, sx.Interpreter});
u9b_chk('secondary_named', @() get(ax.YAxis, 'SecondaryLabel').String);
u9b_chk('secondary_hidden_name', @() {isprop(ax.XAxis, 'SecondaryLabel'), any(strcmp(properties(ax.XAxis), 'SecondaryLabel')), isfield(get(ax.XAxis), 'SecondaryLabel')});
sx.Visible = 'off';
drawnow;
u9b_chk('secondary_hide', @() {sx.Visible, sx.String});
sx.Visible = 'on';
sx.String = 'custom';
drawnow;
u9b_chk('secondary_custom', @() {sx.String, sx.StringMode});
ax.XAxis.Exponent = 3;
drawnow;
u9b_chk('secondary_custom_kept', @() {sx.String, ax.XTickLabel'});
sx.StringMode = 'auto';
drawnow;
u9b_chk('secondary_auto_again', @() sx.String);
ax.XAxis.Exponent = 0;
drawnow;
u9b_chk('secondary_zero', @() sx.String);
u9b_chk('secondary_same_handle', @() ax.XAxis.SecondaryLabel == sx);
yyaxis(ax, 'right');
plot(ax, [0 1], [0 3e6]);
ax.YLim = [0 3e6];
ax.YTick = (0:3) * 1e6;
drawnow;
u9b_chk('secondary_yyaxis', @() ax.YAxis(end).SecondaryLabel.String);
ax2 = axes(figure('Visible', 'off'));
semilogx(ax2, [1 1e6], [1 2]);
drawnow;
u9b_chk('secondary_log', @() ax2.XAxis.SecondaryLabel.String);
ax3 = axes(figure('Visible', 'off'));
plot3(ax3, [0 1], [0 1], [0 7e5]);
ax3.ZLim = [0 7e5];
ax3.ZTick = (0:7) * 1e5;
drawnow;
u9b_chk('secondary_z', @() ax3.ZAxis.SecondaryLabel.String);
pax = polaraxes(figure('Visible', 'off'));
polarplot(pax, [0 1], [0 5e4]);
pax.RLim = [0 5e4];
pax.RTick = 0:10000:50000;
drawnow;
u9b_chk('polar_r', @() {pax.RAxis.Exponent, pax.RAxis.SecondaryLabel.String, pax.RTickLabel'});
close all force

function r = set_get(h, name, value)
set(h, name, value);
r = get(h, name);
end

function r = marker_words(s)
% A marker chart's colour channel and the words its two marker colours answer.
r = {s.CData, s.CDataMode, s.MarkerEdgeColor, s.MarkerFaceColor};
if strcmp(s.CDataMode, 'auto')
    r{1} = 'series colour';
end
end

function r = id_of(fn)
% 'ran', or the refusal's identifier.
try
    fn();
    r = 'ran';
catch err
    r = err.identifier;
end
end

function r = as_statement(fn)
% 'ran', or the refusal's identifier and sentence.
try
    fn();
    r = 'ran';
catch err
    r = [err.identifier ' | ' err.message];
end
end
