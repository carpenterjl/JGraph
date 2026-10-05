function u5_forms
% U5 probe: the forms of each maker, and how the value properties play together. Headless.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
f = figure('Visible', 'off');
fns = {'uilabel', 'uibutton', 'uieditfield', 'uitextarea', 'uidropdown', 'uilistbox', 'uicheckbox', 'uislider', 'uispinner', ...
    'uiimage', 'uihyperlink', 'uiradiobutton', 'uitogglebutton', 'uigridlayout'};
ax = axes(f);
for k = 1:numel(fns)
    name = fns{k}; fn = str2func(name);
    before = findall(groot, 'Type', 'figure');
    tryp([name ' ()'], @() noarg(fn));
    delete(setdiff(findall(groot, 'Type', 'figure'), before));
    tryp([name ' (uifigure)'], @() kind(fn(uf)));
    tryp([name ' (figure)'], @() kind(fn(f)));
    tryp([name ' (uipanel)'], @() kind(fn(uipanel(uf))));
    tryp([name ' (classic uipanel)'], @() kind(fn(uipanel(f))));
    tryp([name ' (grid)'], @() kind(fn(uigridlayout(uf))));
    tryp([name ' (uibuttongroup)'], @() kind(fn(uibuttongroup(uf))));
    tryp([name ' (classic uibuttongroup)'], @() kind(fn(uibuttongroup(f))));
    tryp([name ' (axes)'], @() kind(fn(ax)));
    tryp([name ' (label)'], @() kind(fn(uilabel(uf))));
    tryp([name ' (5.5)'], @() kind(fn(5.5)));
    tryp([name ' ([])'], @() kind(fn([])));
    tryp([name ' (Parent, uf)'], @() kind(fn('Parent', uf)));
    tryp([name ' (Parent, [])'], @() kind(fn('Parent', [])));
    tryp([name ' (uf, Bogus, 1)'], @() kind(fn(uf, 'Bogus', 1)));
    tryp([name ' (uf, Tag)'], @() kind(fn(uf, 'Tag')));
    tryp([name ' (uf, struct)'], @() get(fn(uf, struct('Tag', 's')), 'Tag'));
    tryp([name ' (uf, Tag, t, Visible, off)'], @() get(fn(uf, 'Tag', 't', 'Visible', 'off'), 'Visible'));
    tryp([name ' (uf, tag, t) lower case'], @() get(fn(uf, 'tag', 't'), 'Tag'));
    tryp([name ' (uf, Ta, t) prefix'], @() get(fn(uf, 'Ta', 't'), 'Tag'));
    tryp([name ' (uf, "Tag", "t") strings'], @() get(fn(uf, "Tag", "t"), 'Tag'));
    tryp([name ' (uf, Position, [1 2 30 40], Tag, t)'], @() get(fn(uf, 'Position', [1 2 30 40], 'Tag', 't'), 'Position'));
    tryp([name ' Units'], @() get(fn(uf), 'Units'));
    tryp([name ' set Units'], @() set(fn(uf), 'Units', 'pixels'));
    tryp([name ' bogus get'], @() get(fn(uf), 'Bogus'));
    tryp([name ' bogus set'], @() set(fn(uf), 'Bogus', 1));
    tryp([name ' dot bogus get'], @() dotget(fn(uf)));
    tryp([name ' dot bogus set'], @() dotset(fn(uf)));
    tryp([name ' set Type'], @() set(fn(uf), 'Type', 'x'));
    tryp([name ' set BeingDeleted'], @() set(fn(uf), 'BeingDeleted', 'on'));
    tryp([name ' isprop Units, isgraphics, ishghandle, isvalid'], @() flags(fn(uf)));
    tryp([name ' Parent <- figure'], @() reparent(fn(uf), f));
    tryp([name ' Parent <- []'], @() reparent(fn(uf), []));
    tryp([name ' Parent <- axes'], @() reparent(fn(uf), ax));
    tryp([name ' Parent <- panel'], @() reparent(fn(uf), uipanel(uf)));
    tryp([name ' in panel default Position'], @() get(fn(uipanel(uf)), 'Position'));
    tryp([name ' findobj by Type'], @() numel(findobj(uf, 'Type', get(fn(uf), 'Type'))));
    delete(allchild(uf)); delete(allchild(f)); ax = axes(f);
end

%% styles
tryp('uibutton(uf, push)', @() kind(uibutton(uf, 'push')));
tryp('uibutton(uf, state)', @() kind(uibutton(uf, 'state')));
tryp('uibutton(uf, PUSH)', @() kind(uibutton(uf, 'PUSH')));
tryp('uibutton(uf, st)', @() kind(uibutton(uf, 'st')));
tryp('uibutton(uf, "state")', @() kind(uibutton(uf, "state")));
tryp('uibutton(state)', @() kindclose(uibutton('state')));
tryp('uibutton(uf, bogus)', @() kind(uibutton(uf, 'bogus')));
tryp('uibutton(uf, state, Text, x)', @() get(uibutton(uf, 'state', 'Text', 'x'), 'Text'));
tryp('uibutton(uf, Text, x, state)', @() get(uibutton(uf, 'Text', 'x', 'state'), 'Text'));
tryp('uibutton(state, Parent, uf)', @() kind(uibutton('state', 'Parent', uf)));
tryp('uibutton(Parent, uf, state)', @() kind(uibutton('Parent', uf, 'state')));
tryp('uieditfield(uf, text)', @() kind(uieditfield(uf, 'text')));
tryp('uieditfield(uf, numeric)', @() kind(uieditfield(uf, 'numeric')));
tryp('uieditfield(uf, num)', @() kind(uieditfield(uf, 'num')));
tryp('uieditfield(numeric)', @() kindclose(uieditfield('numeric')));
tryp('uieditfield(uf, bogus)', @() kind(uieditfield(uf, 'bogus')));
tryp('uislider(uf, slider)', @() kind(uislider(uf, 'slider')));
tryp('uislider(uf, range)', @() kind(uislider(uf, 'range')));
tryp('uislider(uf, bogus)', @() kind(uislider(uf, 'bogus')));
tryp('uispinner(uf, numeric)', @() kind(uispinner(uf, 'numeric')));
tryp('uilabel(uf, text)', @() kind(uilabel(uf, 'text')));
tryp('uidropdown(uf, bogus)', @() kind(uidropdown(uf, 'bogus')));
delete(allchild(uf));

%% DropDown
d = uidropdown(uf);
show('dd default', d);
d.Items = {'x', 'y', 'z'}; show('dd Items xyz', d);
d.Value = 'y'; show('dd Value y', d);
d.Items = {'y', 'z', 'w'}; show('dd Items yzw (y kept?)', d);
d.Items = {'a', 'b'}; show('dd Items ab (y gone)', d);
d.ValueIndex = 2; show('dd ValueIndex 2', d);
tryp('dd ValueIndex 3', @() setget(d, 'ValueIndex', 3));
tryp('dd ValueIndex 0', @() setget(d, 'ValueIndex', 0));
tryp('dd ValueIndex []', @() setget(d, 'ValueIndex', []));
show('dd after ValueIndex []', d);
tryp('dd ValueIndex 1.5', @() setget(d, 'ValueIndex', 1.5));
tryp('dd ValueIndex [1 2]', @() setget(d, 'ValueIndex', [1 2]));
tryp('dd Value "a"', @() setget(d, 'Value', "a"));
tryp('dd Value A (case)', @() setget(d, 'Value', 'A'));
tryp('dd Value {a}', @() setget(d, 'Value', {'a'}));
tryp('dd Value 1', @() setget(d, 'Value', 1));
tryp('dd Value []', @() setget(d, 'Value', []));
show('dd after Value []', d);
tryp('dd Value empty char', @() setget(d, 'Value', ''));
show('dd after Value empty char', d);
d.Items = {'a', 'b', 'c'}; d.ItemsData = [10 20 30]; show('dd ItemsData 10 20 30', d);
tryp('dd Value 20', @() setget(d, 'Value', 20));
show('dd after Value 20', d);
tryp('dd Value b with ItemsData', @() setget(d, 'Value', 'b'));
tryp('dd Value int8(30)', @() setget(d, 'Value', int8(30)));
show('dd after int8(30)', d);
d.ItemsData = {'p', 'q', 'r'}; show('dd ItemsData cell pqr', d);
tryp('dd Value q', @() setget(d, 'Value', 'q'));
d.ItemsData = [1 2]; show('dd ItemsData shorter', d);
tryp('dd Value c with short ItemsData', @() setget(d, 'Value', 'c'));
d.ValueIndex = 3; show('dd ValueIndex 3 past ItemsData', d);
d.ItemsData = [1 2 3 4 5]; show('dd ItemsData longer', d);
tryp('dd Value 5 (data past items)', @() setget(d, 'Value', 5));
d.ItemsData = {1, 'two', [3 3]}; show('dd ItemsData mixed cell', d);
tryp('dd Value [3 3]', @() setget(d, 'Value', [3 3]));
show('dd after [3 3]', d);
d.ItemsData = ["s1" "s2" "s3"]; show('dd ItemsData string', d);
d.ItemsData = []; show('dd ItemsData cleared', d);
d.Items = {'a', 'a', 'b'}; d.ValueIndex = 2; show('dd duplicate items index 2', d);
d.Items = {}; show('dd Items {}', d);
tryp('dd Items {} Value x', @() setget(d, 'Value', 'x'));
d.Items = "one"; show('dd Items string scalar', d);
tryp('dd Items char abc', @() setget(d, 'Items', 'abc'));
tryp('dd Items numbers', @() setget(d, 'Items', [1 2 3]));
tryp('dd Items cell numbers', @() setget(d, 'Items', {1, 2}));
tryp('dd Items column cell', @() setget(d, 'Items', {'a'; 'b'}));
tryp('dd Items 2x2 cell', @() setget(d, 'Items', {'a', 'b'; 'c', 'd'}));
tryp('dd Items categorical', @() setget(d, 'Items', categorical({'u', 'v'})));
tryp('dd Items missing', @() setget(d, 'Items', ["a" missing]));
delete(d);
d = uidropdown(uf, 'Editable', 'on', 'Items', {'a', 'b'});
show('dd editable default', d);
d.Value = 'free'; show('dd editable free text', d);
tryp('dd editable Value 5', @() setget(d, 'Value', 5));
d.Value = 'b'; show('dd editable back to b', d);
d.Value = 'free'; d.Editable = 'off'; show('dd editable off with free text', d);
tryp('dd editable ItemsData', @() setget(uidropdown(uf, 'Editable', 'on'), 'ItemsData', [1 2 3 4]));
tryp('dd ItemsData then editable', @() setget(uidropdown(uf, 'ItemsData', [1 2 3 4]), 'Editable', 'on'));
tryp('dd ValueIndex 3 with ItemsData [1 2]', @() setget(uidropdown(uf, 'Items', {'a', 'b', 'c'}, 'ItemsData', [1 2]), 'ValueIndex', 3));
tryp('dd ctor Value before Items', @() get(uidropdown(uf, 'Value', 'q', 'Items', {'p', 'q'}), 'Value'));
tryp('dd ctor Value not in Items', @() get(uidropdown(uf, 'Value', 'zz'), 'Value'));
tryp('dd ctor ItemsData Value', @() get(uidropdown(uf, 'Items', {'p', 'q'}, 'ItemsData', [7 8], 'Value', 8), 'ValueIndex'));
delete(allchild(uf));

%% ListBox
l = uilistbox(uf);
show('lb default', l);
l.Value = 'Item 3'; show('lb Value Item 3', l);
tryp('lb Value {}', @() setget(l, 'Value', {}));
show('lb after {}', l);
tryp('lb Value []', @() setget(l, 'Value', []));
tryp('lb Value {Item 1}', @() setget(l, 'Value', {'Item 1'}));
tryp('lb Value {Item 1, Item 2} single', @() setget(l, 'Value', {'Item 1', 'Item 2'}));
tryp('lb Value bogus', @() setget(l, 'Value', 'bogus'));
tryp('lb ValueIndex [1 2] single', @() setget(l, 'ValueIndex', [1 2]));
tryp('lb ValueIndex 9', @() setget(l, 'ValueIndex', 9));
l.Multiselect = 'on'; show('lb multiselect on', l);
l.Value = {'Item 3', 'Item 1'}; show('lb multi Value 3,1', l);
l.Value = 'Item 2'; show('lb multi Value char', l);
l.ValueIndex = [4 2]; show('lb multi ValueIndex [4 2]', l);
tryp('lb multi ValueIndex [2 2]', @() setget(l, 'ValueIndex', [2 2]));
tryp('lb multi Value dup', @() setget(l, 'Value', {'Item 1', 'Item 1'}));
tryp('lb multi Value {}', @() setget(l, 'Value', {}));
show('lb multi after {}', l);
tryp('lb multi Value ["Item 1";"Item 4"]', @() setget(l, 'Value', ["Item 1"; "Item 4"]));
show('lb multi after strings', l);
l.Multiselect = 'off'; show('lb multiselect back off with two', l);
l.Items = {'Item 4', 'new'}; show('lb Items changed', l);
l.Multiselect = 'on'; l.Value = {'Item 4', 'new'}; l.ItemsData = [5 6]; show('lb multi ItemsData numeric', l);
tryp('lb multi Value [6 5]', @() setget(l, 'Value', [6 5]));
tryp('lb multi Value {5}', @() setget(l, 'Value', {5}));
tryp('lb multi Value 7', @() setget(l, 'Value', 7));
l.ItemsData = {'p', [1 2]}; show('lb multi ItemsData cell', l);
tryp('lb multi Value {[1 2]}', @() setget(l, 'Value', {[1 2]}));
show('lb after', l);
l.Items = {}; show('lb Items {}', l);
delete(l);
l = uilistbox(uf, 'Items', {'a', 'b', 'c'}, 'ItemsData', [1 2 3], 'Value', 2); show('lb ctor ItemsData Value', l);
tryp('lb ctor multi Value', @() get(uilistbox(uf, 'Multiselect', 'on', 'Value', {'Item 2', 'Item 4'}), 'ValueIndex'));
tryp('lb ctor Value then multi', @() get(uilistbox(uf, 'Value', {'Item 2', 'Item 4'}, 'Multiselect', 'on'), 'ValueIndex'));
delete(allchild(uf));

%% EditField (text)
e = uieditfield(uf);
vals = {'abc', "str", '', "", string(missing), 5, {'a'}, ['ab'; 'cd'], sprintf('a\nb'), 'tab	in', true, [], categorical({'x'})};
for k = 1:numel(vals), tryp(sprintf('ef Value <- %s', v2s(vals{k})), @() setget(e, 'Value', vals{k})); end
tryp('ef CharacterLimits [2 4] with abc', @() climit(uf, 'abc', [2 4]));
tryp('ef CharacterLimits [2 4] with a', @() climit(uf, 'a', [2 4]));
tryp('ef CharacterLimits [2 4] with empty', @() climit(uf, '', [2 4]));
tryp('ef CharacterLimits [2 4] with abcdef', @() climit(uf, 'abcdef', [2 4]));
e = uieditfield(uf, 'CharacterLimits', [2 4], 'Value', 'abc');
tryp('ef limited Value a', @() setget(e, 'Value', 'a'));
tryp('ef limited Value abcde', @() setget(e, 'Value', 'abcde'));
tryp('ef limited Value empty', @() setget(e, 'Value', ''));
cl = {[0 0], [3 3], [5 1], [-1 3], [0 Inf], [1.5 3], [Inf Inf], 5, [1 2 3], 'a', [NaN 3]};
for k = 1:numel(cl), tryp(sprintf('ef CharacterLimits <- %s', v2s(cl{k})), @() setget(uieditfield(uf), 'CharacterLimits', cl{k})); end
it = {'text', 'digits', 'letters', 'alphanumerics', 'bogus', 'DIGITS', 'dig', 5};
for k = 1:numel(it), tryp(sprintf('ef InputType <- %s', v2s(it{k})), @() setget(uieditfield(uf), 'InputType', it{k})); end
tryp('ef digits with abc', @() setget(uieditfield(uf, 'Value', 'abc'), 'InputType', 'digits'));
e = uieditfield(uf, 'InputType', 'digits');
tryp('ef digits Value 123', @() setget(e, 'Value', '123'));
tryp('ef digits Value 12a', @() setget(e, 'Value', '12a'));
tryp('ef digits Value 1.5', @() setget(e, 'Value', '1.5'));
tryp('ef digits Value -1', @() setget(e, 'Value', '-1'));
e = uieditfield(uf, 'InputType', 'letters');
tryp('ef letters Value abc', @() setget(e, 'Value', 'abc'));
tryp('ef letters Value ab c', @() setget(e, 'Value', 'ab c'));
tryp('ef letters Value ab1', @() setget(e, 'Value', 'ab1'));
e = uieditfield(uf, 'InputType', 'alphanumerics');
tryp('ef alnum Value ab1', @() setget(e, 'Value', 'ab1'));
tryp('ef alnum Value ab_1', @() setget(e, 'Value', 'ab_1'));
delete(allchild(uf));

%% NumericEditField and Spinner
for kind2 = {'numeric', 'spinner'}
    mk = @() uieditfield(uf, 'numeric'); tag = 'nf';
    if strcmp(kind2{1}, 'spinner'), mk = @() uispinner(uf); tag = 'sp'; end
    vals = {5, 2.5, -3, int8(3), single(2.5), true, '5', "5", [1 2], [], NaN, Inf, -Inf, 1 + 2i, {5}, uint8(200)};
    for k = 1:numel(vals), tryp(sprintf('%s Value <- %s', tag, v2s(vals{k})), @() cv(setget(mk(), 'Value', vals{k}))); end
    n = mk(); n.Limits = [0 10];
    for v = {0, 10, -1, 11, 5.5}
        tryp(sprintf('%s [0 10] Value <- %g', tag, v{1}), @() setget(n, 'Value', v{1}));
    end
    n.LowerLimitInclusive = 'off'; n.UpperLimitInclusive = 'off'; n.Value = 5;
    for v = {0, 10, 0.001}
        tryp(sprintf('%s (0 10) Value <- %g', tag, v{1}), @() setget(n, 'Value', v{1}));
    end
    n = mk(); n.Value = 50;
    tryp(sprintf('%s Value 50 then Limits [0 10]', tag), @() setget(n, 'Limits', [0 10]));
    fprintf('   Value now %s\n', v2s(n.Value));
    n = mk(); n.Value = -50;
    tryp(sprintf('%s Value -50 then Limits [0 10]', tag), @() setget(n, 'Limits', [0 10]));
    fprintf('   Value now %s\n', v2s(n.Value));
    n = mk(); n.Limits = [0 10]; n.Value = 10;
    tryp(sprintf('%s Value at upper then UpperLimitInclusive off', tag), @() setget(n, 'UpperLimitInclusive', 'off'));
    fprintf('   Value now %s\n', v2s(n.Value));
    n = mk(); n.Limits = [0 10]; n.Value = 0;
    tryp(sprintf('%s Value at lower then LowerLimitInclusive off', tag), @() setget(n, 'LowerLimitInclusive', 'off'));
    fprintf('   Value now %s\n', v2s(n.Value));
    lims = {[0 10], [10 0], [5 5], [-Inf Inf], [0 Inf], [-Inf 0], [Inf Inf], [NaN 1], 5, [1 2 3], 'a', [1; 2], int8([1 5]), [], {1, 2}, [0 1e400]};
    for k = 1:numel(lims), tryp(sprintf('%s Limits <- %s', tag, v2s(lims{k})), @() setget(mk(), 'Limits', lims{k})); end
    n = mk(); n.Limits = [5 5];
    n = mk(); n.Value = 2.6; n.RoundFractionalValues = 'on';
    fprintf('%s 2.6 then Round on: %s\n', tag, v2s(n.Value));
    for v = {2.5, 3.5, -2.5, 2.4999, 1e10 + 0.5}
        tryp(sprintf('%s rounding Value <- %.10g', tag, v{1}), @() setget(n, 'Value', v{1}));
    end
    n = mk(); n.Limits = [0 2.5]; n.RoundFractionalValues = 'on';
    tryp(sprintf('%s rounding [0 2.5] Value <- 2.5', tag), @() setget(n, 'Value', 2.5));
    tryp(sprintf('%s rounding [0 2.5] Value <- 2.2', tag), @() setget(n, 'Value', 2.2));
    n = mk(); n.Value = 2.6; n.Limits = [0 2.9];
    tryp(sprintf('%s 2.6 in [0 2.9] then Round on', tag), @() setget(n, 'RoundFractionalValues', 'on'));
    fprintf('   Value now %s\n', v2s(n.Value));
    n = mk();
    tryp(sprintf('%s Value [] AllowEmpty off', tag), @() setget(n, 'Value', []));
    n.AllowEmpty = 'on';
    tryp(sprintf('%s Value [] AllowEmpty on', tag), @() setget(n, 'Value', []));
    tryp(sprintf('%s AllowEmpty off while empty', tag), @() setget(n, 'AllowEmpty', 'off'));
    fprintf('   Value now %s\n', v2s(n.Value));
    tryp(sprintf('%s Value zeros(0,1) AllowEmpty on', tag), @() setget(setget2(mk(), 'AllowEmpty', 'on'), 'Value', zeros(0, 1)));
    fm = {'%d', '%.2f', '%11.4g', '%s', 'abc', '%d units', '%5.1f%%', '', 5, '%d %d', '%x', '%e', '%i', '%c', '$%.2f', "%.3f", '%bx', '%*d', '%u', '%o', '%G', '%+d', '%ld'};
    for k = 1:numel(fm), tryp(sprintf('%s ValueDisplayFormat <- %s', tag, v2s(fm{k})), @() setget(mk(), 'ValueDisplayFormat', fm{k})); end
end
st = {1, 0.5, 0, -1, [1 2], 'a', Inf, NaN, int8(2), [], 1e-3, true};
for k = 1:numel(st), tryp(sprintf('sp Step <- %s', v2s(st{k})), @() setget(uispinner(uf), 'Step', st{k})); end
s = uispinner(uf, 'RoundFractionalValues', 'on');
tryp('sp rounding Step <- 0.5', @() setget(s, 'Step', 0.5));
s = uispinner(uf, 'Step', 0.5);
tryp('sp Step 0.5 then Round on', @() setget(s, 'RoundFractionalValues', 'on'));
fprintf('   Step now %s\n', v2s(s.Step));
delete(allchild(uf));

%% Slider and RangeSlider
s = uislider(uf);
show2('sl default', s);
for v = {50, 0, 100, -1, 101, [1 2], 'a', NaN, int8(5), true, [], 33.333}
    tryp(sprintf('sl Value <- %s', v2s(v{1})), @() cv(setget(s, 'Value', v{1})));
end
s = uislider(uf); s.Value = 80; s.Limits = [0 50]; show2('sl 80 then Limits [0 50]', s);
s = uislider(uf); s.Value = 20; s.Limits = [30 50]; show2('sl 20 then Limits [30 50]', s);
s = uislider(uf); s.Limits = [0 1]; show2('sl Limits [0 1]', s);
s = uislider(uf); s.Limits = [-1000 1000]; show2('sl Limits [-1000 1000]', s);
s = uislider(uf); s.Limits = [0 7]; show2('sl Limits [0 7]', s);
s = uislider(uf); s.Limits = [0.001 0.002]; show2('sl Limits [0.001 0.002]', s);
s = uislider(uf); s.Limits = [0.01 0.99]; show2('sl Limits [0.01 0.99]', s);
s = uislider(uf); s.Limits = [1 12]; show2('sl Limits [1 12]', s);
s = uislider(uf); s.Limits = [0 1e6]; show2('sl Limits [0 1e6]', s);
s = uislider(uf); s.Limits = [-5 5]; show2('sl Limits [-5 5]', s);
s = uislider(uf); s.Limits = [0 255]; show2('sl Limits [0 255]', s);
s = uislider(uf); s.Limits = [20 20000]; show2('sl Limits [20 20000]', s);
lims = {[10 0], [5 5], [-Inf Inf], [0 Inf], [NaN 1], 5, [1 2 3], 'a', int8([1 5]), []};
for k = 1:numel(lims), tryp(sprintf('sl Limits <- %s', v2s(lims{k})), @() setget(uislider(uf), 'Limits', lims{k})); end
s = uislider(uf); s.MajorTicks = [0 50 100]; show2('sl MajorTicks [0 50 100]', s);
s.Limits = [0 200]; show2('   then Limits [0 200]', s);
s.MajorTicksMode = 'auto'; show2('   then MajorTicksMode auto', s);
s = uislider(uf); s.MajorTickLabels = {'lo', 'hi'}; show2('sl MajorTickLabels {lo,hi}', s);
s = uislider(uf); s.MinorTicks = 0:5:100; show2('sl MinorTicks 0:5:100', s);
s = uislider(uf); s.MajorTicks = []; show2('sl MajorTicks []', s);
s = uislider(uf); s.MajorTicks = [200 300]; show2('sl MajorTicks outside', s);
mt = {[3 1 2], [1 1 2], 'a', {1, 2}, NaN, [1 Inf], [1; 2], int8([1 2])};
for k = 1:numel(mt), tryp(sprintf('sl MajorTicks <- %s', v2s(mt{k})), @() setget(uislider(uf), 'MajorTicks', mt{k})); end
for k = 1:numel(mt), tryp(sprintf('sl MinorTicks <- %s', v2s(mt{k})), @() setget(uislider(uf), 'MinorTicks', mt{k})); end
ml = {{'a', 'b'}, ["a" "b"], 'ab', [1 2], {1, 2}, {}, {'a'; 'b'}, "x"};
for k = 1:numel(ml), tryp(sprintf('sl MajorTickLabels <- %s', v2s(ml{k})), @() setget(uislider(uf), 'MajorTickLabels', ml{k})); end
s = uislider(uf); s.Step = 5; show2('sl Step 5', s);
tryp('sl Step 5 Value 12', @() setget(s, 'Value', 12));
tryp('sl Step 5 Value 13', @() setget(s, 'Value', 13));
s.StepMode = 'auto'; show2('sl StepMode back auto', s);
stv = {0, -1, 200, [1 2], 'a', NaN, Inf, 0.001};
for k = 1:numel(stv), tryp(sprintf('sl Step <- %s', v2s(stv{k})), @() setget(uislider(uf), 'Step', stv{k})); end
s = uislider(uf); p0 = s.Position; s.Orientation = 'vertical'; show2('sl vertical', s); fprintf('   Position %s -> %s Outer %s\n', mat2str(p0), mat2str(s.Position), mat2str(s.OuterPosition));
tryp('sl ctor vertical Position', @() get(uislider(uf, 'Orientation', 'vertical'), 'Position'));
tryp('sl Position [10 10 300 3]', @() setget(uislider(uf), 'Position', [10 10 300 3]));
tryp('sl Position [10 10 300 40]', @() setget(uislider(uf), 'Position', [10 10 300 40]));
tryp('sl OuterPosition after Position [10 10 300 3]', @() get(setget2(uislider(uf), 'Position', [10 10 300 3]), 'OuterPosition'));
tryp('sl OuterPosition <-', @() setget(uislider(uf), 'OuterPosition', [10 10 300 60]));
tryp('sl InnerPosition after OuterPosition <-', @() get(setget2(uislider(uf), 'OuterPosition', [10 10 300 60]), 'InnerPosition'));
tryp('sl OuterPosition with no ticks', @() get(setget2(uislider(uf), 'MajorTicks', []), 'OuterPosition'));
tryp('sl OuterPosition with no labels', @() get(setget2(uislider(uf), 'MajorTickLabels', {}), 'OuterPosition'));
for lim = {[0 1], [-1000 1000], [0 7], [0.001 0.002], [0.01 0.99], [1 12], [0 1e6], [-5 5], [0 255], [20 20000], [0 50], [30 50], [0 3], [0 10], [0 15], [0 25], [0 40], [0 60], [0 150], [0 300], [0 500], [0 1000], [-1 1], [5 6], [0 0.5], [100 200], [-100 0], [0 12345], [0.5 2.5]}
    s = uislider(uf); s.Limits = lim{1}; t0 = tic; while toc(t0) < 6 && isequal(s.MajorTicks, [0 20 40 60 80 100]), drawnow; pause(0.2); end; pause(0.4); drawnow;
    show2(sprintf('sl settled Limits %s', mat2str(lim{1})), s); delete(s);
end
for w = [50 100 150 300 600]
    s = uislider(uf, 'Position', [10 10 w 3]); t0 = tic; while toc(t0) < 2, drawnow; pause(0.2); end
    show2(sprintf('sl settled width %d', w), s); delete(s);
end
s = uislider(uf, 'Orientation', 'vertical'); t0 = tic; while toc(t0) < 2, drawnow; pause(0.2); end
show2('sl settled vertical', s); delete(s);
r = uislider(uf, 'range');
show2('rs default', r);
for v = {[10 20], [20 10], [10 10], 50, [0 100], [-1 50], [50 101], [1 2 3], 'a', [NaN 5], [10; 20], int8([10 20]), []}
    tryp(sprintf('rs Value <- %s', v2s(v{1})), @() cv(setget(r, 'Value', v{1})));
end
r = uislider(uf, 'range'); r.Value = [60 80]; r.Limits = [0 50]; show2('rs [60 80] then Limits [0 50]', r);
r = uislider(uf, 'range'); r.Value = [10 80]; r.Limits = [20 50]; show2('rs [10 80] then Limits [20 50]', r);
delete(allchild(uf));

%% TextArea, CheckBox, StateButton, Label, Button text
t = uitextarea(uf);
vals = {'one', "str", {'a', 'b'}, {'a'; 'b'}, ["p" "q"], ["p"; "q"], sprintf('x\ny'), ['ab'; 'cd'], '', {}, {''}, 5, {1}, [], string(missing), categorical({'u'; 'v'}), {'a', 'b'; 'c', 'd'}, {sprintf('m\nn'), 'o'}};
for k = 1:numel(vals), tryp(sprintf('ta Value <- %s', v2s(vals{k})), @() setget(t, 'Value', vals{k})); end
for cls = {'Label', 'Button', 'CheckBox', 'Hyperlink'}
    mk = str2func(['ui' lower(cls{1})]);
    for k = 1:numel(vals), tryp(sprintf('%s Text <- %s', cls{1}, v2s(vals{k})), @() setget(mk(uf), 'Text', vals{k})); end
end
c = uicheckbox(uf);
for v = {true, false, 1, 0, 2, 'on', "on", 'off', [], [1 0], int8(1), 0.5, NaN, {true}}
    tryp(sprintf('cb Value <- %s', v2s(v{1})), @() cv(setget(c, 'Value', v{1})));
end
sb = uibutton(uf, 'state');
for v = {true, 1, 2, 'on', [], [1 0]}
    tryp(sprintf('stb Value <- %s', v2s(v{1})), @() cv(setget(sb, 'Value', v{1})));
end
delete(allchild(uf));

%% Icon, ImageSource
b = uibutton(uf);
ic = {'', 'nosuchfile.png', fullfile(matlabroot, 'toolbox', 'matlab', 'icons', 'greenarrowicon.gif'), 5, rand(4, 4, 3), uint8(255 * rand(4, 4, 3)), rand(4, 4), 'info', 'success', 'error', 'warning', 'question', 'none', "", rand(4, 4, 4), true(3, 3, 3)};
for k = 1:numel(ic)
    tryp(sprintf('Button Icon <- %s', short(ic{k})), @() short(setget(b, 'Icon', ic{k})));
end
im = uiimage(uf);
for k = 1:numel(ic)
    tryp(sprintf('Image ImageSource <- %s', short(ic{k})), @() short(setget(im, 'ImageSource', ic{k})));
end
delete(allchild(uf));

%% the button group of a uifigure
bg = uibuttongroup(uf);
fprintf('ubg empty: SelectedObject %s Buttons %s\n', v2s(bg.SelectedObject), v2s(bg.Buttons));
r1 = uiradiobutton(bg, 'Text', 'r1'); fprintf('first radio: %s | pos %s\n', st3(bg), mat2str(r1.Position));
r2 = uiradiobutton(bg, 'Text', 'r2'); fprintf('second radio: %s | pos %s\n', st3(bg), mat2str(r2.Position));
r3 = uiradiobutton(bg, 'Text', 'r3', 'Value', true); fprintf('third radio made true: %s\n', st3(bg));
r2.Value = true; fprintf('r2 true: %s\n', st3(bg));
tryp('r2 false (selected)', @() setget(r2, 'Value', false)); fprintf('   %s\n', st3(bg));
tryp('r1 false (unselected)', @() setget(r1, 'Value', false)); fprintf('   %s\n', st3(bg));
tryp('r1 Value 1', @() cv(setget(r1, 'Value', 1))); fprintf('   %s\n', st3(bg));
tryp('r1 Value 2', @() setget(r1, 'Value', 2));
tryp('r1 Value on', @() setget(r1, 'Value', 'on'));
bg.SelectedObject = r3; fprintf('SelectedObject r3: %s\n', st3(bg));
tryp('SelectedObject []', @() setget(bg, 'SelectedObject', [])); fprintf('   %s\n', st3(bg));
tryp('SelectedObject figure', @() setget(bg, 'SelectedObject', uf));
tryp('SelectedObject outside radio', @() setget(bg, 'SelectedObject', uiradiobutton(uibuttongroup(uf))));
tryp('SelectedObject [r1 r2]', @() setget(bg, 'SelectedObject', [r1 r2]));
delete(r3); fprintf('deleted the selected r3: %s\n', st3(bg));
r1.Value = true; delete(r1); fprintf('deleted the selected r1: %s\n', st3(bg));
delete(r2); fprintf('deleted the last: %s\n', v2s(bg.SelectedObject));
r4 = uiradiobutton(bg, 'Text', 'r4', 'Value', false); fprintf('first radio made false: %s\n', st3(bg));
tryp('toggle added to radios', @() kind(uitogglebutton(bg, 'Text', 't1'))); fprintf('   %s\n', st3(bg));
tryp('uicontrol radio added to ui radios', @() kind(uicontrol(bg, 'Style', 'radiobutton', 'String', 'c1'))); fprintf('   %s\n', st3(bg));
tryp('button added to ui radios', @() kind(uibutton(bg))); tryp('panel added to ui radios', @() class(uipanel(bg)));
fprintf('Buttons: %s | Children: %s\n', names(bg.Buttons), names(bg.Children));
delete(bg);
bg = uibuttongroup(uf);
t1 = uitogglebutton(bg, 'Text', 't1'); fprintf('first toggle: %s | pos %s\n', st3(bg), mat2str(t1.Position));
t2 = uitogglebutton(bg, 'Text', 't2'); fprintf('second toggle: %s | pos %s\n', st3(bg), mat2str(t2.Position));
t2.Value = true; fprintf('t2 true: %s\n', st3(bg));
tryp('t2 false (selected)', @() setget(t2, 'Value', false)); fprintf('   %s\n', st3(bg));
bg2 = uibuttongroup(uf); q = uiradiobutton(bg2, 'Text', 'q');
tryp('move selected radio to another group', @() reparent(q, bg)); fprintf('   bg %s | bg2 %s\n', st3(bg), v2s(bg2.SelectedObject));
tryp('radio Parent <- uifigure', @() reparent(uiradiobutton(bg2), uf));
tryp('radio Parent <- panel', @() reparent(uiradiobutton(bg2), uipanel(uf)));
tryp('radio in classic group', @() kind(uiradiobutton(uibuttongroup(f))));
tryp('uibuttongroup SelectionChangedFcn', @() setget(bg, 'SelectionChangedFcn', @(s, e) disp(1)));
tryp('radio ValueChangedFcn', @() get(uiradiobutton(bg), 'ValueChangedFcn'));
delete(allchild(uf));

%% children, stacking, deletion
p = uipanel(uf); b1 = uibutton(p, 'Tag', 'b1'); l1 = uilabel(p, 'Tag', 'l1'); c1 = uicontrol(p, 'Tag', 'c1'); a1 = uiaxes(p, 'Tag', 'a1');
fprintf('panel children: %s\n', strjoin(arrayfun(@(h) h.Tag, p.Children', 'UniformOutput', false), ' '));
fprintf('findall types: %s\n', strjoin(unique(arrayfun(@(h) h.Type, findall(p)', 'UniformOutput', false)), ' '));
fprintf('ancestor(b1, figure) is uf: %d | ancestor(b1, uipanel) is p: %d\n', ancestor(b1, 'figure') == uf, ancestor(b1, 'uipanel') == p);
tryp('uistack label', @() uistack(l1, 'bottom'));
fprintf('after uistack: %s\n', strjoin(arrayfun(@(h) h.Tag, p.Children', 'UniformOutput', false), ' '));
tryp('copyobj button', @() kind(copyobj(b1, p)));
tryp('gcbo outside', @() v2s(gcbo));
delete(uf); delete(f);
end

function s = noarg(fn)
h = fn();
fig = ancestor(h, 'figure');
s = sprintf('%s in %s | figure Visible %s Position %s | own Position %s', class(h), class(h.Parent), char(fig.Visible), mat2str(fig.Position(3:4)), mat2str(h.Position));
end

function s = kind(h)
s = sprintf('%s type %s parent %s Position %s', class(h), h.Type, class(h.Parent), mat2str(h.Position));
end

function s = kindclose(h)
s = kind(h);
delete(ancestor(h, 'figure'));
end

function v = dotget(h)
v = h.Bogus;
end

function v = dotset(h)
h.Bogus = 1; v = 1;
end

function s = flags(h)
s = sprintf('isprop Units %d isgraphics %d ishghandle %d isvalid %d ishandle %d isa Component %d', isprop(h, 'Units'), isgraphics(h), ishghandle(h), isvalid(h), ishandle(h), isa(h, 'matlab.ui.control.internal.model.ComponentModel'));
end

function s = reparent(h, p)
h.Parent = p;
s = sprintf('parent now %s Position %s', class(h.Parent), mat2str(h.Position));
end

function v = setget(h, name, value)
set(h, name, value);
v = get(h, name);
end

function h = setget2(h, name, value)
set(h, name, value);
end

function s = cv(v)
s = sprintf('%s (%s)', v2s(v), class(v));
end

function show(label, d)
fprintf('%s: Value=%s ValueIndex=%s Items=%s ItemsData=%s\n', label, v2s(d.Value), v2s(d.ValueIndex), v2s(d.Items), v2s(d.ItemsData));
end

function show2(label, s)
fprintf('%s: Value=%s Limits=%s MajorTicks=%s (%s) MinorTicks=%s (%s) Labels=%s (%s) Step=%s (%s)\n', label, v2s(s.Value), v2s(s.Limits), ...
    v2s(s.MajorTicks), s.MajorTicksMode, v2s(s.MinorTicks), s.MinorTicksMode, v2s(s.MajorTickLabels), s.MajorTickLabelsMode, v2s(s.Step), s.StepMode);
end

function v = climit(uf, text, lim)
e = uieditfield(uf, 'Value', text);
e.CharacterLimits = lim;
v = sprintf('limits %s value %s', mat2str(e.CharacterLimits), v2s(e.Value));
end

function s = short(v)
if isnumeric(v) || islogical(v)
    s = sprintf('%s %s', class(v), mat2str(size(v)));
elseif ischar(v) && numel(v) > 40
    [~, n, x] = fileparts(v); s = ['<path>' n x];
else
    s = v2s(v);
end
end

function s = st3(bg)
k = flipud(bg.Children);
parts = cell(1, numel(k));
for i = 1:numel(k)
    try, t = k(i).Text; catch, t = k(i).String; end %#ok<NOCOM>
    parts{i} = sprintf('%s=%s', t, mat2str(k(i).Value));
end
sel = bg.SelectedObject;
if isempty(sel), w = '(none)'; else, try, w = sel.Text; catch, w = sel.String; end, end %#ok<NOCOM>
s = sprintf('%s sel=%s', strjoin(parts, ' '), w);
end

function s = names(k)
parts = cell(1, numel(k));
for i = 1:numel(k)
    try, parts{i} = k(i).Text; catch, parts{i} = k(i).String; end %#ok<NOCOM>
end
s = strjoin(parts, ' ');
end
