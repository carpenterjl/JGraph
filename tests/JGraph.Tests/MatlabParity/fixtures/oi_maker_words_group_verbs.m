% record: -noFigureWindows
% Open items 167, 168 and 198. 167: R2025b's pcolor makes a flat surface, its CData the colours,
% the view flat, the box on and both limits set when the axes is not held (probe pc1). 168: the axes
% verbs refuse a group or a transform first in their own words (gv2, gv3); a three-element row given
% to patch is one colour (pl1); plot reads a malformed call in its own words before it draws (pl1-pl3);
% a deleted parent is refused, not read as data (dl1). 198: scatter and scatter3 of a table name the
% variable behind each channel (sc1).

f = figure('Visible', 'off');
ax = axes(f);
C = magic(4);

% --- 167: pcolor is a surface ------------------------------------------------------------------------
h = pcolor(ax, C);
u2_chk('pc_type', @() h.Type);
u2_chk('pc_faces', @() {h.FaceColor, h.EdgeColor, h.LineStyle, h.LineWidth, h.FaceAlpha, h.EdgeAlpha});
u2_chk('pc_look', @() {h.FaceLighting, h.Marker, h.MeshStyle, char(h.AlignVertexCenters), h.CDataMapping});
u2_chk('pc_data', @() {h.XData, h.YData, h.ZData, h.CData});
u2_chk('pc_modes', @() {h.XDataMode, h.YDataMode, h.CDataMode});
u2_chk('pc_axes', @() {ax.View, char(ax.Box), ax.XLim, ax.YLim, ax.XLimMode, ax.YLimMode, ax.ZLim, ax.CLim, char(ax.XGrid)});
u2_chk('pc_found', @() {numel(findobj(ax, 'Type', 'surface')), numel(findobj(ax, 'Type', 'image')), numel(ax.Children)});
shading(ax, 'flat');
u2_chk('pc_shading_flat', @() {h.EdgeColor, h.FaceColor});
shading(ax, 'interp');
u2_chk('pc_shading_interp', @() {h.EdgeColor, h.FaceColor});
shading(ax, 'faceted');
u2_chk('pc_shading_faceted', @() {h.EdgeColor, h.FaceColor});

cla(ax, 'reset');
h = pcolor(ax, [1 2 4], [10 20], [1 2 3; 4 5 6]);
u2_chk('pc_vectors', @() {h.XData, h.YData, h.ZData, h.CData, ax.XLim, ax.YLim, ax.View, h.XDataMode});
cla(ax, 'reset');
[X, Y] = meshgrid([0 1 3], [0 2]);
h = pcolor(ax, X, Y, [1 2 3; 4 5 6]);
u2_chk('pc_matrices', @() {h.XData, h.YData, ax.XLim, ax.YLim});
cla(ax, 'reset');
h = pcolor(ax, [5 5], [1 2], [1 2; 3 4]);
u2_chk('pc_no_span', @() {ax.XLim, ax.YLim});

cla(ax, 'reset');
plot(ax, [0 10], [0 10]);
hold(ax, 'on');
view(ax, 3);
pcolor(ax, C);
u2_chk('pc_held', @() {ax.View, ax.XLimMode, numel(ax.Children)});
hold(ax, 'off');

cla(ax, 'reset');
h = pcolor(ax, C, 'EdgeColor', 'none', 'Tag', 'p', 'FaceA', 0.5);
u2_chk('pc_options', @() {h.EdgeColor, h.Tag, h.FaceAlpha});
u2_chk('pc_bad_option', @() pcolor(ax, C, 'Foo', 1));
u2_chk('pc_bad_option_left', @() numel(ax.Children));
u2_chk('pc_vector', @() pcolor(ax, [1 2 3]));
u2_chk('pc_mismatch', @() pcolor(ax, 1:3, 1:2, magic(3)));
u2_chk('pc_complex', @() pcolor(ax, [1 2; 3 4i]));
u2_chk('pc_none', @() pcolor());
u2_chk('pc_two', @() pcolor(ax, C, C));
g = hggroup(ax);
u2_chk('pc_group', @() pcolor(g, C));

% --- 168: the axes verbs given a group or a transform ---------------------------------------------------
verbs = {
    'title',       @(p) title(p, 'x')
    'subtitle',    @(p) subtitle(p, 'x')
    'xlabel',      @(p) xlabel(p, 'x')
    'ylabel',      @(p) ylabel(p, 'x')
    'zlabel',      @(p) zlabel(p, 'x')
    'title_pairs', @(p) title(p, 'a', 'Color', 'r')
    'hold_on',     @(p) hold(p, 'on')
    'hold_bare',   @(p) hold(p)
    'ishold',      @(p) ishold(p)
    'grid_on',     @(p) grid(p, 'on')
    'grid_bare',   @(p) grid(p)
    'box_on',      @(p) box(p, 'on')
    'box_bare',    @(p) box(p)
    'axis_tight',  @(p) axis(p, 'tight')
    'axis_lim',    @(p) axis(p, [0 1 0 1])
    'colorbar',    @(p) colorbar(p)
    'xlim',        @(p) xlim(p, [0 1])
    'xlim_q',      @(p) xlim(p)
    'ylim',        @(p) ylim(p, [0 1])
    'zlim',        @(p) zlim(p, [0 1])
    'clim',        @(p) clim(p, [0 1])
    'caxis',       @(p) caxis(p, [0 1])
    'xticks',      @(p) xticks(p, [0 1])
    'yticks',      @(p) yticks(p, [0 1])
    'zticks',      @(p) zticks(p, [0 1])
    'xticklabels', @(p) xticklabels(p, {'a', 'b'})
    'yticklabels', @(p) yticklabels(p, {'a', 'b'})
    'xtickangle',  @(p) xtickangle(p, 45)
    'ytickangle',  @(p) ytickangle(p, 45)
    'xtickformat', @(p) xtickformat(p, '%.1f')
    'ytickformat', @(p) ytickformat(p, '%.1f')
    'rlim',        @(p) rlim(p, [0 1])
    'view',        @(p) view(p, 3)
    'colormap',    @(p) colormap(p, 'gray')
    'daspect',     @(p) daspect(p, [1 1 1])
    'daspect_q',   @(p) daspect(p)
    'pbaspect',    @(p) pbaspect(p, [1 1 1])
    'shading',     @(p) shading(p, 'flat')
    'lighting',    @(p) lighting(p, 'gouraud')
    'xline',       @(p) xline(p, 1)
    'yline',       @(p) yline(p, 1)
    'colororder',  @(p) colororder(p, [1 0 0])
    'camlight',    @(p) camlight(p)
    'axes',        @(p) axes(p)
    'text',        @(p) get(get(text(p, 0, 0, 'a'), 'Parent'), 'Type')
    };
for k = 1:size(verbs, 1)
    for kind = {'g', 't'}
        cla(ax, 'reset');
        plot(ax, 1:3, [2 1 3]);
        hold(ax, 'on');
        if strcmp(kind{1}, 'g'), p = hggroup(ax); else, p = hgtransform(ax); end
        line(p, [0 1], [1 0]);
        u2_chk(['gv_' verbs{k, 1} '_' kind{1}], @() quiet(verbs{k, 2}, p));
        u2_chk(['gv_' verbs{k, 1} '_' kind{1} '_after'], @() {isvalid(p), numel(ax.Children), char(ax.NextPlot), ax.Title.String});
    end
end

% --- 168: a three-element row given to patch is one colour ----------------------------------------------
cla(ax, 'reset');
hold(ax, 'on');
u2_chk('patch_row', @() pinfo(patch(ax, [0 1 0], [0 0 1], [1 0 0])));
u2_chk('patch_row_columns', @() pinfo(patch(ax, [0 1 0]', [0 0 1]', [1 0 0])));
u2_chk('patch_column', @() pinfo(patch(ax, [0 1 0], [0 0 1], [1 0 0]')));
u2_chk('patch_fraction', @() pinfo(patch(ax, [0 1 0], [0 0 1], [0.2 0.4 0.6])));
u2_chk('patch_out', @() pinfo(patch(ax, [0 1 0], [0 0 1], [1 2 3])));
u2_chk('patch_out_column', @() pinfo(patch(ax, [0 1 0]', [0 0 1]', [1 2 3]')));
u2_chk('patch_negative', @() pinfo(patch(ax, [0 1 0], [0 0 1], [-1 0 0])));
u2_chk('patch_four', @() pinfo(patch(ax, [0 1 1 0], [0 0 1 1], [1 2 3 4])));
u2_chk('patch_four_row3', @() pinfo(patch(ax, [0 1 1 0], [0 0 1 1], [1 0 0])));
u2_chk('patch_z_row', @() pinfo(patch(ax, [0 1 0], [0 0 1], [0 0 0], [1 0 0])));
u2_chk('patch_z_out', @() pinfo(patch(ax, [0 1 0], [0 0 1], [0 0 0], [1 2 3])));
u2_chk('patch_two_faces', @() pinfo(patch(ax, [0 1 0; 1 1 0]', [0 0 1; 0 1 1]', [1 0 0])));
u2_chk('fill_row', @() pinfo(fill(ax, [0 1 0], [0 0 1], [1 0 0])));
u2_chk('fill_out', @() pinfo(fill(ax, [0 1 0], [0 0 1], [1 2 3])));
u2_chk('patch_uint8', @() pinfo(patch(ax, [0 1 0], [0 0 1], uint8([255 0 0]))));
u2_chk('patch_logical', @() pinfo(patch(ax, [0 1 0], [0 0 1], logical([1 0 0]))));

% --- 168: plot's own words for a malformed call ------------------------------------------------------
cla(ax, 'reset');
hold(ax, 'on');
x = 1:3; y = [2 1 3];
tails = {
    {'Tag', 'a', 'r'}
    {'LineWidth', 2, 'r'}
    {'Tag', 'a', x, y}
    {'LineWidth', 2, x, y}
    {'r', 'Tag', 'a', x, y, 'b'}
    {'Type', 'x'}
    {'BeingDeleted', 'on'}
    {'Tag'}
    {'r', 'Tag'}
    {'Tag', 'a', 'Color'}
    {'r', 'b'}
    {'Tag', 'Color', 'r'}
    {'r', 'b', 'Tag', 'a'}
    {'r', 'Tag', 'a'}
    {'b', 'Tag', 'a', 'r'}
    {'Foo', 'a', 'r'}
    {'r', 'b', 'c'}
    {'Tag', 'r'}
    {'-', 'b'}
    {'Tag', 'a', 'r', 'b'}
    {'Color', 'r', 'b'}
    {'LineWidth', 2, 'r', 'Tag', 'a'}
    {'r', 2}
    {'Tag', 2, 'r'}
    {'Tag', 'a', 2}
    {'r', x, y, 'b', 'Tag', 'a'}
    {'r', x, 'Tag', 'a'}
    {'ro', 'Tag', 'a', 'b'}
    {'Tag', 'r', 'Foo'}
    {'Type', 'x', 'r'}
    {'r', 'Type', 'x'}
    {'Type', 'x', 'Foo', 1}
    {'Ty', 'x'}
    {x}
    {'r', x, y}
    };
for k = 1:numel(tails)
    args = tails{k};
    u2_chk(sprintf('plot_tail_%02d', k), @() madeLines(plot(ax, x, y, args{:})));
end
u2_chk('plot_y_spec_xy', @() madeLines(plot(ax, y, 'r', x, y)));
u2_chk('semilogy_pair_spec', @() madeLines(semilogy(ax, x, y, 'Tag', 'a', 'r')));
u2_chk('semilogy_data_after', @() madeLines(semilogy(ax, x, y, 'Tag', 'a', x, y)));
u2_chk('semilogx_readonly', @() madeLines(semilogx(ax, x, y, 'Type', 'x')));
u2_chk('loglog_pair_spec', @() madeLines(loglog(ax, x, y, 'Tag', 'a', 'r')));
u2_chk('plot3_pair_spec', @() madeLines(plot3(ax, x, y, y, 'Tag', 'a', 'r')));
u2_chk('plot3_readonly', @() madeLines(plot3(ax, x, y, y, 'Type', 'x')));
u2_chk('line_readonly', @() madeLines(line(ax, x, y, 'Type', 'x')));
u2_chk('bar_readonly', @() madeLines(bar(ax, y, 'Type', 'x')));
u2_chk('stem_readonly', @() madeLines(stem(ax, x, y, 'Type', 'x')));

% --- 168: a deleted parent ----------------------------------------------------------------------------
makers = {
    'line',        @(p) line(p, x, y)
    'line_props',  @(p) line(p, 'XData', x, 'YData', y)
    'text',        @(p) text(p, 1, 1, 'a')
    'patch',       @(p) patch(p, x, y, 'r')
    'surface',     @(p) surface(p, C)
    'image',       @(p) image(p, C)
    'light',       @(p) light(p)
    'rectangle',   @(p) rectangle(p)
    'hggroup',     @(p) hggroup(p)
    'hgtransform', @(p) hgtransform(p)
    'plot',        @(p) plot(p, x, y)
    'plot3',       @(p) plot3(p, x, y, y)
    'semilogy',    @(p) semilogy(p, x, y)
    'stairs',      @(p) stairs(p, x, y)
    'stem',        @(p) stem(p, x, y)
    'scatter',     @(p) scatter(p, x, y)
    'bar',         @(p) bar(p, y)
    'area',        @(p) area(p, x, y)
    'errorbar',    @(p) errorbar(p, x, y, y)
    'histogram',   @(p) histogram(p, [1 2 2])
    'fill',        @(p) fill(p, x, y, 'r')
    'imagesc',     @(p) imagesc(p, C)
    'parent_line', @(p) line(x, y, 'Parent', p)
    'parent_plot', @(p) plot(x, y, 'Parent', p)
    'parent_bar',  @(p) bar(y, 'Parent', p)
    'parent_text', @(p) text(1, 1, 'a', 'Parent', p)
    };
cla(ax, 'reset');
hold(ax, 'on');
for k = 1:size(makers, 1)
    for kind = {'hggroup', 'hgtransform'}
        if strcmp(kind{1}, 'hggroup'), p = hggroup(ax); else, p = hgtransform(ax); end
        delete(p);
        n0 = numel(ax.Children);
        u2_chk(['dead_' makers{k, 1} '_' kind{1}], @() quiet(makers{k, 2}, p));
        u2_chk(['dead_' makers{k, 1} '_' kind{1} '_left'], @() numel(ax.Children) - n0);
    end
end

% --- 198: scatter of a table --------------------------------------------------------------------------
cla(ax, 'reset');
tbl = table([1; 2; 3], [4; 5; 6], [7; 8; 9], 'VariableNames', {'Var1', 'Var2', 'Var3'});
s = scatter(ax, tbl, 'Var1', 'Var2');
u2_chk('tbl_scatter', @() {s.Type, s.XData, s.YData, s.XVariable, s.YVariable, size(s.SourceTable)});
s = scatter(ax, tbl, 'Var1', 'Var2', 'filled', 'Tag', 'k');
u2_chk('tbl_scatter_options', @() {s.Tag, s.MarkerFaceColor});
s = scatter(ax, tbl, 'Var1', 'Var2', 'ColorVariable', 'Var3');
u2_chk('tbl_scatter_color', @() s.ColorVariable);
s = scatter(ax, tbl, 'Var1', 'Var2', 'SizeVariable', 'Var3');
u2_chk('tbl_scatter_size', @() s.SizeVariable);
s = scatter3(ax, tbl, 'Var1', 'Var2', 'Var3');
u2_chk('tbl_scatter3', @() {s.XVariable, s.YVariable, s.ZVariable, s.ZData});
close(f);

function r = quiet(make, p)
% What a verb given p answers, as text: made, or the type of what it made. A refusal propagates.
r = 'made';
make(p);
end

function r = pinfo(h)
fc = h.FaceColor;
if ~ischar(fc), fc = mat2str(fc, 4); end
r = {fc, mat2str(size(h.CData)), mat2str(size(h.FaceVertexCData))};
end

function r = madeLines(h)
r = {numel(h), get(h(1), 'Tag')};
end
