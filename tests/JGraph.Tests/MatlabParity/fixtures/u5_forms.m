% record: -noFigureWindows
% U5 of the app-building plan (ADR 0202): the forms each maker of a uifigure's components takes -
% no parent, a parent of each kind, a style, name-value pairs, a struct - and R2025b's refusals for
% the others, in a uifigure and a classic figure that are never shown.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
f = figure('Visible', 'off');
ax = axes(f);
makers = {'uilabel', 'uibutton', 'uieditfield', 'uitextarea', 'uidropdown', 'uilistbox', 'uicheckbox', 'uislider', 'uispinner', ...
    'uiimage', 'uihyperlink', 'uiradiobutton', 'uitogglebutton', 'uigridlayout'};
for k = 1:numel(makers)
    n = makers{k};
    u2_chk([n '_in_uifigure'], @() u5_kind(feval(n, uf)));
    u2_chk([n '_in_figure'], @() u5_kind(feval(n, f)));
    u2_chk([n '_in_uipanel'], @() u5_kind(feval(n, uipanel(uf))));
    u2_chk([n '_in_classic_uipanel'], @() u5_kind(feval(n, uipanel(f))));
    u2_chk([n '_in_grid'], @() u5_kind(feval(n, uigridlayout(uf))));
    u2_chk([n '_in_group'], @() u5_kind(feval(n, uibuttongroup(uf))));
    u2_chk([n '_in_classic_group'], @() u5_kind(feval(n, uibuttongroup(f))));
    u2_chk([n '_in_axes'], @() u5_kind(feval(n, ax)));
    u2_chk([n '_in_label'], @() u5_kind(feval(n, uilabel(uf))));
    u2_chk([n '_number'], @() u5_kind(feval(n, 5.5)));
    u2_chk([n '_empty'], @() u5_kind(feval(n, [])));
    u2_chk([n '_named_parent'], @() u5_kind(feval(n, 'Parent', uf)));
    u2_chk([n '_unknown'], @() u5_kind(feval(n, uf, 'Bogus', 1)));
    u2_chk([n '_odd'], @() u5_kind(feval(n, uf, 'Tag')));
    u2_chk([n '_struct'], @() get(feval(n, uf, struct('Tag', 's')), 'Tag'));
    u2_chk([n '_pairs'], @() get(feval(n, uf, 'Tag', 't', 'Visible', 'off'), 'Visible'));
    u2_chk([n '_lower_case'], @() get(feval(n, uf, 'tag', 't'), 'Tag'));
    u2_chk([n '_prefix'], @() get(feval(n, uf, 'Ta', 't'), 'Tag'));
    u2_chk([n '_strings'], @() get(feval(n, uf, "Tag", "t"), 'Tag'));
    u2_chk([n '_position'], @() get(feval(n, uf, 'Position', [1 2 30 40], 'Tag', 't'), 'Position'));
    u2_chk([n '_units'], @() get(feval(n, uf), 'Units'));
    u2_err([n '_set_units'], @() set(feval(n, uf), 'Units', 'pixels'));
    u2_chk([n '_get_unknown'], @() get(feval(n, uf), 'Bogus'));
    u2_err([n '_set_unknown'], @() set(feval(n, uf), 'Bogus', 1));
    u2_err([n '_set_type'], @() set(feval(n, uf), 'Type', 'x'));
    u2_err([n '_set_beingdeleted'], @() set(feval(n, uf), 'BeingDeleted', 'on'));
    u2_chk([n '_to_figure'], @() u5_reparent(feval(n, uf), f));
    u2_chk([n '_to_axes'], @() u5_reparent(feval(n, uf), ax));
    u2_chk([n '_to_panel'], @() u5_reparent(feval(n, uf), uipanel(uf)));
    if ~strcmp(n, 'uigridlayout')
        u2_chk([n '_in_panel_position'], @() get(feval(n, uipanel(uf)), 'Position'));
    end
    delete(allchild(uf)); delete(allchild(f)); ax = axes(f);
end

% --- a maker with no parent makes a uifigure of its own ----------------------------------------
for k = 1:numel(makers)
    before = numel(findall(groot, 'Type', 'figure'));
    h = feval(makers{k});
    owner = ancestor(h, 'figure');
    fprintf('CHK|%s_alone|%s; %d new figure; a uifigure %d; %s|exact\n', makers{k}, u5_kind(h), ...
        numel(findall(groot, 'Type', 'figure')) - before, strcmp(get(owner, 'HandleVisibility'), 'off'), mat2str(owner.Position(3:4)));
    delete(owner);
end

% --- styles -------------------------------------------------------------------------------------
u2_chk('uibutton_push', @() u5_kind(uibutton(uf, 'push')));
u2_chk('uibutton_state', @() u5_kind(uibutton(uf, 'state')));
u2_chk('uibutton_upper', @() u5_kind(uibutton(uf, 'PUSH')));
u2_chk('uibutton_prefix', @() u5_kind(uibutton(uf, 'st')));
u2_chk('uibutton_string', @() u5_kind(uibutton(uf, "state")));
u2_chk('uibutton_bad', @() u5_kind(uibutton(uf, 'bogus')));
u2_chk('uibutton_style_then_pairs', @() get(uibutton(uf, 'state', 'Text', 'x'), 'Text'));
u2_chk('uibutton_pairs_then_style', @() get(uibutton(uf, 'Text', 'x', 'state'), 'Text'));
u2_chk('uibutton_style_named_parent', @() u5_kind(uibutton('state', 'Parent', uf)));
u2_chk('uibutton_named_parent_style', @() u5_kind(uibutton('Parent', uf, 'state')));
u2_chk('uieditfield_text', @() u5_kind(uieditfield(uf, 'text')));
u2_chk('uieditfield_numeric', @() u5_kind(uieditfield(uf, 'numeric')));
u2_chk('uieditfield_prefix', @() u5_kind(uieditfield(uf, 'num')));
u2_chk('uieditfield_bad', @() u5_kind(uieditfield(uf, 'bogus')));
u2_chk('uislider_slider', @() u5_kind(uislider(uf, 'slider')));
u2_chk('uislider_range', @() u5_kind(uislider(uf, 'range')));
u2_chk('uislider_bad', @() u5_kind(uislider(uf, 'bogus')));
u2_chk('uispinner_no_styles', @() u5_kind(uispinner(uf, 'numeric')));
u2_chk('uilabel_no_styles', @() u5_kind(uilabel(uf, 'text')));
u2_chk('uidropdown_no_styles', @() u5_kind(uidropdown(uf, 'bogus')));
delete(allchild(uf));

% --- the tree -----------------------------------------------------------------------------------
p = uipanel(uf);
b1 = uibutton(p, 'Tag', 'b1'); l1 = uilabel(p, 'Tag', 'l1'); c1 = uicontrol(p, 'Tag', 'c1'); a1 = uiaxes(p, 'Tag', 'a1');
fprintf('CHK|tree_children|%s|exact\n', u2_tags(p.Children));
fprintf('CHK|tree_ancestors|%d %d|exact\n', ancestor(b1, 'figure') == uf, ancestor(b1, 'uipanel') == p);
fprintf('CHK|tree_findobj|%d %d %d|exact\n', numel(findobj(uf, 'Type', 'uibutton')), numel(findobj(uf, 'Type', 'uilabel')), numel(findall(uf, 'Tag', 'l1')));
uistack(l1, 'bottom');
fprintf('CHK|tree_after_uistack|%s|exact\n', u2_tags(p.Children));
delete(b1);
fprintf('CHK|tree_after_delete|%s %d|exact\n', u2_tags(p.Children), numel(p.Children));
delete(uf); delete(f);
