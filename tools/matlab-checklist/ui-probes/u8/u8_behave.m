function u8_behave
% U8 probe: how tables, tabs, menus and toolbars behave. Headless.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
kinds = {'F', f; 'U', uf};

%% event data classes
names = {'CellEditData', 'CellSelectionChangeData', 'TableSelectionChangedData', 'SelectionChangedData', 'ActionData', ...
    'DisplayDataChangedData', 'ClickedData', 'DoubleClickedData', 'MenuSelectedData', 'TableClickedData', ...
    'TableDoubleClickedData', 'ContextMenuOpeningData', 'TableInteraction', 'SizeChangedData'};
pk = {'matlab.ui.eventdata.', 'matlab.ui.eventdata.internal.', 'matlab.ui.control.'};
for k = 1:numel(names)
    for q = 1:numel(pk)
        mc = meta.class.fromName([pk{q} names{k}]);
        if isempty(mc), continue; end
        pl = mc.PropertyList;
        pub = pl(arrayfun(@(p) ischar(p.GetAccess) && strcmp(p.GetAccess, 'public') && ~p.Hidden, pl));
        sup = arrayfun(@(s) s.Name, mc.SuperclassList, 'UniformOutput', false);
        fprintf('EVT %s%s : props=%s supers=%s\n', pk{q}, names{k}, strjoin({pub.Name}, ','), strjoin(sup, ','));
    end
end

for q = 1:2
    K = kinds{q, 1}; p = kinds{q, 2};
    pre = @(s) [K ' ' s];

    %% table data
    datas = {'magic3', magic(3); 'cell', {1, 'a', true; 2, 'b', false}; 'logical', [true false; false true]; ...
        'table', table([1; 2], {'a'; 'b'}, [true; false], 'VariableNames', {'N', 'S', 'L'}); ...
        'string', ["a" "b"; "c" "d"]; 'single', single([1.5 2.5]); 'int8', int8([1 2; 3 4]); 'char', 'abc'; 'cellstr', {'a', 'b'}; ...
        'empty', []; 'zeros03', zeros(0, 3); 'cellnested', {1, {2}}; 'cellmat', {1, [1 2]}; 'struct', struct('a', 1); ...
        'complex', [1+2i 3]; 'nan', [NaN Inf -Inf]; 'datetime', datetime(2020, 1, 1:2)'; 'categorical', categorical({'x'; 'y'}); ...
        'tabledt', table(datetime(2020, 1, 1:2)', categorical({'x'; 'y'}), ["s"; "t"]); 'nd', zeros(2, 2, 2); 'cellfn', {@sin}; ...
        'col', (1:4)'; 'tablerows', table([1; 2], 'RowNames', {'r1', 'r2'}); 'duration', seconds(1:2)'};
    for k = 1:size(datas, 1)
        tryp(pre(['Data ' datas{k, 1}]), @() datarep(uitable(p, 'Data', datas{k, 2})));
        delete(allchild(p));
    end
    tryp(pre('ColumnName numbered read with 3 cols'), @() get(uitable(p, 'Data', magic(3)), 'ColumnName'));
    tryp(pre('ColumnName read with table data'), @() get(uitable(p, 'Data', datas{4, 2}), 'ColumnName'));
    tryp(pre('RowName read with table rows'), @() get(uitable(p, 'Data', datas{23, 2}), 'RowName'));
    tryp(pre('ColumnName {A,B} with 3 cols'), @() get(uitable(p, 'Data', magic(3), 'ColumnName', {'A', 'B'}), 'ColumnName'));
    tryp(pre('ColumnName {A;B;C} column cell'), @() get(uitable(p, 'Data', magic(3), 'ColumnName', {'A'; 'B'; 'C'}), 'ColumnName'));
    tryp(pre('ColumnName {A,B,C} row cell'), @() get(uitable(p, 'Data', magic(3), 'ColumnName', {'A', 'B', 'C'}), 'ColumnName'));
    tryp(pre('ColumnName string row'), @() get(uitable(p, 'ColumnName', ["A" "B"]), 'ColumnName'));
    tryp(pre('ColumnName {1,2}'), @() get(uitable(p, 'ColumnName', {1, 2}), 'ColumnName'));
    tryp(pre('ColumnName {A|B}'), @() get(uitable(p, 'ColumnName', {'A|B', 'C'}), 'ColumnName'));
    tryp(pre('ColumnName []'), @() get(uitable(p, 'ColumnName', []), 'ColumnName'));
    tryp(pre('ColumnName {}'), @() get(uitable(p, 'ColumnName', {}), 'ColumnName'));
    tryp(pre('ColumnName char matrix'), @() get(uitable(p, 'ColumnName', ['ab'; 'cd']), 'ColumnName'));
    tryp(pre('ColumnName 1:3'), @() get(uitable(p, 'ColumnName', 1:3), 'ColumnName'));
    tryp(pre('RowName {r1;r2}'), @() get(uitable(p, 'RowName', {'r1'; 'r2'}), 'RowName'));
    tryp(pre('RowName {r1,r2} row'), @() get(uitable(p, 'RowName', {'r1', 'r2'}), 'RowName'));
    tryp(pre('RowName []'), @() get(uitable(p, 'RowName', []), 'RowName'));
    tryp(pre('RowName x'), @() get(uitable(p, 'RowName', 'x'), 'RowName'));
    tryp(pre('ColumnEditable true'), @() get(uitable(p, 'ColumnEditable', true), 'ColumnEditable'));
    tryp(pre('ColumnEditable [1 0 1] double'), @() get(uitable(p, 'ColumnEditable', [1 0 1]), 'ColumnEditable'));
    tryp(pre('ColumnEditable [true false]'), @() get(uitable(p, 'ColumnEditable', [true false]), 'ColumnEditable'));
    tryp(pre('ColumnEditable [true;false]'), @() get(uitable(p, 'ColumnEditable', [true; false]), 'ColumnEditable'));
    tryp(pre('ColumnEditable []'), @() get(uitable(p, 'ColumnEditable', []), 'ColumnEditable'));
    tryp(pre('ColumnSortable true'), @() get(uitable(p, 'ColumnSortable', true), 'ColumnSortable'));
    tryp(pre('ColumnSortable [true false]'), @() get(uitable(p, 'ColumnSortable', [true false]), 'ColumnSortable'));
    fm = {'char', 'numeric', 'logical', 'short', 'long', 'shortE', 'longE', 'shortG', 'longG', 'shortEng', 'longEng', 'bank', '+', 'rat', 'hex', 'bogus', ''};
    for k = 1:numel(fm)
        tryp(pre(['ColumnFormat {' fm{k} '}']), @() get(uitable(p, 'ColumnFormat', fm(k)), 'ColumnFormat'));
    end
    tryp(pre('ColumnFormat {[]}'), @() get(uitable(p, 'ColumnFormat', {[]}), 'ColumnFormat'));
    tryp(pre('ColumnFormat {{a,b}}'), @() get(uitable(p, 'ColumnFormat', {{'a', 'b'}}), 'ColumnFormat'));
    tryp(pre('ColumnFormat {{a;b}}'), @() get(uitable(p, 'ColumnFormat', {{'a'; 'b'}}), 'ColumnFormat'));
    tryp(pre('ColumnFormat {numeric;char} column'), @() get(uitable(p, 'ColumnFormat', {'numeric'; 'char'}), 'ColumnFormat'));
    tryp(pre('ColumnFormat ["numeric" "char"]'), @() get(uitable(p, 'ColumnFormat', ["numeric" "char"]), 'ColumnFormat'));
    tryp(pre('ColumnFormat {{1,2}}'), @() get(uitable(p, 'ColumnFormat', {{1, 2}}), 'ColumnFormat'));
    cw = {'auto', 'fit', '1x', {'auto'}, {'fit'}, {'1x'}, {50}, {50, 'auto'}, {'1x', '2x'}, {'fit', 50, '2x', 'auto'}, 50, [50 60], {-1}, {0}, {'0x'}, {'x'}, {50; 60}, {1.5}, {'1.5x'}, {int8(5)}, {"auto"}, "auto"};
    for k = 1:numel(cw)
        tryp(pre(['ColumnWidth ' v2s(cw{k})]), @() get(uitable(p, 'ColumnWidth', cw{k}), 'ColumnWidth'));
    end
    bc = {[1 0 0], [1 0 0; 0 1 0; 0 0 1], 'red', {'red', 'blue'}, ["red" "blue"], [1 1 1 1], 'none', '#ff0000', zeros(0, 3)};
    for k = 1:numel(bc)
        tryp(pre(['BackgroundColor ' v2s(bc{k})]), @() get(uitable(p, 'BackgroundColor', bc{k}), 'BackgroundColor'));
    end
    tryp(pre('ForegroundColor red'), @() get(uitable(p, 'ForegroundColor', 'red'), 'ForegroundColor'));
    tryp(pre('Extent'), @() get(uitable(p, 'Data', magic(3)), 'Extent'));
    tryp(pre('Selection default with data'), @() get(uitable(p, 'Data', magic(3)), 'Selection'));
    tryp(pre('Selection [1 2] cell'), @() get(uitable(p, 'Data', magic(3), 'Selection', [1 2]), 'Selection'));
    tryp(pre('Selection [1 2;2 3] cell'), @() get(uitable(p, 'Data', magic(3), 'Selection', [1 2; 2 3]), 'Selection'));
    tryp(pre('Selection [9 9] out of range'), @() get(uitable(p, 'Data', magic(3), 'Selection', [9 9]), 'Selection'));
    tryp(pre('Selection [1 2 3] in cell mode'), @() get(uitable(p, 'Data', magic(3), 'Selection', [1 2 3]), 'Selection'));
    tryp(pre('Selection row [1 3]'), @() get(uitable(p, 'Data', magic(3), 'SelectionType', 'row', 'Selection', [1 3]), 'Selection'));
    tryp(pre('Selection row [3 1] order kept'), @() get(uitable(p, 'Data', magic(3), 'SelectionType', 'row', 'Selection', [3 1]), 'Selection'));
    tryp(pre('Selection row [1;3] column'), @() get(uitable(p, 'Data', magic(3), 'SelectionType', 'row', 'Selection', [1; 3]), 'Selection'));
    tryp(pre('Selection column 2'), @() get(uitable(p, 'Data', magic(3), 'SelectionType', 'column', 'Selection', 2), 'Selection'));
    tryp(pre('Selection row 0'), @() get(uitable(p, 'Data', magic(3), 'SelectionType', 'row', 'Selection', 0), 'Selection'));
    tryp(pre('Selection row 1.5'), @() get(uitable(p, 'Data', magic(3), 'SelectionType', 'row', 'Selection', 1.5), 'Selection'));
    tryp(pre('Selection two rows Multiselect off'), @() get(uitable(p, 'Data', magic(3), 'SelectionType', 'row', 'Multiselect', 'off', 'Selection', [1 3]), 'Selection'));
    tryp(pre('Selection then SelectionType change'), @() selthen(uitable(p, 'Data', magic(3), 'Selection', [1 2])));
    tryp(pre('Selection then Data change'), @() seldata(uitable(p, 'Data', magic(3), 'Selection', [1 2])));
    tryp(pre('Selection with no data'), @() get(uitable(p, 'Selection', [1 1]), 'Selection'));
    tryp(pre('DisplayData numeric'), @() datarep2(get(uitable(p, 'Data', magic(3)), 'DisplayData')));
    tryp(pre('DisplaySelection'), @() get(uitable(p, 'Data', magic(3), 'Selection', [1 2]), 'DisplaySelection'));
    tryp(pre('Data(2,2) = 99 indexed write'), @() idxwrite(uitable(p, 'Data', magic(3))));
    tryp(pre('Data{2,2} = x on cell'), @() idxcell(uitable(p, 'Data', {1, 'a'; 2, 'b'})));
    tryp(pre('scroll bottom'), @() scroll(uitable(p, 'Data', magic(3)), 'bottom'));
    tryp(pre('scroll row 2'), @() scroll(uitable(p, 'Data', magic(3)), 'row', 2));
    tryp(pre('table Position in panel'), @() get(uitable(uipanel(p)), 'Position'));
    tryp(pre('table Units normalized Position'), @() get(uitable(p, 'Units', 'normalized'), 'Position'));
    tryp(pre('table Units normalized set keeps place'), @() unitsthen(uitable(p)));
    tryp(pre('table FontSize 20 points pixels'), @() fontthen(uitable(p)));
    tryp(pre('table Enable inactive'), @() get(uitable(p, 'Enable', 'inactive'), 'Enable'));
    tryp(pre('table Enable true'), @() get(uitable(p, 'Enable', true), 'Enable'));
    tryp(pre('table TooltipString'), @() get(uitable(p, 'TooltipString', 'x'), 'Tooltip'));
    tryp(pre('table Tooltip cell'), @() get(uitable(p, 'Tooltip', {'a', 'b'}), 'Tooltip'));
    tryp(pre('table RearrangeableColumns alias'), @() get(uitable(p, 'RearrangeableColumns', 'on'), 'ColumnRearrangeable'));
    tryp(pre('table CellEditCallback forms'), @() cbforms(uitable(p), 'CellEditCallback'));
    tryp(pre('table isprop Extent/Value/String'), @() sprintf('%d%d%d', isprop(uitable(p), 'Extent'), isprop(uitable(p), 'Value'), isprop(uitable(p), 'String')));
    delete(allchild(p));

    %% tabs
    tg = uitabgroup(p);
    tryp(pre('tg SelectedTab none'), @() v2s(tg.SelectedTab));
    t1 = uitab(tg, 'Title', 'One'); t2 = uitab(tg, 'Title', 'Two'); t3 = uitab(tg, 'Title', 'Three');
    tryp(pre('tg SelectedTab after three'), @() tg.SelectedTab.Title);
    tryp(pre('tg Children titles'), @() strjoin(arrayfun(@(t) t.Title, tg.Children', 'UniformOutput', false), ','));
    tryp(pre('tg Children size'), @() size(tg.Children));
    tryp(pre('tg Position'), @() tg.Position);
    tryp(pre('tg getpixelposition'), @() getpixelposition(tg));
    tryp(pre('tg InnerPosition'), @() tg.InnerPosition);
    tryp(pre('t1 Position'), @() t1.Position);
    tryp(pre('t1 Units'), @() t1.Units);
    tryp(pre('t1 InnerPosition'), @() t1.InnerPosition);
    tryp(pre('t1 OuterPosition'), @() t1.OuterPosition);
    tryp(pre('t1 getpixelposition'), @() getpixelposition(t1));
    tryp(pre('t1 getpixelposition recursive'), @() getpixelposition(t1, true));
    tryp(pre('t2 Position (unselected)'), @() t2.Position);
    tryp(pre('t2 getpixelposition (unselected)'), @() getpixelposition(t2));
    tryp(pre('t1 set Position'), @() set(t1, 'Position', [0 0 0.5 0.5]));
    tryp(pre('t1 set Units pixels then Position'), @() tabunits(t1));
    tryp(pre('select t2'), @() seltab(tg, t2));
    tryp(pre('select by index 3'), @() seltab(tg, 3));
    tryp(pre('select []'), @() seltab(tg, []));
    tryp(pre('select foreign tab'), @() seltab(tg, uitab(uitabgroup(p, 'Position', tg.Position))));
    tryp(pre('select a panel'), @() seltab(tg, uipanel(p)));
    delete(setdiff(allchild(p), tg));
    tg.SelectedTab = t2;
    delete(t2);
    tryp(pre('after deleting selected middle'), @() tg.SelectedTab.Title);
    t4 = uitab(tg, 'Title', 'Four');
    tg.SelectedTab = t4; delete(t4);
    tryp(pre('after deleting selected last'), @() tg.SelectedTab.Title);
    tg.SelectedTab = t1; delete(t1);
    tryp(pre('after deleting selected first'), @() tg.SelectedTab.Title);
    delete(t3);
    tryp(pre('after deleting the only tab'), @() v2s(tg.SelectedTab));
    delete(tg);
    tg = uitabgroup(p); a = uitab(tg, 'Title', 'A'); b = uitab(tg, 'Title', 'B'); c = uitab(tg, 'Title', 'C');
    tg.SelectedTab = b;
    tryp(pre('reorder Children flip'), @() reorder(tg));
    tryp(pre('uistack tab top'), @() stacktab(tg, a));
    tryp(pre('tab Parent <- other group'), @() movetab(c, uitabgroup(p)));
    tryp(pre('source group after move'), @() [strjoin(arrayfun(@(t) t.Title, tg.Children', 'UniformOutput', false), ',') ' sel=' tg.SelectedTab.Title]);
    delete(allchild(p));
    locs = {'top', 'bottom', 'left', 'right'};
    for k = 1:4
        tg = uitabgroup(p, 'TabLocation', locs{k}, 'Units', 'pixels', 'Position', [20 20 300 200]);
        t = uitab(tg, 'Title', 'Alpha'); uitab(tg, 'Title', 'A much longer title');
        drawnow; pause(0.3);
        tryp(pre(['loc ' locs{k} ' tab Position']), @() pix(t, 'Position'));
        tryp(pre(['loc ' locs{k} ' tab InnerPosition']), @() pix(t, 'InnerPosition'));
        tryp(pre(['loc ' locs{k} ' tab OuterPosition']), @() pix(t, 'OuterPosition'));
        tryp(pre(['loc ' locs{k} ' tg InnerPosition']), @() pix(tg, 'InnerPosition'));
        tryp(pre(['loc ' locs{k} ' child getpixelposition rec']), @() childpix(t, q));
        delete(allchild(p));
    end
    tg = uitabgroup(p, 'Units', 'pixels', 'Position', [20 20 300 200]);
    tryp(pre('empty group InnerPosition'), @() pix(tg, 'InnerPosition'));
    t = uitab(tg);
    tryp(pre('untitled tab Position px'), @() pix(t, 'Position'));
    t.Title = 'Now titled';
    drawnow; pause(0.3);
    tryp(pre('titled later Position px'), @() pix(t, 'Position'));
    tryp(pre('tab Title cell'), @() get(uitab(tg, 'Title', {'a', 'b'}), 'Title'));
    tryp(pre('tab Title string'), @() get(uitab(tg, 'Title', "s"), 'Title'));
    tryp(pre('tab Title 5'), @() get(uitab(tg, 'Title', 5), 'Title'));
    tryp(pre('tab Title char matrix'), @() get(uitab(tg, 'Title', ['ab'; 'cd']), 'Title'));
    tryp(pre('axes in tab Parent class'), @() class(get(axesin(t, q), 'Parent')));
    tryp(pre('axes in tab Position Units'), @() axrep(axesin(t, q)));
    tryp(pre('panel in tab default Position'), @() get(uipanel(t), 'Position'));
    tryp(pre('tabgroup in tab'), @() class(uitabgroup(t)));
    tryp(pre('tab Visible'), @() get(t, 'Visible'));
    tryp(pre('tab findobj uitab count'), @() numel(findobj(p, 'Type', 'uitab')));
    tryp(pre('tab ancestor figure'), @() class(ancestor(t, 'figure')));
    tryp(pre('SelectionChangedFcn on code select'), @() codesel(p));
    tryp(pre('tg Visible off children Visible'), @() tgvis(p));
    tryp(pre('tg SizeChangedFcn'), @() cbforms(uitabgroup(p), 'SizeChangedFcn'));
    tryp(pre('tg delete(tg) children valid'), @() tgdel(p));
    tryp(pre('tg Units normalized -> pixels Position'), @() tgunits(p));
    tryp(pre('tg in panel default Position'), @() get(uitabgroup(uipanel(p)), 'Position'));
    delete(allchild(p));

    %% menus
    tryp(pre('builtin menus findall'), @() numel(findall(p, 'Type', 'uimenu')));
    tryp(pre('MenuBar'), @() get(p, 'MenuBar'));
    m1 = uimenu(p, 'Text', 'A'); m2 = uimenu(p, 'Text', 'B'); m3 = uimenu(p, 'Text', 'C');
    tryp(pre('menu Positions'), @() [m1.Position m2.Position m3.Position]);
    tryp(pre('figure Children texts'), @() texts(get(p, 'Children')));
    tryp(pre('allchild texts'), @() texts(allchild(p)));
    m3.Position = 1;
    tryp(pre('after m3.Position=1 Positions'), @() [m1.Position m2.Position m3.Position]);
    tryp(pre('after m3.Position=1 Children'), @() texts(get(p, 'Children')));
    tryp(pre('m1.Position=9'), @() setpos(m1, 9, [m1 m2 m3]));
    tryp(pre('m1.Position=0'), @() setpos(m1, 0, [m1 m2 m3]));
    tryp(pre('m1.Position=1.5'), @() setpos(m1, 1.5, [m1 m2 m3]));
    s1 = uimenu(m1, 'Text', 'a1'); s2 = uimenu(m1, 'Text', 'a2'); s3 = uimenu(m1, 'Text', 'a3', 'Separator', 'on');
    tryp(pre('submenu Positions'), @() [s1.Position s2.Position s3.Position]);
    tryp(pre('submenu Children texts'), @() texts(m1.Children));
    delete(s2);
    tryp(pre('after delete s2 Positions'), @() [s1.Position s3.Position]);
    tryp(pre('uimenu Position at creation'), @() mkpos(m1));
    tryp(pre('Label alias'), @() get(uimenu(p, 'Label', 'L'), 'Text'));
    tryp(pre('Label read'), @() get(uimenu(p, 'Text', 'L'), 'Label'));
    tryp(pre('Callback alias'), @() func2str(get(uimenu(p, 'Callback', @sin), 'MenuSelectedFcn')));
    tryp(pre('Callback read'), @() func2str(get(uimenu(p, 'MenuSelectedFcn', @sin), 'Callback')));
    tryp(pre('Text &File'), @() get(uimenu(p, 'Text', '&File'), 'Text'));
    tryp(pre('Text cell'), @() get(uimenu(p, 'Text', {'a'}), 'Text'));
    tryp(pre('Text string'), @() get(uimenu(p, 'Text', "s"), 'Text'));
    tryp(pre('Text 5'), @() get(uimenu(p, 'Text', 5), 'Text'));
    acc = {'a', 'A', 'ab', '1', '', ' ', "s", 5, '+'};
    for k = 1:numel(acc)
        tryp(pre(['Accelerator ' v2s(acc{k})]), @() get(uimenu(m1, 'Accelerator', acc{k}), 'Accelerator'));
    end
    tryp(pre('Accelerator on top-level'), @() get(uimenu(p, 'Accelerator', 'q'), 'Accelerator'));
    tryp(pre('Checked on top-level'), @() char(get(uimenu(p, 'Checked', 'on'), 'Checked')));
    tryp(pre('Checked on parent with children'), @() chkparent(p));
    tryp(pre('Separator on top-level'), @() char(get(uimenu(p, 'Separator', 'on'), 'Separator')));
    tryp(pre('Enable class'), @() class(get(m1, 'Enable')));
    tryp(pre('ForegroundColor red'), @() get(uimenu(p, 'ForegroundColor', 'red'), 'ForegroundColor'));
    tryp(pre('delete parent deletes children'), @() delkids(p));
    tryp(pre('menu Parent <- other menu'), @() mvmenu(p));
    tryp(pre('menu Parent <- contextmenu'), @() mvmenu2(p));
    tryp(pre('findobj uimenu count (depth)'), @() numel(findobj(p, 'Type', 'uimenu')));
    tryp(pre('findobj -depth 1 uimenu'), @() numel(findobj(p, '-depth', 1, 'Type', 'uimenu')));
    tryp(pre('gcbo/gcbf in menu callback need a click'), @() 'n/a');
    tryp(pre('menu HandleVisibility default in figure Children'), @() numel(get(p, 'Children')));
    tryp(pre('MenuBar none keeps uimenus'), @() mbnone(p));
    delete(allchild(p));
    if q == 1, set(p, 'MenuBar', 'figure'); end

    %% toolbars
    tryp(pre('builtin toolbars findall'), @() numel(findall(p, 'Type', 'uitoolbar')));
    tryp(pre('ToolBar'), @() get(p, 'ToolBar'));
    tb = uitoolbar(p);
    tryp(pre('ToolBar after uitoolbar'), @() get(p, 'ToolBar'));
    tryp(pre('toolbars findall after one'), @() numel(findall(p, 'Type', 'uitoolbar')));
    p1 = uipushtool(tb, 'Tooltip', 'one'); g1 = uitoggletool(tb, 'Tooltip', 'two'); p2 = uipushtool(tb, 'Tooltip', 'three', 'Separator', 'on');
    tryp(pre('toolbar Children tips'), @() tips(tb.Children));
    tryp(pre('figure Children types'), @() types(get(p, 'Children')));
    tryp(pre('allchild types'), @() types(allchild(p)));
    tryp(pre('two toolbars'), @() types(allchild(twotb(p))));
    cd = {rand(16, 16, 3), rand(20, 30, 3), uint8(255 * ones(16, 16, 3)), zeros(16, 16), ones(16, 16, 3) * 2, NaN(16, 16, 3), [], true(16, 16, 3), single(rand(4, 4, 3)), uint16(ones(4, 4, 3)), int8(ones(4, 4, 3)), rand(16, 16, 4)};
    for k = 1:numel(cd)
        tryp(pre(['CData ' class(cd{k}) ' ' mat2str(size(cd{k}))]), @() cdrep(uipushtool(tb, 'CData', cd{k})));
    end
    ic = {'', 'bogus.png', fullfile(matlabroot, 'toolbox', 'matlab', 'icons', 'help_ex.png'), rand(16, 16, 3), "s", 5, {'a'}};
    for k = 1:numel(ic)
        tryp(pre(['Icon ' v2s(ic{k})]), @() iconrep(uipushtool(tb, 'Icon', ic{k})));
    end
    tryp(pre('Icon then CData'), @() iconcd(uipushtool(tb)));
    tryp(pre('TooltipString alias'), @() get(uipushtool(tb, 'TooltipString', 'x'), 'Tooltip'));
    tryp(pre('Tooltip cell'), @() get(uipushtool(tb, 'Tooltip', {'a', 'b'}), 'Tooltip'));
    tryp(pre('Tooltip char matrix'), @() get(uipushtool(tb, 'Tooltip', ['ab'; 'cd']), 'Tooltip'));
    tryp(pre('State on from code fires'), @() statefire(tb));
    tryp(pre('State class'), @() class(get(g1, 'State')));
    tryp(pre('State 1'), @() char(get(uitoggletool(tb, 'State', 1), 'State')));
    tryp(pre('State bogus'), @() char(get(uitoggletool(tb, 'State', 'bogus'), 'State')));
    tryp(pre('tool Parent <- other toolbar'), @() mvtool(p1, uitoolbar(p)));
    tryp(pre('uistack tool'), @() stacktool(tb, p2));
    tryp(pre('toolbar Visible off'), @() char(get(uitoolbar(p, 'Visible', 'off'), 'Visible')));
    tryp(pre('delete toolbar deletes tools'), @() deltb(p));
    delete(allchild(p));
end

%% classic only: the figure's own bars
f2 = figure('Visible', 'off');
tryp('F default findall uimenu', @() numel(findall(f2, 'Type', 'uimenu')));
tryp('F default findall uitoolbar', @() numel(findall(f2, 'Type', 'uitoolbar')));
tryp('F default allchild types', @() types(allchild(f2)));
tryp('F top menus tags', @() strjoin(arrayfun(@(m) m.Tag, flipud(findall(f2, 'Type', 'uimenu', 'Parent', f2))', 'UniformOutput', false), ','));
tryp('F top menus texts', @() strjoin(arrayfun(@(m) m.Text, flipud(findall(f2, 'Type', 'uimenu', 'Parent', f2))', 'UniformOutput', false), ','));
um = uimenu(f2, 'Text', 'Mine');
tryp('F user menu Position beside builtins', @() um.Position);
tryp('F MenuBar words', @() set(f2, 'MenuBar'));
tryp('F ToolBar words', @() set(f2, 'ToolBar'));
set(f2, 'MenuBar', 'none');
tryp('F MenuBar none findall uimenu', @() numel(findall(f2, 'Type', 'uimenu')));
tryp('F MenuBar none user Position', @() um.Position);
tryp('F MenuBar none ToolBar auto findall uitoolbar', @() numel(findall(f2, 'Type', 'uitoolbar')));
set(f2, 'MenuBar', 'figure');
tryp('F MenuBar figure again findall uimenu', @() numel(findall(f2, 'Type', 'uimenu', 'Parent', f2)));
set(f2, 'ToolBar', 'none');
tryp('F ToolBar none findall uitoolbar', @() numel(findall(f2, 'Type', 'uitoolbar')));
set(f2, 'ToolBar', 'figure');
tryp('F ToolBar figure findall uitoolbar', @() numel(findall(f2, 'Type', 'uitoolbar')));
tryp('F toolbar tags', @() strjoin(arrayfun(@(m) m.Tag, findall(f2, 'Type', 'uitoolbar')', 'UniformOutput', false), ','));
tryp('F toolbar tool tags', @() strjoin(arrayfun(@(m) m.Tag, flipud(allchild(findall(f2, 'Type', 'uitoolbar')))', 'UniformOutput', false), ','));
tryp('F get(f,Children) with bars', @() types(get(f2, 'Children')));
uf2 = uifigure('Visible', 'off');
tryp('U MenuBar figure', @() mbset(uf2, 'figure'));
tryp('U ToolBar figure', @() tbset(uf2, 'figure'));
tryp('U findall uimenu after', @() numel(findall(uf2, 'Type', 'uimenu')));
delete(findall(groot, 'Type', 'figure'));
end

function s = datarep(t)
d = t.Data;
s = sprintf('%s | Display=%s | ColumnName=%s | RowName=%s | Sortable=%s Editable=%s', datarep2(d), datarep2(t.DisplayData), ...
    v2s(t.ColumnName), v2s(t.RowName), v2s(t.ColumnSortable), v2s(t.ColumnEditable));
end

function s = datarep2(d)
if istable(d), s = v2s(d); elseif iscell(d), s = v2s(d); else, s = sprintf('%s %s', class(d), mat2str(size(d))); end
end

function s = selthen(t)
t.SelectionType = 'row';
s = v2s(t.Selection);
end

function s = seldata(t)
t.Data = magic(2);
s = v2s(t.Selection);
end

function s = idxwrite(t)
t.Data(2, 2) = 99;
s = mat2str(t.Data);
end

function s = idxcell(t)
t.Data{2, 2} = 'x';
s = v2s(t.Data);
end

function s = unitsthen(t)
t.Units = 'normalized';
s = mat2str(t.Position, 6);
end

function s = fontthen(t)
t.FontSize = 20; a = t.FontSize; t.FontUnits = 'pixels'; b = t.FontSize; t.FontUnits = 'points'; c = t.FontSize;
s = mat2str([a b c], 8);
end

function s = cbforms(h, name)
parts = {};
vals = {@sin, 'disp(1)', {@sin, 1}, "str", 5, {5}, []};
for k = 1:numel(vals)
    try
        set(h, name, vals{k});
        parts{end + 1} = ['ok:' v2s(get(h, name))]; %#ok<AGROW>
    catch e
        parts{end + 1} = ['ERR ' e.identifier]; %#ok<AGROW>
    end
end
s = strjoin(parts, ' ; ');
end

function s = tabunits(t)
t.Units = 'pixels';
s = mat2str(t.Position);
end

function s = seltab(tg, t)
tg.SelectedTab = t;
s = tg.SelectedTab.Title;
end

function s = reorder(tg)
tg.Children = flipud(tg.Children(:));
s = [strjoin(arrayfun(@(t) t.Title, tg.Children', 'UniformOutput', false), ',') ' sel=' tg.SelectedTab.Title];
end

function s = stacktab(tg, t)
uistack(t, 'top');
s = strjoin(arrayfun(@(t) t.Title, tg.Children', 'UniformOutput', false), ',');
end

function s = movetab(t, g)
t.Parent = g;
s = [g.SelectedTab.Title ' n=' num2str(numel(g.Children))];
end

function s = pix(h, name)
u = h.Units; h.Units = 'pixels';
s = mat2str(get(h, name), 6);
try, h.Units = u; catch, end
end

function s = childpix(t, q)
if q == 1
    c = uicontrol(t, 'Units', 'pixels', 'Position', [10 10 50 20]);
else
    c = uibutton(t, 'Position', [10 10 50 20]);
end
drawnow;
s = [mat2str(getpixelposition(c, true), 6) ' / ' mat2str(getpixelposition(t, true), 6)];
end

function ax = axesin(t, q)
if q == 1, ax = axes(t); else, ax = uiaxes(t); end
end

function s = axrep(ax)
s = sprintf('%s %s', ax.Units, mat2str(ax.Position, 5));
end

function s = codesel(p)
tg = uitabgroup(p); a = uitab(tg); b = uitab(tg); %#ok<NASGU>
setappdata(p, 'fired', 0);
tg.SelectionChangedFcn = @(~, ~) setappdata(p, 'fired', getappdata(p, 'fired') + 1);
tg.SelectedTab = b; drawnow;
s = getappdata(p, 'fired');
end

function s = tgvis(p)
tg = uitabgroup(p, 'Visible', 'off'); t = uitab(tg); b = uipanel(t);
s = sprintf('%s %s', char(tg.Visible), char(b.Visible));
end

function s = tgdel(p)
tg = uitabgroup(p); t = uitab(tg); b = uipanel(t);
delete(tg);
s = sprintf('%d%d', isvalid(t), isvalid(b));
end

function s = tgunits(p)
tg = uitabgroup(p);
tg.Units = 'pixels';
s = mat2str(tg.Position, 6);
end

function s = texts(h)
s = sprintf('%s %s', mat2str(size(h)), strjoin(arrayfun(@(m) txt(m), h(:)', 'UniformOutput', false), ','));
end

function s = txt(m)
if isprop(m, 'Text'), s = m.Text; else, s = ['<' m.Type '>']; end
end

function s = types(h)
s = sprintf('%s %s', mat2str(size(h)), strjoin(arrayfun(@(m) m.Type, h(:)', 'UniformOutput', false), ','));
end

function s = tips(h)
s = sprintf('%s %s', mat2str(size(h)), strjoin(arrayfun(@(m) m.Tooltip, h(:)', 'UniformOutput', false), ','));
end

function s = setpos(m, v, all)
m.Position = v;
s = mat2str(arrayfun(@(x) x.Position, all));
end

function s = mkpos(m)
x = uimenu(m, 'Text', 'first', 'Position', 1);
s = [mat2str(arrayfun(@(c) c.Position, m.Children')) ' ' texts(m.Children)];
delete(x);
end

function s = chkparent(p)
m = uimenu(p, 'Text', 'P'); c = uimenu(m, 'Text', 'c'); %#ok<NASGU>
m.Checked = 'on';
s = char(m.Checked);
end

function s = delkids(p)
m = uimenu(p); c = uimenu(m);
delete(m);
s = isvalid(c);
end

function s = mvmenu(p)
a = uimenu(p, 'Text', 'a'); b = uimenu(p, 'Text', 'b'); c = uimenu(a, 'Text', 'c');
c.Parent = b;
s = sprintf('%d %d %s', numel(a.Children), numel(b.Children), class(c.Parent));
end

function s = mvmenu2(p)
a = uimenu(p, 'Text', 'a'); cm = uicontextmenu(p);
a.Parent = cm;
s = sprintf('%d %s', numel(cm.Children), class(a.Parent));
end

function s = mbnone(p)
m = uimenu(p, 'Text', 'keep');
old = get(p, 'MenuBar');
set(p, 'MenuBar', 'none');
s = sprintf('%d %s', isvalid(m), get(p, 'MenuBar'));
set(p, 'MenuBar', old);
end

function p = twotb(p)
uitoolbar(p);
end

function s = cdrep(h)
c = h.CData;
s = sprintf('%s %s Icon=%s', class(c), mat2str(size(c)), v2s(h.Icon));
end

function s = iconrep(h)
c = h.CData;
s = sprintf('Icon=%s CData=%s %s', v2s(h.Icon), class(c), mat2str(size(c)));
end

function s = iconcd(h)
h.Icon = fullfile(matlabroot, 'toolbox', 'matlab', 'icons', 'help_ex.png');
a = size(h.CData);
h.CData = rand(16, 16, 3);
s = sprintf('%s then Icon=%s', mat2str(a), v2s(h.Icon));
end

function s = statefire(tb)
f = ancestor(tb, 'figure');
setappdata(f, 'log', '');
g = uitoggletool(tb, 'OnCallback', @(~, ~) setappdata(f, 'log', [getappdata(f, 'log') 'on ']), ...
    'OffCallback', @(~, ~) setappdata(f, 'log', [getappdata(f, 'log') 'off ']), ...
    'ClickedCallback', @(~, ~) setappdata(f, 'log', [getappdata(f, 'log') 'click ']));
g.State = 'on'; drawnow;
g.State = 'off'; drawnow;
s = ['[' getappdata(f, 'log') ']'];
end

function s = mvtool(t, tb)
t.Parent = tb;
s = sprintf('%d %s', numel(tb.Children), class(t.Parent));
end

function s = stacktool(tb, t)
before = tips(tb.Children);
uistack(t, 'bottom');
s = [before ' -> ' tips(tb.Children)];
end

function s = deltb(p)
tb = uitoolbar(p); t = uipushtool(tb);
delete(tb);
s = isvalid(t);
end

function s = mbset(f, v)
set(f, 'MenuBar', v);
s = get(f, 'MenuBar');
end

function s = tbset(f, v)
set(f, 'ToolBar', v);
s = get(f, 'ToolBar');
end
