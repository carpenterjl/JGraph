% record: -noFigureWindows
% Open items 136 and 137 (ADR 0255). 136: every maker takes any property of what it makes as a
% trailing 'Name', value option after its data and its line spec, a start of a name standing for the
% name; an unknown one is refused in R2025b's words and leaves nothing made; a CreateFcn given in the
% call runs after every other option (probes oi136_options, oi136_order, oi136_create). 137: a group
% or a transform first, or named by 'Parent', puts what is made in the group, drawn in the group's
% axes as though it were held; a member's Parent is its group, and a group's children are what was
% made in it, groups too (oi136_unheld, oi136_create).

f = figure('Visible', 'off');
ax = axes(f);
hold(ax, 'on');
x = 1:3; y = [2 1 3]; z = [1 2 1];
Z = [1 2; 3 4];
C = magic(3);
o = {'Tag', 't', 'UserData', 7};

% --- 136: any property of what is made, with an axes first -----------------------------------------
u2_chk('mk_plot', @() made(plot(ax, x, y, o{:})));
u2_chk('mk_plot_spec', @() made(plot(ax, x, y, 'r-o', o{:})));
u2_chk('mk_plot_y', @() made(plot(ax, y, o{:})));
u2_chk('mk_plot_y_spec', @() made(plot(ax, y, 'g', o{:})));
u2_chk('mk_plot_groups', @() made(plot(ax, x, y, 'r', x, z, 'b', o{:})));
u2_chk('mk_plot_groups_nospec', @() made(plot(ax, x, y, x, z, o{:})));
u2_chk('mk_plot3', @() made(plot3(ax, x, y, z, 'k:', o{:})));
u2_chk('mk_line', @() made(line(ax, x, y, z, o{:})));
u2_chk('mk_stairs', @() made(stairs(ax, x, y, 'r', o{:})));
u2_chk('mk_stem', @() made(stem(ax, x, y, 'filled', o{:})));
u2_chk('mk_stem3', @() made(stem3(ax, x, y, z, o{:})));
u2_chk('mk_scatter', @() made(scatter(ax, x, y, 20, 'r', o{:})));
u2_chk('mk_scatter_filled', @() made(scatter(ax, x, y, 'filled', o{:})));
u2_chk('mk_scatter3', @() made(scatter3(ax, x, y, z, o{:})));
u2_chk('mk_bar', @() made(bar(ax, y, o{:})));
u2_chk('mk_bar_width', @() made(bar(ax, y, 0.5, o{:})));
u2_chk('mk_bar_stacked', @() made(bar(ax, [y; z], 'stacked', o{:})));
u2_chk('mk_barh', @() made(barh(ax, y, o{:})));
u2_chk('mk_area', @() made(area(ax, x, y, o{:})));
u2_chk('mk_errorbar', @() made(errorbar(ax, x, y, z, o{:})));
u2_chk('mk_histogram', @() made(histogram(ax, [1 2 2 3 3 3], o{:})));
u2_chk('mk_fill', @() made(fill(ax, x, y, 'r', o{:})));
u2_chk('mk_patch', @() made(patch(ax, x, y, z, 'r', o{:})));
u2_chk('mk_rectangle', @() made(rectangle(ax, 'Position', [0 0 1 1], o{:})));
u2_chk('mk_light', @() made(light(ax, o{:})));
u2_chk('mk_text', @() made(text(ax, 1, 2, 'hi', o{:})));
u2_chk('mk_surface', @() made(surface(ax, [0 1; 0 1], [0 0; 1 1], Z, o{:})));
u2_chk('mk_image', @() made(image(ax, C, o{:})));
u2_chk('mk_quiver', @() made(quiver(ax, x, y, z, y, o{:})));
u2_chk('mk_semilogy', @() made(semilogy(ax, x, y, o{:})));
u9b_chkdiv('mk_pcolor', @() made(pcolor(ax, C, o{:})), '0255'); % pcolor's object is an image here, a surface in R2025b (open item 167)

% --- without an axes -------------------------------------------------------------------------------
cla(ax);
u2_chk('mk_bare_plot', @() made(plot(x, y, o{:})));
u2_chk('mk_bare_scatter', @() made(scatter(x, y, o{:})));
u2_chk('mk_bare_bar', @() made(bar(y, o{:})));
u2_chk('mk_bare_patch', @() made(patch(x, y, 'r', o{:})));
u2_chk('mk_bare_rectangle', @() made(rectangle('Position', [0 0 1 1], o{:})));
u2_chk('mk_bare_light', @() made(light(o{:})));
u2_chk('mk_bare_stem', @() made(stem(x, y, o{:})));
u2_chk('mk_bare_area', @() made(area(x, y, o{:})));

% --- the values arrive -----------------------------------------------------------------------------
cla(ax);
h = plot(ax, x, y, 'Visible', 'off', 'Clipping', 'off', 'SeriesIndex', 3, 'XData', [5 6 7]);
u2_chk('val_plot', @() {char(h.Visible), char(h.Clipping), h.SeriesIndex, h.XData});
h = scatter(ax, x, y, 'MarkerFaceAlpha', 0.5);
u2_chk('val_scatter', @() h.MarkerFaceAlpha);
h = bar(ax, y, 'BarWidth', 0.3, 'FaceColor', 'r');
u2_chk('val_bar', @() {h.BarWidth, h.FaceColor});
h = stem(ax, x, y, 'BaseValue', 1);
u2_chk('val_stem', @() h.BaseValue);
h = patch(ax, x, y, 'r', 'FaceAlpha', 0.5, 'Tag', 'pa');
u2_chk('val_patch', @() {h.FaceAlpha, h.Tag});
h = rectangle(ax, 'Position', [0 0 1 1], 'Curvature', 1);
u2_chk('val_rectangle', @() h.Curvature);
h = light(ax, 'Visible', 'off');
u2_chk('val_light', @() char(h.Visible));

% --- a start of a name, a name twice, the maker's own around another ---------------------------------
h = plot(ax, x, y, 'Ta', 'pt', 'UserD', 3);
u2_chk('start_names', @() {h.Tag, h.UserData});
h = plot(ax, x, y, 'Color', 'r', 'Color', 'b', 'Tag', 'a', 'Tag', 'b');
u2_chk('twice', @() {h.Color, h.Tag});
h = plot(ax, x, y, 'r', 'Color', 'g');
u2_chk('spec_then_color', @() h.Color);
h = plot(ax, x, y, 'LineWidth', 3, 'Tag', 'w', 'LineWidth', 4);
u2_chk('own_around_other', @() {h.LineWidth, h.Tag});
h = bar(ax, y, 'BarWidth', 0.2, 'Tag', 'w', 'BarWidth', 0.4);
u2_chk('bar_own_around_other', @() {h.BarWidth, h.Tag});

% --- names that are not right ----------------------------------------------------------------------
cla(ax);
plot(ax, x, y);
u2_chk('bad_plot', @() refused(@() plot(ax, x, y, 'Foo', 1)));
u2_chk('bad_ambiguous', @() refused(@() plot(ax, x, y, 'Mark', 1)));
u2_chk('bad_line', @() refused(@() line(ax, x, y, 'Foo', 1)));
u2_chk('bad_scatter', @() refused(@() scatter(ax, x, y, 'Tag', 'ok', 'Foo', 1)));
u2_chk('bad_bar', @() refused(@() bar(ax, y, 'Foo', 1)));
u2_chk('bad_patch', @() refused(@() patch(ax, x, y, 'r', 'Foo', 1)));
u2_chk('bad_rectangle', @() refused(@() rectangle(ax, 'Foo', 1)));
u2_chk('bad_light', @() refused(@() light(ax, 'Foo', 1)));
u2_chk('bad_stem', @() refused(@() stem(ax, x, y, 'Foo', 1)));
u2_chk('bad_stairs', @() refused(@() stairs(ax, x, y, 'Foo', 1)));
u2_chk('bad_left_nothing', @() numel(ax.Children));
u2_chk('bad_spec', @() refused(@() plot(ax, x, y, 'Tag')));

% --- CreateFcn after every other option --------------------------------------------------------------
global oi136_seen
oi136_seen = {};
plot(ax, x, y, 'CreateFcn', @(h, e) seen(h), 'Tag', 'after', 'UserData', 5);
bar(ax, y, 'Tag', 'before', 'CreateFcn', @(h, e) seen(h));
line(ax, x, y, 'CreateFcn', @(h, e) seen(h), 'Tag', 'lineafter');
u2_chk('create_fcn', @() oi136_seen);

% --- 'Parent' among the options ------------------------------------------------------------------------
cla(ax);
g = hggroup(ax);
u2_chk('parent_plot', @() parentType(plot(x, y, 'Parent', g)));
u2_chk('parent_bar', @() parentType(bar(y, 'Parent', g)));
u2_chk('parent_scatter', @() parentType(scatter(x, y, 'Parent', g)));
u2_chk('parent_axes', @() parentType(plot(x, y, 'Parent', ax)));
f2 = figure('Visible', 'off');
ax2 = axes(f2);
u2_chk('parent_wins', @() {isequal(get(plot(ax, x, y, 'Parent', ax2), 'Parent'), ax2), isequal(get(line(ax, x, y, 'Parent', ax2), 'Parent'), ax2)});
close(f2);

% --- 137: a group or a transform first -------------------------------------------------------------
cla(ax);
g = hggroup(ax);
t = hgtransform(ax);
u2_chk('grp_line', @() made(line(g, [0 1], [1 0], 'Tag', 'lg')));
u2_chk('grp_line_props', @() made(line(g, 'XData', [0 1], 'YData', [1 0], 'Tag', 'lgp')));
u2_chk('grp_line_t', @() made(line(t, [0 1], [1 0], 'Tag', 'lt')));
u2_chk('grp_text', @() made(text(g, 1, 2, 'hi', 'Tag', 'tg')));
u2_chk('grp_text_t', @() made(text(t, 1, 2, 3, 'hi', 'Tag', 'tt')));
u2_chk('grp_patch', @() made(patch(g, x, y, 'r', 'Tag', 'pg')));
u2_chk('grp_surface', @() made(surface(g, [0 1; 0 1], [0 0; 1 1], Z, 'Tag', 'sg')));
u2_chk('grp_surface_t', @() made(surface(t, Z, 'Tag', 'st')));
u2_chk('grp_rectangle', @() made(rectangle(g, 'Position', [0 0 1 1], 'Tag', 'rg')));
u2_chk('grp_image', @() made(image(g, C, 'Tag', 'ig')));
u2_chk('grp_image_props', @() made(image(t, 'CData', C, 'Tag', 'it')));
u2_chk('grp_light', @() made(light(g, 'Tag', 'lig')));
u2_chk('grp_hggroup', @() made(hggroup(g, 'Tag', 'gg')));
u2_chk('grp_hgtransform', @() made(hgtransform(g, 'Tag', 'tg2')));
u2_chk('grp_plot', @() made(plot(g, x, y)));
u2_chk('grp_scatter', @() made(scatter(g, x, y)));
u2_chk('grp_bar', @() made(bar(g, y)));
u2_chk('grp_fill', @() made(fill(g, x, y, 'r')));
u2_chk('grp_imagesc', @() made(imagesc(g, C)));
u2_chk('grp_stairs', @() made(stairs(g, x, y)));
u2_chk('grp_children', @() {numel(g.Children), numel(t.Children)});

% --- drawn as though held --------------------------------------------------------------------------
makers = {
    'plot',      @(p) plot(p, x, y)
    'plot3',     @(p) plot3(p, x, y, y)
    'semilogy',  @(p) semilogy(p, x, y)
    'stairs',    @(p) stairs(p, x, y)
    'stem',      @(p) stem(p, x, y)
    'stem3',     @(p) stem3(p, x, y, y)
    'scatter',   @(p) scatter(p, x, y)
    'bar',       @(p) bar(p, y)
    'barh',      @(p) barh(p, y)
    'area',      @(p) area(p, x, y)
    'errorbar',  @(p) errorbar(p, x, y, y)
    'histogram', @(p) histogram(p, [1 2 2])
    'fill',      @(p) fill(p, x, y, 'r')
    'fill3',     @(p) fill3(p, x, y, y, 'r')
    'imagesc',   @(p) imagesc(p, C)
    'image',     @(p) image(p, C)
    'line',      @(p) line(p, x, y)
    'patch',     @(p) patch(p, x, y, 'r')
    'surface',   @(p) surface(p, C)
    'text',      @(p) text(p, 1, 1, 'a')
    'rectangle', @(p) rectangle(p)
    'light',     @(p) light(p)
    };
for k = 1:size(makers, 1)
    for kind = {'hggroup', 'hgtransform'}
        cla(ax, 'reset');
        hold(ax, 'off');
        plot(ax, x, y, 'Tag', 'before');
        if strcmp(kind{1}, 'hggroup'), g = hggroup(ax); else, g = hgtransform(ax); end
        h = makers{k, 2}(g);
        u2_chk(['held_' makers{k, 1} '_' kind{1}], @() {get(get(h(1), 'Parent'), 'Type'), isvalid(g), numel(g.Children), ...
            numel(findobj(ax, 'Tag', 'before')), char(ax.NextPlot)});
    end
end

% --- a group's children, and a member given another parent ---------------------------------------------
cla(ax, 'reset');
hold(ax, 'on');
g = hggroup(ax);
a = line(g, x, y, 'Tag', 'first');
b = text(g, 1, 1, 'b', 'Tag', 'second');
c = hggroup(g, 'Tag', 'third');
light(g, 'Tag', 'fourth');
u2_chk('kids', @() get(g.Children, 'Tag')');
set(b, 'Parent', ax);
u2_chk('kids_text_left', @() {get(g.Children, 'Tag')', get(get(b, 'Parent'), 'Type')});
set(a, 'Parent', c);
u2_chk('kids_line_moved', @() {get(g.Children, 'Tag')', get(c.Children, 'Tag'), get(get(a, 'Parent'), 'Tag')});
close(f);

function r = made(h)
r = {numel(h), get(h(1), 'Type'), get(h(1), 'Tag'), get(h(1), 'UserData'), get(get(h(1), 'Parent'), 'Type')};
end

function r = parentType(h)
r = get(get(h, 'Parent'), 'Type');
end

function r = refused(make)
try
    make();
    r = 'made';
catch e
    r = ['refused ' e.identifier ' ' e.message];
end
end

function seen(h)
global oi136_seen
oi136_seen{end + 1} = [get(h, 'Type') ':' get(h, 'Tag')];
end
