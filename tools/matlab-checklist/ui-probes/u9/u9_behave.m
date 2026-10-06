function u9_behave
% U9 probe: how knobs, switches, gauges, pickers, trees and nodes behave; the event classes; a
% context menu on a component; the settled rectangles. Headless.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
f = figure('Visible', 'off', 'Position', [100 100 560 420]);

%% event data classes
names = {'ValueChangedData', 'ValueChangingData', 'NodeExpandedData', 'NodeCollapsedData', 'NodeTextChangedData', ...
    'SelectedNodesChangedData', 'CheckedNodesChangedData', 'ClickedData', 'DoubleClickedData', 'TreeInteraction', ...
    'ListBoxInteraction', 'ComponentInteraction', 'DropDownInteraction', 'ContextMenuOpeningData', 'ValueChangeData', ...
    'ImageClickedData', 'HyperlinkClickedData', 'ButtonPushedData', 'DropDownOpeningData', 'Interaction'};
pk = {'matlab.ui.eventdata.', 'matlab.ui.eventdata.internal.'};
for k = 1:numel(names)
    for q = 1:numel(pk)
        mc = meta.class.fromName([pk{q} names{k}]);
        if isempty(mc), continue; end
        pl = mc.PropertyList;
        pub = pl(arrayfun(@(p) ischar(p.GetAccess) && strcmp(p.GetAccess, 'public') && ~p.Hidden, pl));
        sup = arrayfun(@(s) s.Name, mc.SuperclassList, 'UniformOutput', false);
        fprintf('EVT %s%s : props=%s supers=%s handle=%d\n', pk{q}, names{k}, strjoin({pub.Name}, ','), strjoin(sup, ','), mc.HandleCompatible);
    end
end
for k = 1:numel(names)
    for q = 1:numel(pk)
        tryp(['construct ' names{k}], @() class(feval([pk{q} names{k}])));
        tryp(['construct ' names{k} ' (1)'], @() class(feval([pk{q} names{k}], 1)));
    end
end
tryp('TreeInteraction(n, 2)', @() fieldsof(matlab.ui.eventdata.TreeInteraction(uitreenode(uitree(uf)), 2)));
tryp('ListBoxInteraction(2)', @() fieldsof(matlab.ui.eventdata.ListBoxInteraction(2)));
tryp('ClickedData(interaction)', @() fieldsof(matlab.ui.eventdata.ClickedData(matlab.ui.eventdata.ListBoxInteraction(2))));
delete(allchild(uf));

%% trees and nodes
tr = uitree(uf);
tryp('tree Children empty', @() v2s(tr.Children));
n1 = uitreenode(tr, 'Text', 'n1'); n2 = uitreenode(tr, 'Text', 'n2'); n3 = uitreenode(tr, 'Text', 'n3');
c1 = uitreenode(n1, 'Text', 'c1'); c2 = uitreenode(n1, 'Text', 'c2');
tryp('tree Children order', @() texts(tr));
tryp('tree Children class size', @() v2s(tr.Children));
tryp('node Children', @() v2s(n1.Children));
tryp('node Parent', @() class(c1.Parent));
tryp('leaf Children', @() v2s(c1.Children));
tryp('uf.Children with tree', @() v2s(uf.Children));
tryp('findobj uitreenode', @() numel(findobj(uf, 'Type', 'uitreenode')));
tryp('findobj Text c1', @() numel(findobj(tr, 'Text', 'c1')));
tryp('findall uitreenode', @() numel(findall(uf, 'Type', 'uitreenode')));
tryp('allchild(tr)', @() v2s(allchild(tr)));
tryp('allchild(n1)', @() v2s(allchild(n1)));
tryp('isa node', @() [isa(n1, 'matlab.ui.container.TreeNode') isa(n1, 'matlab.ui.container.Container') isa(n1, 'matlab.ui.control.Component') isa(n1, 'matlab.graphics.Graphics') isa(tr, 'matlab.ui.container.Tree') isa(tr, 'matlab.ui.control.Component')]);
tryp('ancestor(c1, figure)', @() class(ancestor(c1, 'figure')));
tryp('ancestor(c1, uitree)', @() class(ancestor(c1, 'uitree')));
tryp('SelectedNodes default', @() v2s(tr.SelectedNodes));
tryp('SelectedNodes <- n1', @() setget(tr, 'SelectedNodes', n1));
tryp('SelectedNodes <- [n1 n2] single', @() setget(tr, 'SelectedNodes', [n1 n2]));
tryp('SelectedNodes <- []', @() setget(tr, 'SelectedNodes', []));
tryp('SelectedNodes <- {n1}', @() setget(tr, 'SelectedNodes', {n1}));
tryp('SelectedNodes <- 5', @() setget(tr, 'SelectedNodes', 5));
tryp('SelectedNodes <- tr', @() setget(tr, 'SelectedNodes', tr));
tryp('SelectedNodes <- other tree node', @() setget(tr, 'SelectedNodes', uitreenode(uitree(uf))));
tryp('SelectedNodes <- c1 nested', @() setget(tr, 'SelectedNodes', c1));
tryp('Multiselect on', @() setget(tr, 'Multiselect', 'on'));
tryp('SelectedNodes <- [n1 n2] multi', @() setget(tr, 'SelectedNodes', [n1 n2]));
tryp('SelectedNodes <- [n1; n2] column', @() setget(tr, 'SelectedNodes', [n1; n2]));
tryp('SelectedNodes <- [n1 n1] dup', @() setget(tr, 'SelectedNodes', [n1 n1]));
tryp('SelectedNodes <- [n2 n1 c1] order', @() nodetexts(setget(tr, 'SelectedNodes', [n2 n1 c1], true)));
tryp('SelectedNodes <- [n1 other]', @() setget(tr, 'SelectedNodes', [n1 uitreenode(uitree(uf))]));
tryp('Multiselect off with two', @() nodetexts(setget(tr, 'Multiselect', 'off', true, 'SelectedNodes')));
tryp('SelectedNodes after delete selected', @() delsel(tr, n2));
tryp('SelectedNodes after delete parent of selected', @() delsel(tr, n1, c1));
tryp('texts after deletes', @() texts(tr));
tryp('SelectedNodes after node moved out', @() movesel(tr, uitree(uf)));
tryp('node Text <- 5', @() setget(n3, 'Text', 5));
tryp('node Text <- "s"', @() setget(n3, 'Text', "s"));
tryp('node Text <- {a}', @() setget(n3, 'Text', {'a'}));
tryp('node Text <- empty', @() setget(n3, 'Text', ''));
tryp('node Text <- ["a" "b"]', @() setget(n3, 'Text', ["a" "b"]));
tryp('node Text <- char matrix', @() setget(n3, 'Text', ['ab'; 'cd']));
tryp('node Text <- sprintf newline', @() setget(n3, 'Text', sprintf('a\nb')));
tryp('node NodeData <- struct', @() setget(n3, 'NodeData', struct('a', 1)));
tryp('node NodeData <- handle', @() setget(n3, 'NodeData', uf));
tryp('node NodeData <- cell', @() setget(n3, 'NodeData', {1, 'a'}));
tryp('node NodeData <- "s"', @() setget(n3, 'NodeData', "s"));
tryp('node Icon <- nofile.png', @() setget(n3, 'Icon', 'nofile.png'));
tryp('node Icon <- error', @() setget(n3, 'Icon', 'error'));
tryp('node Icon <- zeros(4,4,3)', @() setget(n3, 'Icon', zeros(4, 4, 3)));
tryp('node Icon <- 5', @() setget(n3, 'Icon', 5));
tryp('node Icon <- empty', @() setget(n3, 'Icon', ''));
tryp('node Icon <- []', @() setget(n3, 'Icon', []));
tryp('node Icon <- "s"', @() setget(n3, 'Icon', "s"));
tryp('node Icon <- ones(2,2)', @() setget(n3, 'Icon', ones(2, 2)));
tryp('node Icon <- uint8 cube', @() setget(n3, 'Icon', uint8(zeros(2, 2, 3))));
tryp('node Icon <- 2 x 2 x 3 doubles over 1', @() setget(n3, 'Icon', 2 * ones(2, 2, 3)));
tryp('node Visible', @() get(n3, 'Visible'));
tryp('node Position', @() get(n3, 'Position'));
tryp('node Enable', @() get(n3, 'Enable'));
tryp('node Layout', @() get(n3, 'Layout'));
tryp('tree Editable on', @() setget(tr, 'Editable', 'on'));
tryp('delete(tr) then nodes', @() deltree(uf));
tryp('tree in grid Position', @() get(uitree(uigridlayout(uf)), 'Position'));
tryp('tree in grid Layout', @() get(uitree(uigridlayout(uf)), 'Layout'));
tryp('node Parent write to grid', @() set(uitreenode(uitree(uf)), 'Parent', uigridlayout(uf)));
delete(allchild(uf));

%% checkbox trees
ct = uitree(uf, 'checkbox');
p1 = uitreenode(ct, 'Text', 'p1'); p2 = uitreenode(ct, 'Text', 'p2');
k1 = uitreenode(p1, 'Text', 'k1'); k2 = uitreenode(p1, 'Text', 'k2'); k3 = uitreenode(k1, 'Text', 'k3');
tryp('CheckedNodes default', @() v2s(ct.CheckedNodes));
tryp('CheckedNodes <- p1', @() nodetexts(setget(ct, 'CheckedNodes', p1, true)));
tryp('CheckedNodes <- k1 only', @() nodetexts(setget(ct, 'CheckedNodes', k1, true)));
tryp('CheckedNodes <- [k1 k2] all children', @() nodetexts(setget(ct, 'CheckedNodes', [k1 k2], true)));
tryp('CheckedNodes <- k3 deepest', @() nodetexts(setget(ct, 'CheckedNodes', k3, true)));
tryp('CheckedNodes <- [k3 k2]', @() nodetexts(setget(ct, 'CheckedNodes', [k3 k2], true)));
tryp('CheckedNodes <- [p2 k1]', @() nodetexts(setget(ct, 'CheckedNodes', [p2 k1], true)));
tryp('CheckedNodes <- [p1; p2] column', @() nodetexts(setget(ct, 'CheckedNodes', [p1; p2], true)));
tryp('CheckedNodes <- [p1 p1] dup', @() nodetexts(setget(ct, 'CheckedNodes', [p1 p1], true)));
tryp('CheckedNodes <- []', @() v2s(setget(ct, 'CheckedNodes', [], true)));
tryp('CheckedNodes <- 5', @() setget(ct, 'CheckedNodes', 5));
tryp('CheckedNodes <- other tree node', @() setget(ct, 'CheckedNodes', uitreenode(uitree(uf, 'checkbox'))));
tryp('CheckedNodes <- plain tree node', @() setget(ct, 'CheckedNodes', uitreenode(uitree(uf))));
tryp('CheckedNodes <- ct', @() setget(ct, 'CheckedNodes', ct));
tryp('CheckedNodes on plain tree', @() setget(uitree(uf), 'CheckedNodes', []));
tryp('CheckedNodes read plain tree', @() get(uitree(uf), 'CheckedNodes'));
tryp('Multiselect on checkbox tree', @() get(ct, 'Multiselect'));
tryp('SelectedNodes <- [p1 p2] on checkbox tree', @() setget(ct, 'SelectedNodes', [p1 p2]));
tryp('CheckedNodes after new child of checked', @() newchild(ct, p2));
tryp('CheckedNodes after delete checked', @() delchecked(ct, p1, k1, k2, k3));
tryp('CheckedNodes after move checked out', @() movechecked(ct, uitree(uf, 'checkbox')));
tryp('texts checkbox tree', @() texts(ct));
delete(allchild(uf));

%% discrete knobs and switches
dk = uiknob(uf, 'discrete');
tryp('dk Items <- {a,b,c}', @() setget2(dk, 'Items', {'a', 'b', 'c'}, 'Value', 'ValueIndex'));
tryp('dk Value <- b', @() setget2(dk, 'Value', 'b', 'Value', 'ValueIndex'));
tryp('dk Value <- B case', @() setget2(dk, 'Value', 'B', 'Value', 'ValueIndex'));
tryp('dk Value <- zz', @() setget(dk, 'Value', 'zz'));
tryp('dk Value <- 2', @() setget(dk, 'Value', 2));
tryp('dk Value <- {b}', @() setget(dk, 'Value', {'b'}));
tryp('dk Value <- "c"', @() setget2(dk, 'Value', "c", 'Value', 'ValueIndex'));
tryp('dk ValueIndex <- 1', @() setget2(dk, 'ValueIndex', 1, 'Value', 'ValueIndex'));
tryp('dk ValueIndex <- 9', @() setget(dk, 'ValueIndex', 9));
tryp('dk ValueIndex <- 0', @() setget(dk, 'ValueIndex', 0));
tryp('dk ValueIndex <- []', @() setget(dk, 'ValueIndex', []));
tryp('dk Items <- {} ', @() setget2(dk, 'Items', {}, 'Value', 'ValueIndex'));
tryp('dk Items <- {x} after empty', @() setget2(dk, 'Items', {'x'}, 'Value', 'ValueIndex'));
tryp('dk Items <- {a;b} column', @() setget2(dk, 'Items', {'a'; 'b'}, 'Items', 'Value'));
tryp('dk Items <- ["s" "t"]', @() setget2(dk, 'Items', ["s" "t"], 'Items', 'Value'));
tryp('dk Items <- {a, 5}', @() setget(dk, 'Items', {'a', 5}));
tryp('dk Items <- abc char', @() setget2(dk, 'Items', 'abc', 'Items', 'Value'));
tryp('dk ItemsData <- [10 20]', @() setget2(dk, 'ItemsData', [10 20], 'Value', 'ItemsData'));
tryp('dk Value <- 20 with data', @() setget2(dk, 'Value', 20, 'Value', 'ValueIndex'));
tryp('dk Value <- t text with data', @() setget(dk, 'Value', 't'));
tryp('dk ItemsData <- {a,b}', @() setget2(dk, 'ItemsData', {'x', 'y'}, 'Value', 'ItemsData'));
tryp('dk ItemsData <- []', @() setget2(dk, 'ItemsData', [], 'Value', 'ItemsData'));
tryp('dk keeps value when Items still has it', @() keepvalue(dk));
tryp('knob Items', @() get(uiknob(uf), 'Items'));
tryp('knob ValueIndex', @() get(uiknob(uf), 'ValueIndex'));
tryp('dk Limits', @() get(dk, 'Limits'));
sw = uiswitch(uf);
tryp('sw Value <- On', @() setget2(sw, 'Value', 'On', 'Value', 'ValueIndex'));
tryp('sw Value <- on case', @() setget2(sw, 'Value', 'on', 'Value', 'ValueIndex'));
tryp('sw Value <- OFF', @() setget2(sw, 'Value', 'OFF', 'Value', 'ValueIndex'));
tryp('sw Value <- true', @() setget(sw, 'Value', true));
tryp('sw Value <- 1', @() setget(sw, 'Value', 1));
tryp('sw Value <- Maybe', @() setget(sw, 'Value', 'Maybe'));
tryp('sw Value <- "On"', @() setget2(sw, 'Value', "On", 'Value', 'ValueIndex'));
tryp('sw Items <- {a,b,c}', @() setget(sw, 'Items', {'a', 'b', 'c'}));
tryp('sw Items <- {a}', @() setget(sw, 'Items', {'a'}));
tryp('sw Items <- {}', @() setget(sw, 'Items', {}));
tryp('sw Items <- {Lo,Hi}', @() setget2(sw, 'Items', {'Lo', 'Hi'}, 'Value', 'ValueIndex'));
tryp('sw Value <- Hi', @() setget2(sw, 'Value', 'Hi', 'Value', 'ValueIndex'));
tryp('sw Items <- {Hi,Lo} swapped keeps value', @() setget2(sw, 'Items', {'Hi', 'Lo'}, 'Value', 'ValueIndex'));
tryp('sw Items <- ["p";"q"]', @() setget2(sw, 'Items', ["p"; "q"], 'Items', 'Value'));
tryp('sw ItemsData <- [0 1]', @() setget2(sw, 'ItemsData', [0 1], 'Value', 'ItemsData'));
tryp('sw Value <- 1 with data', @() setget2(sw, 'Value', 1, 'Value', 'ValueIndex'));
tryp('sw Value <- true with data', @() setget2(sw, 'Value', true, 'Value', 'ValueIndex'));
tryp('sw ItemsData <- [1 2 3]', @() setget(sw, 'ItemsData', [1 2 3]));
tryp('sw ItemsData <- {a}', @() setget(sw, 'ItemsData', {'a'}));
tryp('sw ValueIndex <- 2', @() setget2(sw, 'ValueIndex', 2, 'Value', 'ValueIndex'));
tryp('sw ValueIndex <- 3', @() setget(sw, 'ValueIndex', 3));
tryp('sw Orientation <- vertical', @() setget(sw, 'Orientation', 'vertical'));
tryp('rocker Orientation <- horizontal', @() setget(uiswitch(uf, 'rocker'), 'Orientation', 'horizontal'));
tryp('toggle Orientation <- horizontal', @() setget(uiswitch(uf, 'toggle'), 'Orientation', 'horizontal'));
delete(allchild(uf));

%% knobs and gauges
kn = uiknob(uf);
tryp('knob Value <- 150', @() setget(kn, 'Value', 150));
tryp('knob Value <- -1', @() setget(kn, 'Value', -1));
tryp('knob Value <- NaN', @() setget(kn, 'Value', NaN));
tryp('knob Value <- 50', @() setget(kn, 'Value', 50));
tryp('knob Limits <- [0 10] with Value 50', @() setget2(kn, 'Limits', [0 10], 'Limits', 'Value'));
tryp('knob Limits <- [60 100] with Value', @() setget2(kn, 'Limits', [60 100], 'Limits', 'Value'));
tryp('knob Limits <- [10 0]', @() setget(kn, 'Limits', [10 0]));
tryp('knob Limits <- [5 5]', @() setget(kn, 'Limits', [5 5]));
tryp('knob Limits <- [0 Inf]', @() setget(kn, 'Limits', [0 Inf]));
tryp('knob Limits <- [-Inf 0]', @() setget(kn, 'Limits', [-Inf 0]));
tryp('knob Limits <- [0 NaN]', @() setget(kn, 'Limits', [0 NaN]));
tryp('knob Limits <- [0;100]', @() setget(kn, 'Limits', [0; 100]));
tryp('knob Limits <- int8', @() setget(kn, 'Limits', int8([0 50])));
tryp('knob Limits <- single', @() setget(kn, 'Limits', single([0 50])));
tryp('knob Value <- int8(3)', @() setget(kn, 'Value', int8(3)));
tryp('knob Value <- single', @() setget(kn, 'Value', single(2.5)));
tryp('knob Value <- true', @() setget(kn, 'Value', true));
tryp('knob Value <- [1 2]', @() setget(kn, 'Value', [1 2]));
tryp('knob Value <- 5 char', @() setget(kn, 'Value', '5'));
tryp('knob MajorTicks <- [0 50 100]', @() setget2(kn, 'MajorTicks', [0 50 100], 'MajorTicks', 'MajorTicksMode'));
tryp('knob MajorTickLabels after ticks', @() [v2s(kn.MajorTickLabels) ' ' kn.MajorTickLabelsMode]);
tryp('knob MajorTicks <- [100 0 50] unsorted', @() setget(kn, 'MajorTicks', [100 0 50]));
tryp('knob MajorTicks <- [0 500]', @() setget(kn, 'MajorTicks', [0 500]));
tryp('knob MajorTicks <- []', @() setget2(kn, 'MajorTicks', [], 'MajorTicks', 'MajorTickLabels'));
tryp('knob MajorTicksMode <- auto', @() setget2(kn, 'MajorTicksMode', 'auto', 'MajorTicks', 'MajorTickLabels'));
tryp('knob MajorTickLabels <- {a,b}', @() setget2(kn, 'MajorTickLabels', {'a', 'b'}, 'MajorTickLabels', 'MajorTickLabelsMode'));
tryp('knob MajorTickLabels <- ["a" "b"]', @() setget(kn, 'MajorTickLabels', ["a" "b"]));
tryp('knob MajorTickLabels <- 1:3', @() setget(kn, 'MajorTickLabels', 1:3));
tryp('knob MajorTickLabels <- {}', @() setget(kn, 'MajorTickLabels', {}));
tryp('knob MajorTickLabelsMode <- auto', @() setget2(kn, 'MajorTickLabelsMode', 'auto', 'MajorTickLabels', 'MajorTickLabelsMode'));
tryp('knob MinorTicks <- 0:10:100', @() setget2(kn, 'MinorTicks', 0:10:100, 'MinorTicks', 'MinorTicksMode'));
tryp('knob MinorTicks <- []', @() setget(kn, 'MinorTicks', []));
tryp('knob MinorTicksMode <- auto', @() setget2(kn, 'MinorTicksMode', 'auto', 'MinorTicks', 'MinorTicksMode'));
ga = uigauge(uf);
tryp('gauge Value <- 150', @() setget(ga, 'Value', 150));
tryp('gauge Value <- -5', @() setget(ga, 'Value', -5));
tryp('gauge Value <- NaN', @() setget(ga, 'Value', NaN));
tryp('gauge Value <- Inf', @() setget(ga, 'Value', Inf));
tryp('gauge Value <- int8(3)', @() setget(ga, 'Value', int8(3)));
tryp('gauge Value <- true', @() setget(ga, 'Value', true));
tryp('gauge Value <- [1 2]', @() setget(ga, 'Value', [1 2]));
tryp('gauge Limits <- [0 10] with Value', @() setget2(ga, 'Limits', [0 10], 'Limits', 'Value'));
tryp('gauge Limits <- [10 0]', @() setget(ga, 'Limits', [10 0]));
tryp('gauge Limits <- [5 5]', @() setget(ga, 'Limits', [5 5]));
tryp('gauge ScaleColors <- [1 0 0; 0 1 0]', @() setget2(ga, 'ScaleColors', [1 0 0; 0 1 0], 'ScaleColors', 'ScaleColorLimits'));
tryp('gauge ScaleColors <- {red,green}', @() setget2(ga, 'ScaleColors', {'red', 'green'}, 'ScaleColors', 'ScaleColorLimits'));
tryp('gauge ScaleColors <- red', @() setget2(ga, 'ScaleColors', 'red', 'ScaleColors', 'ScaleColorLimits'));
tryp('gauge ScaleColors <- ["red" "green"]', @() setget2(ga, 'ScaleColors', ["red" "green"], 'ScaleColors', 'ScaleColorLimits'));
tryp('gauge ScaleColors <- [1 0 0]', @() setget2(ga, 'ScaleColors', [1 0 0], 'ScaleColors', 'ScaleColorLimits'));
tryp('gauge ScaleColors <- rgb', @() setget2(ga, 'ScaleColors', 'rgb', 'ScaleColors', 'ScaleColorLimits'));
tryp('gauge ScaleColors <- {r,g,b}', @() setget2(ga, 'ScaleColors', {'r', 'g', 'b'}, 'ScaleColors', 'ScaleColorLimits'));
tryp('gauge ScaleColors <- [1 0 0 0.5]', @() setget(ga, 'ScaleColors', [1 0 0 0.5]));
tryp('gauge ScaleColors <- [2 0 0]', @() setget(ga, 'ScaleColors', [2 0 0]));
tryp('gauge ScaleColors <- none', @() setget(ga, 'ScaleColors', 'none'));
tryp('gauge ScaleColors <- {red, 5}', @() setget(ga, 'ScaleColors', {'red', 5}));
tryp('gauge ScaleColorLimits <- [0 50; 50 100] with 3 colours', @() setget2(ga, 'ScaleColorLimits', [0 50; 50 100], 'ScaleColors', 'ScaleColorLimits'));
tryp('gauge ScaleColorLimits <- [0 30; 30 60; 60 100]', @() setget2(ga, 'ScaleColorLimits', [0 30; 30 60; 60 100], 'ScaleColors', 'ScaleColorLimits'));
tryp('gauge ScaleColorLimits <- [50 0]', @() setget(ga, 'ScaleColorLimits', [50 0]));
tryp('gauge ScaleColorLimits <- [0 500]', @() setget2(ga, 'ScaleColorLimits', [0 500], 'ScaleColors', 'ScaleColorLimits'));
tryp('gauge ScaleColorLimits <- []', @() setget2(ga, 'ScaleColorLimits', [], 'ScaleColors', 'ScaleColorLimits'));
tryp('gauge ScaleColorLimits <- [0 50 100]', @() setget(ga, 'ScaleColorLimits', [0 50 100]));
tryp('gauge ScaleColors <- [] ', @() setget2(ga, 'ScaleColors', [], 'ScaleColors', 'ScaleColorLimits'));
tryp('gauge ScaleColorLimits <- [0 50] no colours', @() setget2(ga, 'ScaleColorLimits', [0 50], 'ScaleColors', 'ScaleColorLimits'));
tryp('gauge ScaleColors <- [1 0 0; 0 1 0] with one limit row', @() setget2(ga, 'ScaleColors', [1 0 0; 0 1 0], 'ScaleColors', 'ScaleColorLimits'));
tryp('gauge ScaleDirection <- counterclockwise', @() setget(ga, 'ScaleDirection', 'counterclockwise'));
tryp('gauge ScaleDirection <- counter', @() setget(ga, 'ScaleDirection', 'counter'));
tryp('gauge MajorTicks <- [0 50 100]', @() setget2(ga, 'MajorTicks', [0 50 100], 'MajorTicks', 'MajorTickLabels'));
tryp('gauge MajorTicks <- []', @() setget2(ga, 'MajorTicks', [], 'MajorTicks', 'MajorTickLabels'));
tryp('gauge MajorTicksMode <- auto', @() setget2(ga, 'MajorTicksMode', 'auto', 'MajorTicks', 'MajorTickLabels'));
tryp('linear Orientation <- vertical', @() setget(uigauge(uf, 'linear'), 'Orientation', 'vertical'));
tryp('linear Orientation <- north', @() setget(uigauge(uf, 'linear'), 'Orientation', 'north'));
tryp('ninety Orientation <- southeast', @() setget(uigauge(uf, 'ninetydegree'), 'Orientation', 'southeast'));
tryp('ninety Orientation <- north', @() setget(uigauge(uf, 'ninetydegree'), 'Orientation', 'north'));
tryp('semi Orientation <- south', @() setget(uigauge(uf, 'semicircular'), 'Orientation', 'south'));
tryp('semi Orientation <- east', @() setget(uigauge(uf, 'semicircular'), 'Orientation', 'east'));
tryp('semi Orientation <- northwest', @() setget(uigauge(uf, 'semicircular'), 'Orientation', 'northwest'));
tryp('gauge Orientation', @() get(ga, 'Orientation'));
tryp('linear ScaleDirection', @() get(uigauge(uf, 'linear'), 'ScaleDirection'));
delete(allchild(uf));

%% automatic ticks of knobs and gauges, read after the view settles
ranges = {[0 100], [0 1], [0 10], [0 1000], [-50 50], [0 7], [1 12], [0 0.5], [0 360], [0 255], [0 5], [0 50], [10 20], [0 3], [0 2], [-1 1], [0 1e6], [0.01 0.99], [0 60], [0 24], [0 200], [0 15], [0 25], [0 30], [0 40], [0 80], [20 80], [0 9], [0 11], [0 1e-3], [-100 100], [0 120], [0 150], [0 4], [0 6], [0 8]};
kinds = {'knob', @() uiknob(uf); 'gauge', @() uigauge(uf); 'linear', @() uigauge(uf, 'linear'); 'ninety', @() uigauge(uf, 'ninetydegree'); 'semi', @() uigauge(uf, 'semicircular')};
warm = uiknob(uf); drawnow; pause(2); delete(warm);
for q = 1:size(kinds, 1)
    for r = 1:numel(ranges)
        h = kinds{q, 2}();
        h.Limits = ranges{r};
        drawnow; pause(0.4);
        fprintf('TICKS %s %s : major=%s minor=%s labels=%s\n', kinds{q, 1}, mat2str(ranges{r}), mat2str(h.MajorTicks), mat2str(h.MinorTicks), v2s(h.MajorTickLabels));
        delete(h);
    end
end
delete(allchild(uf));

%% date picker
d1 = datetime(2024, 1, 15); d2 = datetime(2024, 3, 5, 14, 30, 0);
dp = uidatepicker(uf);
tryp('dp Value default', @() v2s(dp.Value));
tryp('dp Value isnat', @() isnat(dp.Value));
tryp('dp Value <- d1', @() setget(dp, 'Value', d1));
tryp('dp Value <- d2 with time', @() setget(dp, 'Value', d2));
tryp('dp Value <- NaT', @() setget(dp, 'Value', NaT));
tryp('dp Value <- char', @() setget(dp, 'Value', '15-Jan-2024'));
tryp('dp Value <- "s"', @() setget(dp, 'Value', "15-Jan-2024"));
tryp('dp Value <- number', @() setget(dp, 'Value', 739266));
tryp('dp Value <- []', @() setget(dp, 'Value', []));
tryp('dp Value <- [d1 d2]', @() setget(dp, 'Value', [d1 d2]));
tryp('dp Value <- datetime.empty', @() setget(dp, 'Value', datetime.empty));
tryp('dp Value <- zoned', @() setget(dp, 'Value', datetime(2024, 1, 15, 'TimeZone', 'UTC')));
tryp('dp Value <- formatted', @() setget(dp, 'Value', datetime(2024, 1, 15, 'Format', 'uuuu/MM/dd')));
tryp('dp Value Format after', @() dp.Value.Format);
tryp('dp Value <- year 0', @() setget(dp, 'Value', datetime(0, 1, 1)));
tryp('dp Value <- year -1', @() setget(dp, 'Value', datetime(-1, 1, 1)));
tryp('dp Value <- year 10000', @() setget(dp, 'Value', datetime(10000, 1, 1)));
tryp('dp Limits default', @() v2s(dp.Limits));
tryp('dp Limits <- [d1 d2]', @() setget(dp, 'Limits', [d1 d2]));
tryp('dp Value <- outside limits', @() setget(dp, 'Value', datetime(2020, 1, 1)));
tryp('dp Value <- inside limits', @() setget(dp, 'Value', datetime(2024, 2, 1)));
tryp('dp Limits <- [d2 d1] reversed', @() setget(dp, 'Limits', [d2 d1]));
tryp('dp Limits <- [d1 d1]', @() setget(dp, 'Limits', [d1 d1]));
tryp('dp Limits <- excludes value', @() setget2(dp, 'Limits', [datetime(2025, 1, 1) datetime(2025, 2, 1)], 'Limits', 'Value'));
tryp('dp Limits <- [d1 NaT]', @() setget(dp, 'Limits', [d1 NaT]));
tryp('dp Limits <- d1 scalar', @() setget(dp, 'Limits', d1));
tryp('dp Limits <- chars', @() setget(dp, 'Limits', {'01-Jan-2024', '01-Feb-2024'}));
tryp('dp Limits <- [d1; d2]', @() setget(dp, 'Limits', [d1; d2]));
tryp('dp Limits <- []', @() setget(dp, 'Limits', []));
tryp('dp Limits <- zoned', @() setget(dp, 'Limits', [datetime(2024, 1, 1, 'TimeZone', 'UTC') datetime(2024, 2, 1, 'TimeZone', 'UTC')]));
dp.Limits = [datetime(0, 1, 1) datetime(9999, 12, 31)];
tryp('dp DisabledDates <- d1', @() setget(dp, 'DisabledDates', d1));
tryp('dp DisabledDates <- [d1 d2]', @() setget(dp, 'DisabledDates', [d1 d2]));
tryp('dp DisabledDates <- [d1; d2]', @() setget(dp, 'DisabledDates', [d1; d2]));
tryp('dp DisabledDates <- [d1 d1]', @() setget(dp, 'DisabledDates', [d1 d1]));
tryp('dp DisabledDates <- [d2 d1] unsorted', @() setget(dp, 'DisabledDates', [d2 d1]));
tryp('dp DisabledDates <- []', @() setget(dp, 'DisabledDates', []));
tryp('dp DisabledDates <- NaT', @() setget(dp, 'DisabledDates', NaT));
tryp('dp DisabledDates <- char', @() setget(dp, 'DisabledDates', '15-Jan-2024'));
tryp('dp DisabledDates <- value date', @() setget2(dp, 'DisabledDates', datetime(2024, 2, 1), 'DisabledDates', 'Value'));
tryp('dp DisabledDaysOfWeek <- 1', @() setget(dp, 'DisabledDaysOfWeek', 1));
tryp('dp DisabledDaysOfWeek <- [1 7]', @() setget(dp, 'DisabledDaysOfWeek', [1 7]));
tryp('dp DisabledDaysOfWeek <- [7 1]', @() setget(dp, 'DisabledDaysOfWeek', [7 1]));
tryp('dp DisabledDaysOfWeek <- [1 1]', @() setget(dp, 'DisabledDaysOfWeek', [1 1]));
tryp('dp DisabledDaysOfWeek <- 1:7', @() setget(dp, 'DisabledDaysOfWeek', 1:7));
tryp('dp DisabledDaysOfWeek <- 8', @() setget(dp, 'DisabledDaysOfWeek', 8));
tryp('dp DisabledDaysOfWeek <- 0', @() setget(dp, 'DisabledDaysOfWeek', 0));
tryp('dp DisabledDaysOfWeek <- 1.5', @() setget(dp, 'DisabledDaysOfWeek', 1.5));
tryp('dp DisabledDaysOfWeek <- Monday', @() setget(dp, 'DisabledDaysOfWeek', 'Monday'));
tryp('dp DisabledDaysOfWeek <- {Monday,Sunday}', @() setget(dp, 'DisabledDaysOfWeek', {'Monday', 'Sunday'}));
tryp('dp DisabledDaysOfWeek <- ["Sat" "Sun"]', @() setget(dp, 'DisabledDaysOfWeek', ["Saturday" "Sunday"]));
tryp('dp DisabledDaysOfWeek <- [1;7]', @() setget(dp, 'DisabledDaysOfWeek', [1; 7]));
tryp('dp DisabledDaysOfWeek <- []', @() setget(dp, 'DisabledDaysOfWeek', []));
tryp('dp DisabledDaysOfWeek <- true', @() setget(dp, 'DisabledDaysOfWeek', true));
tryp('dp DisabledDaysOfWeek <- int8(2)', @() setget(dp, 'DisabledDaysOfWeek', int8(2)));
tryp('dp DisabledDaysOfWeek <- value day', @() disabledday(dp));
tryp('dp DisplayFormat <- dd/MM/uuuu', @() setget2(dp, 'DisplayFormat', 'dd/MM/uuuu', 'DisplayFormat', 'Value'));
tryp('dp DisplayFormat <- MM/dd/yyyy', @() setget2(dp, 'DisplayFormat', 'MM/dd/yyyy', 'DisplayFormat', 'Value'));
tryp('dp DisplayFormat <- uuuu-MM-dd', @() setget2(dp, 'DisplayFormat', 'uuuu-MM-dd', 'DisplayFormat', 'Value'));
tryp('dp DisplayFormat <- MMM d, uuuu', @() setget2(dp, 'DisplayFormat', 'MMM d, uuuu', 'DisplayFormat', 'Value'));
tryp('dp DisplayFormat <- MMMM d', @() setget2(dp, 'DisplayFormat', 'MMMM d', 'DisplayFormat', 'Value'));
tryp('dp DisplayFormat <- uuuu', @() setget2(dp, 'DisplayFormat', 'uuuu', 'DisplayFormat', 'Value'));
tryp('dp DisplayFormat <- dd-MMM-uuuu HH:mm', @() setget2(dp, 'DisplayFormat', 'dd-MMM-uuuu HH:mm', 'DisplayFormat', 'Value'));
tryp('dp DisplayFormat <- HH:mm', @() setget(dp, 'DisplayFormat', 'HH:mm'));
tryp('dp DisplayFormat <- bogus', @() setget(dp, 'DisplayFormat', 'bogus'));
tryp('dp DisplayFormat <- empty', @() setget(dp, 'DisplayFormat', ''));
tryp('dp DisplayFormat <- 5', @() setget(dp, 'DisplayFormat', 5));
tryp('dp DisplayFormat <- "dd/MM/uuuu"', @() setget(dp, 'DisplayFormat', "dd/MM/uuuu"));
tryp('dp DisplayFormat <- eeee', @() setget2(dp, 'DisplayFormat', 'eeee', 'DisplayFormat', 'Value'));
tryp('dp DisplayFormat <- QQQ uuuu', @() setget2(dp, 'DisplayFormat', 'QQQ uuuu', 'DisplayFormat', 'Value'));
tryp('dp DisplayFormat <- DDD', @() setget2(dp, 'DisplayFormat', 'DDD', 'DisplayFormat', 'Value'));
tryp('dp DisplayFormat <- yyyy only', @() setget2(dp, 'DisplayFormat', 'yyyy', 'DisplayFormat', 'Value'));
tryp('dp DisplayFormat <- dd.MM.yy', @() setget2(dp, 'DisplayFormat', 'dd.MM.yy', 'DisplayFormat', 'Value'));
tryp('dp DisplayFormat <- d/M/u', @() setget2(dp, 'DisplayFormat', 'd/M/u', 'DisplayFormat', 'Value'));
tryp('dp DisplayFormat <- MM', @() setget2(dp, 'DisplayFormat', 'MM', 'DisplayFormat', 'Value'));
tryp('dp DisplayFormat <- dd', @() setget2(dp, 'DisplayFormat', 'dd', 'DisplayFormat', 'Value'));
tryp('dp DisplayFormat <- with quotes', @() setget2(dp, 'DisplayFormat', '''Date:'' dd-MMM-uuuu', 'DisplayFormat', 'Value'));
tryp('dp Value NaT Format', @() setget2(dp, 'Value', NaT, 'Value', 'DisplayFormat'));
tryp('dp Limits Format', @() dp.Limits.Format);
tryp('dp DisabledDates Format', @() dp.DisabledDates.Format);
tryp('dp Editable <- off', @() setget(dp, 'Editable', 'off'));
tryp('dp Placeholder <- pick', @() setget(dp, 'Placeholder', 'pick'));
delete(allchild(uf));

%% colour picker and lamp
cp = uicolorpicker(uf);
tryp('cp Value <- red', @() setget(cp, 'Value', 'red'));
tryp('cp Value <- #00FF00', @() setget(cp, 'Value', '#00FF00'));
tryp('cp Value <- #0f0', @() setget(cp, 'Value', '#0f0'));
tryp('cp Value <- [0 0 1]', @() setget(cp, 'Value', [0 0 1]));
tryp('cp Value <- [0;0;1]', @() setget(cp, 'Value', [0; 0; 1]));
tryp('cp Value <- none', @() setget(cp, 'Value', 'none'));
tryp('cp Value <- [1 0 0 0.5]', @() setget(cp, 'Value', [1 0 0 0.5]));
tryp('cp Value <- [2 0 0]', @() setget(cp, 'Value', [2 0 0]));
tryp('cp Value <- k', @() setget(cp, 'Value', 'k'));
tryp('cp Value <- "blue"', @() setget(cp, 'Value', "blue"));
tryp('cp Value <- bogus', @() setget(cp, 'Value', 'bogus'));
tryp('cp Value <- []', @() setget(cp, 'Value', []));
tryp('cp Value <- uint8([255 0 0])', @() setget(cp, 'Value', uint8([255 0 0])));
tryp('cp Value <- single', @() setget(cp, 'Value', single([0 1 0])));
tryp('cp Value <- [true false false]', @() setget(cp, 'Value', [true false false]));
tryp('cp Icon <- error', @() setget(cp, 'Icon', 'error'));
tryp('cp Icon <- nofile.png', @() setget(cp, 'Icon', 'nofile.png'));
tryp('cp Icon <- zeros(4,4,3)', @() setget(cp, 'Icon', zeros(4, 4, 3)));
tryp('cp Icon <- 5', @() setget(cp, 'Icon', 5));
tryp('cp Icon <- empty', @() setget(cp, 'Icon', ''));
tryp('cp BackgroundColor <- red', @() setget(cp, 'BackgroundColor', 'red'));
tryp('cp BackgroundColor <- none', @() setget(cp, 'BackgroundColor', 'none'));
lp = uilamp(uf);
tryp('lamp Color <- r', @() setget(lp, 'Color', 'r'));
tryp('lamp Color <- none', @() setget(lp, 'Color', 'none'));
tryp('lamp Color <- [1 0 0 0.5]', @() setget(lp, 'Color', [1 0 0 0.5]));
tryp('lamp Color <- zz', @() setget(lp, 'Color', 'zz'));
tryp('lamp Color <- #ff0000', @() setget(lp, 'Color', '#ff0000'));
tryp('lamp Color <- []', @() setget(lp, 'Color', []));
tryp('lamp BackgroundColor', @() get(lp, 'BackgroundColor'));
tryp('lamp FontSize', @() get(lp, 'FontSize'));
delete(allchild(uf));

%% a context menu on a component
cm = uicontextmenu(uf); uimenu(cm, 'Text', 'a');
cm2 = uicontextmenu(f);
b = uibutton(uf);
tryp('b.ContextMenu default', @() v2s(b.ContextMenu));
tryp('b.ContextMenu <- cm', @() setget(b, 'ContextMenu', cm));
tryp('b.ContextMenu <- cm of other figure', @() setget(b, 'ContextMenu', cm2));
tryp('b.ContextMenu <- uimenu', @() setget(b, 'ContextMenu', uimenu(uf)));
tryp('b.ContextMenu <- []', @() setget(b, 'ContextMenu', []));
tryp('b.ContextMenu <- 5', @() setget(b, 'ContextMenu', 5));
tryp('b.ContextMenu <- [cm cm]', @() setget(b, 'ContextMenu', [cm cm]));
tryp('b.UIContextMenu <- cm', @() setget(b, 'UIContextMenu', cm));
tryp('b.ContextMenu after UIContextMenu', @() v2s(b.ContextMenu));
tryp('b.ContextMenu after delete(cm)', @() afterdelete(b, cm));
cm = uicontextmenu(uf); uimenu(cm, 'Text', 'a');
tryp('knob.ContextMenu <- cm', @() setget(uiknob(uf), 'ContextMenu', cm));
tryp('tree.ContextMenu <- cm', @() setget(uitree(uf), 'ContextMenu', cm));
tryp('node.ContextMenu <- cm', @() setget(uitreenode(uitree(uf)), 'ContextMenu', cm));
tryp('node.ContextMenu <- cm of other figure', @() setget(uitreenode(uitree(uf)), 'ContextMenu', cm2));
tryp('lamp.ContextMenu <- cm', @() setget(uilamp(uf), 'ContextMenu', cm));
tryp('grid.ContextMenu <- cm', @() setget(uigridlayout(uf), 'ContextMenu', cm));
tryp('uf.ContextMenu <- cm', @() setget(uf, 'ContextMenu', cm));
tryp('uiaxes.ContextMenu <- cm', @() setget(uiaxes(uf), 'ContextMenu', cm));
tryp('open(cm, 10, 10)', @() run(@() open(cm, 10, 10)));
tryp('open(cm)', @() run(@() open(cm)));
tryp('open(cm, [10 10])', @() run(@() open(cm, [10 10])));
tryp('cm.ContextMenuOpeningFcn', @() v2s(cm.ContextMenuOpeningFcn));
tryp('cm Position', @() v2s(cm.Position));
tryp('cm.ContextMenu <- cm', @() setget(cm, 'ContextMenu', cm));
delete(allchild(uf));

%% rectangles once the view has settled
warm = uiknob(uf); drawnow; pause(2); delete(warm);
rk = {'knob', @() uiknob(uf); 'knob long labels', @() uiknob(uf, 'MajorTickLabels', {'Minimum', 'Maximum', 'Middle', 'x', 'y', 'z'}); ...
    'knob FontSize 20', @() uiknob(uf, 'FontSize', 20); 'knob 120 wide', @() uiknob(uf, 'Position', [100 100 120 120]); ...
    'dknob', @() uiknob(uf, 'discrete'); 'dknob long items', @() uiknob(uf, 'discrete', 'Items', {'Alpha', 'Beta', 'Gamma', 'Delta Epsilon'}); ...
    'dknob 2 items', @() uiknob(uf, 'discrete', 'Items', {'a', 'b'}); 'dknob 8 items', @() uiknob(uf, 'discrete', 'Items', {'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h'}); ...
    'switch', @() uiswitch(uf); 'switch long', @() uiswitch(uf, 'Items', {'Stopped', 'Running'}); 'switch vertical', @() uiswitch(uf, 'Orientation', 'vertical'); ...
    'rocker', @() uiswitch(uf, 'rocker'); 'rocker horizontal', @() uiswitch(uf, 'rocker', 'Orientation', 'horizontal'); 'toggle', @() uiswitch(uf, 'toggle'); ...
    'gauge', @() uigauge(uf); 'linear', @() uigauge(uf, 'linear'); 'linear vertical', @() uigauge(uf, 'linear', 'Orientation', 'vertical'); ...
    'ninety', @() uigauge(uf, 'ninetydegree'); 'semi', @() uigauge(uf, 'semicircular'); 'lamp', @() uilamp(uf); ...
    'datepicker', @() uidatepicker(uf); 'colorpicker', @() uicolorpicker(uf); 'tree', @() uitree(uf); 'checkbox tree', @() uitree(uf, 'checkbox')};
for k = 1:size(rk, 1)
    h = rk{k, 2}();
    drawnow; pause(0.5);
    fprintf('RECT %s : Position=%s Inner=%s Outer=%s\n', rk{k, 1}, mat2str(h.Position), mat2str(h.InnerPosition), mat2str(h.OuterPosition));
    delete(h);
end
delete(allchild(uf));
delete(uf); delete(f);
end

function r = fieldsof(o)
names = properties(o);
parts = cell(1, numel(names));
for k = 1:numel(names)
    parts{k} = sprintf('%s=%s', names{k}, v2s(o.(names{k})));
end
r = sprintf('%s: %s', class(o), strjoin(parts, '; '));
end

function r = setget(h, name, value, raw, other)
if nargin < 4, raw = false; end
if nargin < 5, other = name; end
set(h, name, value);
r = get(h, other);
if ~raw, r = v2s(r); end
end

function r = setget2(h, name, value, a, b)
set(h, name, value);
r = sprintf('%s=%s %s=%s', a, v2s(get(h, a)), b, v2s(get(h, b)));
end

function r = nodetexts(nodes)
if isempty(nodes), r = sprintf('empty %s', mat2str(size(nodes))); return; end
r = sprintf('%s [%s]', mat2str(size(nodes)), strjoin(arrayfun(@(n) n.Text, nodes(:)', 'UniformOutput', false), ','));
end

function r = texts(holder)
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

function r = delsel(tr, victim, selected)
if nargin < 3, selected = victim; end
tr.Multiselect = 'on';
tr.SelectedNodes = selected;
before = nodetexts(tr.SelectedNodes);
delete(victim);
r = sprintf('%s -> %s', before, nodetexts(tr.SelectedNodes));
end

function r = movesel(tr, other)
n = uitreenode(tr, 'Text', 'mover');
tr.SelectedNodes = n;
n.Parent = other;
r = sprintf('%s | other %s', nodetexts(tr.SelectedNodes), nodetexts(other.SelectedNodes));
end

function r = deltree(uf)
t = uitree(uf);
n = uitreenode(t);
delete(t);
r = sprintf('tree valid %d node valid %d', isvalid(t), isvalid(n));
end

function r = newchild(ct, p)
ct.CheckedNodes = p;
n = uitreenode(p, 'Text', 'newborn');
r = nodetexts(ct.CheckedNodes);
delete(n);
end

function r = delchecked(ct, p1, k1, k2, k3)
ct.CheckedNodes = [k2 k3];
before = nodetexts(ct.CheckedNodes);
n = uitreenode(p1, 'Text', 'doomed');
ct.CheckedNodes = [ct.CheckedNodes; n];
mid = nodetexts(ct.CheckedNodes);
delete(n);
r = sprintf('%s -> %s -> %s', before, mid, nodetexts(ct.CheckedNodes));
end

function r = movechecked(ct, other)
n = uitreenode(ct, 'Text', 'mover');
ct.CheckedNodes = n;
n.Parent = other;
r = sprintf('%s | other %s', nodetexts(ct.CheckedNodes), nodetexts(other.CheckedNodes));
end

function r = keepvalue(dk)
dk.ItemsData = [];
dk.Items = {'a', 'b', 'c'};
dk.Value = 'b';
dk.Items = {'c', 'b'};
r = sprintf('%s %d', dk.Value, dk.ValueIndex);
dk.Items = {'x', 'y'};
r = sprintf('%s | %s %d', r, dk.Value, dk.ValueIndex);
end

function r = disabledday(dp)
dp.Value = datetime(2024, 1, 15);
dp.DisabledDaysOfWeek = 2;
r = sprintf('%s value %s', v2s(dp.DisabledDaysOfWeek), v2s(dp.Value));
dp.DisabledDaysOfWeek = [];
end

function r = afterdelete(b, cm)
b.ContextMenu = cm;
delete(cm);
r = v2s(b.ContextMenu);
end

function r = run(fn)
lastwarn('');
fn();
r = 'ok';
[wm, wid] = lastwarn;
if ~isempty(wm), r = sprintf('%s  WARN %s | %s', r, wid, oneline(wm)); end
end
