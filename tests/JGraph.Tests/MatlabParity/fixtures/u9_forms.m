% record: -noFigureWindows
% U9 of the app-building plan (ADR 0207): the forms each maker of the stage takes - no parent, a
% parent of each kind, name-value pairs, a struct, a style word - what each takes as a parent, and
% R2025b's refusals for the rest, in a uifigure and a classic figure that are never shown.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
ax = axes(f);
makers = {'uiknob', 'uiswitch', 'uigauge', 'uilamp', 'uidatepicker', 'uicolorpicker', 'uitree', 'uitreenode'};
for k = 1:numel(makers)
    n = makers{k};
    u2_chk([n '_in_uifigure'], @() u8_chain(feval(n, uf)));
    u2_chk([n '_in_figure'], @() u8_chain(feval(n, f)));
    u2_chk([n '_in_uipanel'], @() u8_chain(feval(n, uipanel(uf))));
    u2_chk([n '_in_classic_uipanel'], @() u8_chain(feval(n, uipanel(f))));
    u2_chk([n '_in_grid'], @() u8_chain(feval(n, uigridlayout(uf))));
    u2_chk([n '_in_group'], @() u8_chain(feval(n, uibuttongroup(uf))));
    u2_chk([n '_in_tabgroup'], @() u8_chain(feval(n, uitabgroup(uf))));
    u2_chk([n '_in_tab'], @() u8_chain(feval(n, uitab(uitabgroup(uf)))));
    u2_chk([n '_in_classic_tab'], @() u8_chain(feval(n, uitab(uitabgroup(f)))));
    u2_chk([n '_in_menu'], @() u8_chain(feval(n, uimenu(uf))));
    u2_chk([n '_in_contextmenu'], @() u8_chain(feval(n, uicontextmenu(uf))));
    u2_chk([n '_in_toolbar'], @() u8_chain(feval(n, uitoolbar(uf))));
    u2_chk([n '_in_axes'], @() u8_chain(feval(n, ax)));
    u2_chk([n '_in_uiaxes'], @() u8_chain(feval(n, uiaxes(uf))));
    u2_chk([n '_in_uicontrol'], @() u8_chain(feval(n, uicontrol(f))));
    u2_chk([n '_in_label'], @() u8_chain(feval(n, uilabel(uf))));
    u2_chk([n '_in_tree'], @() u8_chain(feval(n, uitree(uf))));
    u2_chk([n '_in_checkbox_tree'], @() u8_chain(feval(n, uitree(uf, 'checkbox'))));
    u2_chk([n '_in_treenode'], @() u8_chain(feval(n, uitreenode(uitree(uf)))));
    u2_chk([n '_in_classic_tree'], @() u8_chain(feval(n, uitree(f))));
    u2_chk([n '_in_table'], @() u8_chain(feval(n, uitable(uf))));
    u2_chk([n '_in_number'], @() u8_chain(feval(n, 5.5)));
    u2_chk([n '_in_empty'], @() u8_chain(feval(n, [])));
    u2_chk([n '_in_root'], @() u8_chain(feval(n, groot)));
    u2_chk([n '_named_parent'], @() u8_chain(feval(n, 'Parent', uf)));
    u2_chk([n '_named_classic_parent'], @() u8_chain(feval(n, 'Parent', f)));
    u2_chk([n '_named_tree_parent'], @() u8_chain(feval(n, 'Parent', uitree(uf))));
    u2_chk([n '_unknown'], @() u8_chain(feval(n, uf, 'Bogus', 1)));
    u2_chk([n '_odd'], @() u8_chain(feval(n, uf, 'Tag')));
    u2_chk([n '_numbers'], @() u8_chain(feval(n, uf, 5, 6)));
    u2_chk([n '_to_panel'], @() u8_move(feval(n, uf), uipanel(uf)));
    u2_chk([n '_to_axes'], @() u8_move(feval(n, uf), ax));
    u2_chk([n '_to_tree'], @() u8_move(feval(n, uf), uitree(uf)));
    u2_chk([n '_to_node'], @() u8_move(feval(n, uf), uitreenode(uitree(uf))));
    u2_chk([n '_to_grid'], @() u8_move(feval(n, uf), uigridlayout(uf)));
    delete(allchild(uf)); delete(allchild(f)); ax = axes(f);
end

% In the parent each takes: pairs, a struct, case, strings, and what cannot be written.
homes = {'uiknob', @() uf; 'uiswitch', @() uf; 'uigauge', @() uf; 'uilamp', @() uf; 'uidatepicker', @() uf; 'uicolorpicker', @() uf; ...
    'uitree', @() uf; 'uitreenode', @() uitree(uf)};
for k = 1:size(homes, 1)
    n = homes{k, 1}; home = homes{k, 2};
    u2_chk([n '_pairs'], @() get(feval(n, home(), 'Tag', 't', 'UserData', 5), 'UserData'));
    u2_chk([n '_struct'], @() get(feval(n, home(), struct('Tag', 's')), 'Tag'));
    u2_chk([n '_lower_case'], @() get(feval(n, home(), 'tag', 't'), 'Tag'));
    u2_chk([n '_prefix'], @() get(feval(n, home(), 'Ta', 't'), 'Tag'));
    u2_chk([n '_strings'], @() get(feval(n, home(), "Tag", "t"), 'Tag'));
    u2_chk([n '_get_unknown'], @() get(feval(n, home()), 'Bogus'));
    u2_err([n '_set_unknown'], @() set(feval(n, home()), 'Bogus', 1));
    u2_err([n '_set_type'], @() set(feval(n, home()), 'Type', 'x'));
    u2_err([n '_set_beingdeleted'], @() set(feval(n, home()), 'BeingDeleted', 'on'));
    u2_chk([n '_get_units'], @() get(feval(n, home()), 'Units'));
    u2_chk([n '_isprop'], @() double([isprop(feval(n, home()), 'Units') isprop(feval(n, home()), 'Position') isprop(feval(n, home()), 'Visible') isprop(feval(n, home()), 'Enable') isprop(feval(n, home()), 'Layout')]));
    u2_chk([n '_isa'], @() double([isa(feval(n, home()), 'matlab.ui.control.Component') isa(feval(n, home()), 'matlab.graphics.Graphics') isa(feval(n, home()), 'matlab.ui.container.Container') isa(feval(n, home()), 'handle')]));
    u2_chk([n '_findobj'], @() numel(findobj(feval(n, home()), 'flat', 'Type', get(feval(n, home()), 'Type'))));
    delete(allchild(uf));
end

% The style words.
u2_chk('uiknob_continuous', @() get(uiknob(uf, 'continuous'), 'Type'));
u2_chk('uiknob_discrete', @() get(uiknob(uf, 'discrete'), 'Type'));
u2_chk('uiknob_DISCRETE', @() get(uiknob(uf, 'DISCRETE'), 'Type'));
u2_chk('uiknob_disc', @() get(uiknob(uf, 'disc'), 'Type'));
u2_chk('uiknob_string_style', @() get(uiknob(uf, "discrete"), 'Type'));
u2_chk('uiknob_bogus_style', @() get(uiknob(uf, 'bogus'), 'Type'));
u2_chk('uiknob_discrete_items', @() get(uiknob(uf, 'discrete', 'Items', {'a', 'b'}), 'Value'));
u2_chk('uiknob_discrete_value_first', @() get(uiknob(uf, 'discrete', 'Value', 'b', 'Items', {'a', 'b'}), 'Value'));
u2_chk('uiknob_items_on_continuous', @() get(uiknob(uf, 'Items', {'a'}), 'Type'));
u2_chk('uiknob_value_outside', @() get(uiknob(uf, 'Value', 150), 'Value'));
u2_chk('uiknob_value_then_limits', @() get(uiknob(uf, 'Value', 150, 'Limits', [0 200]), 'Value'));
u2_chk('uiknob_limits_then_value', @() get(uiknob(uf, 'Limits', [0 200], 'Value', 150), 'Value'));
u2_chk('uiswitch_slider', @() get(uiswitch(uf, 'slider'), 'Type'));
u2_chk('uiswitch_rocker', @() get(uiswitch(uf, 'rocker'), 'Type'));
u2_chk('uiswitch_toggle', @() get(uiswitch(uf, 'toggle'), 'Type'));
u2_chk('uiswitch_ROCKER', @() get(uiswitch(uf, 'ROCKER'), 'Type'));
u2_chk('uiswitch_rock', @() get(uiswitch(uf, 'rock'), 'Type'));
u2_chk('uiswitch_bogus', @() get(uiswitch(uf, 'bogus'), 'Type'));
u2_chk('uiswitch_toggle_horizontal', @() get(uiswitch(uf, 'toggle', 'Orientation', 'horizontal'), 'Position'));
u2_chk('uiswitch_value_on', @() get(uiswitch(uf, 'Value', 'On'), 'Value'));
u2_chk('uiswitch_value_lower', @() get(uiswitch(uf, 'Value', 'on'), 'Value'));
u2_chk('uiswitch_value_true', @() get(uiswitch(uf, 'Value', true), 'Value'));
u2_chk('uiswitch_value_then_items', @() get(uiswitch(uf, 'Value', 'b', 'Items', {'a', 'b'}), 'Value'));
u2_chk('uiswitch_three_items', @() get(uiswitch(uf, 'Items', {'a', 'b', 'c'}), 'Items'));
u2_chk('uigauge_circular', @() get(uigauge(uf, 'circular'), 'Type'));
u2_chk('uigauge_linear', @() get(uigauge(uf, 'linear'), 'Type'));
u2_chk('uigauge_ninetydegree', @() get(uigauge(uf, 'ninetydegree'), 'Type'));
u2_chk('uigauge_semicircular', @() get(uigauge(uf, 'semicircular'), 'Type'));
u2_chk('uigauge_NINETYDEGREE', @() get(uigauge(uf, 'NINETYDEGREE'), 'Type'));
u2_chk('uigauge_semi', @() get(uigauge(uf, 'semi'), 'Type'));
u2_chk('uigauge_bogus', @() get(uigauge(uf, 'bogus'), 'Type'));
u2_chk('uigauge_value_outside', @() get(uigauge(uf, 'Value', 150), 'Value'));
u2_chk('uigauge_linear_vertical', @() get(uigauge(uf, 'linear', 'Orientation', 'vertical'), 'Position'));
u2_chk('uigauge_circular_orientation', @() get(uigauge(uf, 'Orientation', 'vertical'), 'Type'));
u2_chk('uitree_checkbox', @() get(uitree(uf, 'checkbox'), 'Type'));
u2_chk('uitree_CHECKBOX', @() get(uitree(uf, 'CHECKBOX'), 'Type'));
u2_chk('uitree_check', @() get(uitree(uf, 'check'), 'Type'));
u2_chk('uitree_tree', @() get(uitree(uf, 'tree'), 'Type'));
u2_chk('uitree_bogus', @() get(uitree(uf, 'bogus'), 'Type'));
u2_chk('uitree_v0_in_figure', @() get(uitree(uf, 'v0'), 'Type'));
u2_err('uitree_v0', @() uitree('v0'));
u2_chk('uitree_checkbox_multiselect', @() get(uitree(uf, 'checkbox', 'Multiselect', 'on'), 'Type'));
u2_chk('uitree_checkednodes_on_tree', @() get(uitree(uf, 'CheckedNodes', []), 'Type'));
u2_chk('uitreenode_text', @() get(uitreenode(uitree(uf), 'Text', 'x'), 'Text'));
u2_chk('uitreenode_text_number', @() get(uitreenode(uitree(uf), 'Text', 5), 'Text'));
u2_chk('uitreenode_odd_text', @() get(uitreenode(uitree(uf), 'x'), 'Text'));
u2_chk('uitreenode_under_node', @() u8_chain(uitreenode(uitreenode(uitree(uf)), 'Text', 'child')));
u2_chk('uitreenode_nodedata', @() get(uitreenode(uitree(uf), 'NodeData', struct('a', 1)), 'NodeData'));
u2_chk('uitreenode_visible', @() get(uitreenode(uitree(uf), 'Tag', 't', 'Visible', 'off'), 'Tag'));
u9_chk('uidatepicker_value', @() get(uidatepicker(uf, 'Value', datetime(2024, 1, 15)), 'Value'));
u9_chk('uidatepicker_value_char', @() get(uidatepicker(uf, 'Value', '15-Jan-2024'), 'Value'));
u9_chk('uidatepicker_value_then_limits', @() get(uidatepicker(uf, 'Value', datetime(2024, 1, 15), 'Limits', [datetime(2024, 2, 1) datetime(2024, 3, 1)]), 'Value'));
u9_chk('uidatepicker_limits_then_value', @() get(uidatepicker(uf, 'Limits', [datetime(2024, 2, 1) datetime(2024, 3, 1)], 'Value', datetime(2024, 1, 15)), 'Value'));
u2_chk('uicolorpicker_value', @() get(uicolorpicker(uf, 'Value', 'blue'), 'Value'));
u2_chk('uicolorpicker_bogus_value', @() get(uicolorpicker(uf, 'Value', 'bogus'), 'Value'));
u2_chk('uilamp_color', @() get(uilamp(uf, 'Color', 'r'), 'Color'));
delete(uf); delete(f);
