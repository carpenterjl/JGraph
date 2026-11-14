% record: -noFigureWindows
% Open items 296, 297 and 313 (ADR 0360). 296: a group's Visible is its own - a member keeps its own
% and is not drawn or fitted while its group is hidden - and a transform's Matrix moves its members'
% drawing and the limits, not their data; a group survives each engine's own figure file (probe p296).
% 297: findall(ax) reaches the axes' children and the labels a script has made, not its rulers,
% toolbar or grid; a legend and a colour bar are their figure's or panel's children, in front of their
% axes (probes p297, p297b). 313: legend of a group, cla keeping a group, the axes verbs given a
% deleted object, plot's value refusals, stem and stairs counting their words, text first in plot,
% scatter of a table with several variables, and xscale, yscale and zscale (probes p313, p313b).
x = 1:3; y = [2 1 3];

% --- 296: a group's Visible is its own -------------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
g = hggroup(ax, 'Tag', 'grp');
a = line(g, [1 2 3], [1 3 2], 'Tag', 'm1');
inner = hggroup(g, 'Tag', 'inner');
b = line(inner, [1 2], [5 6], 'Tag', 'm2');
g.Visible = 'off';
u2_chk('vis_group', @() char(g.Visible));
u2_chk('vis_member', @() char(a.Visible));
u2_chk('vis_inner', @() char(inner.Visible));
u2_chk('vis_deep', @() char(b.Visible));
u2_chk('vis_limits', @() [ax.XLim ax.YLim]);
a.Visible = 'off';
g.Visible = 'on';
u2_chk('vis_member_after', @() char(a.Visible));
u2_chk('vis_group_after', @() char(g.Visible));
u2_chk('vis_ylim_member_off', @() ax.YLim);
close(f);

% A transform keeps its members' data and holds the move in its Matrix.
f = figure('Visible', 'off');
ax = axes(f);
t = hgtransform(ax, 'Tag', 'xf');
l = line(t, [0 1], [0 0], 'Tag', 'tl');
t.Matrix = makehgtform('translate', [0 5 0]);
u2_chk('tf_data', @() [l.XData l.YData]);
u2_chk('tf_matrix', @() t.Matrix);
u2_chk('tf_limits', @() [ax.XLim ax.YLim]);
t.Matrix = makehgtform('scale', 3);
u2_chk('tf_scale', @() {ax.XLim, l.XData});
t.Matrix = makehgtform('zrotate', pi/2);
u2_chk('tf_rot_ylim', @() ax.YLim);
l.YData = [1 1];
u2_chk('tf_set_ydata', @() l.YData);
tx = text(t, 1, 0, 'hi', 'Tag', 'ttx');
u2_chk('tf_text_pos', @() tx.Position);
t.Matrix = eye(4);
l.Parent = ax;
u2_chk('tf_released_ydata', @() l.YData);
close(f);

% Nested transforms compose.
f = figure('Visible', 'off');
ax = axes(f);
t1 = hgtransform(ax);
t2 = hgtransform(t1);
line(t2, [0 1], [0 0]);
t1.Matrix = makehgtform('translate', [0 5 0]);
t2.Matrix = makehgtform('scale', 2);
u2_chk('nest_limits', @() [ax.XLim ax.YLim]);
close(f);

% Each engine's own file keeps the group, its Visible, its Matrix and the order of the axes' children.
f = figure('Visible', 'off');
ax = axes(f);
hold(ax, 'on');
plot(ax, 1:3, [1 3 2], 'Tag', 'plain');
g = hggroup(ax, 'Tag', 'grp', 'Visible', 'off');
line(g, [0 1], [0 1], 'Tag', 'inline');
inner = hggroup(g, 'Tag', 'inner');
line(inner, [0 1], [1 0], 'Tag', 'deep');
t = hgtransform(ax, 'Tag', 'xf');
line(t, [0 1], [0 0], 'Tag', 'tline');
t.Matrix = makehgtform('translate', [0 5 0]);
plot(ax, 1:3, [3 2 1], 'Tag', 'last');
tmp = [tempname '.fig'];
savefig(f, tmp);
close(f);
h = openfig(tmp, 'invisible');
hax = findobj(h, 'Type', 'axes');
u2_chk('file_children', @() get(hax.Children, 'Tag')');
u2_chk('file_types', @() get(hax.Children, 'Type')');
gg = findobj(h, 'Tag', 'grp');
u2_chk('file_group_vis', @() char(gg.Visible));
u2_chk('file_group_children', @() get(gg.Children, 'Tag')');
u2_chk('file_member_vis', @() char(get(findobj(h, 'Tag', 'inline'), 'Visible')));
tt = findobj(h, 'Tag', 'xf');
u2_chk('file_matrix', @() tt.Matrix);
u2_chk('file_tline_y', @() get(findobj(h, 'Tag', 'tline'), 'YData'));
u2_chk('file_deep_parent', @() get(get(findobj(h, 'Tag', 'deep'), 'Parent'), 'Tag'));
u2_chk('file_groups', @() numel(findobj(h, 'Type', 'hggroup')));
u2_chk('file_limits', @() [hax.YLim]);
delete(h);
delete(tmp);

% --- 297: what findall, findobj and allchild reach ------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
u2_chk('bare_findall_ax', @() get(findall(ax), 'Type'));
plot(ax, 1:3, [1 3 2], 'Tag', 'p');
u2_chk('plot_findall_ax', @() get(findall(ax), 'Type')');
u2_chk('plot_allchild_ax', @() get(allchild(ax), 'Type'));
hold(ax, 'on');
g = hggroup(ax, 'Tag', 'g');
line(g, [0 1], [0 1], 'Tag', 'gm');
legend(ax, 'show');
colorbar(ax);
grid(ax, 'on');
title(ax, 'T');
xlabel(ax, 'X');
u2_chk('full_findall_ax', @() get(findall(ax), 'Type')');
u2_chk('full_findall_ax_tag', @() get(findall(ax), 'Tag')');
u2_chk('full_findobj_ax', @() get(findobj(ax), 'Type')');
u2_chk('full_findobj_f', @() get(findobj(f), 'Type')');
u2_chk('full_allchild_ax', @() get(allchild(ax), 'Type')');
u2_chk('full_children_f', @() get(f.Children, 'Type')');
u2_chk('full_children_ax', @() get(ax.Children, 'Type')');
u2_chk('full_counts_ax', @() [numel(findall(ax, 'Type', 'line')), numel(findall(ax, 'Type', 'text')), ...
    numel(findall(ax, 'Type', 'legend')), numel(findall(ax, 'Type', 'colorbar')), ...
    numel(findall(ax, 'Type', 'numericruler')), numel(findall(ax, 'Type', 'axestoolbar'))]);
u2_chk('full_counts_f', @() [numel(findall(f, 'Type', 'text')), numel(findall(f, 'Type', 'legend')), ...
    numel(findall(f, 'Type', 'colorbar')), numel(findall(f, 'Type', 'numericruler')), numel(findall(f, 'Type', 'axestoolbar'))]);
u2_chk('full_findall_g', @() get(findall(g), 'Type')');
u2_chk('full_allchild_g', @() get(allchild(g), 'Type'));
u2_chk('full_findall_ax_depth1', @() get(findall(ax, '-depth', 1), 'Type')');
u2_chk('full_parents', @() {get(get(findall(f, 'Type', 'legend'), 'Parent'), 'Type'), ...
    get(get(findall(f, 'Type', 'colorbar'), 'Parent'), 'Type'), get(ax.Title.Parent, 'Type'), ...
    get(ax.XAxis.Parent, 'Type'), get(ax.Toolbar.Parent, 'Type')});
u2_chk('full_findall_legend', @() get(findall(findall(f, 'Type', 'legend')), 'Type'));
u2_chk('full_findall_colorbar', @() get(findall(findall(f, 'Type', 'colorbar')), 'Type'));
set(0, 'ShowHiddenHandles', 'on');
u2_chk('shh_findobj_ax', @() get(findobj(ax), 'Type')');
u2_chk('shh_children_ax', @() get(ax.Children, 'Type')');
set(0, 'ShowHiddenHandles', 'off');
close(f);

f = figure('Visible', 'off');
ax = axes(f);
surf(ax, peaks(5));
light(ax);
u2_chk('surf_findall_ax', @() get(findall(ax), 'Type')');
close(f);
f = figure('Visible', 'off');
pax = polaraxes(f);
polarplot(pax, 1:3, 1:3);
u2_chk('polar_findall_ax', @() get(findall(pax), 'Type')');
close(f);
f = figure('Visible', 'off');
ax = axes(f);
yyaxis(ax, 'left');
plot(ax, 1:3);
yyaxis(ax, 'right');
plot(ax, 3:-1:1);
u2_chk('yy_findall_ax', @() get(findall(ax), 'Type')');
close(f);

% The labels findall reaches: those a script made or reached, title first.
f = figure('Visible', 'off');
ax = axes(f);
plot(ax, 1:3, 'Tag', 'p');
xlabel(ax, 'X');
ax.XLabel.Tag = 'XL';
u2_chk('l_x', @() get(findall(ax), 'Tag')');
title(ax, 'T');
ax.Title.Tag = 'TT';
u2_chk('l_xt', @() get(findall(ax), 'Tag')');
s = ax.YLabel;
s.Tag = 'YL';
u2_chk('l_y_read', @() get(findall(ax), 'Tag')');
ax.ZLabel.String = '';
ax.ZLabel.Tag = 'ZL';
subtitle(ax, 'S');
ax.Subtitle.Tag = 'ST';
u2_chk('l_sub', @() get(findall(ax), 'Tag')');
u2_chk('l_findobj', @() get(findobj(ax), 'Tag')');
title(ax, '');
u2_chk('l_title_cleared', @() get(findall(ax), 'Tag')');
close(f);
f = figure('Visible', 'off');
ax = axes(f);
u2_chk('fresh_title_read', @() {ax.Title.String, numel(findall(ax))});
close(f);

% A legend and a colour bar stand in front of their axes among the figure's children.
f = figure('Visible', 'off');
ax1 = axes(f, 'Position', [0.1 0.1 0.3 0.3], 'Tag', 'a1');
plot(ax1, 1:3);
ax2 = axes(f, 'Position', [0.5 0.5 0.3 0.3], 'Tag', 'a2');
plot(ax2, 1:3);
legend(ax1, 'show');
colorbar(ax2);
legend(ax2, 'show');
u2_chk('two_children_f', @() get(f.Children, 'Type')');
u2_chk('two_findobj_f', @() get(findobj(f), 'Type')');
u2_chk('two_children_ax1', @() get(ax1.Children, 'Type'));
legend(ax1, 'off');
u2_chk('off_children_f', @() get(f.Children, 'Type')');
lg = legend(ax2);
lg.Visible = 'off';
u2_chk('hidden_children_f', @() get(f.Children, 'Type')');
u2_chk('hidden_findobj_f', @() numel(findobj(f, 'Type', 'legend')));
close(f);
f = figure('Visible', 'off');
p = uipanel(f);
ax = axes(p);
plot(ax, 1:3);
lg = legend(ax, 'show');
cb = colorbar(ax);
u2_chk('panel_parents', @() {get(lg.Parent, 'Type'), get(cb.Parent, 'Type')});
u2_chk('panel_children', @() get(p.Children, 'Type')');
close(f);
f = figure('Visible', 'off');
ax = axes(f);
plot(ax, 1:3);
lg = legend(ax, 'show');
u2_chk('lg_ancestors', @() {isempty(ancestor(lg, 'axes')), get(ancestor(lg, 'figure'), 'Type')});
cla(ax);
u2_chk('lg_after_cla', @() {isvalid(lg), get(f.Children, 'Type')'});
delete(ax);
u2_chk('lg_after_delete_ax', @() isvalid(lg));
close(f);

% --- 313: legend of a group -------------------------------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
plot(ax, x, y, 'Tag', 'p1');
hold(ax, 'on');
g = hggroup(ax, 'Tag', 'g');
line(g, [0 1], [1 0], 'Tag', 'gm1');
line(g, [0 1], [0 1], 'Tag', 'gm2');
u2_chk('lg_displayname0', @() g.DisplayName);
lg = legend(g, 'a');
u2_chk('lg_group', @() {lg.String, numel(lg.PlotChildren), get(lg.PlotChildren, 'Type'), get(lg.Parent, 'Type')});
u2_chk('lg_group_name', @() {g.DisplayName, char(g.Annotation.LegendInformation.IconDisplayStyle)});
delete(lg);
lg = legend(g);
u2_chk('lg_bare_string', @() lg.String);
delete(lg);
lg = legend([g; findobj(ax, 'Tag', 'p1')], 'grp', 'line');
u2_chk('lg_pair', @() {lg.String, get(lg.PlotChildren, 'Type')'});
plot(ax, 1:3, 'Tag', 'later');
u2_chk('lg_after_plot', @() lg.String);
delete(lg);
t = hgtransform(ax);
line(t, [0 1], [1 1]);
lg = legend(t, 'tt');
u2_chk('lg_transform', @() {lg.String, get(lg.PlotChildren, 'Type')});
u2_chk('lg_group_location', @() legend(g, 'a', 'Location', 'north').Location);
close(f);

% --- 313: cla keeping a group ---------------------------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
plot(ax, x, y, 'Tag', 'p1');
hold(ax, 'on');
g = hggroup(ax, 'Tag', 'g');
line(g, [0 1], [1 0], 'Tag', 'gm1');
text(ax, 1, 1, 'a', 'Tag', 'tx');
title(ax, 'T');
u2_chk('cla_g', @() isequal(cla(g), ax));
u2_chk('cla_g_after', @() {isvalid(g), numel(ax.Children), ax.Title.String, char(ax.NextPlot), get(ax.Children, 'Tag'), numel(g.Children)});
cla(ax, 'reset');
plot(ax, x, y, 'Tag', 'p1');
hold(ax, 'on');
g = hggroup(ax, 'Tag', 'g');
line(g, [0 1], [1 0], 'Tag', 'gm1');
xlim(ax, [0 10]);
cla(g, 'reset');
u2_chk('cla_g_reset_after', @() {isvalid(g), numel(ax.Children), ax.XLim, char(ax.XLimMode)});
cla(ax, 'reset');
g = hggroup(ax);
inner = hggroup(g);
line(inner, [0 1], [1 0]);
plot(ax, x, y);
u2_chk('cla_inner', @() cla(inner));
u2_chk('cla_inner_after', @() {isvalid(g), isvalid(inner), numel(ax.Children)});
cla(ax, 'reset');
hold(ax, 'on');
a = plot(ax, 1:3, 'Tag', 'a');
b = plot(ax, 1:3, 'Tag', 'b');
plot(ax, 1:3, 'Tag', 'c');
cla([a b]);
u2_chk('cla_two_after', @() get(ax.Children, 'Tag')');
u2_chk('cla_word', @() cla('foo'));
u2_chk('cla_reset_word', @() cla(a, 'resett'));
g = hggroup(ax);
delete(g);
plot(ax, 1:3, 'Tag', 'd');
u2_chk('cla_dead', @() cla(g));
u2_chk('cla_dead_after', @() numel(ax.Children));
close(f);

% --- 313: the axes verbs given a deleted group or axes -------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
g = hggroup(ax);
delete(g);
dax = axes(f);
delete(dax);
ax = axes(f);
verbs = {
    'title',       @(p) title(p, 'x')
    'subtitle',    @(p) subtitle(p, 'x')
    'xlabel',      @(p) xlabel(p, 'x')
    'ylabel',      @(p) ylabel(p, 'x')
    'zlabel',      @(p) zlabel(p, 'x')
    'hold_on',     @(p) hold(p, 'on')
    'grid_on',     @(p) grid(p, 'on')
    'box_on',      @(p) box(p, 'on')
    'axis_tight',  @(p) axis(p, 'tight')
    'xlim',        @(p) xlim(p, [0 1])
    'legend',      @(p) legend(p, 'a')
    'cla',         @(p) cla(p)
    'colorbar',    @(p) colorbar(p)
    'view',        @(p) view(p, 3)
    'xscale',      @(p) xscale(p, 'log')
    };
for k = 1:size(verbs, 1)
    u2_chk(['dead_g_' verbs{k, 1}], @() quiet(verbs{k, 2}, g));
    u2_chk(['dead_ax_' verbs{k, 1}], @() quiet(verbs{k, 2}, dax));
end
u2_chk('dead_left', @() {numel(ax.Children), ax.Title.String});
close(f);

% --- 313: plot's value refusals -------------------------------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
hold(ax, 'on');
tails = {
    {'LineWidth', 'abc'}
    {'Tag', 5}
    {'Visible', 'maybe'}
    {'Color', 'zz'}
    {'LineStyle', 'q'}
    {'Marker', 'w'}
    {'MarkerSize', -1}
    {'LineWidth', -1}
    {'Color', [2 0 0]}
    {'DisplayName', 5}
    {'r', 'LineWidth', 'abc'}
    {'LineWidth', 'abc', 'Tag', 5}
    {'Tag', 5, 'LineWidth', 'abc'}
    {'LineWidth', [1 2]}
    {'LineWidth', 0}
    {'LineWidth', Inf}
    {'LineWidth', true}
    {'LineWidth', "2"}
    {'MarkerSize', 'abc'}
    {'Color', [1 0]}
    {'Color', 5}
    {'LineStyle', 5}
    {'Marker', 5}
    {'Tag', {'a'}}
    {'DisplayName', {'a'}}
    {'LineW', 'abc'}
    {'linewidth', 'abc'}
    };
for k = 1:numel(tails)
    args = tails{k};
    u2_chk(sprintf('pv_%02d', k), @() numel(plot(ax, x, y, args{:})));
end
oks = {
    {'Visible', 2}
    {'Visible', true}
    {'Visible', 'ON'}
    {'Color', 'bl'}
    {'Color', 'blue'}
    {'Color', '#f00'}
    {'Color', 'none'}
    {'LineStyle', '-.'}
    {'Marker', 's'}
    {'Marker', 'sq'}
    };
for k = 1:numel(oks)
    args = oks{k};
    u2_chk(sprintf('pv_ok_%02d', k), @() numel(plot(ax, x, y, args{:})));
end
u2_chk('pv_semilogy', @() numel(semilogy(ax, x, y, 'LineWidth', 'abc')));
u2_chk('pv_semilogx', @() numel(semilogx(ax, x, y, 'Tag', 5)));
u2_chk('pv_loglog', @() numel(loglog(ax, x, y, 'Visible', 'maybe')));
u2_chk('pv_plot3', @() numel(plot3(ax, x, y, y, 'LineWidth', 'abc')));
u2_chk('pv_two_groups', @() numel(plot(ax, x, y, x, y, 'Color', 'zz')));
u9b_chkdiv('pv_left', @() numel(ax.Children), 'ADR0360'); % R2025b leaves the refused lines drawn; ADR 0255's take-back keeps the axes clean here
close(f);

% --- 313: stem and stairs count their words --------------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
hold(ax, 'on');
u2_chk('stem_spec_pair', @() numel(stem(ax, x, y, 'r', 'LineWidth', 2)));
u2_chk('stem_spec_name', @() numel(stem(ax, x, y, 'r', 'LineWidth')));
u2_chk('stem_name', @() numel(stem(ax, x, y, 'LineWidth')));
u2_chk('stem_spec_filled_pair', @() numel(stem(ax, x, y, 'filled', 'r', 'LineWidth', 2)));
u2_chk('stem_y_spec_pair', @() numel(stem(ax, y, 'r', 'LineWidth', 2)));
u2_chk('stem_spec_spec', @() numel(stem(ax, x, y, 'r', 'b')));
u2_chk('stem_pair_spec', @() numel(stem(ax, x, y, 'LineWidth', 2, 'r')));
u2_chk('stem_filled_name', @() numel(stem(ax, x, y, 'filled', 'LineWidth')));
u2_chk('stem_spec_filled_name', @() numel(stem(ax, x, y, 'r', 'filled', 'LineWidth')));
u2_chk('stem_bad_spec', @() numel(stem(ax, x, y, 'q')));
u2_chk('stem_bad_spec_pair', @() numel(stem(ax, x, y, 'q', 'LineWidth', 2)));
u2_chk('stem_three', @() numel(stem(ax, x, y, 'LineWidth', 2, 'Color')));
u2_chk('stem_noax_spec_name', @() numel(stem(x, y, 'r', 'LineWidth')));
u2_chk('stairs_spec_pair', @() numel(stairs(ax, x, y, 'r', 'LineWidth', 2)));
u2_chk('stairs_spec_name', @() numel(stairs(ax, x, y, 'r', 'LineWidth')));
u2_chk('stairs_name', @() numel(stairs(ax, x, y, 'LineWidth')));
u2_chk('stairs_y_spec_pair', @() numel(stairs(ax, y, 'r', 'LineWidth', 2)));
u2_chk('stairs_spec_spec', @() numel(stairs(ax, x, y, 'r', 'b')));
u2_chk('stairs_pair_spec', @() numel(stairs(ax, x, y, 'LineWidth', 2, 'r')));
u2_chk('stairs_bad_spec', @() numel(stairs(ax, x, y, 'q')));
u2_chk('stairs_three', @() numel(stairs(ax, x, y, 'LineWidth', 2, 'Color')));
u2_chk('stairs_y_name', @() numel(stairs(ax, x, 'LineWidth')));
u2_chk('stairs_spec_only', @() numel(stairs(ax, x, y, 'r')));
close(f);

% --- 313: text first in plot and its relatives ------------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
u2_chk('txt_ax', @() plot(ax, 'a', x, y));
u2_chk('txt_bare', @() plot('a', x, y));
u2_chk('txt_bare_one', @() plot('a'));
u2_chk('txt_ax_one', @() plot(ax, 'abc'));
u2_chk('txt_semilogy', @() semilogy('a', x, y));
u2_chk('txt_plot3', @() plot3('a', x, y, y));
u2_chk('txt_second', @() plot(x, 'a', y));
close(f);

% --- 313: scatter of a table with several variables -------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
tbl = table([1; 2; 3], [4; 5; 6], [7; 8; 9]);
s = scatter(ax, tbl, 'Var1', {'Var2', 'Var3'});
u2_chk('tbl_multi', @() {numel(s), {s.YVariable}, {s.XVariable}, {s.YData}, {s.DisplayName}});
u2_chk('tbl_multi_axes', @() {char(ax.NextPlot), isempty(ax.Legend)});
cla(ax, 'reset');
s = scatter(ax, tbl, {'Var1', 'Var2'}, 'Var3');
u2_chk('tbl_multix', @() {numel(s), {s.XVariable}});
cla(ax, 'reset');
s = scatter(ax, tbl, {'Var1', 'Var2'}, {'Var2', 'Var3'});
u2_chk('tbl_pair', @() {numel(s), {s.XVariable; s.YVariable}});
u2_chk('tbl_mismatch', @() scatter(ax, tbl, {'Var1', 'Var2'}, {'Var1', 'Var2', 'Var3'}));
cla(ax, 'reset');
s = scatter(ax, tbl, 'Var1', ["Var2", "Var3"]);
u2_chk('tbl_string', @() numel(s));
cla(ax, 'reset');
s = scatter(ax, tbl, 'Var1', [2 3]);
u2_chk('tbl_index', @() numel(s));
cla(ax, 'reset');
s = scatter3(ax, tbl, 'Var1', 'Var2', {'Var2', 'Var3'});
u2_chk('tbl3', @() {numel(s), {s.ZVariable}});
close(f);

% --- 313: xscale, yscale and zscale ---------------------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
plot(ax, 1:10);
u2_chk('xs_get', @() {xscale(ax), class(xscale(ax))});
xscale(ax, 'log');
yscale(ax, "log");
zscale(ax, 'log');
u2_chk('xs_set', @() {ax.XScale, ax.YScale, ax.ZScale, yscale(ax)});
u2_chk('xs_out', @() xscale(ax, 'linear'));
u2_chk('xs_three', @() xscale(ax, 'log', 'x'));
u2_chk('xs_bad_ax', @() xscale(5, 'log'));
u2_chk('xs_str_ax', @() xscale('log', 'linear'));
xscale linear
u2_chk('xs_cmd', @() get(gca, 'XScale'));
xscale('log');
u2_chk('xs_gca', @() {get(gca, 'XScale'), xscale()});
u2_chk('xs_vec_get', @() xscale([ax ax]));
st('xs_bad', @() xscale(ax, 'foo'));
st('xs_num', @() xscale(ax, 5));
st('xs_mode', @() xscale(ax, 'mode'));
st('xs_empty', @() xscale(ax, ''));
st('xs_cell', @() xscale(ax, {'linear'}));
u2_chk('xs_cell_after', @() ax.XScale);
st('xs_partial', @() xscale(ax, 'lo'));
u2_chk('xs_partial_after', @() ax.XScale);
st('xs_cap', @() xscale(ax, 'Linear'));
u2_chk('xs_cap_after', @() ax.XScale);
st('ys_bad', @() yscale(ax, 'foo'));
yyaxis(ax, 'right');
yscale(ax, 'log');
u2_chk('yy_right', @() {ax.YAxis(1).Scale, ax.YAxis(2).Scale, yscale(ax)});
yyaxis(ax, 'left');
u2_chk('yy_left', @() yscale(ax));
pax = polaraxes(f);
u2_chk('xs_polar', @() xscale(pax, 'log'));
u2_chk('ys_polar', @() yscale(pax));
close(f);
f = figure('Visible', 'off');
ax = axes(f);
plot(ax, datetime(2020, 1, 1:3), 1:3);
u2_chk('xs_datetime', @() xscale(ax));
st('xs_datetime_set', @() xscale(ax, 'log'));
cb = colorbar(ax);
u2_chk('xs_colorbar', @() xscale(cb));
st('ys_colorbar_set', @() yscale(cb, 'log'));
g = hggroup(ax);
st('xs_group', @() xscale(g, 'log'));
l = line(ax, 1:3, 1:3);
st('xs_line', @() xscale(l, 'log'));
close(f);

function r = quiet(make, p)
% What a verb given p answers, as text: made. A refusal propagates.
r = 'made';
make(p);
end

function st(name, fn)
% A statement call: what it refuses, or 'ok'.
try
    fn();
    fprintf('CHK|%s|ok|exact\n', name);
catch err
    fprintf('CHK|%s|%s|exact\n', name, err.identifier);
    fprintf('CHK|%s_msg|%s|exact\n', name, u2_text(err.message));
end
end
