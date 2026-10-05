% record: -noFigureWindows
% U8 of the app-building plan (ADR 0206): how a figure's menus and toolbars behave - the order of
% menus and what writing a Position does to it, the older names a menu still answers to, where
% menus, toolbars and components stand among a figure's children, and a tool's pictures and state -
% in a classic figure (F) and a uifigure (U).
for q = 1:2
    if q == 1
        % MenuBar 'none': a classic figure's own six menus are R2025b's, and are not asked about.
        p = figure('Visible', 'off', 'Position', [100 100 560 420], 'MenuBar', 'none', 'ToolBar', 'none'); w = 'F';
    else
        p = uifigure('Visible', 'off', 'Position', [100 100 560 420]); w = 'U';
    end

    % menus in order
    m1 = uimenu(p, 'Text', 'A'); m2 = uimenu(p, 'Text', 'B'); m3 = uimenu(p, 'Text', 'C');
    fprintf('CHK|%s_positions|%s|exact\n', w, mat2str([m1.Position m2.Position m3.Position]));
    fprintf('CHK|%s_children|%s|exact\n', w, u8_titles(p));
    m3.Position = 1;
    fprintf('CHK|%s_moved_first|%s %s|exact\n', w, mat2str([m1.Position m2.Position m3.Position]), u8_titles(p));
    m1.Position = 9;
    fprintf('CHK|%s_moved_past|%s %s|exact\n', w, mat2str([m1.Position m2.Position m3.Position]), u8_titles(p));
    m1.Position = 0;
    fprintf('CHK|%s_moved_zero|%s %s|exact\n', w, mat2str([m1.Position m2.Position m3.Position]), u8_titles(p));
    m1.Position = 1.5;
    fprintf('CHK|%s_moved_between|%s %s|exact\n', w, mat2str([m1.Position m2.Position m3.Position]), u8_titles(p));
    m2.Position = 1;
    fprintf('CHK|%s_moved_again|%s %s|exact\n', w, mat2str([m1.Position m2.Position m3.Position]), u8_titles(p));
    u2_err([w '_position_negative'], @() set(m1, 'Position', -1));
    s1 = uimenu(m1, 'Text', 'a1'); s2 = uimenu(m1, 'Text', 'a2'); s3 = uimenu(m1, 'Text', 'a3', 'Separator', 'on');
    fprintf('CHK|%s_sub_positions|%s %s|exact\n', w, mat2str([s1.Position s2.Position s3.Position]), u8_titles(m1));
    delete(s2);
    fprintf('CHK|%s_sub_after_delete|%s %s|exact\n', w, mat2str([s1.Position s3.Position]), u8_titles(m1));
    uimenu(m1, 'Text', 'first', 'Position', 1);
    fprintf('CHK|%s_made_first|%s|exact\n', w, u8_titles(m1));
    delete(allchild(p));

    % the older names, and what a menu takes
    u2_chk([w '_label_alias'], @() get(uimenu(p, 'Label', 'L'), 'Text'));
    u2_chk([w '_label_read'], @() get(uimenu(p, 'Text', 'L'), 'Label'));
    u2_chk([w '_callback_alias'], @() class(get(uimenu(p, 'Callback', @sin), 'MenuSelectedFcn')));
    u2_chk([w '_callback_read'], @() class(get(uimenu(p, 'MenuSelectedFcn', @sin), 'Callback')));
    u2_chk([w '_names_listed'], @() double([any(strcmp(fieldnames(get(uimenu(p))), 'Label')) isprop(uimenu(p), 'Label') isprop(uimenu(p), 'Callback')]));
    u2_chk([w '_text_ampersand'], @() get(uimenu(p, 'Text', '&File'), 'Text'));
    u2_chk([w '_text_cell'], @() get(uimenu(p, 'Text', {'a'}), 'Text'));
    u2_chk([w '_text_number'], @() get(uimenu(p, 'Text', 5), 'Text'));
    keys = {'a', 'A', 'ab', '1', '', ' ', "s", 5, '+'};
    for k = 1:numel(keys)
        u2_chk(sprintf('%s_accelerator_%02d', w, k), @() get(uimenu(uimenu(p), 'Accelerator', keys{k}), 'Accelerator'));
    end
    u2_chk([w '_accelerator_top'], @() get(uimenu(p, 'Accelerator', 'q'), 'Accelerator'));
    u2_chk([w '_checked_top'], @() get(uimenu(p, 'Checked', 'on'), 'Checked'));
    u2_chk([w '_separator_top'], @() get(uimenu(p, 'Separator', 'on'), 'Separator'));
    u2_chk([w '_units'], @() get(uimenu(p), 'Units'));
    u2_chk([w '_callbacks'], @() u8_callbacks(uimenu(p), 'MenuSelectedFcn'));
    delete(allchild(p));
    a = uimenu(p, 'Text', 'a'); b = uimenu(p, 'Text', 'b'); c = uimenu(a, 'Text', 'c');
    c.Parent = b;
    fprintf('CHK|%s_entry_moved|%d %d %s|exact\n', w, numel(a.Children), numel(b.Children), get(c.Parent, 'Text'));
    u2_err([w '_menu_under_itself'], @() set(b, 'Parent', c));
    cm = uicontextmenu(p);
    a.Parent = cm;
    fprintf('CHK|%s_to_contextmenu|%d %s|exact\n', w, numel(cm.Children), get(a.Parent, 'Type'));
    delete(b);
    fprintf('CHK|%s_parent_deleted|%d|exact\n', w, isgraphics(c));
    set(p, 'MenuBar', 'none');
    keep = uimenu(p, 'Text', 'keep');
    fprintf('CHK|%s_menubar_none|%d %s|exact\n', w, isgraphics(keep), get(p, 'MenuBar'));
    delete(allchild(p));

    % a figure's children, newest first whatever they are, an axes last
    if q == 1, uicontrol(p); else, uibutton(p); end
    uimenu(p, 'Text', 'm1'); uicontextmenu(p); uitoolbar(p); uipanel(p);
    if q == 1, axes(p); else, uiaxes(p); end
    uitable(p); uitabgroup(p); uimenu(p, 'Text', 'm2');
    fprintf('CHK|%s_figure_children|%s|exact\n', w, u8_types(get(p, 'Children')));
    fprintf('CHK|%s_findobj|%s|exact\n', w, u8_types(findobj(p, '-depth', 1)));
    delete(allchild(p));

    % toolbars and tools
    tb = uitoolbar(p);
    fprintf('CHK|%s_toolbar_property|%s|exact\n', w, get(p, 'ToolBar'));
    p1 = uipushtool(tb, 'Tooltip', 'one'); g1 = uitoggletool(tb, 'Tooltip', 'two'); p2 = uipushtool(tb, 'Tooltip', 'three', 'Separator', 'on'); %#ok<NASGU>
    fprintf('CHK|%s_tools|%s|exact\n', w, u8_titles(tb));
    uitoolbar(p);
    fprintf('CHK|%s_two_toolbars|%s|exact\n', w, u8_types(allchild(p)));
    pictures = {0.5 * ones(16, 16, 3), 0.5 * ones(20, 30, 3), uint8(255 * ones(16, 16, 3)), zeros(16, 16), 2 * ones(16, 16, 3), NaN(16, 16, 3), [], ...
        ones(16, 16, 3) > 0, single(0.5 * ones(4, 4, 3)), uint16(ones(4, 4, 3)), int8(ones(4, 4, 3)), 0.5 * ones(16, 16, 4)};
    for k = 1:numel(pictures)
        u2_chk(sprintf('%s_cdata_%02d', w, k), @() u8_picture(uipushtool(tb, 'CData', pictures{k})));
    end
    icons = {'', 'bogus', 0.5 * ones(16, 16, 3), "s", 5, {'a'}, ['ab'; 'cd'], []};
    for k = 1:numel(icons)
        u2_chk(sprintf('%s_icon_%02d', w, k), @() u8_picture(uipushtool(tb, 'Icon', icons{k})));
    end
    u2_chk([w '_tooltipstring'], @() get(uipushtool(tb, 'TooltipString', 'x'), 'Tooltip'));
    u2_chk([w '_tooltip_cell'], @() u5_text(get(uipushtool(tb, 'Tooltip', {'a', 'b'}), 'Tooltip')));
    u2_chk([w '_state_number'], @() get(uitoggletool(tb, 'State', 1), 'State'));
    u2_chk([w '_state_word'], @() get(uitoggletool(tb, 'State', 'bogus'), 'State'));
    u2_chk([w '_push_state'], @() get(uipushtool(tb), 'State'));
    u2_chk([w '_tool_position'], @() get(p1, 'Position'));
    u2_chk([w '_toolbar_position'], @() get(tb, 'Position'));
    other = uitoolbar(p);
    p1.Parent = other;
    fprintf('CHK|%s_tool_moved|%d %s|exact\n', w, numel(other.Children), get(p1.Parent, 'Type'));
    u2_chk([w '_toolbar_hidden'], @() get(uitoolbar(p, 'Visible', 'off'), 'Visible'));
    spare = uitoolbar(p); tool = uipushtool(spare);
    delete(spare);
    fprintf('CHK|%s_toolbar_deleted|%d|exact\n', w, isgraphics(tool));
    fprintf('CHK|%s_ancestor|%s|exact\n', w, get(ancestor(g1, 'figure'), 'Type'));
    fprintf('CHK|%s_findobj_tools|%d|exact\n', w, numel(findobj(tb, 'Type', 'uitoggletool')));

    % A State a script writes runs the callback of that state, once the queue is drained, and
    % never the ClickedCallback.
    setappdata(p, 'log', '');
    g = uitoggletool(tb, 'OnCallback', @(~, ~) setappdata(p, 'log', [getappdata(p, 'log') 'on ']), ...
        'OffCallback', @(~, ~) setappdata(p, 'log', [getappdata(p, 'log') 'off ']), ...
        'ClickedCallback', @(~, ~) setappdata(p, 'log', [getappdata(p, 'log') 'click ']));
    g.State = 'on'; drawnow;
    g.State = 'on'; drawnow;
    g.State = 'off'; drawnow;
    fprintf('CHK|%s_state_written|[%s]|exact\n', w, getappdata(p, 'log'));
    delete(p);
end
