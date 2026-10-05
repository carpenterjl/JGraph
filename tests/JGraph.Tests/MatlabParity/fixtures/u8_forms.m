% record: -noFigureWindows
% U8 of the app-building plan (ADR 0206): the forms each maker of the stage takes - no parent, a
% parent of each kind, name-value pairs, a struct - what each takes as a parent, what the older
% makers say of a tab group, a menu and a toolbar as one, and R2025b's refusals for the rest, in a
% uifigure and a classic figure that are never shown.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
ax = axes(f);
makers = {'uitable', 'uitabgroup', 'uitab', 'uimenu', 'uicontextmenu', 'uitoolbar', 'uipushtool', 'uitoggletool'};
for k = 1:numel(makers)
    n = makers{k};
    u2_chk([n '_in_uifigure'], @() u8_chain(feval(n, uf)));
    u2_chk([n '_in_figure'], @() u8_chain(feval(n, f)));
    u2_chk([n '_in_uipanel'], @() u8_chain(feval(n, uipanel(uf))));
    u2_chk([n '_in_classic_uipanel'], @() u8_chain(feval(n, uipanel(f))));
    u2_chk([n '_in_grid'], @() u8_chain(feval(n, uigridlayout(uf))));
    u2_chk([n '_in_group'], @() u8_chain(feval(n, uibuttongroup(uf))));
    u2_chk([n '_in_tabgroup'], @() u8_chain(feval(n, uitabgroup(uf))));
    u2_chk([n '_in_classic_tabgroup'], @() u8_chain(feval(n, uitabgroup(f))));
    u2_chk([n '_in_tab'], @() u8_chain(feval(n, uitab(uitabgroup(uf)))));
    u2_chk([n '_in_classic_tab'], @() u8_chain(feval(n, uitab(uitabgroup(f)))));
    u2_chk([n '_in_menu'], @() u8_chain(feval(n, uimenu(uf))));
    u2_chk([n '_in_contextmenu'], @() u8_chain(feval(n, uicontextmenu(uf))));
    u2_chk([n '_in_toolbar'], @() u8_chain(feval(n, uitoolbar(uf))));
    u2_chk([n '_in_classic_toolbar'], @() u8_chain(feval(n, uitoolbar(f))));
    u2_chk([n '_in_pushtool'], @() u8_chain(feval(n, uipushtool(uitoolbar(f)))));
    u2_chk([n '_in_axes'], @() u8_chain(feval(n, ax)));
    u2_chk([n '_in_uicontrol'], @() u8_chain(feval(n, uicontrol(f))));
    u2_chk([n '_in_label'], @() u8_chain(feval(n, uilabel(uf))));
    u2_chk([n '_in_root'], @() u8_chain(feval(n, groot)));
    u2_chk([n '_named_parent'], @() u8_chain(feval(n, 'Parent', uf)));
    u2_chk([n '_named_classic_parent'], @() u8_chain(feval(n, 'Parent', f)));
    u2_chk([n '_unknown'], @() u8_chain(feval(n, uf, 'Bogus', 1)));
    u2_chk([n '_odd'], @() u8_chain(feval(n, uf, 'Tag')));
    u2_chk([n '_to_panel'], @() u8_move(feval(n, uf), uipanel(uf)));
    u2_chk([n '_to_axes'], @() u8_move(feval(n, f), ax));
    delete(allchild(uf)); delete(allchild(f)); ax = axes(f);
end

% In the parent each takes: pairs, a struct, case, strings, and what cannot be written.
homes = {'uitable', @() uf; 'uitabgroup', @() uf; 'uitab', @() uitabgroup(uf); 'uimenu', @() uf; 'uicontextmenu', @() uf; ...
    'uitoolbar', @() uf; 'uipushtool', @() uitoolbar(uf); 'uitoggletool', @() uitoolbar(uf)};
for k = 1:size(homes, 1)
    n = homes{k, 1}; home = homes{k, 2};
    u2_chk([n '_pairs'], @() get(feval(n, home(), 'Tag', 't', 'UserData', 5), 'UserData'));
    u2_chk([n '_lower_case'], @() get(feval(n, home(), 'tag', 't'), 'Tag'));
    u2_chk([n '_strings'], @() get(feval(n, home(), "Tag", "t"), 'Tag'));
    u2_chk([n '_get_unknown'], @() get(feval(n, home()), 'Bogus'));
    u2_err([n '_set_unknown'], @() set(feval(n, home()), 'Bogus', 1));
    u2_err([n '_set_type'], @() set(feval(n, home()), 'Type', 'x'));
    u2_err([n '_set_beingdeleted'], @() set(feval(n, home()), 'BeingDeleted', 'on'));
    u2_chk([n '_isprop'], @() double([isprop(feval(n, home()), 'Units') isprop(feval(n, home()), 'Position') isprop(feval(n, home()), 'Visible') isprop(feval(n, home()), 'Enable')]));
    u2_chk([n '_findobj'], @() numel(findobj(feval(n, home()), 'flat', 'Type', get(feval(n, home()), 'Type'))));
    delete(allchild(uf));
end
u2_chk('uitable_struct', @() get(uitable(uf, struct('Tag', 's')), 'Tag'));
u2_chk('uitabgroup_struct', @() get(uitabgroup(uf, struct('Tag', 's')), 'Tag'));
u2_chk('uimenu_struct', @() get(uimenu(uf, struct('Tag', 's')), 'Tag'));
u2_chk('uitoolbar_struct', @() get(uitoolbar(uf, struct('Tag', 's')), 'Tag'));
u2_chk('uitable_prefix', @() get(uitable(uf, 'Ta', 't'), 'Tag'));
u2_chk('uimenu_prefix', @() get(uimenu(uf, 'Ta', 't'), 'Tag'));
u2_chk('uitoolbar_prefix', @() get(uitoolbar(uf, 'Ta', 't'), 'Tag'));
u2_chk('uicontextmenu_prefix', @() get(uicontextmenu(uf, 'Ta', 't'), 'Tag'));
u2_chk('uitabgroup_prefix', @() get(uitabgroup(uf, 'Ta', 't'), 'Tag'));
delete(allchild(uf));

% What the older makers say of the objects of U8 as a parent.
older = {'uicontrol', 'uipanel', 'uibuttongroup', 'uibutton', 'uilabel', 'uigridlayout', 'uiaxes', 'axes'};
for k = 1:numel(older)
    n = older{k};
    if any(strcmp(n, {'uicontrol', 'axes'})), p = f; else, p = uf; end
    u2_chk([n '_in_tabgroup'], @() u8_chain(feval(n, uitabgroup(p))));
    u2_chk([n '_in_tab'], @() u8_chain(feval(n, uitab(uitabgroup(p)))));
    u2_chk([n '_in_menu'], @() u8_chain(feval(n, uimenu(p))));
    u2_chk([n '_in_contextmenu'], @() u8_chain(feval(n, uicontextmenu(p))));
    u2_chk([n '_in_toolbar'], @() u8_chain(feval(n, uitoolbar(p))));
    u2_chk([n '_in_pushtool'], @() u8_chain(feval(n, uipushtool(uitoolbar(p)))));
    u2_chk([n '_to_tabgroup'], @() u8_move(feval(n, p), uitabgroup(p)));
    u2_chk([n '_to_tab'], @() u8_move(feval(n, p), uitab(uitabgroup(p))));
    u2_chk([n '_to_menu'], @() u8_move(feval(n, p), uimenu(p)));
    u2_chk([n '_to_table'], @() u8_move(feval(n, p), uitable(p)));
    u2_chk([n '_to_toolbar'], @() u8_move(feval(n, p), uitoolbar(p)));
    delete(allchild(uf)); delete(allchild(f));
end

% The objects of U8 moved about.
u2_chk('tab_to_figure', @() u8_move(uitab(uitabgroup(uf)), uf));
u2_chk('tab_to_panel', @() u8_move(uitab(uitabgroup(uf)), uipanel(uf)));
u2_chk('tab_to_tab', @() u8_move(uitab(uitabgroup(uf)), uitab(uitabgroup(uf))));
u2_chk('tab_to_group', @() u8_move(uitab(uitabgroup(uf)), uitabgroup(uf)));
u2_chk('tabgroup_to_grid', @() u8_move(uitabgroup(uf), uigridlayout(uf)));
u2_chk('tabgroup_to_tabgroup', @() u8_move(uitabgroup(uf), uitabgroup(uf)));
u2_chk('tabgroup_to_tab', @() u8_move(uitabgroup(uf), uitab(uitabgroup(uf))));
u2_chk('table_to_tab', @() u8_move(uitable(uf), uitab(uitabgroup(uf))));
u2_chk('table_to_grid', @() u8_move(uitable(uf), uigridlayout(uf)));
u2_chk('table_to_other_figure', @() u8_move(uitable(uf), f));
u2_chk('menu_to_panel', @() u8_move(uimenu(uf), uipanel(uf)));
u2_chk('menu_to_toolbar', @() u8_move(uimenu(uf), uitoolbar(uf)));
u2_chk('menu_to_menu', @() u8_move(uimenu(uf), uimenu(uf)));
u2_chk('menu_to_contextmenu', @() u8_move(uimenu(uf), uicontextmenu(uf)));
u2_chk('menu_to_other_figure', @() u8_move(uimenu(uf), f));
u2_chk('contextmenu_to_menu', @() u8_move(uicontextmenu(uf), uimenu(uf)));
u2_chk('contextmenu_to_other_figure', @() u8_move(uicontextmenu(uf), f));
u2_chk('toolbar_to_panel', @() u8_move(uitoolbar(uf), uipanel(uf)));
u2_chk('toolbar_to_other_figure', @() u8_move(uitoolbar(uf), f));
u2_chk('pushtool_to_figure', @() u8_move(uipushtool(uitoolbar(uf)), uf));
u2_chk('pushtool_to_menu', @() u8_move(uipushtool(uitoolbar(uf)), uimenu(uf)));
u2_chk('pushtool_to_toolbar', @() u8_move(uipushtool(uitoolbar(uf)), uitoolbar(uf)));
u2_chk('toggletool_to_figure', @() u8_move(uitoggletool(uitoolbar(uf)), uf));
delete(findall(groot, 'Type', 'figure'));

% With no parent named: in the current figure, which is made when there is none.
t = uitab;
fprintf('CHK|uitab_alone|%s|exact\n', u8_chain(t));
fprintf('CHK|uitab_alone_figures|%d|exact\n', numel(findall(groot, 'Type', 'figure')));
p = uipushtool;
fprintf('CHK|uipushtool_alone|%s|exact\n', u8_chain(p));
g = uitoggletool;
fprintf('CHK|uitoggletool_alone|%s|exact\n', u8_chain(g));
fprintf('CHK|toolbars_made|%d|exact\n', numel(findall(gcf, 'Type', 'uitoolbar', 'Tag', '')));
m = uimenu('Text', 'm');
fprintf('CHK|uimenu_alone|%s|exact\n', u8_chain(m));
tb = uitable('Tag', 'x');
fprintf('CHK|uitable_alone|%s|exact\n', u8_chain(tb));
tg = uitabgroup;
fprintf('CHK|uitabgroup_alone|%s|exact\n', u8_chain(tg));
c = uicontextmenu;
fprintf('CHK|uicontextmenu_alone|%s|exact\n', u8_chain(c));
fprintf('CHK|alone_figures|%d|exact\n', numel(findall(groot, 'Type', 'figure')));
delete(findall(groot, 'Type', 'figure'));
