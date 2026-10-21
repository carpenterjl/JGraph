% oi136_create - open item 136, fourth probe: a CreateFcn given in the making call runs after every
% other option is set, whichever side of it they stand; and a group's members made in it are its
% children, newest first. Headless in R2025b and in JGraph.
set(groot, 'DefaultFigureVisible', 'off');
f = figure;
ax = axes(f);
hold(ax, 'on');
x = 1:3; y = [2 1 3];
plot(ax, x, y, 'CreateFcn', @(h, e) fprintf('plot create: tag=%s ud=%s\n', h.Tag, mat2str(h.UserData)), 'Tag', 'after', 'UserData', 5);
bar(ax, y, 'Tag', 'before', 'CreateFcn', @(h, e) fprintf('bar create: tag=%s\n', h.Tag));
line(ax, x, y, 'CreateFcn', @(h, e) fprintf('line create: tag=%s\n', h.Tag), 'Tag', 'lineafter');
g = hggroup(ax);
a = line(g, x, y, 'Tag', 'first');
b = text(g, 1, 1, 'b', 'Tag', 'second');
c = hggroup(g, 'Tag', 'third');
d = light(g, 'Tag', 'fourth');
fprintf('group children: %s\n', strjoin(get(g.Children, 'Tag')', ' '));
set(b, 'Parent', ax);
fprintf('after text left: %s, text parent %s\n', strjoin(get(g.Children, 'Tag')', ' '), b.Parent.Type);
set(a, 'Parent', c);
fprintf('after line moved in: %s; inner %s; line parent %s\n', strjoin(get(g.Children, 'Tag')', ' '), get(c.Children, 'Tag'), a.Parent.Tag);
disp('done');
