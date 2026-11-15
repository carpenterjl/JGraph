% record: -noFigureWindows
% Open item 343 (ADR 0376), the leftovers of ADR 0372: a stem's MarkerFaceColor under 'filled', an
% area's, a bar's, a histogram's and a rectangle's own edge, patch('XData', ..., 'YData', ...)'s face,
% MarkerEdgeColor 'flat' on a surface and a patch, plot of a table by variable numbers, strjoin of a
% char array; a tiled layout as a node of the figure tree (its tiles' and their legends' and colour
% bars' Parent, its Children, clf, delete, findall, allchild, a file round trip); the annotation pane
% after clf reset and a panel's own pane; what findall answers; a rotation's residue in the limits
% (probes p7fa-p7fe).
close all force
x = 1:3; y = [2 1 3];

% --- stem 'filled' ---------------------------------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
h = stem(ax, x, y, 'filled');
u9b_chk('stem_filled_mfc', @() h.MarkerFaceColor);
u9b_chk('stem_filled_mec', @() h.MarkerEdgeColor);
u9b_chk('stem_filled_marker', @() h.Marker);
u9b_chk('stem_filled_mec_auto', @() setget(h, 'MarkerEdgeColor', 'auto'));
cla(ax);
h = stem(ax, x, y);
u9b_chk('stem_plain_mfc', @() h.MarkerFaceColor);
u9b_chk('stem_plain_mec', @() h.MarkerEdgeColor);
cla(ax);
h = stem(ax, x, y, 'filled', 'MarkerFaceColor', 'r');
u9b_chk('stem_filled_red_mfc', @() h.MarkerFaceColor);
cla(ax);
h = stem(ax, x, y, 'filled');
u9b_chk('stem_set_auto_mfc', @() setget(h, 'MarkerFaceColor', 'auto'));
u9b_chk('stem_set_none_mfc', @() setget(h, 'MarkerFaceColor', 'none'));
u9b_chk('stem_set_auto_again', @() setget(h, 'MarkerFaceColor', 'auto'));
cla(ax);
h = stem(ax, x, [y; y + 1]', 'filled');
u9b_chk('stem_filled_two_mfc', @() get(h, 'MarkerFaceColor'));
cla(ax);
h = stem(ax, x, y, 'fi');
u9b_chk('stem_fi_mfc', @() h.MarkerFaceColor);
cla(ax);
h = stem3(ax, x, y, y, 'filled');
u9b_chk('stem3_filled_mfc', @() h.MarkerFaceColor);
u9b_chk('stem3_filled_mec', @() h.MarkerEdgeColor);
u9b_chk('stem3_set_auto_mfc', @() setget(h, 'MarkerFaceColor', 'r'));
cla(ax);
h = plot(ax, x, y);
u9b_chk('line_set_auto_mfc', @() setget(h, 'MarkerFaceColor', 'auto'));
cla(ax);

% --- an area's, a bar's, a histogram's and a rectangle's own edge ----------------------------------
h = area(ax, x, y);
u9b_chk('area_edge', @() {h.EdgeColor, h.LineWidth});
cla(ax);
h = area(ax, [y; y]');
u9b_chk('area_two_edge', @() get(h, 'EdgeColor'));
cla(ax);
h = bar(ax, x, y);
u9b_chk('bar_edge', @() {h.EdgeColor, h.LineWidth});
cla(ax);
h = bar(ax, [y; y]');
u9b_chk('bar_two_edge', @() get(h, 'EdgeColor'));
u9b_chk('bar_two_lw', @() get(h, 'LineWidth'));
cla(ax);
h = bar(ax, x, y, 'EdgeColor', 'none');
u9b_chk('bar_edge_none', @() h.EdgeColor);
cla(ax);
h = bar(ax, x, y, 'EdgeColor', 'r', 'LineWidth', 2);
u9b_chk('bar_edge_given', @() {h.EdgeColor, h.LineWidth});
cla(ax);
h = barh(ax, x, y);
u9b_chk('barh_edge', @() {h.EdgeColor, h.LineWidth});
cla(ax);
h = histogram(ax, [1 2 2 3 3 3]);
u9b_chk('hist_edge', @() {h.EdgeColor, h.LineWidth, h.EdgeAlpha});
cla(ax);
h = histogram(ax, [1 2 2 3 3 3], 'DisplayStyle', 'stairs');
u9b_chk('hist_stairs_lw', @() h.LineWidth);
cla(ax);
h = histogram(ax, categorical({'a', 'b', 'b'}));
u9b_chk('histcat_edge', @() {h.EdgeColor, h.LineWidth});
cla(ax);
h = histogram2(ax, [1 2 2 3], [1 1 2 3]);
u9b_chk('hist2_edge', @() {h.EdgeColor, h.LineWidth});
cla(ax);
r = rectangle(ax, 'Position', [0 0 1 1]);
u9b_chk('rect_edge', @() {r.EdgeColor, r.LineWidth, r.FaceColor, r.LineStyle});
cla(ax);
r = rectangle(ax, 'Position', [0 0 1 1], 'Curvature', 1);
u9b_chk('rect_curv_edge', @() {r.EdgeColor, r.LineWidth});
cla(ax);

% --- patch('XData', ..., 'YData', ...)'s face ------------------------------------------------------
p = patch(ax, 'XData', [0 1 1], 'YData', [0 0 1]);
u9b_chk('patch_xy_face', @() p.FaceColor);
u9b_chk('patch_xy_edge', @() {p.EdgeColor, p.LineWidth});
u9b_chk('patch_xy_cdata', @() p.CData);
cla(ax);
p = patch(ax, 'Vertices', [0 0; 1 0; 1 1], 'Faces', [1 2 3]);
u9b_chk('patch_vf_face', @() p.FaceColor);
u9b_chk('patch_vf_edge', @() {p.EdgeColor, p.LineWidth});
cla(ax);
p = patch(ax);
u9b_chk('patch_none_face', @() p.FaceColor);
cla(ax);
p = patch('XData', [0 1 1], 'YData', [0 0 1], 'Parent', ax);
u9b_chk('patch_xy_parent_face', @() p.FaceColor);
cla(ax);
p = patch(ax, 'XData', [0 1 1], 'YData', [0 0 1], 'FaceColor', 'r');
u9b_chk('patch_xy_red_face', @() p.FaceColor);
cla(ax);
p = patch(ax, [0 1 1], [0 0 1], 'r');
u9b_chk('patch_pos_face', @() p.FaceColor);
cla(ax);

% --- MarkerEdgeColor 'flat' ------------------------------------------------------------------------
s = surf(ax, magic(3));
u9b_chk('surf_mec_flat', @() setget(s, 'MarkerEdgeColor', 'flat'));
u9b_chk('surf_mfc_flat', @() setget(s, 'MarkerFaceColor', 'flat'));
u9b_chk('surf_mec_auto', @() setget(s, 'MarkerEdgeColor', 'auto'));
u9b_chk('surf_mfc_auto', @() setget(s, 'MarkerFaceColor', 'auto'));
u9b_chk('surf_mec_interp', @() setget(s, 'MarkerEdgeColor', 'interp'));
cla(ax);
s = surf(ax, magic(3), 'MarkerEdgeColor', 'flat');
u9b_chk('surf_mec_flat_opt', @() s.MarkerEdgeColor);
cla(ax);
s = mesh(ax, magic(3));
u9b_chk('mesh_mec_flat', @() setget(s, 'MarkerEdgeColor', 'flat'));
cla(ax);
p = patch(ax, [0 1 1], [0 0 1], 'r');
u9b_chk('patch_mec_flat', @() setget(p, 'MarkerEdgeColor', 'flat'));
u9b_chk('patch_mfc_flat', @() setget(p, 'MarkerFaceColor', 'flat'));
u9b_chk('patch_mec_interp', @() setget(p, 'MarkerEdgeColor', 'interp'));
cla(ax);
h = plot(ax, x, y);
u9b_chk('line_mec_flat', @() setget(h, 'MarkerEdgeColor', 'flat'));
cla(ax);

% --- plot of a table by variable numbers -----------------------------------------------------------
tbl = table([1; 2; 3], [4; 5; 6], [7; 8; 9], 'VariableNames', {'a', 'b', 'c'});
u9b_chk('plot_tbl_12', @() plotprops(plot(ax, tbl, 1, 2)));
cla(ax);
u9b_chk('plot_tbl_a2', @() plotprops(plot(ax, tbl, 'a', 2)));
cla(ax);
u9b_chk('plot_tbl_1b', @() plotprops(plot(ax, tbl, 1, 'b')));
cla(ax);
u9b_chk('plot_tbl_ab', @() plotprops(plot(ax, tbl, 'a', 'b')));
cla(ax);
u9b_chk('plot_tbl_1_23_n', @() numel(plot(ax, tbl, 1, [2 3])));
cla(ax);
u9b_chk('plot_tbl_1_23_yvar', @() get(plot(ax, tbl, 1, [2 3]), 'YVariable'));
cla(ax);
u9b_chk('plot_tbl_1_23_ydata', @() get(plot(ax, tbl, 1, [2 3]), 'YData'));
cla(ax);
u9b_chk('plot_tbl_logical', @() plotprops(plot(ax, tbl, logical([1 0 0]), logical([0 1 0]))));
cla(ax);
u9b_chk('plot_tbl_4', @() plotprops(plot(ax, tbl, 1, 4)));
cla(ax);
u9b_chk('plot_tbl_15', @() plotprops(plot(ax, tbl, 1.5, 2)));
cla(ax);
u9b_chk('plot_tbl_12_labels', @() plotlabels(ax, tbl, 1, 2));
cla(ax);
f2 = figure('Visible', 'off');
u9b_chk('plot_tbl_ab_fresh_labels', @() plotlabels(axes(f2), tbl, 'a', 'b'));
close(f2);
f2 = figure('Visible', 'off');
plot(tbl, 'a', 'b');
u9b_chk('plot_tbl_ab_gca_labels', @() {get(get(gca, 'XLabel'), 'String'), get(get(gca, 'YLabel'), 'String')});
close(f2);
figure(f);
hl = plot(ax, tbl, 'a', 'b');
hl.YVariable = 3;
u9b_chk('plot_tbl_set_yvar_num', @() {hl.YVariable, hl.YData});
close(f);

% --- strjoin of a char array -----------------------------------------------------------------------
u9b_chk('strjoin_col', @() strjoin(['a'; 'b']));
u9b_chk('strjoin_row', @() strjoin('ab'));
u9b_chk('strjoin_col_delim', @() strjoin(['a'; 'b'], '-'));
u9b_chk('strjoin_mat', @() strjoin(['ab'; 'cd']));
u9b_chk('strjoin_empty', @() strjoin(''));
u9b_chk('strjoin_num', @() strjoin([1 2]));
u9b_chk('strjoin_cellcol', @() strjoin({'a'; 'b'}));
u9b_chk('strjoin_strcol', @() strjoin(["a"; "b"]));

% --- a tiled layout in the figure tree -------------------------------------------------------------
f = figure('Visible', 'off');
t = tiledlayout(f, 2, 2);
u9b_chk('t_parent_isfig', @() isequal(t.Parent, f));
u9b_chk('t_children_n0', @() numel(t.Children));
u9b_chk('fig_children_types0', @() types(f.Children));
ax1 = nexttile(t);
u9b_chk('ax_parent_ist', @() isequal(ax1.Parent, t));
u9b_chk('ax_parent_type', @() ax1.Parent.Type);
u9b_chk('ax_get_parent_type', @() get(get(ax1, 'Parent'), 'Type'));
u9b_chk('ax_ancestor_fig', @() isequal(ancestor(ax1, 'figure'), f));
u9b_chk('ax_ancestor_t', @() isequal(ancestor(ax1, 'tiledlayout'), t));
u9b_chk('fig_children_types1', @() types(f.Children));
u9b_chk('t_children_types1', @() types(t.Children));
plot(ax1, 1:3);
ax1.Tag = 'a1';
ax2 = nexttile(t);
plot(ax2, 3:-1:1);
ax2.Tag = 'a2';
u9b_chk('t_children_order2', @() isequal(t.Children(:), [ax2; ax1]));
lg = legend(ax1);
u9b_chk('lg_parent_ist', @() isequal(lg.Parent, t));
cb = colorbar(ax2);
u9b_chk('cb_parent_ist', @() isequal(cb.Parent, t));
u9b_chk('t_children_tags3', @() tags(t.Children));
u9b_chk('fig_children_types3', @() types(f.Children));
u9b_chk('fig_allchild_types3', @() types(allchild(f)));
u9b_chk('t_allchild_tags3', @() tags(allchild(t)));
u9b_chk('fig_findobj_tags3', @() tags(findobj(f)));
u9b_chk('fig_findall_axes_n', @() numel(findall(f, 'Type', 'axes')));
u9b_chk('fig_findobj_layout', @() types(findobj(f, 'Type', 'tiledlayout')));
u9b_chk('t_findobj_tags', @() tags(findobj(t)));
u9b_chk('t_findall_tags', @() tags(findall(t)));
u9b_chk('fig_findobj_depth1', @() types(findobj(f, '-depth', 1)));
u9b_chk('findobj_parent_t', @() numel(findobj(f, 'Parent', t)));
u9b_chk('gca_is_ax2', @() isequal(gca, ax2));
u9b_chk('lg_ancestor_fig', @() isequal(ancestor(lg, 'figure'), f));
ax3 = nexttile(t); ax3.Tag = 'a3';
u9b_chk('t_children_plus_ax3', @() tags(t.Children));
cb3 = colorbar(ax3);
u9b_chk('t_children_plus_cb3', @() tags(t.Children));
lg2 = legend(ax2);
u9b_chk('t_children_plus_lg2', @() tags(t.Children));
legend(ax1, 'off');
u9b_chk('t_children_lg1_off', @() tags(t.Children));
legend(ax1, 'show');
u9b_chk('t_children_lg1_again', @() tags(t.Children));
u9b_chk('fig_findall_tags', @() treetags(findall(f)));
u9b_chk('fig_allchild_types', @() types(allchild(f)));
delete(ax2);
u9b_chk('del_ax2_cb_valid', @() isvalid(cb));
u9b_chk('del_ax2_lg2_valid', @() isvalid(lg2));
u9b_chk('del_ax2_t_children', @() tags(t.Children));
delete(t);
u9b_chk('del_t_ax1_valid', @() isvalid(ax1));
u9b_chk('del_t_ax3_valid', @() isvalid(ax3));
u9b_chk('del_t_cb3_valid', @() isvalid(cb3));
u9b_chk('del_t_fig_children', @() types(f.Children));
u9b_chk('del_t_fig_findall_n', @() numel(findall(f, 'Type', 'axes')));
close(f);

f = figure('Visible', 'off');
t = tiledlayout(f, 1, 2);
ax1 = nexttile(t);
colorbar(ax1);
legend(ax1, 'show');
u9b_chk('cb_then_lg', @() tags(t.Children));
clf(f);
u9b_chk('clf_t_valid', @() isvalid(t));
u9b_chk('clf_ax_valid', @() isvalid(ax1));
u9b_chk('clf_fig_children', @() types(f.Children));
close(f);
f = figure('Visible', 'off');
t = tiledlayout(f, 1, 2);
nexttile(t);
clf(f, 'reset');
u9b_chk('clfr_t_valid', @() isvalid(t));
u9b_chk('clfr_fig_children', @() types(f.Children));
close(f);
f = figure('Visible', 'off');
t = tiledlayout(f, 2, 2);
ax = axes(t);
u9b_chk('axes_in_t_parent', @() isequal(ax.Parent, t));
u9b_chk('axes_in_t_tile', @() ax.Layout.Tile);
u9b_chk('axes_in_t_children', @() numel(t.Children));
close(f);
f = figure('Visible', 'off');
t = tiledlayout(f, 1, 2);
nexttile(t);
axf = axes(f, 'Tag', 'free');
u9b_chk('layout_then_free', @() types(f.Children));
u9b_chk('layout_then_free_t_valid', @() isvalid(t));
close(f);
f = figure('Visible', 'off');
t = tiledlayout(f, 1, 2);
nexttile(t);
t2 = tiledlayout(f, 2, 1);
u9b_chk('t_again_old_valid', @() isvalid(t));
u9b_chk('t_again_fig_children', @() types(f.Children));
close(f);
f = figure('Visible', 'off');
axf = axes(f);
t = tiledlayout(f, 1, 2);
u9b_chk('free_then_layout', @() types(f.Children));
u9b_chk('free_then_layout_valid', @() isvalid(axf));
close(f);
f = figure('Visible', 'off');
t = tiledlayout(f, 1, 2);
nexttile(t);
subplot(2, 1, 1, 'Parent', f);
u9b_chk('layout_then_subplot', @() types(f.Children));
u9b_chk('layout_then_subplot_t_valid', @() isvalid(t));
close(f);
f = figure('Visible', 'off');
ax = nexttile;
u9b_chk('implicit_parent_type', @() ax.Parent.Type);
u9b_chk('implicit_fig_children', @() types(f.Children));
plot(1:3);
u9b_chk('implicit_gca_parent', @() get(get(gca, 'Parent'), 'Type'));
close(f);

f = figure('Visible', 'off');
t = tiledlayout(f, 1, 2, 'TileSpacing', 'compact');
title(t, 'Grid');
ax1 = nexttile(t);
plot(ax1, 1:3, 'DisplayName', 'up');
legend(ax1);
ax2 = nexttile(t);
imagesc(ax2, magic(3));
colorbar(ax2);
u9b_chk('save_fig_children', @() types(f.Children));
u9b_chk('save_t_children', @() types(t.Children));
fn = [tempname '.fig'];
savefig(f, fn);
close(f);
g = openfig(fn, 'invisible');
u9b_chk('open_fig_children', @() types(g.Children));
tt = findobj(g, 'Type', 'tiledlayout');
u9b_chk('open_t_n', @() numel(tt));
u9b_chk('open_t_children_sorted', @() strjoin(sort(cellstr(get(tt.Children, 'Type')))', ' '));
u9b_chk('open_t_spacing', @() tt.TileSpacing);
u9b_chk('open_lg_string', @() get(findobj(g, 'Type', 'legend'), 'String'));
close(g);
delete(fn);

% --- the annotation pane after clf reset, and a panel's own pane ----------------------------------
f = figure('Visible', 'off');
axes(f);
pane = findall(f, 'Type', 'annotationpane');
clf(f, 'reset');
u9b_chk('pane_clfreset', @() types(allchild(f)));
u9b_chk('pane_clfreset_same', @() [isvalid(pane), isequal(findall(f, 'Type', 'annotationpane'), pane)]);
axes(f);
u9b_chk('pane_clfreset_axes', @() types(allchild(f)));
close(f);
f = figure('Visible', 'off');
clf(f, 'reset');
u9b_chk('pane_fresh_clfreset', @() types(allchild(f)));
close(f);
f = figure('Visible', 'off');
axes(f);
clf(f);
clf(f, 'reset');
u9b_chk('pane_clf_then_reset', @() types(allchild(f)));
clf(f, 'reset');
u9b_chk('pane_reset_twice', @() types(allchild(f)));
close(f);
f = figure('Visible', 'off');
annotation(f, 'line', [0 1], [0 1]);
pane = findall(f, 'Type', 'annotationpane');
clf(f, 'reset');
u9b_chk('pane_ann_clfreset', @() types(allchild(f)));
u9b_chk('pane_ann_clfreset_oldvalid', @() isvalid(pane));
close(f);
f = figure('Visible', 'off', 'MenuBar', 'none', 'ToolBar', 'none');
axes(f);
delete(findall(f, 'Type', 'annotationpane'));
clf(f, 'reset');
u9b_chk('pane_deleted_reset', @() types(allchild(f)));
close(f);
f = figure('Visible', 'off', 'MenuBar', 'none', 'ToolBar', 'none');
p = uipanel(f);
u9b_chk('panel_empty_allchild', @() types(allchild(p)));
ax = axes(p);
u9b_chk('panel_ax_allchild', @() types(allchild(p)));
u9b_chk('panel_ax_children', @() types(p.Children));
u9b_chk('panel_ax_fig_allchild', @() types(allchild(f)));
u9b_chk('panel_ax_findall', @() types(findall(f)));
u9b_chk('panel_pane_tag', @() get(findall(p, 'Type', 'annotationpane'), 'Tag'));
u9b_chk('panel_pane_parent', @() isequal(get(findall(p, 'Type', 'annotationpane'), 'Parent'), p));
delete(ax);
u9b_chk('panel_ax_deleted', @() types(allchild(p)));
close(f);
f = figure('Visible', 'off', 'MenuBar', 'none', 'ToolBar', 'none');
p = uipanel(f);
plot(axes(p), 1:3);
legend(gca);
u9b_chk('panel_plot_allchild', @() types(allchild(p)));
close(f);
f = figure('Visible', 'off', 'MenuBar', 'none', 'ToolBar', 'none');
tg = uitabgroup(f);
tb = uitab(tg);
axes(tb);
u9b_chk('tab_ax_allchild', @() types(allchild(tb)));
close(f);
f = figure('Visible', 'off', 'MenuBar', 'none', 'ToolBar', 'none');
t = tiledlayout(f, 1, 1);
nexttile(t);
u9b_chk('tiled_fig_allchild', @() types(allchild(f)));
u9b_chk('tiled_t_allchild', @() types(allchild(t)));
close(f);

% --- what findall answers --------------------------------------------------------------------------
f = figure('Visible', 'off', 'MenuBar', 'none', 'ToolBar', 'none');
ax = axes(f);
h = plot(ax, 1:3);
fa = findall(f, 'Type', 'line');
u9b_chk('findall_eq_h', @() isequal(fa, h));
u9b_chk('findall_ltype', @() fa.Type);
u9b_chk('findall_empty_size', @() size(findall(f, 'Type', 'surface')));
u9b_chk('findobj_empty_size', @() size(findobj(f, 'Type', 'surface')));
u9b_chk('findobj_empty_root_size', @() size(findobj('Tag', 'no such tag')));
u9b_chk('findall_num_eq', @() isequal(findall(double(f), 'Type', 'line'), h));
u9b_chk('allchild_empty_size', @() size(allchild(h)));
close(f);

% --- a rotation's residue: an exactly orthogonal matrix's entries under eps fit as zero -------------
f = figure('Visible', 'off');
ax = axes(f);
u9b_chk('rot90_lims', @() fitted(ax, makehgtform('zrotate', pi/2), [0 1], [0 0]));
u9b_chk('rot90_y01_lims', @() fitted(ax, makehgtform('zrotate', pi/2), [0 0], [0 1]));
u9b_chk('rot90_y5_lims', @() fitted(ax, makehgtform('zrotate', pi/2), [0 1], [5 5]));
u9b_chk('rot180_lims', @() fitted(ax, makehgtform('zrotate', pi), [0 1], [0 0]));
u9b_chk('rot270_lims', @() fitted(ax, makehgtform('zrotate', 3*pi/2), [0 1], [0 0]));
u9b_chk('rot1e17_lims', @() fitted(ax, makehgtform('zrotate', 1e-17), [0 1], [0 0]));
M = eye(4); M(1, 2) = 1e-17;
u9b_chk('shear1e17_lims', @() fitted(ax, M, [0 0], [0 1]));
M = eye(4); M(2, 1) = 6e-17;
u9b_chk('shear6e17_lims', @() fitted(ax, M, [0 1], [0 0]));
M = eye(4); M(1, 1) = 6e-17;
u9b_chk('scale6e17_lims', @() fitted(ax, M, [0 1], [0 1]));
close(f);
close all force

function v = fitted(ax, M, x, y)
cla(ax);
tr = hgtransform(ax);
tr.Matrix = M;
line(ax, x, y, 'Parent', tr);
drawnow;
v = [ax.XLim, ax.YLim];
end

function v = setget(h, name, value)
set(h, name, value);
v = get(h, name);
end

function v = plotprops(h)
v = {numel(h), get(h, 'XVariable'), get(h, 'YVariable'), get(h, 'XData'), get(h, 'YData')};
end

function v = plotlabels(ax, varargin)
plot(ax, varargin{:});
v = {ax.XLabel.String, ax.YLabel.String};
end

function s = types(h)
if isempty(h)
    s = '';
else
    s = strjoin(cellstr(get(h, 'Type'))', ' ');
end
end

function s = treetags(h)
% The tags of what findall found, less the figure's default menus and toolbar.
keep = ~ismember(cellstr(get(h, 'Type')), {'uimenu', 'uitoolbar', 'uipushtool', 'uitoggletool'});
s = tags(h(keep));
end

function s = tags(h)
if isempty(h)
    s = '';
else
    tg = cellstr(get(h, 'Tag'));
    ty = cellstr(get(h, 'Type'));
    for i = 1:numel(tg)
        if isempty(tg{i})
            tg{i} = ty{i};
        end
    end
    s = strjoin(tg', ' ');
end
end
