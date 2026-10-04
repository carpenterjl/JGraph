function u2_more
% U2 probe: the messages and warning identifiers the first four probes did not catch, and movegui
% on a figure with no menu bar and on a uifigure. Headless: run-probe.ps1 -Name u2_more.
f = figure('Visible', 'off', 'Position', [200 200 300 200]);
c = uicontrol(f);
ss = get(0, 'ScreenSize');
er('spp(c)', @() setpixelposition(c));
er('spp(c,[1 2 3])', @() setpixelposition(c, [1 2 3]));
er('spp(c,''a'')', @() setpixelposition(c, 'abcd'));
er('spp(5.5,[1 2 3 4])', @() setpixelposition(5.5, [1 2 3 4]));
er('spp(c,[1 2 -3 4])', @() setpixelposition(c, [1 2 -3 4]));
er('movegui bogus', @() movegui(f, 'bogus'));
er('movegui(5.5)', @() movegui(5.5));
er('movegui(f,[1 2 3])', @() movegui(f, [1 2 3]));
er('movegui(f,{1})', @() movegui(f, {1}));
er('movegui(c,center)', @() movegui(c, 'center'));
fprintf('movegui(c): figure moved to %s\n', mat2str(f.Position));
er('uistack()', @() uistack());
er('gpp(c,1,2)', @() getpixelposition(c, 1, 2));

p = uipanel(f);
lastwarn('', '');
p.BorderType = 'etchedin'; [m, id] = lastwarn; fprintf('BorderType warning: %s | %s\n', id, m);
lastwarn('', '');
p.ShadowColor = [0 0 0]; [m, id] = lastwarn; fprintf('ShadowColor warning: %s | %s\n', id, m);
lastwarn('', '');
x = p.ShadowColor; [m, id] = lastwarn; fprintf('ShadowColor get warning: %s | %s\n', id, m); %#ok<NASGU>
lastwarn('', '');
p.AutoResizeChildren = 'on'; p.SizeChangedFcn = 'disp(1)'; [m, id] = lastwarn; fprintf('panel SizeChangedFcn-ARC warning: %s | %s\n', id, m);
lastwarn('', '');
f.AutoResizeChildren = 'on'; f.SizeChangedFcn = 'disp(1)'; [m, id] = lastwarn; fprintf('figure SizeChangedFcn-ARC warning: %s | %s\n', id, m);
lastwarn('', '');
f.Children = flipud(f.Children); [m, id] = lastwarn; fprintf('Children flip (uipanel,uicontrol) warning: %s | %s -> %s\n', id, m, strjoin(arrayfun(@(k) k.Type, f.Children, 'UniformOutput', false)', ','));
a = axes(f); a2 = axes(f); a.Tag = 'a'; a2.Tag = 'a2';
k = f.Children; lastwarn('', '');
f.Children = k([1 2 4 3]); [m, id] = lastwarn; fprintf('axes swapped within their group: warning [%s] -> %s\n', id, strjoin(arrayfun(@(k) [k.Type k.Tag], f.Children, 'UniformOutput', false)', ','));
k = f.Children; lastwarn('', '');
f.Children = k([3 2 1 4]); [m, id] = lastwarn; fprintf('axes above a component: warning [%s | %s] -> %s\n', id, m, strjoin(arrayfun(@(k) [k.Type k.Tag], f.Children, 'UniformOutput', false)', ','));
er('Children not handles', @() set(f, 'Children', [1 2 3 4]));
er('panel Parent = groot', @() set(p, 'Parent', groot));
er('uicontrol Parent = 0', @() set(c, 'Parent', 0));
er('uicontrol Parent = 5.5', @() set(c, 'Parent', 5.5));
er('axes Parent = 5.5', @() set(a, 'Parent', 5.5));
er('axes Parent = axes', @() set(a, 'Parent', a2));
er('panel Title logical', @() uipanel(f, 'Title', true));
er('panel BorderWidth -1 at creation', @() uipanel(f, 'BorderWidth', -1));
er('panel Position 3 at creation', @() uipanel(f, 'Position', [1 2 3]));
er('uipanel(f, ''Units'')', @() uipanel(f, 'Units'));
er('uipanel(1,2,3)', @() uipanel(1, 2, 3));
er('uipanel(''Title'',''a'',''Bogus'')', @() uipanel('Title', 'a', 'Bogus'));
er('uipanel(f,5,6)', @() uipanel(f, 5, 6));
er('get panel bogus', @() get(p, 'Bogus'));
er('set panel bogus', @() set(p, 'Bogus', 1));
er('p.Bogus', @() p.Bogus);
er('panel Layout set', @() set(p, 'Layout', 1));
er('root Units set', @() set(0, 'Units', 'points'));
set(0, 'Units', 'pixels');
er('figure Scrollable on', @() set(f, 'Scrollable', 'on'));
er('figure WindowStyle modal', @() set(f, 'WindowStyle', 'modal'));
f.WindowStyle = 'normal';
er('figure IntegerHandle bogus', @() set(f, 'IntegerHandle', 'bogus'));
er('figure AutoResizeChildren bogus', @() set(f, 'AutoResizeChildren', 'bogus'));
er('figure Position 3', @() set(f, 'Position', [1 2 3]));
er('figure Position neg', @() set(f, 'Position', [1 2 -3 4]));
er('figure Position zero', @() set(f, 'Position', [1 2 0 4]));
fprintf('figure Position after zero width: %s\n', mat2str(f.Position));
er('axes Position neg', @() set(a, 'Position', [0 0 -1 1]));
er('axes Position 3', @() set(a, 'Position', [0 0 1]));
er('axes OuterPosition zero', @() set(a, 'OuterPosition', [0 0 0 1]));
er('axes Units set chars', @() set(a, 'Units', 'char'));
fprintf('axes Units char -> %s\n', a.Units);
delete(f);

% movegui: figures with other chrome
for kind = 1:3
    switch kind
        case 1, g = figure('Visible', 'off', 'Position', [200 200 300 200], 'MenuBar', 'none', 'ToolBar', 'none'); name = 'no bars';
        case 2, g = figure('Visible', 'off', 'Position', [200 200 300 200], 'MenuBar', 'none', 'ToolBar', 'figure'); name = 'toolbar only';
        case 3, g = uifigure('Visible', 'off', 'Position', [200 200 300 200]); name = 'uifigure';
    end
    for w = {'northeast', 'southwest', 'center'}
        g.Position = [200 200 300 200];
        movegui(g, w{1});
        pos = g.Position;
        fprintf('movegui %-12s %-9s left=%g bottom=%g rightgap=%g topgap=%g\n', name, w{1}, pos(1) - ss(1), pos(2) - ss(2), ss(3) - (pos(1) + pos(3) - 1), ss(4) - (pos(2) + pos(4) - 1));
    end
    g.Position = [-500 -500 300 200]; movegui(g); fprintf('movegui %-12s onscreen -> %s\n', name, mat2str(g.Position));
    g.Position = [200 200 300 200]; movegui(g, [10 20]); fprintf('movegui %-12s [10 20] -> %s\n', name, mat2str(g.Position));
    delete(g);
end
g = figure('Visible', 'off', 'Position', [200 200 3000 2000]); movegui(g, 'center'); fprintf('movegui larger than the screen: %s\n', mat2str(g.Position)); delete(g);

% findobj order with nested panels, and what a figure's Children holds for a uifigure
uf = uifigure('Visible', 'off');
b = uicontrol(uf, 'Tag', 'b'); p = uipanel(uf, 'Tag', 'p'); a = axes(uf, 'Tag', 'a'); b2 = uicontrol(uf, 'Tag', 'b2');
fprintf('uifigure Children: %s\n', strjoin(arrayfun(@(k) k.Tag, uf.Children, 'UniformOutput', false)', ' '));
fprintf('findobj(uf): %d ; findall(uf): %d\n', numel(findobj(uf)), numel(findall(uf)));
fprintf('gcf inside uifigure creation left CurrentFigure empty? %d\n', isempty(get(0, 'CurrentFigure')));
fprintf('gca with only a uifigure axes: made a new figure? %d\n', ancestor(gca, 'figure') ~= uf);
delete(gcf); delete(uf);
end

function er(label, fn)
try
    fn();
    fprintf('%s : ok\n', label);
catch e
    fprintf('%s : ERR %s | %s\n', label, e.identifier, oneline(e.message));
end
end
