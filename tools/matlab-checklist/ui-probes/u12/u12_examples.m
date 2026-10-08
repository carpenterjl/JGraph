% u12_examples - the guide's two app examples (the Apps and GUIs chapter) run as written, and their
% callbacks called as the window would call them.
f = figure('Name', 'Counter', 'MenuBar', 'none', 'Position', [300 300 240 120]);
t = uicontrol(f, 'Style', 'text', 'String', '0', 'FontSize', 20, 'Position', [20 60 200 40]);
uicontrol(f, 'Style', 'pushbutton', 'String', 'Add one', 'Position', [20 15 200 30], ...
    'Callback', @(src, evt) set(t, 'String', num2str(str2double(t.String) + 1)));
b = findobj(f, 'Style', 'pushbutton');
b.Callback(b, []);
b.Callback(b, []);
fprintf('counter: %s\n', t.String);
close(f);

fig = uifigure('Name', 'Sine');
g = uigridlayout(fig, [2 1], 'RowHeight', {'fit', '1x'});
s = uislider(g, 'Limits', [1 10], 'Value', 2);
ax = uiaxes(g);
t = linspace(0, 1, 500);
draw = @(f) plot(ax, t, sin(2*pi*f*t));
draw(s.Value);
s.ValueChangedFcn = @(src, evt) draw(evt.Value);
s.ValueChangedFcn(s, struct('Value', 5));
fprintf('sine: %d line(s), crossings %d\n', numel(ax.Children), sum(diff(sign(ax.Children(1).YData)) ~= 0));
delete(fig);
