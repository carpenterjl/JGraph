% record: -noFigureWindows
% Open items 326 and 327 (ADR 0372). 326: an invisible figure's default menus and toolbar and its
% annotation pane in findall and allchild, a legend's and a colour bar's own Tag, get of no handles,
% legend(ax) beside a group, a legend of a group through each engine's own figure file, stem's
% 'filled' by any start, plot of a string, xscale of a heatmap, a table channel named by number
% (probes p6a, p6b). 327: a patch's CDataMode written 'auto', a colour argument then FaceColor 'flat',
% a patch's and a fill's own edge, a surface's and a patch's marker edge, histogram of nothing.
close all force
x = 1:3; y = [2 1 3];

% --- 326: the figure's default menus, toolbar and annotation pane ----------------------------------
f = figure('Visible', 'off');
u9b_chk('fig_findall_types', @() types(findall(f)));
u9b_chk('fig_allchild_types', @() types(allchild(f)));
u9b_chk('fig_children_n', @() numel(f.Children));
u9b_chk('fig_findobj_n', @() numel(findobj(f)));
u9b_chk('fig_findall_tags', @() tags(findall(f)));
ax = axes(f);
u9b_chk('figax_findall_types', @() types(findall(f)));
u9b_chk('figax_allchild_types', @() types(allchild(f)));
u9b_chk('figax_children_n', @() numel(f.Children));
annotation(f, 'line', [0.1 0.2], [0.1 0.2]);
u9b_chk('figann_findall_types', @() types(findall(f)));
u9b_chk('figann_allchild_types', @() types(allchild(f)));
close(f);
f = figure('Visible', 'off', 'MenuBar', 'none', 'ToolBar', 'none');
u9b_chk('fignone_findall_types', @() types(findall(f)));
close(f);
f = figure('Visible', 'off', 'MenuBar', 'none');
u9b_chk('fignomenu_findall_types', @() types(findall(f)));
close(f);
f = figure('Visible', 'off', 'ToolBar', 'none');
u9b_chk('fignotool_findall_n', @() numel(findall(f)));
close(f);
f = figure('Visible', 'off', 'ToolBar', 'figure', 'MenuBar', 'none');
u9b_chk('figtool_findall_types', @() types(findall(f)));
close(f);
f = figure('Visible', 'off');
plot(1:3);
u9b_chk('figplot_findall_types', @() types(findall(f)));
u9b_chk('menu_n', @() numel(findall(f, 'Type', 'uimenu')));
u9b_chk('menu_top_tags', @() tags(findall(f, 'Type', 'uimenu', 'Parent', f)));
u9b_chk('menu_top_text', @() strjoin(cellstr(get(findall(f, 'Type', 'uimenu', 'Parent', f), 'Text'))', ' '));
tb = findall(f, 'Type', 'uitoolbar');
u9b_chk('tb_tag', @() get(tb, 'Tag'));
u9b_chk('tb_tools', @() tags(allchild(tb)));
u9b_chk('tb_tool_types', @() types(allchild(tb)));
u9b_chk('tb_vis', @() char(get(tb, 'Visible')));
u9b_chk('tb_hv', @() get(tb, 'HandleVisibility'));
u9b_chk('tb_children', @() [numel(tb.Children), numel(allchild(tb))]);
u9b_chk('tb_parent', @() isequal(tb.Parent, f));
u9b_chk('tool_tip', @() get(findall(f, 'Tag', 'Standard.SaveFigure'), 'Tooltip'));
u9b_chk('tool_sep', @() char(get(findall(f, 'Tag', 'Standard.EditPlot'), 'Separator')));
mf = findall(f, 'Tag', 'figMenuFile');
u9b_chk('mf_text', @() mf.Text);
u9b_chk('mf_parent', @() isequal(mf.Parent, f));
u9b_chk('mf_children', @() [numel(mf.Children), numel(allchild(mf))]);
u9b_chk('mf_items', @() tags(allchild(mf)));
u9b_chk('save_acc', @() get(findall(f, 'Tag', 'figMenuFileSave'), 'Accelerator'));
u9b_chk('undo_enable', @() char(get(findall(f, 'Tag', 'figMenuEditUndo'), 'Enable')));
u9b_chk('nan_parent', @() get(get(findall(f, 'Tag', 'figDataManagerReplaceNaN'), 'Parent'), 'Tag'));
u9b_chk('ap_tag', @() get(findall(f, 'Type', 'annotationpane'), 'Tag'));
u9b_chk('findobj_menu_n', @() numel(findobj(f, 'Type', 'uimenu')));
u9b_chk('groot_menu_n', @() numel(findall(groot, 'Type', 'uimenu')));
set(mf, 'Visible', 'off');
u9b_chk('mf_vis', @() char(mf.Visible));
delete(mf);
u9b_chk('mf_deleted_n', @() numel(findall(f, 'Type', 'uimenu')));
u9b_chk('mf_deleted_valid', @() isvalid(mf));
close(f);

f = figure('Visible', 'off');
m = uimenu(f, 'Text', 'Mine', 'Tag', 'mine');
u9b_chk('mine_order', @() tags(allchild(f)));
u9b_chk('mine_children', @() numel(f.Children));
close(f);
f = figure('Visible', 'off');
f.MenuBar = 'none';
u9b_chk('mb_none_after', @() [numel(findall(f, 'Type', 'uimenu')), numel(findall(f, 'Type', 'uitoolbar'))]);
f.MenuBar = 'figure';
u9b_chk('mb_fig_after', @() [numel(findall(f, 'Type', 'uimenu')), numel(findall(f, 'Type', 'uitoolbar'))]);
f.ToolBar = 'none';
u9b_chk('tb_none_after', @() [numel(findall(f, 'Type', 'uimenu')), numel(findall(f, 'Type', 'uitoolbar'))]);
clf(f);
u9b_chk('clf_keeps', @() [numel(findall(f, 'Type', 'uimenu')), numel(findall(f, 'Type', 'uitoolbar'))]);
delete(allchild(f));
u9b_chk('delall', @() [numel(findall(f, 'Type', 'uimenu')), numel(findall(f, 'Type', 'uitoolbar'))]);
close(f);

f = figure('Visible', 'off', 'MenuBar', 'none', 'ToolBar', 'none');
u9b_chk('pane_fresh', @() numel(findall(f, 'Type', 'annotationpane')));
ax = axes(f);
u9b_chk('pane_axes', @() numel(findall(f, 'Type', 'annotationpane')));
u9b_chk('pane_order', @() types(allchild(f)));
delete(ax);
u9b_chk('pane_after_delete', @() numel(findall(f, 'Type', 'annotationpane')));
axes(f);
clf(f);
u9b_chk('pane_after_clf', @() numel(findall(f, 'Type', 'annotationpane')));
axes(f);
uicontrol(f);
u9b_chk('pane_order_uicontrol', @() types(allchild(f)));
axes(f);
u9b_chk('pane_order_two', @() types(allchild(f)));
u9b_chk('pane_children_n', @() numel(f.Children));
close(f);
f = figure('Visible', 'off', 'MenuBar', 'none', 'ToolBar', 'none');
plot(1:3);
u9b_chk('pane_plot', @() types(allchild(f)));
close(f);
f = figure('Visible', 'off', 'MenuBar', 'none', 'ToolBar', 'none');
uicontrol(f);
u9b_chk('pane_uicontrol_only', @() types(allchild(f)));
close(f);
f = figure('Visible', 'off', 'MenuBar', 'none', 'ToolBar', 'none');
polaraxes(f);
u9b_chk('pane_polar', @() types(allchild(f)));
close(f);

% --- 326: a legend's and a colour bar's own Tag ----------------------------------------------------
f = figure('Visible', 'off');
plot(1:3);
lg = legend;
cb = colorbar;
u9b_chk('lg_tag', @() lg.Tag);
u9b_chk('cb_tag', @() cb.Tag);
lg.Tag = 'mine';
legend('off');
lg = legend;
u9b_chk('lg_tag_new', @() lg.Tag);
cb2 = colorbar('Tag', 'bar');
u9b_chk('cb_tag_kept', @() cb2.Tag);
close(f);
f = figure('Visible', 'off');
plot(1:3);
cb = colorbar('Tag', 'given');
u9b_chk('cb_tag_given', @() cb.Tag);
lg = legend('a', 'Tag', 'given');
u9b_chk('lg_tag_given', @() lg.Tag);
close(f);

% --- 326: get of no handles ------------------------------------------------------------------------
u9b_chk('get_empty_type', @() get([], 'Type'));
u9b_chk('get_empty_tag', @() get([], 'Tag'));
u9b_chk('get_empty_all', @() get([]));
u9b_chk('get_empty_gobj', @() get(gobjects(0), 'Type'));
u9b_chk('get_empty_cell', @() get([], {'Type', 'Tag'}));
u9b_chk('get_empty_bogus', @() get([], 'Bogus'));
u9b_chk('get_empty_zeros', @() get(zeros(1, 0), 'Type'));
u9b_chk('get_empty_char', @() get('', 'Type'));

% --- 326: legend(ax) beside a group ----------------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
g = hggroup(ax);
line(g, [1 2], [1 2], 'DisplayName', 'm1');
line(g, [1 2], [2 1], 'DisplayName', 'm2');
line(ax, [1 2], [3 3], 'DisplayName', 'plain');
lg = legend(ax);
u9b_chk('lgg_string', @() lg.String);
u9b_chk('lgg_n', @() numel(lg.PlotChildren));
close(f);
f = figure('Visible', 'off');
ax = axes(f);
g = hggroup(ax, 'DisplayName', 'grp');
line(g, [1 2], [1 2], 'DisplayName', 'm1');
line(ax, [1 2], [3 3], 'DisplayName', 'plain');
g.Annotation.LegendInformation.IconDisplayStyle = 'on';
lg = legend(ax);
u9b_chk('lgg_on_string', @() lg.String);
u9b_chk('lgg_on_types', @() types(lg.PlotChildren));
close(f);
f = figure('Visible', 'off');
ax = axes(f);
g = hgtransform(ax);
line(g, [1 2], [1 2], 'DisplayName', 'm1');
plot(ax, [1 2], [3 3], 'DisplayName', 'plain');
lg = legend(ax);
u9b_chk('lgt_string', @() lg.String);
close(f);

% --- 326: a legend of a group through each engine's own figure file --------------------------------
f = figure('Visible', 'off');
ax = axes(f);
g = hggroup(ax);
line(g, [1 2], [1 2]);
line(g, [1 2], [2 1]);
line(ax, [1 2], [3 3]);
legend(g, 'grp');
fn = [tempname '.fig'];
savefig(f, fn);
close(f);
f = openfig(fn, 'invisible');
lg = findall(f, 'Type', 'legend');
u9b_chk('lgsave_string', @() lg.String);
u9b_chk('lgsave_types', @() types(lg.PlotChildren));
close(f);
delete(fn);

% --- 326: stem's 'filled' by any start of it -------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
filledFace = get(stem(ax, x, y, 'filled'), 'MarkerFaceColor');
cla(ax);
u9b_chk('stem_f', @() isequal(get(stem(ax, x, y, 'f'), 'MarkerFaceColor'), filledFace));
cla(ax);
u9b_chk('stem_fi', @() isequal(get(stem(ax, x, y, 'fi'), 'MarkerFaceColor'), filledFace));
cla(ax);
u9b_chk('stem_FILL', @() isequal(get(stem(ax, x, y, 'FILL'), 'MarkerFaceColor'), filledFace));
cla(ax);
u9b_chk('stem_fil_r', @() get(stem(ax, x, y, 'fil', 'r'), 'Color'));
cla(ax);
u9b_chk('stem_r_f', @() get(stem(ax, x, y, 'r', 'f'), 'Color'));
cla(ax);
u9b_chk('stem_y_f', @() numel(stem(ax, y, 'f')));
cla(ax);
u9b_chk('stem_filledx', @() numel(stem(ax, x, y, 'filledx')));
cla(ax);
u9b_chk('stem_f_lw', @() get(stem(ax, x, y, 'f', 'LineWidth', 2), 'LineWidth'));
cla(ax);
u9b_chk('stem_f_name', @() numel(stem(ax, x, y, 'f', 'LineWidth')));
close(f);

% --- 326: plot of a string -------------------------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
u9b_chk('plot_str', @() numel(plot("a", x, y)));
u9b_chk('plot_str_ax', @() numel(plot(ax, "a", x, y)));
u9b_chk('plot_char', @() numel(plot('a', x, y)));
u9b_chk('plot_str_only', @() numel(plot("a")));
u9b_chk('plot_strarr', @() numel(plot(["a" "b"], x, y)));
u9b_chk('semilogx_str', @() numel(semilogx("a", x, y)));
u9b_chk('loglog_str', @() numel(loglog("a", x, y)));
u9b_chk('plot3_str', @() numel(plot3("a", x, y, y)));
u9b_chk('plot_str_one', @() numel(plot("a", x)));
u9b_chk('plot_str_ax_one', @() numel(plot(ax, "a", x)));
cla(ax);
u9b_chk('plot_str_spec', @() numel(plot(ax, x, y, "r")));
u9b_chk('plot_str_pair', @() get(plot(ax, x, y, "Color", "r"), 'Color'));
u9b_chk('line_str', @() numel(line(ax, "XData", x, "YData", y)));
close(f);

% --- 326: xscale of a heatmap ----------------------------------------------------------------------
f = figure('Visible', 'off');
h = heatmap(f, magic(3));
u9b_chk('xscale_heatmap', @() xscale(h, 'log'));
u9b_chk('xscale_heatmap_get', @() xscale(h));
u9b_chk('yscale_heatmap', @() yscale(h, 'log'));
u9b_chk('zscale_heatmap', @() zscale(h));
u9b_chk('xscale_gca_heatmap', @() xscale('log'));
close(f);
f = figure('Visible', 'off');
p = uipanel(f);
u9b_chk('xscale_panel', @() xscale(p, 'log'));
close(f);

% --- 326: a table channel named by number ----------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
tbl = table([1;2;3], [4;5;6], [7;8;9]);
u9b_chk('tbl_num_y', @() get(scatter(ax, tbl, 1, 2), 'YVariable'));
cla(ax);
u9b_chk('tbl_num_x', @() get(scatter(ax, tbl, 1, 2), 'XVariable'));
cla(ax);
u9b_chk('tbl_num_ydata', @() get(scatter(ax, tbl, 1, 3), 'YData'));
cla(ax);
u9b_chk('tbl_numv_y', @() get(scatter(ax, tbl, 'Var1', [2 3]), 'YVariable'));
cla(ax);
u9b_chk('tbl_numv_x', @() get(scatter(ax, tbl, 'Var1', [2 3]), 'XVariable'));
cla(ax);
u9b_chk('tbl_char_y', @() get(scatter(ax, tbl, 'Var1', 'Var2'), 'YVariable'));
cla(ax);
u9b_chk('tbl_num_set', @() setget(scatter(ax, tbl, 1, 2), 'YVariable', 3));
cla(ax);
u9b_chk('tbl_name_set', @() setget(scatter(ax, tbl, 1, 2), 'YVariable', 'Var3'));
cla(ax);
u9b_chk('tbl_logical_x', @() get(scatter(ax, tbl, logical([1 0 0]), 2), 'XVariable'));
cla(ax);
u9b_chk('tbl_s3_z', @() get(scatter3(ax, tbl, 1, 2, 3), 'ZVariable'));
close(f);

% --- 327: a patch's CDataMode ----------------------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
p = patch(ax, [0 1 1], [0 0 1], [1; 2; 3]);
u9b_chk('pcd_mode0', @() p.CDataMode);
p.CDataMode = 'auto';
u9b_chk('pcd_mode_auto', @() p.CDataMode);
u9b_chk('pcd_cdata_after', @() p.CData);
u9b_chk('pcd_fvc_after', @() p.FaceVertexCData);
p.CDataMode = 'manual';
u9b_chk('pcd_mode_manual', @() p.CDataMode);
p.CDataMode = 'auto';
p.CData = [4; 5; 6];
u9b_chk('pcd_mode_set_cdata', @() p.CDataMode);
cla(ax);
p = patch(ax, [0 1 1], [0 0 1], 'r');
u9b_chk('pcd_r_mode', @() p.CDataMode);
p.CDataMode = 'auto';
u9b_chk('pcd_r_mode_auto', @() p.CDataMode);
cla(ax);
p = patch(ax, [0 1 1], [0 0 1], 5);
p.CDataMode = 'auto';
u9b_chk('pcd_s_mode_auto', @() {p.CDataMode, p.CData});
cla(ax);
p = patch(ax, 'XData', [0 1 1], 'YData', [0 0 1], 'CDataMode', 'auto');
u9b_chk('pcd_ctor_auto', @() p.CDataMode);
close(f);

% --- 327: a colour argument, then FaceColor --------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
p = patch(ax, [0 1 1], [0 0 1], 'b', 'FaceColor', 'flat');
u9b_chk('pfc_flat', @() {p.FaceColor, p.CData});
cla(ax);
p = patch(ax, [0 1 1], [0 0 1], 'b', 'FaceColor', 'r');
u9b_chk('pfc_r', @() p.FaceColor);
cla(ax);
p = patch(ax, [0 1 1], [0 0 1], 5, 'FaceColor', 'g');
u9b_chk('pfc_s_g', @() {p.FaceColor, p.CData});
cla(ax);
p = patch(ax, [0 1 1], [0 0 1], [1; 2; 3], 'FaceColor', 'none');
u9b_chk('pfc_v_none', @() {p.FaceColor, p.CData});
cla(ax);
p = fill(ax, [0 1 1], [0 0 1], 'b', 'FaceColor', 'flat');
u9b_chk('fill_fc_flat', @() {p.FaceColor, p.CData});
cla(ax);
p = fill(ax, [0 1 1], [0 0 1], 'b', 'FaceColor', 'r');
u9b_chk('fill_fc_r', @() p.FaceColor);
cla(ax);
p = patch(ax, [0 1 1], [0 0 1], 'b', 'EdgeColor', 'r');
u9b_chk('pec_b_r', @() {p.FaceColor, p.EdgeColor});
cla(ax);
p = patch(ax, [0 1 1], [0 0 1], 'b');
p.FaceColor = 'flat';
u9b_chk('pfc_set_flat', @() p.FaceColor);
p.FaceColor = 'g';
u9b_chk('pfc_set_back', @() p.FaceColor);
close(f);

% --- 327: a patch's and a fill's own edge ----------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
p = patch(ax, [0 1 1], [0 0 1], 'r');
u9b_chk('pedge_color', @() p.EdgeColor);
u9b_chk('pedge_width', @() p.LineWidth);
cla(ax);
p = fill(ax, [0 1 1], [0 0 1], 'r');
u9b_chk('fedge_color', @() p.EdgeColor);
u9b_chk('fedge_width', @() p.LineWidth);
cla(ax);
p = patch(ax, 'XData', [0 1 1], 'YData', [0 0 1]);
u9b_chk('pedge_nv_color', @() p.EdgeColor);
u9b_chk('pedge_nv_width', @() p.LineWidth);
cla(ax);
p = patch(ax, 'Faces', [1 2 3], 'Vertices', [0 0; 1 0; 1 1]);
u9b_chk('pedge_fv_color', @() p.EdgeColor);
cla(ax);
p = fill3(ax, [0 1 1], [0 0 1], [0 0 0], 'r');
u9b_chk('f3edge_color', @() p.EdgeColor);
u9b_chk('f3edge_width', @() p.LineWidth);
cla(ax);
p = patch(ax, [0 1 1], [0 0 1], [0 0 0], 'r');
u9b_chk('p3edge_color', @() p.EdgeColor);
cla(ax);
p = patch(ax, [0 1 1], [0 0 1], 'r', 'EdgeColor', 'b', 'LineWidth', 2);
u9b_chk('pedge_given', @() {p.EdgeColor, p.LineWidth});
close(f);

% --- 327: a surface's and a patch's marker edge ----------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
s = surf(ax, magic(3));
u9b_chk('surf_mec', @() s.MarkerEdgeColor);
u9b_chk('surf_mfc', @() s.MarkerFaceColor);
u9b_chk('surf_mec_none', @() setget(s, 'MarkerEdgeColor', 'none'));
u9b_chk('surf_mec_auto', @() setget(s, 'MarkerEdgeColor', 'auto'));
u9b_chk('surf_mec_red', @() setget(s, 'MarkerEdgeColor', 'r'));
u9b_chk('surf_mec_auto2', @() setget(s, 'MarkerEdgeColor', 'auto'));
cla(ax);
s = surface(ax, magic(3));
u9b_chk('surface_mec', @() s.MarkerEdgeColor);
cla(ax);
s = mesh(ax, magic(3));
u9b_chk('mesh_mec', @() s.MarkerEdgeColor);
cla(ax);
s = pcolor(ax, magic(3));
u9b_chk('pcolor_mec', @() s.MarkerEdgeColor);
cla(ax);
p = patch(ax, [0 1 1], [0 0 1], 'r');
u9b_chk('patch_mec', @() {p.MarkerEdgeColor, p.MarkerFaceColor});
u9b_chk('patch_mec_none', @() setget(p, 'MarkerEdgeColor', 'none'));
close(f);

% --- 327: histogram of nothing ---------------------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
h = histogram(ax, [], 3);
u9b_chk('hist_empty3_edges', @() h.BinEdges);
u9b_chk('hist_empty3_values', @() h.Values);
u9b_chk('hist_empty3_lim', @() h.BinLimits);
u9b_chk('hist_empty3_width', @() h.BinWidth);
cla(ax);
h = histogram(ax, []);
u9b_chk('hist_empty_edges', @() h.BinEdges);
cla(ax);
h = histogram(ax, [], 1);
u9b_chk('hist_empty1_edges', @() h.BinEdges);
cla(ax);
h = histogram(ax, zeros(1, 0), 5);
u9b_chk('hist_empty5_edges', @() h.BinEdges);
u9b_chk('histcounts_empty3', @() nthout(2, @histcounts, [], 3));
u9b_chk('histcounts_empty', @() nthout(2, @histcounts, []));
u9b_chk('histcounts_nan3', @() nthout(2, @histcounts, [NaN NaN], 3));
cla(ax);
h = histogram(ax, [], 3, 'BinLimits', [1 4]);
u9b_chk('hist_empty3_lims_edges', @() h.BinEdges);
cla(ax);
h = histogram(ax, NaN(1, 3), 3);
u9b_chk('hist_nan3_edges', @() h.BinEdges);
close(f);
close all force

function s = types(h)
s = strjoin(cellstr(get(h, 'Type'))', ' ');
end

function s = tags(h)
s = strjoin(cellstr(get(h, 'Tag'))', ' ');
end

function v = setget(h, name, value)
set(h, name, value);
v = get(h, name);
end

function v = nthout(k, fn, varargin)
out = cell(1, k);
[out{:}] = fn(varargin{:});
v = out{k};
end
