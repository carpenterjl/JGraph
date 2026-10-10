% Open item 82 (ADR 0220): get(h) and set(h) list R2025b's names and no others. The plot browser's
% Name on anything but a figure is refused; this build's other own names (ZOrder, Opacity, a
% Cartesian axes' polar settings) still answer when named, as R2025b's hidden ones do (the user's
% decision; those lines are divergences); a name R2025b hides (UIContextMenu, a figure's HitTest, a
% panel's ShadowColor) answers and is left out of get(h); a new figure is named ''. Every figure stays invisible. Probes probe_82, probe_82b, probe_82e and
% probe_82g (open-items scratch); the names are tools/matlab-checklist/graphics-probes/
% graphics_class_names.m.

f = figure('Visible', 'off');
ax = axes(f);
hold(ax, 'on');
ln = plot(ax, 1:3);
tx = text(ax, 1, 1, 'a');
pa = patch(ax, [0 1 1], [0 0 1], 'r');
sc = scatter(ax, 1:3, 1:3);
br = bar(ax, 1:3);
im = image(ax, magic(3));
lg = legend(ax);
pn = uipanel(f);

% --- the plot browser's Name, refused on anything but a figure
u9b_chk('isprop_name', @() [isprop(ax, 'Name'), isprop(ln, 'Name'), isprop(tx, 'Name'), isprop(lg, 'Name'), isprop(f, 'Name')]);
u9b_chk('get_name_axes', @() get(ax, 'Name'));
u9b_chk('set_name_line', @() as_statement(@() set(ln, 'Name', 'x')));

% --- this build's other names answer when named (divergences, ADR 0220)
u9b_chkdiv('isprop_own', @() [isprop(ax, 'ZOrder'), isprop(ln, 'Opacity'), ...
    isprop(ln, 'LegendLabel'), isprop(tx, 'Bold'), isprop(pa, 'EdgeWidth'), isprop(sc, 'ColorData'), ...
    isprop(br, 'FillColor'), isprop(im, 'Interpolate'), isprop(lg, 'TextStyle'), isprop(f, 'Size')], '0220');
u9b_chkdiv('isprop_polar_on_axes', @() [isprop(ax, 'RLim'), isprop(ax, 'ThetaDir'), isprop(ax, 'RAxis')], '0220');
u9b_chkdiv('get_own_axes', @() as_statement(@() get(ax, 'ZOrder')), '0220');
u9b_chk('get_unknown_figure', @() get(f, 'Bogus'));
u9b_chk('set_unknown_figure', @() as_statement(@() set(f, 'Bogus', 1)));
u9b_chk('get_unknown_text', @() get(tx, 'Bogus'));

% --- hidden names: answer, and are not listed
u9b_chk('isprop_hidden', @() [isprop(f, 'HitTest'), isprop(f, 'Selected'), isprop(ax, 'UIContextMenu'), ...
    isprop(ln, 'UIContextMenu'), isprop(tx, 'Text'), isprop(pn, 'ShadowColor'), isprop(pn, 'ResizeFcn')]);
u9b_chk('listed_hidden', @() [ismember('HitTest', fieldnames(get(f))), ...
    ismember('UIContextMenu', fieldnames(get(ax))), ismember('UIContextMenu', fieldnames(get(ln))), ...
    ismember('Text', fieldnames(get(tx))), ismember('ShadowColor', fieldnames(get(pn))), ...
    ismember('ResizeFcn', fieldnames(get(pn)))]);
u9b_chk('listed_own', @() [ismember('Name', fieldnames(get(ax))), ismember('ZOrder', fieldnames(get(ln))), ...
    ismember('Opacity', fieldnames(get(pa))), ismember('Size', fieldnames(get(f)))]);

% --- a start of a name several names share (a unique start is open item 86)
u9b_chk('prefix_ambiguous', @() get(ax, 'X'));
u9b_chk('prefix_one_letter', @() get(ln, 'C'));

% --- the polar names belong to a line in polar axes
pax = polaraxes(figure('Visible', 'off'));
pl = polarplot(pax, 1:3, 1:3);
u9b_chk('isprop_polar_line', @() [isprop(pl, 'RData'), isprop(pl, 'ThetaData')]);
u9b_chkdiv('isprop_polar_names_cartesian_line', @() isprop(ln, 'RData'), '0220');
u9b_chk('listed_polar_names_cartesian_line', @() ismember('RData', fieldnames(get(ln))));
u9b_chk('get_polar_line', @() get(pl, 'RData'));
u9b_chk('isprop_polar_axes', @() [isprop(pax, 'RLim'), isprop(pax, 'ThetaDir')]);

% --- open item 89: a group's parent may come first
g1 = hggroup(ax);
u9b_chk('hggroup_parent_first', @() {get(g1, 'Type'), isequal(get(g1, 'Parent'), ax)});
g2 = hgtransform(ax);
u9b_chk('hgtransform_parent_first', @() {get(g2, 'Type'), isequal(get(g2, 'Parent'), ax)});

% --- a new figure's Name
u9b_chk('figure_name', @() f.Name);
u9b_chk('figure_name_get', @() get(f, 'Name'));
u9b_chk('uifigure_name', @() get(uifigure('Visible', 'off'), 'Name'));
g = figure('Visible', 'off', 'Name', 'Results');
u9b_chk('figure_name_given', @() g.Name);
close all force;

function r = as_statement(fn)
% 'ran', or the refusal's identifier and sentence.
try
    fn();
    r = 'ran';
catch err
    r = [err.identifier ' | ' err.message];
end
end
