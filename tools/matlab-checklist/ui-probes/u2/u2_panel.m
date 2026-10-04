function u2_panel
% U2 probe: R2025b's uipanel in a classic figure and in a uifigure - what it holds, takes and
% refuses, and where its inner area lies. Headless: run-probe.ps1 -Name u2_panel.
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);

%% defaults and names
p = uipanel(f);
names = fieldnames(get(p));
fprintf('classic get names (%d): %s\n', numel(names), strjoin(names', ','));
s = set(p);
fprintf('classic set names (%d): %s\n', numel(fieldnames(s)), strjoin(fieldnames(s)', ','));
for n = {'BorderType', 'TitlePosition', 'Units', 'FontUnits', 'FontWeight', 'FontAngle', 'Enable', 'HandleVisibility', 'BusyAction'}
    fprintf('  set %s options: %s\n', n{1}, v2s(s.(n{1})));
end
fprintf('classic defaults:\n'); showall(p);
fprintf('class=%s Type=%s\n', class(p), p.Type);
q = uipanel(uf);
names = fieldnames(get(q));
fprintf('uifigure get names (%d): %s\n', numel(names), strjoin(names', ','));
fprintf('uifigure defaults:\n'); showall(q);
for hidden = {'ShadowColor', 'BorderColor', 'HighlightColor', 'ResizeFcn', 'UIContextMenu', 'TooltipString', 'Selected', 'SelectionHighlight', 'HitTest', 'PickableParts', 'Extent', 'Layout', 'ContextMenu'}
    tryp(['hidden get ' hidden{1}], @() get(p, hidden{1}));
end
delete(p); delete(q);

%% argument forms
tryp('uipanel()', @() class(uipanel()));
fprintf('gcf after bare uipanel is f? %d ; figures: %d\n', isequal(gcf, f), numel(findall(0, 'Type', 'figure')));
tryp('uipanel(f)', @() get(uipanel(f), 'Parent') == f);
tryp('uipanel(f,''Title'',''T'')', @() get(uipanel(f, 'Title', 'T'), 'Title'));
tryp('uipanel(''Parent'',f)', @() get(uipanel('Parent', f), 'Parent') == f);
tryp('uipanel(''Title'')', @() uipanel('Title'));
tryp('uipanel(f,''Title'')', @() uipanel(f, 'Title'));
tryp('uipanel(5.5)', @() uipanel(5.5));
tryp('uipanel(struct)', @() get(uipanel(struct('Title', 'S')), 'Title'));
tryp('uipanel(f,struct)', @() get(uipanel(f, struct('Title', 'S2')), 'Title'));
tryp('uipanel(f,''Bogus'',1)', @() uipanel(f, 'Bogus', 1));
tryp('uipanel(f,''BorderType'',''bogus'')', @() uipanel(f, 'BorderType', 'bogus'));
c = uicontrol(f);
tryp('uipanel(uicontrol)', @() uipanel(c));
tryp('uipanel(uicontrol,''Title'',''x'')', @() uipanel(c, 'Title', 'x'));
ax = axes(f);
tryp('uipanel(axes)', @() uipanel(ax));
tryp('uipanel(''Parent'',axes)', @() uipanel('Parent', ax));
pp = uipanel(f);
tryp('uipanel(panel)', @() get(uipanel(pp), 'Parent') == pp);
tryp('uicontrol(panel)', @() get(uicontrol(pp), 'Parent') == pp);
tryp('uicontrol(''Parent'',panel)', @() get(uicontrol('Parent', pp), 'Parent') == pp);
tryp('axes(panel)', @() get(axes(pp), 'Parent') == pp);
tryp('axes(''Parent'',panel)', @() get(axes('Parent', pp), 'Parent') == pp);
tryp('uipanel(uifigure)', @() get(uipanel(uf), 'Parent') == uf);
tryp('uicontrol(uifigure)', @() get(uicontrol(uf), 'Parent') == uf);
clf(f);

%% values each property takes and refuses
p = uipanel(f, 'Units', 'pixels', 'Position', [50 60 200 150]);
sets = {
    'Title', {'abc', "str", 5, {'a', 'b'}, ['ab'; 'cd'], '', [], true, {'a'}, ["a" "b"]}
    'BorderType', {'none', 'line', 'etchedin', 'etchedout', 'beveledin', 'beveledout', 'LINE', 'n', 'etched', 'bogus', 5}
    'BorderWidth', {2, 0, -1, 1.5, 'a', [1 2], [], NaN, Inf, int8(3)}
    'TitlePosition', {'lefttop', 'centertop', 'righttop', 'leftbottom', 'centerbottom', 'rightbottom', 'CENTERTOP', 'left', 'c', 'bogus'}
    'BackgroundColor', {[1 0 0], 'r', 'red', '#00FF00', 'none', [2 0 0], [1 0], 'bogus', "blue", uint8([255 0 0])}
    'ForegroundColor', {[0 0 1], 'none', 'g'}
    'HighlightColor', {[0 1 1], 'k', 'none'}
    'BorderColor', {[0 1 0], 'k', 'none'}
    'ShadowColor', {[0 1 0], 'k'}
    'FontSize', {10, 0, -1, 'a', [1 2], 8.5}
    'FontName', {'Arial', '', 5, "Courier"}
    'FontWeight', {'bold', 'light', 'demi', 'normal', 'b', 'heavy'}
    'FontAngle', {'italic', 'oblique', 'normal', 'bogus'}
    'FontUnits', {'pixels', 'normalized', 'inches', 'centimeters', 'points', 'bogus'}
    'Units', {'pixels', 'normalized', 'inches', 'centimeters', 'points', 'characters', 'PIX', 'bogus'}
    'Position', {[10 20 30 40], [10 20 0 40], [10 20 -5 40], [1 2 3], 'a', [10 20 30 NaN], [10 20 30 Inf], {1}}
    'InnerPosition', {[10 20 30 40]}
    'OuterPosition', {[10 20 30 40]}
    'Visible', {'off', 'on', true, false, 1, 0, 'bogus', "off"}
    'Enable', {'off', 'inactive', 'on', 'bogus'}
    'Scrollable', {'on', 'off', true}
    'AutoResizeChildren', {'on', 'off'}
    'Clipping', {'off', 'on'}
    'Tag', {'t', 5, "s"}
    'Tooltip', {'tip', {'a', 'b'}, 5}
    'HandleVisibility', {'off', 'callback', 'on', 'bogus'}
    'SizeChangedFcn', {@(s, e) 1, 'disp(1)', {@disp, 1}, '', [], 5}
    'ResizeFcn', {'disp(2)'}
    'ButtonDownFcn', {'disp(3)', ''}
    'Type', {'x'}
    'BeingDeleted', {'on'}
    'Children', {[]}
    'Parent', {uf, f, [], 0, ax}
    };
for k = 1:size(sets, 1)
    name = sets{k, 1};
    for j = 1:numel(sets{k, 2})
        v = sets{k, 2}{j};
        try
            set(p, 'Units', 'pixels');
            set(p, name, v);
            fprintf('%s <- %s : %s\n', name, v2s(v), v2s(get(p, name)));
        catch e
            fprintf('%s <- %s : ERR %s | %s\n', name, v2s(v), e.identifier, oneline(e.message));
        end
    end
end
fprintf('after ResizeFcn set: SizeChangedFcn=%s\n', v2s(p.SizeChangedFcn));
delete(p);

%% where the inner area lies (classic): a normalized child filling it, read back in pixels
fprintf('== classic inner areas of a 200x150 panel at [50 60]\n');
cases = {
    {}
    {'Title', 'T'}
    {'Title', 'T', 'FontSize', 12}
    {'Title', 'T', 'FontSize', 20}
    {'Title', 'T', 'FontUnits', 'pixels', 'FontSize', 20}
    {'Title', 'Tg', 'FontName', 'Arial', 'FontSize', 10}
    {'Title', 'T', 'TitlePosition', 'centertop'}
    {'Title', 'T', 'TitlePosition', 'leftbottom'}
    {'Title', 'T', 'TitlePosition', 'rightbottom'}
    {'BorderType', 'none'}
    {'BorderType', 'none', 'Title', 'T'}
    {'BorderType', 'etchedin'}
    {'BorderType', 'etchedout'}
    {'BorderType', 'beveledin'}
    {'BorderType', 'beveledout'}
    {'BorderWidth', 3}
    {'BorderWidth', 0}
    {'BorderWidth', 3, 'Title', 'T'}
    {'BorderType', 'etchedin', 'BorderWidth', 3}
    {'BorderType', 'beveledin', 'BorderWidth', 3}
    {'BorderType', 'etchedin', 'Title', 'T'}
    {'Title', {'two'}}
    };
for k = 1:numel(cases)
    p = uipanel(f, 'Units', 'pixels', 'Position', [50 60 200 150], cases{k}{:});
    c = uicontrol(p, 'Style', 'text', 'Units', 'normalized', 'Position', [0 0 1 1]);
    c.Units = 'pixels';
    lab = strjoin(cellfun(@(x) char(string(x)), cases{k}, 'UniformOutput', false), ',');
    fprintf('  [%s] child=%s Inner=%s Outer=%s gpp(child)=%s gpp(child,true)=%s\n', lab, mat2str(c.Position, 8), ...
        mat2str(p.InnerPosition, 8), mat2str(p.OuterPosition, 8), mat2str(getpixelposition(c), 8), mat2str(getpixelposition(c, true), 8));
    delete(p);
end

%% the same in a uifigure
fprintf('== uifigure inner areas of a 200x150 panel at [50 60]\n');
ucases = {
    {}
    {'Title', 'T'}
    {'Title', 'T', 'FontSize', 20}
    {'Title', 'T', 'TitlePosition', 'centertop'}
    {'BorderType', 'none'}
    {'BorderType', 'none', 'Title', 'T'}
    {'BorderWidth', 3}
    {'BorderWidth', 3, 'Title', 'T'}
    };
for k = 1:numel(ucases)
    p = uipanel(uf, 'Position', [50 60 200 150], ucases{k}{:});
    c = uicontrol(p, 'Style', 'text', 'Units', 'normalized', 'Position', [0 0 1 1]);
    drawnow;
    c.Units = 'pixels';
    lab = strjoin(cellfun(@(x) char(string(x)), ucases{k}, 'UniformOutput', false), ',');
    fprintf('  [%s] child=%s Inner=%s Outer=%s gpp(child,true)=%s\n', lab, mat2str(c.Position, 8), ...
        mat2str(p.InnerPosition, 8), mat2str(p.OuterPosition, 8), mat2str(getpixelposition(c, true), 8));
    delete(p);
end
tryp('uifigure panel Units set', @() setget(uipanel(uf), 'Units', 'normalized'));
tryp('uifigure panel BorderType etchedin', @() setget(uipanel(uf), 'BorderType', 'etchedin'));
tryp('uifigure panel TitlePosition leftbottom', @() setget(uipanel(uf), 'TitlePosition', 'leftbottom'));

%% nesting, axes in a panel
p = uipanel(f, 'Units', 'pixels', 'Position', [50 60 300 250], 'Title', 'Outer');
p2 = uipanel(p, 'Units', 'pixels', 'Position', [20 30 200 150]);
b = uicontrol(p2, 'Position', [10 10 50 20]);
fprintf('nested: gpp(p2)=%s gpp(p2,true)=%s gpp(b)=%s gpp(b,true)=%s\n', mat2str(getpixelposition(p2)), mat2str(getpixelposition(p2, true)), mat2str(getpixelposition(b)), mat2str(getpixelposition(b, true)));
a = axes(p2);
fprintf('axes in panel: Units=%s Position=%s Outer=%s gpp=%s gpp(true)=%s\n', a.Units, mat2str(a.Position, 6), mat2str(a.OuterPosition, 6), mat2str(getpixelposition(a), 8), mat2str(getpixelposition(a, true), 8));
fprintf('ancestor(b,figure)==f %d ; ancestor(b,uipanel)==p2 %d ; ancestor(a,''uipanel'',''toplevel'')==p %d\n', ancestor(b, 'figure') == f, ancestor(b, 'uipanel') == p2, ancestor(a, 'uipanel', 'toplevel') == p);
fprintf('p.Children: %s ; p2.Children: %s\n', kids(p), kids(p2));
p2.Visible = 'off';
fprintf('p2 off: b.Visible=%s a.Visible=%s\n', char(b.Visible), char(a.Visible));
p2.Visible = 'on';
p2.Enable = 'off';
fprintf('p2 Enable off: b.Enable=%s\n', char(b.Enable));
delete(f); delete(uf);
end

function v = setget(h, name, value)
set(h, name, value);
v = get(h, name);
delete(h);
end

function s = kids(h)
k = h.Children;
parts = arrayfun(@(x) x.Type, k, 'UniformOutput', false);
s = strjoin(parts(:)', ',');
end

function showall(h)
s = get(h);
names = fieldnames(s);
for k = 1:numel(names)
    fprintf('  %s = %s\n', names{k}, v2s(s.(names{k})));
end
end
