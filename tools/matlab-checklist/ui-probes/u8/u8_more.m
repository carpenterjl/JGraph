function u8_more
% U8 probe, second round: the order of a figure's children, tables in grids, small facts. Headless.
kinds = {'F', @() figure('Visible', 'off', 'Position', [100 100 560 420]); 'U', @() uifigure('Visible', 'off', 'Position', [100 100 560 420])};
for q = 1:2
    K = kinds{q, 1}; p = kinds{q, 2}();
    pre = @(s) [K ' ' s];
    % creation order: control, menu, contextmenu, toolbar, panel, axes, table, tabgroup, menu
    if q == 1, c = uicontrol(p); else, c = uibutton(p); end %#ok<NASGU>
    uimenu(p, 'Text', 'm1'); uicontextmenu(p); uitoolbar(p); uipanel(p);
    if q == 1, axes(p); else, uiaxes(p); end
    uitable(p); uitabgroup(p); uimenu(p, 'Text', 'm2');
    tryp(pre('Children types in order'), @() types(get(p, 'Children')));
    tryp(pre('allchild types in order'), @() types(allchild(p)));
    tryp(pre('findobj types'), @() types(findobj(p)));
    tryp(pre('findall -depth 1'), @() types(findall(p, '-depth', 1)));
    delete(allchild(p));

    % a tab's children order, and a tabgroup among components
    tg = uitabgroup(p); t = uitab(tg);
    if q == 1, uicontrol(t); axes(t); else, uibutton(t); uiaxes(t); end
    uipanel(t); uitable(t);
    tryp(pre('tab Children types'), @() types(t.Children));
    delete(allchild(p));

    % SelectedTab at creation, in a making call
    tg = uitabgroup(p); a = uitab(tg, 'Title', 'a'); b = uitab(tg, 'Title', 'b'); %#ok<NASGU>
    tryp(pre('uitabgroup(SelectedTab) in maker'), @() get(uitabgroup(p, 'SelectedTab', b), 'SelectedTab'));
    tryp(pre('tg.SelectedTab = a (same)'), @() seltab(tg, a));
    tryp(pre('tg numeric index into Children'), @() tg.Children(2).Title);
    tryp(pre('SelectedTab deleted handle'), @() seldel(tg));
    delete(allchild(p));

    % tab group sizes by Units
    tg = uitabgroup(p, 'Units', 'pixels', 'Position', [20 20 300 200]);
    t = uitab(tg, 'Title', 'x');
    drawnow; pause(0.5);
    tryp(pre('tab Units normalized Position'), @() unitpos(t, 'normalized'));
    tryp(pre('tab Units points Position'), @() unitpos(t, 'points'));
    tryp(pre('tab InnerPosition set'), @() set(t, 'InnerPosition', [1 1 5 5]));
    tryp(pre('tab OuterPosition set'), @() set(t, 'OuterPosition', [1 1 5 5]));
    tryp(pre('tg InnerPosition set'), @() set(tg, 'InnerPosition', [1 1 5 5]));
    tryp(pre('tg OuterPosition set'), @() setget(tg, 'OuterPosition', [30 30 200 100]));
    tryp(pre('tg Position after'), @() tg.Position);
    tryp(pre('tab Scrollable on'), @() char(get(uitab(tg, 'Scrollable', 'on'), 'Scrollable')));
    tryp(pre('tab BackgroundColor none'), @() get(uitab(tg, 'BackgroundColor', 'none'), 'BackgroundColor'));
    tryp(pre('tab ForegroundColor none'), @() get(uitab(tg, 'ForegroundColor', 'none'), 'ForegroundColor'));
    tryp(pre('tab Tooltip cell'), @() get(uitab(tg, 'Tooltip', {'a', 'b'}), 'Tooltip'));
    tryp(pre('tab TooltipString'), @() get(uitab(tg, 'TooltipString', 'x'), 'Tooltip'));
    tryp(pre('tg TooltipString'), @() get(uitabgroup(p, 'TooltipString', 'x'), 'Tooltip'));
    tryp(pre('tab Enable'), @() get(t, 'Enable'));
    tryp(pre('tab ButtonDownFcn'), @() class(get(uitab(tg, 'ButtonDownFcn', @sin), 'ButtonDownFcn')));
    delete(allchild(p));

    % table sizes and the rest
    tb = uitable(p, 'Data', magic(4));
    tryp(pre('table size(Data) after Data(:,5)=1'), @() growcol(tb));
    tryp(pre('table Data = {} then class'), @() setclass(tb, {}));
    tryp(pre('table Data = [] then class size'), @() setclass(tb, []));
    tryp(pre('table Data string scalar'), @() setclass(tb, "s"));
    tryp(pre('table Data = int8'), @() setclass(tb, int8([1 2])));
    tryp(pre('table Data cell with string'), @() setclass(tb, {"s", 1}));
    tryp(pre('table Data cell with empty'), @() setclass(tb, {[], 1}));
    tryp(pre('table Data cell with int8'), @() setclass(tb, {int8(1), single(2)}));
    tryp(pre('table Data cell with char matrix'), @() setclass(tb, {['ab'; 'cd']}));
    tryp(pre('table Data sparse'), @() setclass(tb, sparse([1 0; 0 1])));
    tryp(pre('table ColumnName numbered after set cell then back'), @() cnback(tb));
    tryp(pre('table RowName numbered written'), @() get(uitable(p, 'RowName', 'numbered'), 'RowName'));
    tryp(pre('table ColumnName "numbered" string'), @() get(uitable(p, 'ColumnName', "numbered"), 'ColumnName'));
    tryp(pre('table ColumnName Numbered caps'), @() get(uitable(p, 'ColumnName', 'Numbered'), 'ColumnName'));
    tryp(pre('table ColumnName abc char'), @() get(uitable(p, 'ColumnName', 'abc'), 'ColumnName'));
    tryp(pre('table ColumnName cell with empty'), @() get(uitable(p, 'ColumnName', {'a', '', 'c'}), 'ColumnName'));
    tryp(pre('table ColumnName cell with []'), @() get(uitable(p, 'ColumnName', {'a', [], 'c'}), 'ColumnName'));
    tryp(pre('table ColumnName cell nested'), @() get(uitable(p, 'ColumnName', {'a', {'b'}}), 'ColumnName'));
    tryp(pre('table ColumnName 2-D cell'), @() get(uitable(p, 'ColumnName', {'a', 'b'; 'c', 'd'}), 'ColumnName'));
    tryp(pre('table ColumnName logical'), @() get(uitable(p, 'ColumnName', true), 'ColumnName'));
    tryp(pre('table ColumnName struct'), @() get(uitable(p, 'ColumnName', struct('a', 1)), 'ColumnName'));
    tryp(pre('table RowName 2.5'), @() get(uitable(p, 'RowName', 2.5), 'RowName'));
    tryp(pre('table RowName [1 2;3 4]'), @() get(uitable(p, 'RowName', [1 2; 3 4]), 'RowName'));
    tryp(pre('table RowName string col'), @() get(uitable(p, 'RowName', ["a"; "b"]), 'RowName'));
    tryp(pre('table ColumnEditable 1 double'), @() get(uitable(p, 'ColumnEditable', 1), 'ColumnEditable'));
    tryp(pre('table ColumnEditable on'), @() get(uitable(p, 'ColumnEditable', 'on'), 'ColumnEditable'));
    tryp(pre('table ColumnSortable 1 double'), @() get(uitable(p, 'ColumnSortable', 1), 'ColumnSortable'));
    tryp(pre('table ColumnSortable [true;false]'), @() get(uitable(p, 'ColumnSortable', [true; false]), 'ColumnSortable'));
    tryp(pre('table ColumnSortable []'), @() get(uitable(p, 'ColumnSortable', []), 'ColumnSortable'));
    tryp(pre('table ColumnFormat []'), @() get(uitable(p, 'ColumnFormat', []), 'ColumnFormat'));
    tryp(pre('table ColumnFormat char'), @() get(uitable(p, 'ColumnFormat', 'numeric'), 'ColumnFormat'));
    tryp(pre('table ColumnFormat {NUMERIC}'), @() get(uitable(p, 'ColumnFormat', {'NUMERIC'}), 'ColumnFormat'));
    tryp(pre('table ColumnFormat {short e}'), @() get(uitable(p, 'ColumnFormat', {'short e'}), 'ColumnFormat'));
    tryp(pre('table ColumnFormat {"char"}'), @() get(uitable(p, 'ColumnFormat', {"char"}), 'ColumnFormat'));
    tryp(pre('table ColumnFormat {{}}'), @() get(uitable(p, 'ColumnFormat', {{}}), 'ColumnFormat'));
    tryp(pre('table ColumnFormat {5}'), @() get(uitable(p, 'ColumnFormat', {5}), 'ColumnFormat'));
    tryp(pre('table ColumnWidth ["auto" "fit"]'), @() get(uitable(p, 'ColumnWidth', ["auto" "fit"]), 'ColumnWidth'));
    tryp(pre('table ColumnWidth {"1x"}'), @() get(uitable(p, 'ColumnWidth', {"1x"}), 'ColumnWidth'));
    tryp(pre('table ColumnWidth {}'), @() get(uitable(p, 'ColumnWidth', {}), 'ColumnWidth'));
    tryp(pre('table ColumnWidth AUTO'), @() get(uitable(p, 'ColumnWidth', 'AUTO'), 'ColumnWidth'));
    tryp(pre('table ColumnWidth {AUTO}'), @() get(uitable(p, 'ColumnWidth', {'AUTO'}), 'ColumnWidth'));
    tryp(pre('table ColumnWidth {2X}'), @() get(uitable(p, 'ColumnWidth', {'2X'}), 'ColumnWidth'));
    tryp(pre('table ColumnWidth {Inf}'), @() get(uitable(p, 'ColumnWidth', {Inf}), 'ColumnWidth'));
    tryp(pre('table ColumnWidth {NaN}'), @() get(uitable(p, 'ColumnWidth', {NaN}), 'ColumnWidth'));
    tryp(pre('table ColumnWidth {[1 2]}'), @() get(uitable(p, 'ColumnWidth', {[1 2]}), 'ColumnWidth'));
    tryp(pre('table ColumnWidth {true}'), @() get(uitable(p, 'ColumnWidth', {true}), 'ColumnWidth'));
    tryp(pre('table SelectionType ROW'), @() get(uitable(p, 'SelectionType', 'ROW'), 'SelectionType'));
    tryp(pre('table SelectionType r'), @() get(uitable(p, 'SelectionType', 'r'), 'SelectionType'));
    tryp(pre('table SelectionType bogus'), @() get(uitable(p, 'SelectionType', 'bogus'), 'SelectionType'));
    tryp(pre('table Multiselect 0'), @() char(get(uitable(p, 'Multiselect', 0), 'Multiselect')));
    tryp(pre('table RowStriping x'), @() char(get(uitable(p, 'RowStriping', 'x'), 'RowStriping')));
    tryp(pre('table FontName 5'), @() get(uitable(p, 'FontName', 5), 'FontName'));
    tryp(pre('table FontName empty'), @() get(uitable(p, 'FontName', ''), 'FontName'));
    tryp(pre('table FontSize 0'), @() get(uitable(p, 'FontSize', 0), 'FontSize'));
    tryp(pre('table FontSize -1'), @() get(uitable(p, 'FontSize', -1), 'FontSize'));
    tryp(pre('table FontSize [1 2]'), @() get(uitable(p, 'FontSize', [1 2]), 'FontSize'));
    tryp(pre('table FontSize x'), @() get(uitable(p, 'FontSize', 'x'), 'FontSize'));
    tryp(pre('table FontWeight BOLD'), @() get(uitable(p, 'FontWeight', 'BOLD'), 'FontWeight'));
    tryp(pre('table FontWeight light'), @() get(uitable(p, 'FontWeight', 'light'), 'FontWeight'));
    tryp(pre('table FontAngle oblique'), @() get(uitable(p, 'FontAngle', 'oblique'), 'FontAngle'));
    tryp(pre('table FontUnits normalized size'), @() get(uitable(p, 'FontUnits', 'normalized'), 'FontSize'));
    tryp(pre('table Position 3 numbers'), @() get(uitable(p, 'Position', [1 2 3]), 'Position'));
    tryp(pre('table Position negative size'), @() get(uitable(p, 'Position', [1 2 -3 4]), 'Position'));
    tryp(pre('table InnerPosition set'), @() get(uitable(p, 'InnerPosition', [1 2 30 40]), 'Position'));
    tryp(pre('table OuterPosition set'), @() get(uitable(p, 'OuterPosition', [1 2 30 40]), 'Position'));
    tryp(pre('table Extent set'), @() set(uitable(p), 'Extent', [0 0 1 1]));
    tryp(pre('table Extent 0 data'), @() get(uitable(p), 'Extent'));
    tryp(pre('table Extent 2x2'), @() get(uitable(p, 'Data', magic(2)), 'Extent'));
    tryp(pre('table Extent 10x10'), @() get(uitable(p, 'Data', magic(10)), 'Extent'));
    tryp(pre('table DisplayData set'), @() set(uitable(p), 'DisplayData', 1));
    tryp(pre('table StyleConfigurations set'), @() set(uitable(p), 'StyleConfigurations', 1));
    tryp(pre('table Children set'), @() set(uitable(p), 'Children', []));
    tryp(pre('table KeyPressFcn'), @() class(get(uitable(p, 'KeyPressFcn', @sin), 'KeyPressFcn')));
    tryp(pre('table ButtonDownFcn'), @() class(get(uitable(p, 'ButtonDownFcn', @sin), 'ButtonDownFcn')));
    delete(allchild(p));
    if q == 2
        g = uigridlayout(p, [2 2]);
        tb = uitable(g, 'Data', magic(3));
        drawnow; pause(0.5);
        tryp(pre('table in grid Position'), @() tb.Position);
        tryp(pre('table in grid Layout'), @() [tb.Layout.Row tb.Layout.Column]);
        g.RowHeight = {'fit', '1x'}; g.ColumnWidth = {'fit', '1x'};
        drawnow; pause(0.5);
        tryp(pre('table in fit cell Position'), @() tb.Position);
        tg = uitabgroup(g);
        drawnow; pause(0.5);
        tryp(pre('tabgroup in grid Position'), @() tg.Position);
        g.RowHeight = {'fit', 'fit'}; g.ColumnWidth = {'fit', 'fit'};
        drawnow; pause(0.5);
        tryp(pre('tabgroup in fit cell Position'), @() tg.Position);
        t = uitab(tg, 'Title', 'T'); g2 = uigridlayout(t, [1 1]); b = uibutton(g2);
        drawnow; pause(0.5);
        tryp(pre('button in grid in tab Position'), @() b.Position);
        tryp(pre('grid in tab Position'), @() g2.Position);
        delete(allchild(p));
    end
    delete(p);
end
delete(findall(groot, 'Type', 'figure'));
end

function s = types(h)
s = sprintf('%s %s', mat2str(size(h)), strjoin(arrayfun(@(m) m.Type, h(:)', 'UniformOutput', false), ','));
end

function s = seltab(tg, t)
tg.SelectedTab = t;
s = tg.SelectedTab.Title;
end

function s = seldel(tg)
t = uitab(tg, 'Title', 'gone'); delete(t);
tg.SelectedTab = t;
s = tg.SelectedTab.Title;
end

function s = unitpos(t, u)
old = t.Units; t.Units = u;
s = mat2str(t.Position, 6);
t.Units = old;
end

function s = setget(h, name, v)
set(h, name, v);
s = mat2str(get(h, name), 6);
end

function s = growcol(tb)
tb.Data(:, 5) = 1;
s = mat2str(size(tb.Data));
end

function s = setclass(tb, v)
tb.Data = v;
s = sprintf('%s %s', class(tb.Data), mat2str(size(tb.Data)));
end

function s = cnback(tb)
tb.ColumnName = {'a', 'b'}; tb.ColumnName = 'numbered';
s = v2s(tb.ColumnName);
end
