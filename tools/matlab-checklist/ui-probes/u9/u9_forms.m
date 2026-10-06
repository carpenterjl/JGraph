function u9_forms
% U9 probe: the forms of each maker, what each accepts as a parent, the style words, and the
% verbs of the stage (uistyle, addStyle, removeStyle, expand, collapse, move, scroll, focus).
% Headless.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
fns = {'uiknob', 'uiswitch', 'uigauge', 'uilamp', 'uidatepicker', 'uicolorpicker', 'uitree', 'uitreenode'};
ax = axes(f);
for k = 1:numel(fns)
    name = fns{k}; fn = str2func(name);
    before = findall(groot, 'Type', 'figure');
    tryp([name ' ()'], @() chain(fn()));
    delete(setdiff(findall(groot, 'Type', 'figure'), before));
    figure(f); set(f, 'Visible', 'off');
    tryp([name ' () with a current figure'], @() chain(fn()));
    delete(setdiff(findall(groot, 'Type', 'figure'), before));
    tryp([name ' (uifigure)'], @() chain(fn(uf)));
    tryp([name ' (figure)'], @() chain(fn(f)));
    tryp([name ' (uipanel)'], @() chain(fn(uipanel(uf))));
    tryp([name ' (classic uipanel)'], @() chain(fn(uipanel(f))));
    tryp([name ' (grid)'], @() chain(fn(uigridlayout(uf))));
    tryp([name ' (uibuttongroup)'], @() chain(fn(uibuttongroup(uf))));
    tryp([name ' (tabgroup)'], @() chain(fn(uitabgroup(uf))));
    tryp([name ' (tab)'], @() chain(fn(uitab(uitabgroup(uf)))));
    tryp([name ' (classic tab)'], @() chain(fn(uitab(uitabgroup(f)))));
    tryp([name ' (menu)'], @() chain(fn(uimenu(uf))));
    tryp([name ' (contextmenu)'], @() chain(fn(uicontextmenu(uf))));
    tryp([name ' (toolbar)'], @() chain(fn(uitoolbar(uf))));
    tryp([name ' (axes)'], @() chain(fn(ax)));
    tryp([name ' (uiaxes)'], @() chain(fn(uiaxes(uf))));
    tryp([name ' (uicontrol)'], @() chain(fn(uicontrol(f))));
    tryp([name ' (label)'], @() chain(fn(uilabel(uf))));
    tryp([name ' (tree)'], @() chain(fn(uitree(uf))));
    tryp([name ' (checkbox tree)'], @() chain(fn(uitree(uf, 'checkbox'))));
    tryp([name ' (treenode)'], @() chain(fn(uitreenode(uitree(uf)))));
    tryp([name ' (classic tree)'], @() chain(fn(uitree(f))));
    tryp([name ' (table)'], @() chain(fn(uitable(uf))));
    tryp([name ' (5.5)'], @() chain(fn(5.5)));
    tryp([name ' ([])'], @() chain(fn([])));
    tryp([name ' (groot)'], @() chain(fn(groot)));
    tryp([name ' (Parent, uf)'], @() chain(fn('Parent', uf)));
    tryp([name ' (Parent, f)'], @() chain(fn('Parent', f)));
    tryp([name ' (Parent, tree)'], @() chain(fn('Parent', uitree(uf))));
    tryp([name ' (Parent, [])'], @() chain(fn('Parent', [])));
    tryp([name ' (uf, Bogus, 1)'], @() chain(fn(uf, 'Bogus', 1)));
    tryp([name ' (uf, Tag)'], @() chain(fn(uf, 'Tag')));
    tryp([name ' (uf, 5, 6)'], @() chain(fn(uf, 5, 6)));
    tryp([name ' (Tag, t) pairs only'], @() get(fn('Tag', 't'), 'Tag'));
    delete(setdiff(findall(groot, 'Type', 'figure'), before));
    tryp([name ' (uf, struct)'], @() get(fn(uf, struct('Tag', 's')), 'Tag'));
    tryp([name ' (uf, tag, t) lower case'], @() get(fn(uf, 'tag', 't'), 'Tag'));
    tryp([name ' (uf, Ta, t) prefix'], @() get(fn(uf, 'Ta', 't'), 'Tag'));
    tryp([name ' (uf, "Tag", "t") strings'], @() get(fn(uf, "Tag", "t"), 'Tag'));
    tryp([name ' (uf, Tag, t, Visible, off)'], @() get(fn(uf, 'Tag', 't', 'Visible', 'off'), 'Visible'));
    tryp([name ' Units'], @() get(fn(uf), 'Units'));
    tryp([name ' set Units'], @() set(fn(uf), 'Units', 'pixels'));
    tryp([name ' bogus get'], @() get(fn(uf), 'Bogus'));
    tryp([name ' bogus set'], @() set(fn(uf), 'Bogus', 1));
    tryp([name ' dot bogus get'], @() dotget(fn(uf)));
    tryp([name ' dot bogus set'], @() dotset(fn(uf)));
    tryp([name ' set Type'], @() set(fn(uf), 'Type', 'x'));
    tryp([name ' set BeingDeleted'], @() set(fn(uf), 'BeingDeleted', 'on'));
    tryp([name ' isprop Units, isgraphics, ishghandle, isvalid, ishandle, isa Component'], @() flags(fn(uf)));
    tryp([name ' Parent <- figure'], @() reparent(fn(uf), f));
    tryp([name ' Parent <- []'], @() reparent(fn(uf), []));
    tryp([name ' Parent <- axes'], @() reparent(fn(uf), ax));
    tryp([name ' Parent <- panel'], @() reparent(fn(uf), uipanel(uf)));
    tryp([name ' Parent <- tree'], @() reparent(fn(uf), uitree(uf)));
    tryp([name ' Parent <- node'], @() reparent(fn(uf), uitreenode(uitree(uf))));
    tryp([name ' Parent <- grid'], @() reparent(fn(uf), uigridlayout(uf)));
    tryp([name ' in panel default Position'], @() get(fn(uipanel(uf)), 'Position'));
    tryp([name ' findobj by Type'], @() numel(findobj(uf, 'Type', get(fn(uf), 'Type'))));
    tryp([name ' uf.Children after one'], @() cellfun(@class, num2cell(get(uf, 'Children')), 'UniformOutput', false));
    delete(allchild(uf)); delete(allchild(f)); ax = axes(f);
end

%% style words
tryp('uiknob(uf, continuous)', @() kind(uiknob(uf, 'continuous')));
tryp('uiknob(uf, discrete)', @() kind(uiknob(uf, 'discrete')));
tryp('uiknob(uf, DISCRETE)', @() kind(uiknob(uf, 'DISCRETE')));
tryp('uiknob(uf, disc)', @() kind(uiknob(uf, 'disc')));
tryp('uiknob(uf, "discrete")', @() kind(uiknob(uf, "discrete")));
tryp('uiknob(discrete) no parent', @() kindclose(uiknob('discrete')));
tryp('uiknob(uf, bogus)', @() kind(uiknob(uf, 'bogus')));
tryp('uiknob(uf, discrete, Items, {a,b})', @() get(uiknob(uf, 'discrete', 'Items', {'a', 'b'}), 'Value'));
tryp('uiknob(uf, discrete, Value, b, Items, {a,b})', @() get(uiknob(uf, 'discrete', 'Value', 'b', 'Items', {'a', 'b'}), 'Value'));
tryp('uiknob(uf, Items, {a})', @() kind(uiknob(uf, 'Items', {'a'})));
tryp('uiknob(uf, Value, 150)', @() kind(uiknob(uf, 'Value', 150)));
tryp('uiknob(uf, Value, 150, Limits, [0 200])', @() get(uiknob(uf, 'Value', 150, 'Limits', [0 200]), 'Value'));
tryp('uiknob(uf, Limits, [0 200], Value, 150)', @() get(uiknob(uf, 'Limits', [0 200], 'Value', 150), 'Value'));
tryp('uiknob(uf, discrete, Style, x)', @() kind(uiknob(uf, 'discrete', 'Style', 'x')));
tryp('uiswitch(uf, slider)', @() kind(uiswitch(uf, 'slider')));
tryp('uiswitch(uf, rocker)', @() kind(uiswitch(uf, 'rocker')));
tryp('uiswitch(uf, toggle)', @() kind(uiswitch(uf, 'toggle')));
tryp('uiswitch(uf, ROCKER)', @() kind(uiswitch(uf, 'ROCKER')));
tryp('uiswitch(uf, rock)', @() kind(uiswitch(uf, 'rock')));
tryp('uiswitch(uf, bogus)', @() kind(uiswitch(uf, 'bogus')));
tryp('uiswitch(rocker) no parent', @() kindclose(uiswitch('rocker')));
tryp('uiswitch(uf, toggle, Orientation, horizontal)', @() get(uiswitch(uf, 'toggle', 'Orientation', 'horizontal'), 'Orientation'));
tryp('uiswitch(uf, Value, On)', @() get(uiswitch(uf, 'Value', 'On'), 'Value'));
tryp('uiswitch(uf, Value, on)', @() get(uiswitch(uf, 'Value', 'on'), 'Value'));
tryp('uiswitch(uf, Value, true)', @() get(uiswitch(uf, 'Value', true), 'Value'));
tryp('uiswitch(uf, Value, 1)', @() get(uiswitch(uf, 'Value', 1), 'Value'));
tryp('uiswitch(uf, Value, b, Items, {a,b})', @() get(uiswitch(uf, 'Value', 'b', 'Items', {'a', 'b'}), 'Value'));
tryp('uiswitch(uf, Items, {a,b,c})', @() get(uiswitch(uf, 'Items', {'a', 'b', 'c'}), 'Items'));
tryp('uiswitch(uf, Items, {a})', @() get(uiswitch(uf, 'Items', {'a'}), 'Items'));
tryp('uigauge(uf, circular)', @() kind(uigauge(uf, 'circular')));
tryp('uigauge(uf, linear)', @() kind(uigauge(uf, 'linear')));
tryp('uigauge(uf, ninetydegree)', @() kind(uigauge(uf, 'ninetydegree')));
tryp('uigauge(uf, semicircular)', @() kind(uigauge(uf, 'semicircular')));
tryp('uigauge(uf, NINETYDEGREE)', @() kind(uigauge(uf, 'NINETYDEGREE')));
tryp('uigauge(uf, ninety)', @() kind(uigauge(uf, 'ninety')));
tryp('uigauge(uf, semi)', @() kind(uigauge(uf, 'semi')));
tryp('uigauge(uf, bogus)', @() kind(uigauge(uf, 'bogus')));
tryp('uigauge(linear) no parent', @() kindclose(uigauge('linear')));
tryp('uigauge(uf, Value, 150)', @() get(uigauge(uf, 'Value', 150), 'Value'));
tryp('uigauge(uf, Value, -5)', @() get(uigauge(uf, 'Value', -5), 'Value'));
tryp('uigauge(uf, linear, Orientation, vertical)', @() get(uigauge(uf, 'linear', 'Orientation', 'vertical'), 'Orientation'));
tryp('uigauge(uf, Orientation, vertical) circular', @() kind(uigauge(uf, 'Orientation', 'vertical')));
tryp('uitree(uf, checkbox)', @() kind(uitree(uf, 'checkbox')));
tryp('uitree(uf, CHECKBOX)', @() kind(uitree(uf, 'CHECKBOX')));
tryp('uitree(uf, check)', @() kind(uitree(uf, 'check')));
tryp('uitree(uf, tree)', @() kind(uitree(uf, 'tree')));
tryp('uitree(uf, bogus)', @() kind(uitree(uf, 'bogus')));
tryp('uitree(checkbox) no parent', @() kindclose(uitree('checkbox')));
tryp('uitree(uf, v0)', @() kind(uitree(uf, 'v0')));
tryp('uitree(v0)', @() kindclose(uitree('v0')));
tryp('uitree(uf, checkbox, Multiselect, on)', @() kind(uitree(uf, 'checkbox', 'Multiselect', 'on')));
tryp('uitree(uf, CheckedNodes, [])', @() kind(uitree(uf, 'CheckedNodes', [])));
tryp('uitreenode(tree, Text, x)', @() get(uitreenode(uitree(uf), 'Text', 'x'), 'Text'));
tryp('uitreenode(tree, Text, 5)', @() get(uitreenode(uitree(uf), 'Text', 5), 'Text'));
tryp('uitreenode(tree, x) one odd', @() get(uitreenode(uitree(uf), 'x'), 'Text'));
tryp('uitreenode(node, Text, child)', @() chain(uitreenode(uitreenode(uitree(uf)), 'Text', 'child')));
tryp('uitreenode(tree, NodeData, struct)', @() get(uitreenode(uitree(uf), 'NodeData', struct('a', 1)), 'NodeData'));
tryp('uidatepicker(uf, Value, d)', @() get(uidatepicker(uf, 'Value', datetime(2024, 1, 15)), 'Value'));
tryp('uidatepicker(uf, Value, char)', @() get(uidatepicker(uf, 'Value', '15-Jan-2024'), 'Value'));
tryp('uidatepicker(uf, Value, d, Limits, after)', @() get(uidatepicker(uf, 'Value', datetime(2024, 1, 15), 'Limits', [datetime(2024, 2, 1) datetime(2024, 3, 1)]), 'Value'));
tryp('uidatepicker(uf, Limits, after, Value, d)', @() get(uidatepicker(uf, 'Limits', [datetime(2024, 2, 1) datetime(2024, 3, 1)], 'Value', datetime(2024, 1, 15)), 'Value'));
tryp('uicolorpicker(uf, Value, blue)', @() get(uicolorpicker(uf, 'Value', 'blue'), 'Value'));
tryp('uicolorpicker(uf, Value, bogus)', @() get(uicolorpicker(uf, 'Value', 'bogus'), 'Value'));
tryp('uilamp(uf, Color, r)', @() get(uilamp(uf, 'Color', 'r'), 'Color'));
delete(allchild(uf));

%% uistyle
s = uistyle();
tryp('uistyle()', @() styleinfo(uistyle()));
tryp('uistyle class', @() class(s));
tryp('uistyle isobject ishandle isstruct isa handle isa Style', @() [isobject(s) ishandle(s) isstruct(s) isa(s, 'handle') isa(s, 'matlab.ui.style.Style')]);
tryp('uistyle properties', @() properties(s));
tryp('uistyle fieldnames', @() fieldnames(s));
tryp('uistyle size', @() size(s));
tryp('uistyle isequal two', @() isequal(uistyle(), uistyle()));
tryp('uistyle(BackgroundColor, red)', @() styleinfo(uistyle('BackgroundColor', 'red')));
tryp('uistyle(all)', @() styleinfo(uistyle('BackgroundColor', [0 0 1], 'FontColor', 'g', 'FontWeight', 'bold', 'FontAngle', 'italic', ...
    'FontName', 'Arial', 'HorizontalAlignment', 'center', 'HorizontalClipping', 'off', 'Interpreter', 'html', 'IconAlignment', 'right')));
tryp('uistyle(Icon, error)', @() styleinfo(uistyle('Icon', 'error')));
tryp('uistyle(Icon, nofile.png)', @() styleinfo(uistyle('Icon', 'nofile.png')));
tryp('uistyle(Icon, zeros(4,4,3))', @() styleinfo(uistyle('Icon', zeros(4, 4, 3))));
tryp('uistyle(Icon, 5)', @() styleinfo(uistyle('Icon', 5)));
tryp('uistyle(bogus, 1)', @() styleinfo(uistyle('bogus', 1)));
tryp('uistyle(BackgroundColor) odd', @() styleinfo(uistyle('BackgroundColor')));
tryp('uistyle(5)', @() styleinfo(uistyle(5)));
tryp('uistyle(FontWeight, heavy)', @() styleinfo(uistyle('FontWeight', 'heavy')));
tryp('uistyle(FontWeight, BOLD)', @() styleinfo(uistyle('FontWeight', 'BOLD')));
tryp('uistyle(FontWeight, b)', @() styleinfo(uistyle('FontWeight', 'b')));
tryp('uistyle(FontWeight, "bold")', @() styleinfo(uistyle('FontWeight', "bold")));
tryp('uistyle(FontWeight, empty)', @() styleinfo(uistyle('FontWeight', '')));
tryp('uistyle(FontAngle, oblique)', @() styleinfo(uistyle('FontAngle', 'oblique')));
tryp('uistyle(HorizontalAlignment, middle)', @() styleinfo(uistyle('HorizontalAlignment', 'middle')));
tryp('uistyle(HorizontalAlignment, LEFT)', @() styleinfo(uistyle('HorizontalAlignment', 'LEFT')));
tryp('uistyle(HorizontalClipping, left)', @() styleinfo(uistyle('HorizontalClipping', 'left')));
tryp('uistyle(HorizontalClipping, on)', @() styleinfo(uistyle('HorizontalClipping', 'on')));
tryp('uistyle(HorizontalClipping, true)', @() styleinfo(uistyle('HorizontalClipping', true)));
tryp('uistyle(IconAlignment, leftmargin)', @() styleinfo(uistyle('IconAlignment', 'leftmargin')));
tryp('uistyle(IconAlignment, top)', @() styleinfo(uistyle('IconAlignment', 'top')));
tryp('uistyle(Interpreter, latex)', @() styleinfo(uistyle('Interpreter', 'latex')));
tryp('uistyle(Interpreter, bogus)', @() styleinfo(uistyle('Interpreter', 'bogus')));
tryp('uistyle(BackgroundColor, none)', @() styleinfo(uistyle('BackgroundColor', 'none')));
tryp('uistyle(BackgroundColor, [1 2 3])', @() styleinfo(uistyle('BackgroundColor', [1 2 3])));
tryp('uistyle(BackgroundColor, #ff0000)', @() styleinfo(uistyle('BackgroundColor', '#ff0000')));
tryp('uistyle(BackgroundColor, [])', @() styleinfo(uistyle('BackgroundColor', [])));
tryp('uistyle(FontColor, [1 0 0 0.5])', @() styleinfo(uistyle('FontColor', [1 0 0 0.5])));
tryp('uistyle(FontName, 5)', @() styleinfo(uistyle('FontName', 5)));
tryp('uistyle(FontName, "Arial")', @() styleinfo(uistyle('FontName', "Arial")));
tryp('uistyle(FontName, Bogus Font)', @() styleinfo(uistyle('FontName', 'Bogus Font')));
tryp('uistyle("BackgroundColor", "red") strings', @() styleinfo(uistyle("BackgroundColor", "red")));
tryp('uistyle(backgroundcolor, red) lower', @() styleinfo(uistyle('backgroundcolor', 'red')));
tryp('uistyle(Back, red) prefix', @() styleinfo(uistyle('Back', 'red')));
tryp('uistyle(struct)', @() styleinfo(uistyle(struct('FontWeight', 'bold'))));
tryp('uistyle(uf)', @() styleinfo(uistyle(uf)));
tryp('uistyle(s) copy', @() styleinfo(uistyle(uistyle('FontWeight', 'bold'))));
tryp('s.BackgroundColor = g', @() dotstyle('BackgroundColor', 'g'));
tryp('s.FontWeight = BOLD', @() dotstyle('FontWeight', 'BOLD'));
tryp('s.FontWeight = heavy', @() dotstyle('FontWeight', 'heavy'));
tryp('s.FontWeight = []', @() dotstyle('FontWeight', []));
tryp('s.Bogus = 1', @() dotstyle('Bogus', 1));
tryp('s.Type', @() s.Type);
tryp('value semantics', @() valuesem());
tryp('set(s, FontWeight, bold)', @() styleinfo(setstyle()));
tryp('get(s, FontWeight)', @() get(uistyle('FontWeight', 'bold'), 'FontWeight'));
tryp('get(s)', @() get(uistyle('FontWeight', 'bold')));
tryp('disp(s)', @() evalc('disp(uistyle(''FontWeight'', ''bold''))'));
tryp('display s', @() evalc('s'));
tryp('[s s] array', @() size([uistyle() uistyle()]));
tryp('s == s', @() eq(uistyle(), uistyle()));

%% addStyle and removeStyle
t = uitable(uf, 'Data', magic(4));
tr = uitree(uf);
n1 = uitreenode(tr, 'Text', 'n1'); n2 = uitreenode(tr, 'Text', 'n2'); c1 = uitreenode(n1, 'Text', 'c1'); c2 = uitreenode(n1, 'Text', 'c2'); g1 = uitreenode(c1, 'Text', 'g1');
lb = uilistbox(uf, 'Items', {'one', 'two', 'three'});
dd = uidropdown(uf, 'Items', {'one', 'two', 'three'});
sb = uistyle('BackgroundColor', 'yellow');
sw = uistyle('FontWeight', 'bold');
tryp('StyleConfigurations empty', @() sc(t));
tryp('StyleConfigurations empty vars', @() t.StyleConfigurations.Properties.VariableNames);
tryp('StyleConfigurations empty class', @() class(t.StyleConfigurations));
tryp('addStyle(t, sb)', @() aft(@() addStyle(t, sb), t));
tryp('addStyle(t, sw, table)', @() aft(@() addStyle(t, sw, 'table'), t));
tryp('addStyle(t, sb, row, 1)', @() aft(@() addStyle(t, sb, 'row', 1), t));
tryp('addStyle(t, sb, row, [1 3])', @() aft(@() addStyle(t, sb, 'row', [1 3]), t));
tryp('addStyle(t, sb, row, [1;3])', @() aft(@() addStyle(t, sb, 'row', [1; 3]), t));
tryp('addStyle(t, sb, row, 99)', @() aft(@() addStyle(t, sb, 'row', 99), t));
tryp('addStyle(t, sb, row, 0)', @() aft(@() addStyle(t, sb, 'row', 0), t));
tryp('addStyle(t, sb, row, 1.5)', @() aft(@() addStyle(t, sb, 'row', 1.5), t));
tryp('addStyle(t, sb, row, a)', @() aft(@() addStyle(t, sb, 'row', 'a'), t));
tryp('addStyle(t, sb, row, [])', @() aft(@() addStyle(t, sb, 'row', []), t));
tryp('addStyle(t, sb, row, [1 1])', @() aft(@() addStyle(t, sb, 'row', [1 1]), t));
tryp('addStyle(t, sb, row, true)', @() aft(@() addStyle(t, sb, 'row', true), t));
tryp('addStyle(t, sb, row, int8(2))', @() aft(@() addStyle(t, sb, 'row', int8(2)), t));
tryp('addStyle(t, sb, row)', @() aft(@() addStyle(t, sb, 'row'), t));
tryp('addStyle(t, sb, ROW, 1)', @() aft(@() addStyle(t, sb, 'ROW', 1), t));
tryp('addStyle(t, sb, "row", 1)', @() aft(@() addStyle(t, sb, "row", 1), t));
tryp('addStyle(t, sb, column, 2)', @() aft(@() addStyle(t, sb, 'column', 2), t));
tryp('addStyle(t, sb, column, [2 4])', @() aft(@() addStyle(t, sb, 'column', [2 4]), t));
tryp('addStyle(t, sb, cell, [1 1])', @() aft(@() addStyle(t, sb, 'cell', [1 1]), t));
tryp('addStyle(t, sb, cell, [1 1; 2 2])', @() aft(@() addStyle(t, sb, 'cell', [1 1; 2 2]), t));
tryp('addStyle(t, sb, cell, [1 1 1])', @() aft(@() addStyle(t, sb, 'cell', [1 1 1]), t));
tryp('addStyle(t, sb, cell, [1 9])', @() aft(@() addStyle(t, sb, 'cell', [1 9]), t));
tryp('addStyle(t, sb, cell, 1)', @() aft(@() addStyle(t, sb, 'cell', 1), t));
tryp('addStyle(t, sb, bogus, 1)', @() aft(@() addStyle(t, sb, 'bogus', 1), t));
tryp('addStyle(t, sb, node, 1)', @() aft(@() addStyle(t, sb, 'node', 1), t));
tryp('addStyle(t, sb, item, 1)', @() aft(@() addStyle(t, sb, 'item', 1), t));
tryp('addStyle(t, sb, table, 1)', @() aft(@() addStyle(t, sb, 'table', 1), t));
tryp('addStyle(t, 5)', @() aft(@() addStyle(t, 5), t));
tryp('addStyle(t, struct)', @() aft(@() addStyle(t, struct('FontWeight', 'bold')), t));
tryp('addStyle(t)', @() aft(@() addStyle(t), t));
tryp('addStyle(t, [sb sw])', @() aft(@() addStyle(t, [sb sw]), t));
tryp('addStyle(t, sb, row, 1, column, 2)', @() aft(@() addStyle(t, sb, 'row', 1, 'column', 2), t));
tryp('addStyle(uibutton, sb)', @() aft(@() addStyle(uibutton(uf), sb), t));
tryp('addStyle(uf, sb)', @() aft(@() addStyle(uf, sb), t));
tryp('addStyle(uilabel, sb)', @() aft(@() addStyle(uilabel(uf), sb), t));
tryp('addStyle(uitextarea, sb)', @() aft(@() addStyle(uitextarea(uf), sb), t));
tryp('addStyle(uicontrol, sb)', @() aft(@() addStyle(uicontrol(f), sb), t));
tryp('addStyle(5, sb)', @() aft(@() addStyle(5, sb), t));
tryp('addStyle(classic table, sb, row, 1)', @() aft(@() addStyle(uitable(f, 'Data', magic(3)), sb, 'row', 1), t));
tryp('addStyle(deleted t)', @() aft(@() addStyle(deletedtable(uf), sb), t));
tryp('addStyle(t, sb) output', @() addStyle(t, sb));
tryp('sc after many', @() sc(t));
tryp('removeStyle(t, 1)', @() aft(@() removeStyle(t, 1), t));
tryp('removeStyle(t, [1 2])', @() aft(@() removeStyle(t, [1 2]), t));
tryp('removeStyle(t, 99)', @() aft(@() removeStyle(t, 99), t));
tryp('removeStyle(t, 0)', @() aft(@() removeStyle(t, 0), t));
tryp('removeStyle(t, 1.5)', @() aft(@() removeStyle(t, 1.5), t));
tryp('removeStyle(t, a)', @() aft(@() removeStyle(t, 'a'), t));
tryp('removeStyle(t, [])', @() aft(@() removeStyle(t, []), t));
tryp('removeStyle(t, all)', @() aft(@() removeStyle(t, 'all'), t));
tryp('removeStyle(t, [1 1])', @() aft(@() removeStyle(t, [1 1]), t));
tryp('removeStyle(t, 1, 2)', @() aft(@() removeStyle(t, 1, 2), t));
tryp('removeStyle(t)', @() aft(@() removeStyle(t), t));
tryp('removeStyle(t) when empty', @() aft(@() removeStyle(t), t));
tryp('removeStyle(t, 1) when empty', @() aft(@() removeStyle(t, 1), t));
tryp('removeStyle(uibutton)', @() aft(@() removeStyle(uibutton(uf)), t));
tryp('removeStyle(uf)', @() aft(@() removeStyle(uf), t));
tryp('removeStyle()', @() aft(@() removeStyle(), t));
tryp('removeStyle(t) output', @() removeStyle(t));
tryp('addStyle then edit style var', @() styleedit(t));
tryp('addStyle then Data smaller', @() datasmaller(t, sb));
tryp('StyleConfigurations set', @() setsc(t));
tryp('StyleConfigurations in set(t)', @() isfield(set(t), 'StyleConfigurations'));
tryp('addStyle(tr, sb)', @() aft(@() addStyle(tr, sb), tr));
tryp('addStyle(tr, sb, tree)', @() aft(@() addStyle(tr, sb, 'tree'), tr));
tryp('addStyle(tr, sb, node, n1)', @() aft(@() addStyle(tr, sb, 'node', n1), tr));
tryp('addStyle(tr, sb, node, [n1 c2])', @() aft(@() addStyle(tr, sb, 'node', [n1 c2]), tr));
tryp('addStyle(tr, sb, node, [n1; c2])', @() aft(@() addStyle(tr, sb, 'node', [n1; c2]), tr));
tryp('addStyle(tr, sb, node, 1)', @() aft(@() addStyle(tr, sb, 'node', 1), tr));
tryp('addStyle(tr, sb, node, other tree node)', @() aft(@() addStyle(tr, sb, 'node', uitreenode(uitree(uf))), tr));
tryp('addStyle(tr, sb, node, tr)', @() aft(@() addStyle(tr, sb, 'node', tr), tr));
tryp('addStyle(tr, sb, node, [])', @() aft(@() addStyle(tr, sb, 'node', []), tr));
tryp('addStyle(tr, sb, level, 1)', @() aft(@() addStyle(tr, sb, 'level', 1), tr));
tryp('addStyle(tr, sb, level, [1 3])', @() aft(@() addStyle(tr, sb, 'level', [1 3]), tr));
tryp('addStyle(tr, sb, level, 0)', @() aft(@() addStyle(tr, sb, 'level', 0), tr));
tryp('addStyle(tr, sb, level, 99)', @() aft(@() addStyle(tr, sb, 'level', 99), tr));
tryp('addStyle(tr, sb, level, 1.5)', @() aft(@() addStyle(tr, sb, 'level', 1.5), tr));
tryp('addStyle(tr, sb, row, 1)', @() aft(@() addStyle(tr, sb, 'row', 1), tr));
tryp('addStyle(tr, sb, item, 1)', @() aft(@() addStyle(tr, sb, 'item', 1), tr));
tryp('addStyle(n1, sb)', @() aft(@() addStyle(n1, sb), tr));
tryp('addStyle(checkbox tree, sb, node, n)', @() aft(@() addStyle(uitree(uf, 'checkbox'), sb, 'node', uitreenode(uitree(uf, 'checkbox'))), tr));
tryp('sc tree', @() sc(tr));
tryp('removeStyle(tr, 1)', @() aft(@() removeStyle(tr, 1), tr));
tryp('removeStyle(tr)', @() aft(@() removeStyle(tr), tr));
tryp('delete styled node', @() delnode(tr, sb));
tryp('addStyle(lb, sb)', @() aft(@() addStyle(lb, sb), lb));
tryp('addStyle(lb, sb, listbox)', @() aft(@() addStyle(lb, sb, 'listbox'), lb));
tryp('addStyle(lb, sb, item, 1)', @() aft(@() addStyle(lb, sb, 'item', 1), lb));
tryp('addStyle(lb, sb, item, [1 3])', @() aft(@() addStyle(lb, sb, 'item', [1 3]), lb));
tryp('addStyle(lb, sb, item, 99)', @() aft(@() addStyle(lb, sb, 'item', 99), lb));
tryp('addStyle(lb, sb, item, 0)', @() aft(@() addStyle(lb, sb, 'item', 0), lb));
tryp('addStyle(lb, sb, row, 1)', @() aft(@() addStyle(lb, sb, 'row', 1), lb));
tryp('addStyle(lb, sb, table)', @() aft(@() addStyle(lb, sb, 'table'), lb));
tryp('addStyle(lb, sb, tree)', @() aft(@() addStyle(lb, sb, 'tree'), lb));
tryp('sc listbox', @() sc(lb));
tryp('lb Items shorter after style', @() itemsshorter(lb));
tryp('removeStyle(lb)', @() aft(@() removeStyle(lb), lb));
tryp('addStyle(dd, sb)', @() aft(@() addStyle(dd, sb), dd));
tryp('addStyle(dd, sb, item, 2)', @() aft(@() addStyle(dd, sb, 'item', 2), dd));
tryp('addStyle(dd, sb, dropdown)', @() aft(@() addStyle(dd, sb, 'dropdown'), dd));
tryp('addStyle(dd, sb, listbox)', @() aft(@() addStyle(dd, sb, 'listbox'), dd));
tryp('sc dropdown', @() sc(dd));
tryp('removeStyle(dd)', @() aft(@() removeStyle(dd), dd));
tryp('StyleConfigurations in get names', @() [any(strcmp(fieldnames(get(uilistbox(uf))), 'StyleConfigurations')) any(strcmp(fieldnames(get(uidropdown(uf))), 'StyleConfigurations')) any(strcmp(fieldnames(get(uitree(uf))), 'StyleConfigurations')) any(strcmp(fieldnames(get(uitable(f))), 'StyleConfigurations')) any(strcmp(fieldnames(get(uibutton(uf))), 'StyleConfigurations'))]);
delete(allchild(uf));

%% expand, collapse, move
tr = uitree(uf);
n1 = uitreenode(tr, 'Text', 'n1'); n2 = uitreenode(tr, 'Text', 'n2'); c1 = uitreenode(n1, 'Text', 'c1'); c2 = uitreenode(n1, 'Text', 'c2'); g1 = uitreenode(c1, 'Text', 'g1');
tr2 = uitree(uf); m1 = uitreenode(tr2, 'Text', 'm1');
tryp('expand(tr)', @() run(@() expand(tr)));
tryp('expand(n1)', @() run(@() expand(n1)));
tryp('expand(c2) leaf', @() run(@() expand(c2)));
tryp('expand(tr, all)', @() run(@() expand(tr, 'all')));
tryp('expand(n1, all)', @() run(@() expand(n1, 'all')));
tryp('expand(tr, ALL)', @() run(@() expand(tr, 'ALL')));
tryp('expand(tr, "all")', @() run(@() expand(tr, "all")));
tryp('expand(tr, bogus)', @() run(@() expand(tr, 'bogus')));
tryp('expand(tr, n1)', @() run(@() expand(tr, n1)));
tryp('expand(tr, 1)', @() run(@() expand(tr, 1)));
tryp('expand([n1 n2])', @() run(@() expand([n1 n2])));
tryp('expand(uf)', @() run(@() expand(uf)));
tryp('expand(uilistbox)', @() run(@() expand(uilistbox(uf))));
tryp('expand(5)', @() run(@() expand(5)));
tryp('expand()', @() run(@() expand()));
tryp('expand(tr) output', @() expand(tr));
tryp('collapse(tr)', @() run(@() collapse(tr)));
tryp('collapse(n1)', @() run(@() collapse(n1)));
tryp('collapse(tr, all)', @() run(@() collapse(tr, 'all')));
tryp('collapse(n1, all)', @() run(@() collapse(n1, 'all')));
tryp('collapse(tr, bogus)', @() run(@() collapse(tr, 'bogus')));
tryp('collapse(uf)', @() run(@() collapse(uf)));
tryp('collapse(checkbox tree)', @() run(@() collapse(uitree(uf, 'checkbox'))));
tryp('texts before move', @() texts(tr));
tryp('move(c1, n2)', @() aft(@() move(c1, n2), tr));
tryp('texts after move(c1, n2)', @() texts(tr));
tryp('move(c1, c2, before)', @() aft(@() move(c1, c2, 'before'), tr));
tryp('texts after move(c1, c2, before)', @() texts(tr));
tryp('move(c1, c2, after)', @() aft(@() move(c1, c2, 'after'), tr));
tryp('texts after move(c1, c2, after)', @() texts(tr));
tryp('move(c1, c2, AFTER)', @() aft(@() move(c1, c2, 'AFTER'), tr));
tryp('move(c1, c2, bef)', @() aft(@() move(c1, c2, 'bef'), tr));
tryp('move(c1, c2, "before")', @() aft(@() move(c1, c2, "before"), tr));
tryp('move(c1, c2, bogus)', @() aft(@() move(c1, c2, 'bogus'), tr));
tryp('move(c1, c2, before, 1)', @() aft(@() move(c1, c2, 'before', 1), tr));
tryp('move(c1, c1)', @() aft(@() move(c1, c1), tr));
tryp('move(n1, g1) into own descendant', @() aft(@() move(n1, g1), tr));
tryp('move(c1, tr)', @() aft(@() move(c1, tr), tr));
tryp('texts after move(c1, tr)', @() texts(tr));
tryp('move(c1, tr, before)', @() aft(@() move(c1, tr, 'before'), tr));
tryp('move(c1, uf)', @() aft(@() move(c1, uf), tr));
tryp('move(c1, m1)', @() aft(@() move(c1, m1), tr));
tryp('texts after move(c1, m1)', @() [texts(tr) ' | ' texts(tr2)]);
tryp('move(c1, m1, before) across trees', @() aft(@() move(c1, m1, 'before'), tr2));
tryp('texts after', @() [texts(tr) ' | ' texts(tr2)]);
tryp('move(tr, n1)', @() aft(@() move(tr, n1), tr));
tryp('move(c1)', @() aft(@() move(c1), tr));
tryp('move(5, n1)', @() aft(@() move(5, n1), tr));
tryp('move([c1 c2], n2)', @() aft(@() move([c2 g1], n2), tr));
tryp('move(c1, n1) output', @() move(c1, n1));
tryp('texts end', @() [texts(tr) ' | ' texts(tr2)]);
tryp('n1.Children = [c2 g1] reorder', @() aft(@() set(n1, 'Children', [g1; c2]), tr));
tryp('texts after Children', @() texts(tr));
tryp('n1.Children = c2 only', @() aft(@() set(n1, 'Children', c2), tr));
tryp('n1.Children = [c2; m1] foreign', @() aft(@() set(n1, 'Children', [c2; m1]), tr));
tryp('tr.Children = [n2; n1]', @() aft(@() set(tr, 'Children', [n2; n1]), tr));
tryp('texts after tr.Children', @() texts(tr));
tryp('c2.Parent = tr', @() aft(@() set(c2, 'Parent', tr), tr));
tryp('texts after Parent', @() texts(tr));
tryp('c2.Parent = tr2', @() aft(@() set(c2, 'Parent', tr2), tr2));
tryp('texts after Parent tr2', @() [texts(tr) ' | ' texts(tr2)]);
tryp('c2.Parent = uf', @() aft(@() set(c2, 'Parent', uf), tr2));
tryp('c2.Parent = uilistbox', @() aft(@() set(c2, 'Parent', uilistbox(uf)), tr2));
tryp('c2.Parent = []', @() aft(@() set(c2, 'Parent', []), tr2));
tryp('c2.Parent after []', @() class(c2.Parent));
tryp('c2 valid after []', @() isvalid(c2));
tryp('c2.Parent = tr again', @() aft(@() set(c2, 'Parent', tr), tr));
tryp('tr.Parent = f classic', @() aft(@() set(tr, 'Parent', f), tr));
tryp('tr.Parent = uf', @() aft(@() set(tr, 'Parent', uf), tr));
tryp('copyobj(n1, tr2)', @() texts(tr2, copyobj(n1, tr2)));
tryp('copyobj(tr, uf) texts', @() texts(copyobj(tr, uf)));
tryp('copyobj(n1, uf)', @() class(copyobj(n1, uf)));
delete(allchild(uf));

%% scroll and focus
tr = uitree(uf); n1 = uitreenode(tr, 'Text', 'n1'); c1 = uitreenode(n1, 'Text', 'c1');
tr2 = uitree(uf); m1 = uitreenode(tr2, 'Text', 'm1');
lb = uilistbox(uf, 'Items', {'one', 'two', 'three'});
t = uitable(uf, 'Data', magic(4));
g = uigridlayout(uf, 'Scrollable', 'on');
pn = uipanel(uf, 'Scrollable', 'on');
ta = uitextarea(uf);
tryp('scroll(tr, n1)', @() run(@() scroll(tr, n1)));
tryp('scroll(tr, c1)', @() run(@() scroll(tr, c1)));
tryp('scroll(tr, m1) other tree', @() run(@() scroll(tr, m1)));
tryp('scroll(tr, top)', @() run(@() scroll(tr, 'top')));
tryp('scroll(tr, bottom)', @() run(@() scroll(tr, 'bottom')));
tryp('scroll(tr, TOP)', @() run(@() scroll(tr, 'TOP')));
tryp('scroll(tr, left)', @() run(@() scroll(tr, 'left')));
tryp('scroll(tr, 1)', @() run(@() scroll(tr, 1)));
tryp('scroll(tr, bogus)', @() run(@() scroll(tr, 'bogus')));
tryp('scroll(tr)', @() run(@() scroll(tr)));
tryp('scroll(tr, n1, 1)', @() run(@() scroll(tr, n1, 1)));
tryp('scroll(lb, top)', @() run(@() scroll(lb, 'top')));
tryp('scroll(lb, bottom)', @() run(@() scroll(lb, 'bottom')));
tryp('scroll(lb, 2)', @() run(@() scroll(lb, 2)));
tryp('scroll(lb, 0)', @() run(@() scroll(lb, 0)));
tryp('scroll(lb, 99)', @() run(@() scroll(lb, 99)));
tryp('scroll(lb, 1.5)', @() run(@() scroll(lb, 1.5)));
tryp('scroll(lb, left)', @() run(@() scroll(lb, 'left')));
tryp('scroll(lb, two)', @() run(@() scroll(lb, 'two')));
tryp('scroll(lb, [1 2])', @() run(@() scroll(lb, [1 2])));
tryp('scroll(t, top)', @() run(@() scroll(t, 'top')));
tryp('scroll(t, bottom)', @() run(@() scroll(t, 'bottom')));
tryp('scroll(t, left)', @() run(@() scroll(t, 'left')));
tryp('scroll(t, right)', @() run(@() scroll(t, 'right')));
tryp('scroll(t, row, 2)', @() run(@() scroll(t, 'row', 2)));
tryp('scroll(t, column, 2)', @() run(@() scroll(t, 'column', 2)));
tryp('scroll(t, cell, [1 2])', @() run(@() scroll(t, 'cell', [1 2])));
tryp('scroll(t, row, 99)', @() run(@() scroll(t, 'row', 99)));
tryp('scroll(t, row, 0)', @() run(@() scroll(t, 'row', 0)));
tryp('scroll(t, row)', @() run(@() scroll(t, 'row')));
tryp('scroll(t, cell, [1 2 3])', @() run(@() scroll(t, 'cell', [1 2 3])));
tryp('scroll(t, cell, 1)', @() run(@() scroll(t, 'cell', 1)));
tryp('scroll(t, 2)', @() run(@() scroll(t, 2)));
tryp('scroll(t, bogus)', @() run(@() scroll(t, 'bogus')));
tryp('scroll(t, 10, 20)', @() run(@() scroll(t, 10, 20)));
tryp('scroll(g, top)', @() run(@() scroll(g, 'top')));
tryp('scroll(g, bottom)', @() run(@() scroll(g, 'bottom')));
tryp('scroll(g, left)', @() run(@() scroll(g, 'left')));
tryp('scroll(g, right)', @() run(@() scroll(g, 'right')));
tryp('scroll(g, 10, 20)', @() run(@() scroll(g, 10, 20)));
tryp('scroll(g, [10 20])', @() run(@() scroll(g, [10 20])));
tryp('scroll(g, 10)', @() run(@() scroll(g, 10)));
tryp('scroll(g, -5, 0)', @() run(@() scroll(g, -5, 0)));
tryp('scroll(g, row, 1)', @() run(@() scroll(g, 'row', 1)));
tryp('scroll(g) not scrollable', @() run(@() scroll(uigridlayout(uf), 'top')));
tryp('scroll(pn, top)', @() run(@() scroll(pn, 'top')));
tryp('scroll(pn, 10, 20)', @() run(@() scroll(pn, 10, 20)));
tryp('scroll(uf, top)', @() run(@() scroll(uf, 'top')));
tryp('scroll(uf, 10, 20)', @() run(@() scroll(uf, 10, 20)));
tryp('scroll(f, top) classic', @() run(@() scroll(f, 'top')));
tryp('scroll(classic panel, top)', @() run(@() scroll(uipanel(f), 'top')));
tryp('scroll(ta, top)', @() run(@() scroll(ta, 'top')));
tryp('scroll(ta, bottom)', @() run(@() scroll(ta, 'bottom')));
tryp('scroll(ta, 2)', @() run(@() scroll(ta, 2)));
tryp('scroll(ta, left)', @() run(@() scroll(ta, 'left')));
tryp('scroll(uibutton, top)', @() run(@() scroll(uibutton(uf), 'top')));
tryp('scroll(uiknob, top)', @() run(@() scroll(uiknob(uf), 'top')));
tryp('scroll(uidropdown, top)', @() run(@() scroll(uidropdown(uf), 'top')));
tryp('scroll(uicontrol listbox, top)', @() run(@() scroll(uicontrol(f, 'Style', 'listbox'), 'top')));
tryp('scroll(uitab, top)', @() run(@() scroll(uitab(uitabgroup(uf)), 'top')));
tryp('scroll(5, top)', @() run(@() scroll(5, 'top')));
tryp('scroll()', @() run(@() scroll()));
tryp('scroll(lb)', @() run(@() scroll(lb)));
tryp('scroll(lb, top) output', @() scroll(lb, 'top'));
tryp('ScrollableViewportLocation g', @() get(g, 'ScrollableViewportLocation'));
tryp('ScrollableViewportLocation after scroll(g, 10, 20)', @() aft(@() scroll(g, 10, 20), g, 'ScrollableViewportLocation'));
tryp('focus(tr)', @() run(@() focus(tr)));
tryp('focus(n1)', @() run(@() focus(n1)));
tryp('focus(uiknob)', @() run(@() focus(uiknob(uf))));
tryp('focus(uigauge)', @() run(@() focus(uigauge(uf))));
tryp('focus(uilamp)', @() run(@() focus(uilamp(uf))));
tryp('focus(uidatepicker)', @() run(@() focus(uidatepicker(uf))));
tryp('focus(uicolorpicker)', @() run(@() focus(uicolorpicker(uf))));
tryp('focus(uiswitch)', @() run(@() focus(uiswitch(uf))));
tryp('focus(uitable)', @() run(@() focus(t)));
tryp('focus(uitab)', @() run(@() focus(uitab(uitabgroup(uf)))));
tryp('focus(uimenu)', @() run(@() focus(uimenu(uf))));
delete(allchild(uf));
delete(uf); delete(f);
end

function r = chain(h)
r = class(h);
p = h;
while isprop(p, 'Parent') && ~isempty(p.Parent)
    p = p.Parent;
    r = [r ' < ' class(p)];
end
end

function r = kind(h)
r = sprintf('%s %s', class(h), get(h, 'Type'));
end

function r = kindclose(h)
r = kind(h);
delete(ancestor(h, 'figure'));
end

function r = dotget(h)
r = h.Bogus;
end

function r = dotset(h)
h.Bogus = 1;
r = 'set';
end

function r = flags(h)
r = [isprop(h, 'Units') isgraphics(h) ishghandle(h) isvalid(h) ishandle(h) isa(h, 'matlab.ui.control.Component') isa(h, 'matlab.graphics.Graphics')];
end

function r = reparent(h, p)
set(h, 'Parent', p);
r = class(get(h, 'Parent'));
end

function r = styleinfo(s)
names = properties(s);
parts = cell(1, numel(names));
for k = 1:numel(names)
    parts{k} = sprintf('%s=%s', names{k}, v2s(s.(names{k})));
end
r = strjoin(parts, '; ');
end

function r = dotstyle(name, value)
s = uistyle();
s.(name) = value;
r = styleinfo(s);
end

function r = valuesem()
s = uistyle('FontWeight', 'bold');
s2 = s;
s2.FontName = 'Arial';
r = sprintf('s=%s | s2=%s', v2s(s.FontName), v2s(s2.FontName));
end

function s = setstyle()
s = uistyle();
set(s, 'FontWeight', 'bold');
end

function r = sc(h)
T = h.StyleConfigurations;
r = sprintf('%dx%d vars=%s', size(T, 1), size(T, 2), strjoin(T.Properties.VariableNames, ','));
if size(T, 1) > 0
    r = sprintf('%s classes=%s,%s,%s', r, class(T.Target), class(T.TargetIndex), class(T.Style));
end
for k = 1:size(T, 1)
    tg = T.Target(k);
    if iscategorical(tg) || isstring(tg), tg = char(tg); end
    ti = T.TargetIndex(k);
    if iscell(ti), ti = ti{1}; end
    if isa(ti, 'matlab.graphics.Graphics')
        tis = sprintf('<%d %s>', numel(ti), strjoin(arrayfun(@(n) n.Text, ti(:)', 'UniformOutput', false), ','));
    else
        tis = v2s(ti);
    end
    r = sprintf('%s | %d: %s %s {%s}', r, k, v2s(tg), tis, styleinfo(T.Style(k)));
end
end

function r = aft(fn, h, name)
if nargin < 3, name = 'StyleConfigurations'; end
lastwarn('');
fn();
if strcmp(name, 'StyleConfigurations'), r = sc(h); else, r = v2s(get(h, name)); end
[wm, wid] = lastwarn;
if ~isempty(wm), r = sprintf('%s  WARN %s | %s', r, wid, oneline(wm)); end
end

function r = run(fn)
lastwarn('');
fn();
r = 'ok';
[wm, wid] = lastwarn;
if ~isempty(wm), r = sprintf('%s  WARN %s | %s', r, wid, oneline(wm)); end
end

function t = deletedtable(uf)
t = uitable(uf);
delete(t);
end

function r = styleedit(t)
removeStyle(t);
s = uistyle('FontWeight', 'bold');
addStyle(t, s, 'row', 1);
s.FontWeight = 'normal';
s.FontColor = 'red';
r = sc(t);
end

function r = datasmaller(t, sb)
removeStyle(t);
addStyle(t, sb, 'row', 4);
addStyle(t, sb, 'cell', [4 4]);
t.Data = magic(2);
r = sc(t);
t.Data = magic(4);
end

function r = setsc(t)
T = t.StyleConfigurations;
t.StyleConfigurations = T;
r = 'set';
end

function r = delnode(tr, sb)
removeStyle(tr);
n = uitreenode(tr, 'Text', 'doomed');
addStyle(tr, sb, 'node', n);
addStyle(tr, sb, 'level', 1);
delete(n);
r = sc(tr);
end

function r = itemsshorter(lb)
removeStyle(lb);
s = uistyle('FontWeight', 'bold');
addStyle(lb, s, 'item', 3);
lb.Items = {'a'};
r = sc(lb);
lb.Items = {'one', 'two', 'three'};
end

function r = texts(holder, extra)
if nargin > 1, holder = extra; end
r = walk(holder, 0);
end

function r = walk(h, depth)
r = '';
kids = h.Children;
for k = 1:numel(kids)
    r = [r sprintf('%s%s(%d) ', repmat('.', 1, depth), kids(k).Text, numel(kids(k).Children))];
    r = [r walk(kids(k), depth + 1)];
end
end
