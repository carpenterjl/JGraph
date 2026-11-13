% record: -noFigureWindows
% Open items 143, 144 and 166 (ADR 0342). 144 and 166: a group or a transform is a child of its axes
% like any other - findobj and findall reach it and through it its members, the axes' Children list
% it and not its members, a replacing plot, cla and cla reset delete it, and delete takes its members
% with it (probe pa_groups). 143: a figure's annotation answers R2025b's shape word for its Type and
% sits in a pane whose handle is hidden, so findobj passes it by and findall reaches it; a plain clf
% deletes the annotations and keeps the pane, clf reset and delete of the pane take both (probes
% pa_groups, pa_pane).
x = 1:3;
y = [1 3 2];

% --- 144: findobj and findall reach a group ---------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
g = hggroup(ax, 'Tag', 'group');
t = hgtransform(ax, 'Tag', 'xform');
u2_chk('g_findobj_tag', @() numel(findobj(f, 'Tag', 'group')));
u2_chk('g_findobj_type', @() numel(findobj(f, 'Type', 'hggroup')));
u2_chk('g_findall_tag', @() numel(findall(f, 'Tag', 'group')));
u2_chk('t_findobj_tag', @() numel(findobj(f, 'Tag', 'xform')));
u2_chk('t_findobj_type', @() numel(findobj(f, 'Type', 'hgtransform')));
u2_chk('t_findall_type', @() numel(findall(f, 'Type', 'hgtransform')));
u2_chk('g_is_found', @() isequal(findobj(f, 'Tag', 'group'), g));
u2_chk('g_ax_children_empty', @() get(ax.Children, 'Type')');
a = line(g, x, y, 'Tag', 'member');
line(t, x, y, 'Tag', 'tmember');
inner = hggroup(g, 'Tag', 'inner');
c = line(inner, x, y, 'Tag', 'deep');
u2_chk('g_ax_children', @() get(ax.Children, 'Tag')');
u2_chk('g_ax_children_types', @() get(ax.Children, 'Type')');
u2_chk('g_children', @() get(g.Children, 'Tag')');
u2_chk('g_findobj_member', @() numel(findobj(f, 'Tag', 'member')));
u2_chk('g_findobj_deep', @() numel(findobj(f, 'Tag', 'deep')));
u2_chk('g_findobj_groups', @() numel(findobj(f, 'Type', 'hggroup')));
u2_chk('g_findobj_lines', @() numel(findobj(f, 'Type', 'line')));
u2_chk('g_findobj_ax_tags', @() get(findobj(ax), 'Tag')');
u2_chk('g_findobj_g_tags', @() get(findobj(g), 'Tag')');
u2_chk('g_findobj_depth1', @() get(findobj(ax, '-depth', 1), 'Tag')');
u2_chk('g_findobj_flat', @() get(findobj(g, 'flat'), 'Tag'));
u2_chk('g_allchild', @() get(allchild(ax), 'Tag')');
u2_chk('g_parents', @() {get(get(a, 'Parent'), 'Tag'), get(get(c, 'Parent'), 'Tag'), get(get(inner, 'Parent'), 'Tag'), get(get(g, 'Parent'), 'Type')});
u2_chk('g_ancestor', @() get(ancestor(c, 'axes'), 'Type'));
u2_chk('g_ancestor_group', @() get(ancestor(c, 'hggroup'), 'Tag'));
hold(ax, 'on');
plot(ax, x, y, 'Tag', 'held');
u2_chk('g_held_children', @() get(ax.Children, 'Tag')');
set(a, 'Parent', ax);
u2_chk('g_moved_out', @() {get(ax.Children, 'Tag')', get(g.Children, 'Tag')'});
set(a, 'Parent', g);
u2_chk('g_moved_in', @() {get(ax.Children, 'Tag')', get(g.Children, 'Tag')'});

% --- 166: a replacing plot, cla and delete take a group ------------------------------------------
hold(ax, 'off');
plot(ax, x, y, 'Tag', 'replacing');
u2_chk('r_valid', @() {isvalid(g), isvalid(t), isvalid(inner), isvalid(a), isvalid(c)});
u2_chk('r_children', @() get(ax.Children, 'Tag'));
u2_chk('r_findobj', @() numel(findobj(f, 'Type', 'hggroup')));

g = hggroup(ax, 'Tag', 'g2');
line(g, x, y);
cla(ax);
u2_chk('cla_valid', @() isvalid(g));
u2_chk('cla_children', @() numel(ax.Children));

g = hggroup(ax, 'Tag', 'g3');
m = line(g, x, y);
cla(ax, 'reset');
u2_chk('clareset_valid', @() {isvalid(g), isvalid(m)});

g = hggroup(ax, 'Tag', 'g4');
m = line(g, x, y, 'Tag', 'm4');
n = hggroup(g, 'Tag', 'n4');
k = line(n, x, y, 'Tag', 'k4');
delete(g);
u2_chk('del_valid', @() {isvalid(g), isvalid(m), isvalid(n), isvalid(k)});
u2_chk('del_children', @() numel(ax.Children));

g = hggroup(ax, 'Tag', 'g5');
m = line(g, x, y, 'Tag', 'm5');
delete(m);
u2_chk('del_member', @() {isvalid(g), numel(g.Children), numel(ax.Children)});

cla(ax);
plot(ax, x, y, 'Tag', 'before');
axes(ax);
g = hggroup('Tag', 'g6');
u2_chk('nopar_children', @() get(ax.Children, 'Tag')');
plot(ax, x, y);
u2_chk('nopar_after_plot', @() isvalid(g));

cla(ax);
hold(ax, 'on');
g = hggroup(ax, 'Tag', 'g7');
plot(ax, x, y);
u2_chk('hold_keeps', @() isvalid(g));
hold(ax, 'off');
close(f);
u2_chk('close_valid', @() isvalid(g));

f = figure('Visible', 'off');
ax = axes(f);
hold(ax, 'on');
plot(ax, x, y, 'Tag', 'p1');
g = hggroup(ax, 'Tag', 'gmid');
plot(ax, x, y, 'Tag', 'p2');
text(ax, 1, 1, 'a', 'Tag', 'tx');
line(g, x, y, 'Tag', 'gm');
u2_chk('order_children', @() get(ax.Children, 'Tag')');
u2_chk('order_findobj', @() get(findobj(f), 'Tag')');
g.HandleVisibility = 'off';
u2_chk('hv_off_findobj', @() {numel(findobj(f, 'Tag', 'gmid')), numel(findobj(f, 'Tag', 'gm')), numel(findall(f, 'Tag', 'gm')), numel(ax.Children)});
close(f);

% A group through each engine's own figure file.
f = figure('Visible', 'off');
ax = axes(f);
hold(ax, 'on');
plot(ax, x, y, 'Tag', 'plain');
g = hggroup(ax, 'Tag', 'grp');
line(g, [0 1], [0 1], 'Tag', 'inline');
tmp = [tempname '.fig'];
savefig(f, tmp);
close(f);
h = openfig(tmp, 'invisible');
u2_chk('file_lines', @() sort(get(findobj(h, 'Type', 'line'), 'Tag'))');
u9b_chkdiv('file_group', @() numel(findobj(h, 'Type', 'hggroup')), '0342'); % this build's document keeps a group's members and forgets the group
delete(h);
delete(tmp);

% --- 143: annotations' types and their pane --------------------------------------------------------
f = figure('Visible', 'off');
ax = axes(f);
kinds = {'textbox', 'rectangle', 'ellipse', 'line', 'arrow', 'doublearrow', 'textarrow'};
hs = cell(1, 7);
for i = 1:7
    if any(strcmp(kinds{i}, {'textbox', 'rectangle', 'ellipse'}))
        hs{i} = annotation(f, kinds{i}, [0.1 0.1 0.2 0.2], 'Tag', ['a_' kinds{i}]);
    else
        hs{i} = annotation(f, kinds{i}, [0.1 0.4], [0.2 0.5], 'Tag', ['a_' kinds{i}]);
    end
end
u2_chk('an_types', @() strjoin(cellfun(@(h) get(h, 'Type'), hs, 'UniformOutput', false), ' '));
u2_chk('an_findobj', @() cellfun(@(k) numel(findobj(f, 'Tag', ['a_' k])), kinds));
u2_chk('an_findall', @() cellfun(@(k) numel(findall(f, 'Tag', ['a_' k])), kinds));
u2_chk('an_findobj_type', @() numel(findobj(f, 'Type', 'textboxshape')));
u2_chk('an_findall_type', @() numel(findall(f, 'Type', 'textboxshape')));
u2_chk('an_root_findobj', @() numel(findobj('Tag', 'a_arrow')));
u2_chk('an_parent_type', @() get(get(hs{1}, 'Parent'), 'Type'));
u2_chk('an_parent_hv', @() get(get(hs{1}, 'Parent'), 'HandleVisibility'));
u2_chk('an_parent_parent', @() isequal(get(get(hs{1}, 'Parent'), 'Parent'), f));
u2_chk('an_fig_children', @() get(f.Children, 'Type'));
u2_chk('an_isa', @() {isa(hs{1}, 'matlab.graphics.shape.TextBox'), isa(hs{2}, 'matlab.graphics.shape.Rectangle'), isa(hs{4}, 'matlab.graphics.shape.Line'), isa(hs{6}, 'matlab.graphics.shape.DoubleEndArrow')});
r = rectangle(ax);
u2_chk('rect_isa', @() {get(r, 'Type'), isa(r, 'matlab.graphics.primitive.Rectangle'), isa(r, 'matlab.graphics.shape.Rectangle')});
u2_chk('an_hv', @() get(hs{1}, 'HandleVisibility'));
u2_chk('an_ancestor', @() get(ancestor(hs{1}, 'figure'), 'Type'));
close(f);

f = figure('Visible', 'off');
u2_chk('p_fresh', @() numel(findall(f, 'Type', 'annotationpane')));
a = annotation(f, 'arrow', [0.1 0.2], [0.1 0.2], 'Tag', 'a1');
p = get(a, 'Parent');
u2_chk('p_after', @() numel(findall(f, 'Type', 'annotationpane')));
u2_chk('p_props', @() {get(p, 'Type'), get(p, 'HandleVisibility'), char(get(p, 'Visible')), get(p, 'Tag'), isequal(get(p, 'Parent'), f)});
u2_chk('p_children', @() get(get(p, 'Children'), 'Tag'));
axes(f);
u2_chk('p_order', @() drawnKinds(f)); % the menus and toolbar R2025b's figure has are left out
b = annotation(f, 'ellipse', [0.3 0.3 0.1 0.1], 'Tag', 'a2');
u2_chk('p_same_pane', @() isequal(get(b, 'Parent'), p));
u2_chk('p_children2', @() get(get(p, 'Children'), 'Tag')');
delete(a);
delete(b);
u2_chk('p_after_delete', @() {isvalid(p), numel(findall(f, 'Type', 'annotationpane'))});
c = annotation(f, 'line', [0.1 0.2], [0.1 0.2], 'Tag', 'a3');
clf(f);
u2_chk('p_clf', @() {isvalid(c), isvalid(p), numel(findall(f, 'Type', 'annotationpane'))});
c = annotation(f, 'line', [0.1 0.2], [0.1 0.2], 'Tag', 'a4');
p2 = get(c, 'Parent');
clf(f, 'reset');
u2_chk('p_clf_reset', @() {isvalid(c), isvalid(p2)});
c = annotation(f, 'textbox', [0.1 0.2 0.1 0.1], 'Tag', 'a5');
p3 = get(c, 'Parent');
delete(p3);
u2_chk('p_delete_pane', @() {isvalid(c), isvalid(p3)});
c = annotation(f, 'textbox', [0.1 0.2 0.1 0.1], 'Tag', 'a6');
u2_chk('p_new_pane', @() {numel(findall(f, 'Type', 'annotationpane')), numel(findobj(f, 'Tag', 'a6'))});
set(groot, 'ShowHiddenHandles', 'on');
u2_chk('p_show_hidden', @() numel(findobj(f, 'Tag', 'a6')));
set(groot, 'ShowHiddenHandles', 'off');
u2_chk('p_isgraphics', @() {isgraphics(c, 'textboxshape'), isgraphics(c, 'textbox'), ishghandle(c)});
u2_chk('p_ancestor', @() isequal(ancestor(c, 'figure'), f));
u2_chk('p_ancestor_pane', @() isequal(ancestor(c, 'annotationpane'), get(c, 'Parent')));
close(f);
f = figure('Visible', 'off');
plot(1:3);
t = annotation('textarrow', [0.3 0.4], [0.3 0.4], 'String', 'x');
plot(1:4);
u2_chk('p_replacing_plot', @() isvalid(t));
close(f);
close all force;

function k = drawnKinds(f)
k = get(allchild(f), 'Type')';
k = k(ismember(k, {'annotationpane', 'axes'}));
end
