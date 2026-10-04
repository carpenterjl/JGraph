function u3_styles
% U3 probe: the ten uicontrol styles - defaults, what Value/Min/Max/SliderStep/ListboxTop/CData
% take and refuse, the hidden aliases, the property lists. Headless: run-probe.ps1 -Name u3_styles.
styles = {'pushbutton', 'togglebutton', 'radiobutton', 'checkbox', 'edit', 'text', 'slider', 'frame', 'listbox', 'popupmenu'};
f = figure('Visible', 'off', 'Position', [100 100 560 420]);

%% the property lists
c = uicontrol(f);
names = fieldnames(get(c));
fprintf('get names (%d): %s\n', numel(names), strjoin(names', ' '));
snames = fieldnames(set(c));
fprintf('set names (%d): %s\n', numel(snames), strjoin(snames', ' '));
hidden = {'TooltipString', 'UIContextMenu', 'Selected', 'SelectionHighlight', 'HitTest', 'TooltipStr', 'Tooltips', ...
    'ApplicationData', 'Serializable', 'Copyable', 'PickableParts', 'Clipping', 'FontColor', 'Text', 'Layout', 'ResizeFcn', 'SizeChangedFcn'};
for k = 1:numel(hidden)
    tryp(['get hidden ' hidden{k}], @() get(c, hidden{k}));
end
tryp('isprop TooltipString', @() isprop(c, 'TooltipString'));
tryp('isprop UIContextMenu', @() isprop(c, 'UIContextMenu'));
tryp('isprop Selected', @() isprop(c, 'Selected'));
tryp('set Selected on', @() setget(c, 'Selected', 'on'));
tryp('set SelectionHighlight off', @() setget(c, 'SelectionHighlight', 'off'));
tryp('set HitTest off', @() setget(c, 'HitTest', 'off'));
tryp('set Children', @() setget(c, 'Children', []));
tryp('set Extent', @() setget(c, 'Extent', [0 0 1 1]));
tryp('set Type', @() setget(c, 'Type', 'x'));
tryp('set BeingDeleted', @() setget(c, 'BeingDeleted', 'on'));
delete(c);

%% defaults per style
for s = 1:numel(styles)
    c = uicontrol(f, 'Style', styles{s});
    v = get(c);
    fprintf('default %s: Value=%s Min=%s Max=%s SliderStep=%s ListboxTop=%s HorizontalAlignment=%s BackgroundColor=%s ForegroundColor=%s Position=%s Extent=%s String=%s Enable=%s CData=%s\n', ...
        styles{s}, v2s(v.Value), v2s(v.Min), v2s(v.Max), v2s(v.SliderStep), v2s(v.ListboxTop), v.HorizontalAlignment, ...
        mat2str(v.BackgroundColor, 6), mat2str(v.ForegroundColor, 6), mat2str(v.Position), mat2str(v.Extent, 8), v2s(v.String), v.Enable, v2s(v.CData));
    delete(c);
end

%% Value per style
values = {0, 1, 0.5, 5, -1, [1 2], [2;3], [], true, int8(1), single(0.25), NaN, Inf, 'a', {1}, "1", 1+2i};
for s = 1:numel(styles)
    c = uicontrol(f, 'Style', styles{s}, 'String', {'one', 'two', 'three'});
    for k = 1:numel(values)
        lastwarn('');
        tryp(sprintf('%s Value<-%s', styles{s}, v2s(values{k})), @() setget(c, 'Value', values{k}));
        drawnow;
        if ~isempty(lastwarn), [m, id] = lastwarn; fprintf('   warn %s | %s\n', id, oneline(m)); end
    end
    delete(c);
end

%% Value after a change of style, and of the string
c = uicontrol(f, 'Style', 'listbox', 'String', {'a', 'b', 'c'});
fprintf('listbox Value unset: %s\n', v2s(c.Value));
c.Style = 'checkbox'; fprintf(' -> checkbox: %s\n', v2s(c.Value));
c.Style = 'popupmenu'; fprintf(' -> popupmenu: %s\n', v2s(c.Value));
c.Value = 3; c.String = {'a'}; lastwarn(''); drawnow; fprintf('popup Value 3 then String of one: Value=%s warn=%s\n', v2s(c.Value), lastwarn);
c.Style = 'pushbutton'; fprintf(' -> pushbutton keeps: %s\n', v2s(c.Value));
delete(c);
c = uicontrol(f, 'Style', 'checkbox', 'Value', 1); c.Style = 'listbox'; fprintf('checkbox Value 1 -> listbox: %s\n', v2s(c.Value)); delete(c);
c = uicontrol(f, 'Style', 'togglebutton', 'Min', 2, 'Max', 7); fprintf('toggle Min 2 Max 7 Value: %s\n', v2s(c.Value)); delete(c);
c = uicontrol(f, 'Style', 'slider', 'Min', 2, 'Max', 7); lastwarn(''); drawnow; fprintf('slider Min 2 Max 7 Value: %s warn=[%s]\n', v2s(c.Value), lastwarn); delete(c);
c = uicontrol(f, 'Style', 'checkbox', 'Min', 2, 'Max', 7); fprintf('checkbox Min 2 Max 7 Value: %s\n', v2s(c.Value)); delete(c);

%% Min, Max, SliderStep, ListboxTop
c = uicontrol(f, 'Style', 'slider');
sets = {
    'Max', {10, 0, -5, [1 2], 'a', [], true, int8(3), NaN, Inf}
    'Min', {-10, 20, [1 2], 'a', [], NaN, -Inf}
    'SliderStep', {[0.2 0.5], [0 1], [-1 1], [0.5 0.1], [1 2 3], 'a', [2 3], 0.5, [0.1; 0.2], [NaN 1], [Inf 1], int8([0 1]), true}
    'ListboxTop', {0, 2, 1.5, -1, 'a', [1 2], [], 100, NaN, int8(2)}
    };
for r = 1:size(sets, 1)
    for k = 1:numel(sets{r, 2})
        lastwarn('');
        tryp(sprintf('slider %s<-%s', sets{r, 1}, v2s(sets{r, 2}{k})), @() setget(c, sets{r, 1}, sets{r, 2}{k}));
        if ~isempty(lastwarn), [m, id] = lastwarn; fprintf('   warn %s | %s\n', id, oneline(m)); end
    end
end
delete(c);

%% String on the list styles
for s = {'listbox', 'popupmenu', 'edit', 'text', 'pushbutton'}
    c = uicontrol(f, 'Style', s{1});
    forms = {{'a', 'b', 'c'}, 'a|b|c', ['ab'; 'cd'], '', {}, {'a'; 'b'}, sprintf('l1\nl2'), ["x" "y"], {'a', 5}, 5, [1 2 3], {''}};
    for k = 1:numel(forms)
        tryp(sprintf('%s String<-%s', s{1}, v2s(forms{k})), @() setget(c, 'String', forms{k}));
    end
    delete(c);
end

%% multi-line edit
c = uicontrol(f, 'Style', 'edit', 'Max', 2, 'String', {'line one', 'line two'});
fprintf('multi-line edit String=%s Value=%s Max-Min=%g\n', v2s(c.String), v2s(c.Value), c.Max - c.Min);
c.String = sprintf('a\nb'); fprintf('  newline char: %s size=%s\n', v2s(c.String), mat2str(size(c.String)));
c.String = ['ab '; 'cde']; fprintf('  char matrix: %s\n', v2s(c.String));
c.Max = 1; fprintf('  back to single line: %s\n', v2s(c.String));
delete(c);

%% CData
c = uicontrol(f);
cds = {rand(4, 4, 3) > 2, zeros(4, 4, 3), zeros(4, 4), ones(2, 3, 3) * 2, uint8(zeros(4, 4, 3)), 'a', [], zeros(4, 4, 4), single(zeros(2, 2, 3)), true(2, 2, 3), {1}, NaN(2, 2, 3), int16(zeros(2, 2, 3)), uint16(zeros(2, 2, 3))};
for k = 1:numel(cds)
    tryp(sprintf('CData<-%s %s', class(cds{k}), mat2str(size(cds{k}))), @() describe(setget(c, 'CData', cds{k})));
end
delete(c);

%% the aliases
c = uicontrol(f);
c.Tooltip = 'tip'; fprintf('Tooltip set -> TooltipString=%s\n', v2s(get(c, 'TooltipString')));
set(c, 'TooltipString', 'old'); fprintf('TooltipString set -> Tooltip=%s\n', v2s(c.Tooltip));
tryp('TooltipString<-5', @() setget(c, 'TooltipString', 5));
tryp('TooltipString<-cell', @() setget(c, 'TooltipString', {'a', 'b'}));
tryp('Tooltip<-string', @() setget(c, 'Tooltip', "s"));
tryp('Tooltip<-string array', @() setget(c, 'Tooltip', ["s" "t"]));
tryp('Tooltip<-char matrix', @() setget(c, 'Tooltip', ['ab'; 'cd']));
tryp('Tooltip<-[]', @() setget(c, 'Tooltip', []));
m = uicontextmenu(f);
tryp('ContextMenu<-menu', @() isequal(setget(c, 'ContextMenu', m), m));
tryp('UIContextMenu reads', @() isequal(get(c, 'UIContextMenu'), m));
tryp('UIContextMenu<-[]', @() setget(c, 'UIContextMenu', []));
tryp('ContextMenu after', @() get(c, 'ContextMenu'));
tryp('UIContextMenu<-menu', @() isequal(setget(c, 'UIContextMenu', m), m));
tryp('ContextMenu<-5', @() setget(c, 'ContextMenu', 5));
tryp('ContextMenu<-figure', @() setget(c, 'ContextMenu', f));
tryp('ContextMenu<-uicontrol', @() setget(c, 'ContextMenu', c));
tryp('ContextMenu<-''''', @() setget(c, 'ContextMenu', ''));
delete(m); tryp('ContextMenu after delete(menu)', @() get(c, 'ContextMenu'));
delete(c);

%% Enable and ButtonDownFcn
c = uicontrol(f, 'Style', 'text', 'Enable', 'inactive', 'ButtonDownFcn', @(s, e) disp(class(e)));
fprintf('inactive text: Enable=%s ButtonDownFcn=%s\n', c.Enable, v2s(c.ButtonDownFcn));
delete(c);

%% event data classes
for name = {'matlab.ui.eventdata.ActionData', 'matlab.ui.eventdata.SelectionChangedData', 'matlab.ui.eventdata.MouseData', 'matlab.ui.eventdata.ButtonDownData', 'matlab.graphics.eventdata.Hit'}
    mc = meta.class.fromName(name{1});
    if isempty(mc), fprintf('meta %s: none\n', name{1}); continue; end
    p = mc.PropertyList;
    pn = cell(1, numel(p));
    for k = 1:numel(p)
        acc = p(k).GetAccess; if ~ischar(acc), acc = 'list'; end
        pn{k} = sprintf('%s(%s,hidden=%d)', p(k).Name, acc, p(k).Hidden);
    end
    sup = arrayfun(@(x) x.Name, mc.SuperclassList, 'UniformOutput', false);
    fprintf('meta %s < %s : %s\n', name{1}, strjoin(sup', ','), strjoin(pn, ' '));
end
delete(f);
end

function v = setget(h, name, value)
set(h, name, value);
v = get(h, name);
end

function s = describe(v)
s = sprintf('%s %s', class(v), mat2str(size(v)));
end
