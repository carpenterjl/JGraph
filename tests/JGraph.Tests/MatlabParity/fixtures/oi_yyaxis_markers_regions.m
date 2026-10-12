% record: -noFigureWindows
% Open items 106 to 108 (ADR 0227): an axes yyaxis gave two sides - both rulers in YAxis, each side's
% colour, line styles, children and cla; a marker chart's colour per point, its CData as written,
% scatter3's and bubblechart3's marker words and alphas, a patch's alpha words and R2025b's
% refusals; and xregion and yregion. Probes probe_106 to probe_108b (open-items scratch). The colour
% order is a separate open item and the automatic limits a recorded divergence (ADR 0073), so a
% colour is compared with the order's entry and only limits a script wrote are read.
close all force

% --- item 106: yyaxis
ax = axes(figure('Visible', 'off'));
u9b_chk('yaxis_plain', @() size(ax.YAxis));
co = ax.ColorOrder; % taken before yyaxis, which leaves only the active side's colour in it
yyaxis(ax, 'left');
u9b_chk('yaxis_left', @() size(ax.YAxis));
u9b_chk('ruler_colours', @() {same_colour(ax.YAxis(1).Color, co(1, :)), same_colour(ax.YAxis(2).Color, co(2, :))});
hold(ax, 'on');
a = plot(ax, 1:3);
b = plot(ax, 2:4);
yyaxis(ax, 'right');
c = plot(ax, 1:3);
d = plot(ax, 2:4);
e = plot(ax, 3:5, 'r');
f = plot(ax, 4:6);
u9b_chk('side_colours', @() {same_colour(a.Color, co(1, :)), same_colour(b.Color, co(1, :)), same_colour(c.Color, co(2, :)), same_colour(d.Color, co(2, :))});
u9b_chk('side_styles', @() {a.LineStyle, b.LineStyle, c.LineStyle, d.LineStyle, e.LineStyle, f.LineStyle});
u9b_chk('side_series_index', @() [a.SeriesIndex b.SeriesIndex c.SeriesIndex d.SeriesIndex]);
u9b_chk('style_index_right', @() ax.LineStyleOrderIndex);
u9b_chk('colour_index', @() ax.ColorOrderIndex);
u9b_chk('children_right', @() numel(ax.Children));
u9b_chk('location_right', @() ax.YAxisLocation);
yyaxis(ax, 'left');
u9b_chk('children_left', @() numel(ax.Children));
u9b_chk('style_index_left', @() ax.LineStyleOrderIndex);
u9b_chk('location_left', @() ax.YAxisLocation);
u9b_chk('findobj_both', @() numel(findobj(ax, 'Type', 'line')));
u9b_chk('parent_is_axes', @() d.Parent == ax);
ax.YAxis(2).Limits = [0 100];
ax.YAxis(1).Limits = [-5 5];
u9b_chk('limits_left', @() ylim(ax));
yyaxis(ax, 'right');
u9b_chk('limits_right', @() ylim(ax));
u9b_chk('limits_both', @() get(ax.YAxis, 'Limits'));
u9b_chk('limits_mode', @() {ax.YAxis(1).LimitsMode, ax.YLimMode});
ax.YAxis(2).Color = [1 0 0];
u9b_chk('ycolor_right', @() ax.YColor);
yyaxis(ax, 'left');
u9b_chk('ycolor_left_kept', @() same_colour(ax.YColor, co(1, :)));
ax.YColor = [0 1 0];
u9b_chk('ycolor_left_set', @() ax.YAxis(1).Color);
u9b_chk('colour_list', @() size({ax.YAxis.Color}));
u9b_chk('end_ruler', @() ax.YAxis(end).Color);
u9b_chk('third_ruler', @() id_of(@() ax.YAxis(3)));
ylabel(ax, 'Left');
yyaxis(ax, 'right');
ylabel(ax, 'Right');
u9b_chk('labels', @() {ax.YAxis(1).Label.String, ax.YAxis(2).Label.String, ax.YLabel.String});
ax.YAxis(2).Limits = [1 100];
ax.YAxis(2).Scale = 'log';
ax.YAxis(2).Direction = 'reverse';
u9b_chk('scale_direction', @() {ax.YAxis(1).Scale, ax.YScale, ax.YAxis(1).Direction, ax.YDir});
ax.YAxis(2).TickValues = [1 10 100];
u9b_chk('ticks_right', @() ax.YTick);
ax.YAxis(2).Visible = 'off';
u9b_chk('visible', @() {ax.YAxis(1).Visible, ax.YAxis(2).Visible});
cla(ax);
u9b_chk('cla_right', @() {numel(ax.Children), size(ax.YAxis)});
yyaxis(ax, 'left');
u9b_chk('cla_left_kept', @() numel(ax.Children));
cla(ax, 'reset');
u9b_chk('cla_reset', @() {size(ax.YAxis), numel(ax.LineStyleOrder)});

ax = axes(figure('Visible', 'off'));
yyaxis(ax, 'right');
hold(ax, 'on');
p = plot(ax, 1:3);
q = plot(ax, 1:3);
delete([p q]);
r = plot(ax, 1:3);
u9b_chk('delete_continues', @() r.LineStyle);
s = stem(ax, 1:3);
u9b_chk('stem_style', @() {s.LineStyle, same_colour(s.Color, co(2, :))});
hold(ax, 'off');
plot(ax, 1:3);
plot(ax, 1:3);
u9b_chk('replace_restarts', @() ax.Children(1).LineStyle);
yyaxis(ax, 'left');
plot(ax, 1:3, 'DisplayName', 'a');
yyaxis(ax, 'right');
set(ax.Children, 'DisplayName', 'b');
l = legend(ax);
u9b_chk('legend_left_first', @() l.String);
u9b_chk('bad_side', @() as_statement(@() yyaxis(ax, 'middle')));
pax = polaraxes(figure('Visible', 'off'));
u9b_chk('polar_refused', @() as_statement(@() yyaxis(pax, 'right')));
ax = axes(figure('Visible', 'off'));
plot(ax, 1:3);
yyaxis(ax, 'right');
u9b_chk('existing_left_ink', @() {isequal(ax.YAxis(1).Color, ax.XAxis.Color), same_colour(ax.YAxis(2).Color, co(2, :))});
u9b_chk('existing_children', @() numel(ax.Children));
close all force

% --- item 107: a colour per point, CData as written, scatter3 and bubblechart3, alphas
ax = axes(figure('Visible', 'off'));
rgb = [1 0 0; 0 1 0; 0 0 1];
s = scatter(ax, 1:3, 1:3, 20, rgb);
u9b_chk('rgb_channels', @() {s.CData, s.CDataMode, s.MarkerEdgeColor, s.MarkerFaceColor});
u9b_chk('rgb_filled', @() get(scatter(ax, 1:3, 1:3, 20, rgb, 'filled'), {'CData', 'MarkerFaceColor', 'MarkerEdgeColor'}));
u9b_chk('rgb_option', @() get(scatter(ax, 1:3, 1:3, 'CData', rgb), 'CData'));
u9b_chk('rgb_wrong_rows', @() as_statement(@() scatter(ax, 1:3, 1:3, 20, [1 0 0; 0 1 0])));
s = scatter(ax, 1:3, 1:3);
s.CData = [1 0 0; 0 1 0];
u9b_chk('rgb_wrong_rows_set', @() {s.CData, s.CDataMode});
s.CData = rgb;
u9b_chk('rgb_set', @() s.CData);
s.CDataMode = 'auto';
u9b_chk('rgb_released', @() {size(s.CData), s.CDataMode});
u9b_chk('rgb_out_of_range', @() get(scatter(ax, 1:3, 1:3, 20, [2 0 0; 0 1 0; 0 0 1]), 'CData'));
u9b_chk('three_points_row', @() get(scatter(ax, 1:3, 1:3, 20, [0.1 0.5 0.9]), 'CData'));
u9b_chk('three_points_column', @() get(scatter(ax, 1:3, 1:3, 20, [0.1; 0.5; 0.9]), 'CData'));
u9b_chk('values_column', @() get(scatter(ax, 1:4, 1:4, 20, [1; 2; 3; 4]), 'CData'));
u9b_chk('values_row', @() get(scatter(ax, 1:4, 1:4, 20, [1 2 3 4]), 'CData'));
u9b_chk('values_column_xy', @() get(scatter(ax, (1:4)', (1:4)', 20, [1; 2; 3; 4]), 'CData'));
u9b_chk('triplet_out_of_range', @() get(scatter(ax, 1:4, 1:4, 20, [1 2 3]), 'CData'));
s = scatter(ax, 1:4, 1:4);
s.CData = [1; 2; 3; 4];
u9b_chk('values_column_set', @() s.CData);
u9b_chk('word_q', @() as_statement(@() scatter(ax, 1:3, 1:3, 20, 'q')));
u9b_chk('word_qo', @() id_of(@() scatter(ax, 1:3, 1:3, 20, 'qo')));
u9b_chk('word_hex', @() id_of(@() scatter(ax, 1:3, 1:3, 20, '#FF0000')));
u9b_chk('word_red', @() get(scatter(ax, 1:3, 1:3, 20, 'red'), 'CData'));
cla(ax);
s = scatter(ax, 1:3, 1:3);
alphas = {'MarkerFaceAlpha', 2; 'MarkerEdgeAlpha', -0.1; 'MarkerFaceAlpha', NaN; 'MarkerFaceAlpha', 'bogus'; 'MarkerFaceAlpha', [0.1 0.2]};
for k = 1:size(alphas, 1)
    u9b_chk(sprintf('scatter_alpha_refused_%d', k), @() as_statement(@() set(s, alphas{k, 1}, alphas{k, 2})));
end
u9b_chk('scatter_alpha_option_refused', @() as_statement(@() scatter(ax, 1:3, 1:3, 'MarkerFaceAlpha', 2)));
u9b_chk('bar_alpha_refused', @() as_statement(@() set(bar(ax, 1:3), 'FaceAlpha', 2)));
close all force

ax = axes(figure('Visible', 'off'));
s3 = scatter3(ax, 1:3, 1:3, 1:3);
u9b_chk('s3_defaults', @() {s3.MarkerFaceAlpha, s3.MarkerEdgeAlpha, s3.MarkerEdgeColor, s3.MarkerFaceColor});
s3.MarkerFaceAlpha = 0.4;
s3.MarkerEdgeAlpha = 'flat';
u9b_chk('s3_alphas_set', @() {s3.MarkerFaceAlpha, s3.MarkerEdgeAlpha});
u9b_chk('s3_alpha_options', @() get(scatter3(ax, 1:3, 1:3, 1:3, 'MarkerFaceAlpha', 0.25, 'MarkerEdgeAlpha', 0.35), {'MarkerFaceAlpha', 'MarkerEdgeAlpha'}));
s3 = scatter3(ax, 1:3, 1:3, 1:3, 'MarkerEdgeColor', 'r');
u9b_chk('s3_edge', @() {s3.MarkerEdgeColor, s3.MarkerFaceColor, isequal(s3.CData, [1 0 0])});
u9b_chk('s3_face', @() get(scatter3(ax, 1:3, 1:3, 1:3, 'MarkerFaceColor', 'g'), {'MarkerFaceColor', 'MarkerEdgeColor'}));
u9b_chk('s3_filled_edge', @() get(scatter3(ax, 1:3, 1:3, 1:3, 'filled', 'MarkerEdgeColor', 'k'), {'MarkerEdgeColor', 'MarkerFaceColor'}));
u9b_chk('s3_filled', @() get(scatter3(ax, 1:3, 1:3, 1:3, 'filled'), {'MarkerEdgeColor', 'MarkerFaceColor'}));
u9b_chk('s3_rgb', @() get(scatter3(ax, 1:3, 1:3, 1:3, 20, rgb), 'CData'));
u9b_chk('s3_values', @() get(scatter3(ax, 1:3, 1:3, 1:3, 20, [5 6 7]), 'CData'));
u9b_chk('s3_word_q', @() as_statement(@() scatter3(ax, 1:3, 1:3, 1:3, 20, 'q')));
s3 = scatter3(ax, 1:3, 1:3, 1:3);
s3.MarkerEdgeColor = [0 0 1];
s3.MarkerFaceColor = 'flat';
u9b_chk('s3_words_set', @() {s3.MarkerEdgeColor, s3.MarkerFaceColor});
b3 = bubblechart3(ax, 1:3, 1:3, 1:3, [1 2 3]);
u9b_chk('b3_defaults', @() {b3.MarkerFaceAlpha, b3.MarkerEdgeAlpha, b3.MarkerFaceColor, b3.MarkerEdgeColor});
b3.MarkerEdgeAlpha = 0.5;
u9b_chk('b3_edge_alpha', @() b3.MarkerEdgeAlpha);
u9b_chk('b3_refused', @() as_statement(@() set(b3, 'MarkerFaceAlpha', 2)));
u9b_chk('b3_option', @() get(bubblechart3(ax, 1:3, 1:3, 1:3, [1 2 3], 'MarkerFaceAlpha', 0.25), 'MarkerFaceAlpha'));
close all force

ax = axes(figure('Visible', 'off'));
hold(ax, 'on');
pa = patch(ax, [0 1 1], [0 0 1], 'r');
u9b_chk('patch_defaults', @() {pa.FaceAlpha, pa.EdgeAlpha, pa.FaceVertexAlphaData, pa.AlphaDataMapping});
pa.FaceAlpha = 'flat';
pa.EdgeAlpha = 'interp';
u9b_chk('patch_words', @() {pa.FaceAlpha, pa.EdgeAlpha});
pa.FaceAlpha = 0.6;
u9b_chk('patch_number_again', @() pa.FaceAlpha);
u9b_chk('patch_options', @() get(patch(ax, [0 1 1], [0 0 1], 'r', 'FaceAlpha', 'interp', 'EdgeAlpha', 'flat'), {'FaceAlpha', 'EdgeAlpha'}));
u9b_chk('patch_vertex_alpha', @() get(patch(ax, [0 1 1], [0 0 1], 'r', 'FaceAlpha', 'flat', 'FaceVertexAlphaData', 0.3), {'FaceAlpha', 'FaceVertexAlphaData'}));
u9b_chk('fill_flat', @() get(fill(ax, [0 1 1], [0 0 1], 'r', 'FaceAlpha', 'flat'), 'FaceAlpha'));
u9b_chk('patch_word_refused', @() as_statement(@() patch(ax, [0 1 1], [0 0 1], 'r', 'FaceAlpha', 'bogus')));
u9b_chk('patch_big_refused', @() as_statement(@() patch(ax, [0 1 1], [0 0 1], 'r', 'FaceAlpha', 2)));
u9b_chk('patch_set_big_refused', @() as_statement(@() set(pa, 'FaceAlpha', 2)));
u9b_chk('patch_set_negative_refused', @() as_statement(@() set(pa, 'EdgeAlpha', -1)));
close all force

% --- item 108: xregion and yregion
ax = axes(figure('Visible', 'off'));
q = xregion(ax, 1, 2);
u9b_chk('region_properties', @() properties(q)');
u9b_chk('region_defaults', @() get(q, {'Value', 'InterceptAxis', 'LineWidth', 'EdgeColor', 'EdgeAlpha', 'LineStyle', 'FaceColor', 'FaceAlpha', 'Layer', 'FaceColorMode', 'Visible', 'HandleVisibility', 'DisplayName', 'Type', 'SeriesIndex'}));
u9b_chk('yregion_defaults', @() get(yregion(ax, 1, 2), {'Value', 'InterceptAxis', 'FaceColor', 'FaceAlpha', 'Type'}));
u9b_chk('region_parent', @() q.Parent == ax);
u9b_chk('region_isgraphics', @() {isgraphics(q), isgraphics(q, 'constantregion'), ishghandle(q)});
cla(ax);
forms = {
    'pair', @() xregion(ax, 1, 2)
    'pairs', @() xregion(ax, [1 3], [2 4])
    'two_rows', @() xregion(ax, [1 2; 3 4])
    'two_rows_wide', @() xregion(ax, [1 2 3; 4 5 6])
    'three_rows', @() xregion(ax, [1 2; 3 4; 5 6])
    'row', @() xregion(ax, [1 2])
    'column', @() xregion(ax, [1; 2])
    'reversed', @() xregion(ax, 2, 1)
    'equal', @() xregion(ax, 2, 2)
    'nan', @() xregion(ax, NaN, 2)
    'inf', @() xregion(ax, -Inf, 2)
    };
for k = 1:size(forms, 1)
    u9b_chk(['region_' forms{k, 1}], @() region_values(forms{k, 2}()));
end
u9b_chk('region_two_values', @() get(xregion(ax, [1 3], [2 4]), 'Value'));
u9b_chk('region_options', @() get(xregion(ax, 1, 2, 'FaceColor', 'r', 'EdgeColor', 'k', 'LineWidth', 2, 'LineStyle', '--', 'DisplayName', 'band', 'FaceAlpha', 0.5), ...
    {'FaceColor', 'FaceColorMode', 'EdgeColor', 'LineWidth', 'LineStyle', 'DisplayName', 'FaceAlpha'}));
q = xregion(ax, 1, 2);
q.Value = [5 6];
u9b_chk('region_set_value', @() q.Value);
q.Value = [6 5];
u9b_chk('region_set_reversed', @() q.Value);
q.InterceptAxis = 'y';
q.Layer = 'top';
q.FaceColor = 'none';
q.SeriesIndex = 2;
u9b_chk('region_set_words', @() {q.InterceptAxis, q.Layer, q.FaceColor, q.SeriesIndex});
q.FaceColor = [0 0 1];
u9b_chk('region_set_colour', @() {q.FaceColor, q.FaceColorMode});
q2 = xregion(ax, 1, 2);
q2.SeriesIndex = 2;
u9b_chk('region_series_colour', @() same_colour(q2.FaceColor, co(2, :)));
refusals = {
    'value_one', @() set(q, 'Value', 5)
    'value_three', @() set(q, 'Value', [1 2 3])
    'face_alpha_big', @() set(q, 'FaceAlpha', 2)
    'face_alpha_flat', @() set(q, 'FaceAlpha', 'flat')
    'edge_alpha_flat', @() set(q, 'EdgeAlpha', 'flat')
    'layer', @() set(q, 'Layer', 'middle')
    'line_style', @() set(q, 'LineStyle', 'zz')
    'scalar', @() xregion(ax, 3)
    'uneven', @() xregion(ax, [1 2], [3 4 5])
    'empty', @() xregion(ax, [], [])
    'text', @() xregion(ax, 'a', 2)
    'bogus_option', @() xregion(ax, 1, 2, 'Bogus', 1)
    'odd_options', @() xregion(ax, 1, 2, 'FaceColor')
    };
for k = 1:size(refusals, 1)
    u9b_chk(['region_refused_' refusals{k, 1}], @() as_statement(refusals{k, 2}));
end
u9b_chk('region_no_args', @() id_of(@() xregion()));
pax = polaraxes(figure('Visible', 'off'));
u9b_chk('region_polar', @() as_statement(@() xregion(pax, 1, 2)));
ax = axes(figure('Visible', 'off'));
plot(ax, 0:10, 'DisplayName', 'line');
xregion(ax, 1, 2, 'DisplayName', 'band');
u9b_chk('region_keeps_plot', @() numel(ax.Children));
u9b_chk('region_first_child', @() ax.Children(1).Type);
l = legend(ax);
u9b_chk('region_legend', @() l.String);
yregion(ax, 4, 5);
u9b_chk('region_both', @() numel(findobj(ax, 'Type', 'constantregion')));
plot(ax, 2:4);
u9b_chk('region_replaced', @() numel(ax.Children));
delete(xregion(ax, 1, 2));
u9b_chk('region_deleted', @() numel(ax.Children));
close all force

function r = same_colour(a, b)
% Whether two colours are the same to the byte: R2025b's colour order is written in decimals and
% what it inks rulers and yyaxis lines with is rounded to bytes.
r = isequal(round(a * 255), round(b * 255));
end

function r = region_values(h)
% How many bands a call made, and each one's Value.
r = {size(h), arrayfun(@(x) x.Value, h, 'UniformOutput', false)'};
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
