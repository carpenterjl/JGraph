% oi136_order - open items 136 and 137, second probe: what a refused option leaves behind, which of
% two settings of one name wins, the 'Parent' option on the high-level makers, and a group given
% first when the axes is not held. Headless in R2025b and in JGraph.
set(groot, 'DefaultFigureVisible', 'off');
f = figure;
ax = axes(f);
x = 1:3; y = [2 1 3];

disp('--- a refused option, axes held');
hold(ax, 'on');
plot(ax, x, y);
try, plot(ax, x, y, 'Foo', 1); catch e, disp(e.message); end
fprintf('children after plot Foo: %d\n', numel(ax.Children));
try, bar(ax, y, 'Foo', 1); catch e, disp(e.message); end
fprintf('children after bar Foo: %d\n', numel(ax.Children));
try, line(ax, x, y, 'Foo', 1); catch e, disp(e.message); end
fprintf('children after line Foo: %d\n', numel(ax.Children));
try, scatter(ax, x, y, 'Tag', 'ok', 'Foo', 1); catch e, disp(e.message); end
fprintf('children after scatter Tag Foo: %d\n', numel(ax.Children));

disp('--- a refused option, axes not held');
hold(ax, 'off');
plot(ax, x, y);
plot(ax, x, y);
try, plot(ax, x, y, 'Foo', 1); catch e, disp(e.message); end
fprintf('children after plot Foo: %d\n', numel(ax.Children));

disp('--- one name twice');
h = plot(ax, x, y, 'Color', 'r', 'Color', 'b');
fprintf('Color twice: %s\n', mat2str(h.Color));
h = plot(ax, x, y, 'Tag', 'a', 'Tag', 'b');
fprintf('Tag twice: %s\n', h.Tag);
h = plot(ax, x, y, 'r', 'Color', 'g');
fprintf('spec then Color: %s\n', mat2str(h.Color));
h = plot(ax, x, y, 'LineWidth', 3, 'Tag', 'w', 'LineWidth', 4);
fprintf('LineWidth around Tag: %g\n', h.LineWidth);
h = bar(ax, y, 'BarWidth', 0.2, 'Tag', 'w', 'BarWidth', 0.4);
fprintf('BarWidth around Tag: %g\n', h.BarWidth);

disp('--- Parent as an option');
cla(ax);
hold(ax, 'on');
g = hggroup(ax, 'Tag', 'g');
h = plot(x, y, 'Parent', g);
fprintf('plot Parent g: %s\n', h.Parent.Type);
h = bar(y, 'Parent', g);
fprintf('bar Parent g: %s\n', h.Parent.Type);
h = scatter(x, y, 'Parent', g);
fprintf('scatter Parent g: %s\n', h.Parent.Type);
h = plot(x, y, 'Parent', ax, 'Tag', 'pa');
fprintf('plot Parent ax: %s\n', h.Parent.Type);
f2 = figure;
ax2 = axes(f2);
h = plot(ax, x, y, 'Parent', ax2);
fprintf('plot ax then Parent ax2: %d\n', isequal(h.Parent, ax2));
h = line(ax, x, y, 'Parent', ax2);
fprintf('line ax then Parent ax2: %d\n', isequal(h.Parent, ax2));
figure(f);

disp('--- a group first, axes not held');
cla(ax);
hold(ax, 'off');
g = hggroup(ax, 'Tag', 'g');
plot(ax, x, y, 'Tag', 'before');
try
    h = plot(g, x, y, 'Tag', 'in');
    fprintf('plot g: parent %s, g valid %d, children of ax %d\n', h.Parent.Type, isvalid(g), numel(ax.Children));
catch e
    fprintf('plot g: %s | %s, g valid %d\n', e.identifier, e.message, isvalid(g));
end
cla(ax);
plot(ax, x, y, 'Tag', 'before');
g = hggroup(ax, 'Tag', 'g');
h = line(g, x, y, 'Tag', 'in');
fprintf('line g: parent %s, children of ax %d\n', h.Parent.Type, numel(ax.Children));
cla(ax);
plot(ax, x, y, 'Tag', 'before');
g = hggroup(ax, 'Tag', 'g');
try
    h = plot(x, y, 'Parent', g);
    fprintf('plot Parent g unheld: parent %s, g valid %d, children of ax %d\n', h.Parent.Type, isvalid(g), numel(ax.Children));
catch e
    fprintf('plot Parent g unheld: %s | %s\n', e.identifier, e.message);
end
cla(ax);
t = hgtransform(ax);
try
    h = bar(t, y);
    fprintf('bar t unheld: parent %s, t valid %d, children of ax %d\n', h.Parent.Type, isvalid(t), numel(ax.Children));
catch e
    fprintf('bar t unheld: %s | %s\n', e.identifier, e.message);
end

disp('--- a group first for verbs that are not makers');
cla(ax);
hold(ax, 'on');
g = hggroup(ax);
try, title(g, 'x'); disp('title g ok'); catch e, fprintf('title g: %s | %s\n', e.identifier, e.message); end
try, hold(g, 'on'); disp('hold g ok'); catch e, fprintf('hold g: %s | %s\n', e.identifier, e.message); end
try, h = plot(g, g, 1:3); fprintf('plot g g: %s\n', h.Parent.Type); catch e, fprintf('plot g g: %s | %s\n', e.identifier, e.message); end
try, h = histogram(g, [1 2 2]); fprintf('histogram g: %s\n', h.Parent.Type); catch e, fprintf('histogram g: %s | %s\n', e.identifier, e.message); end
try, h = errorbar(g, x, y, y); fprintf('errorbar g: %s\n', h.Parent.Type); catch e, fprintf('errorbar g: %s | %s\n', e.identifier, e.message); end
try, h = area(g, x, y); fprintf('area g: %s\n', h.Parent.Type); catch e, fprintf('area g: %s | %s\n', e.identifier, e.message); end
try, h = stem(g, x, y); fprintf('stem g: %s\n', h.Parent.Type); catch e, fprintf('stem g: %s | %s\n', e.identifier, e.message); end
try, h = plot3(g, x, y, y); fprintf('plot3 g: %s\n', h.Parent.Type); catch e, fprintf('plot3 g: %s | %s\n', e.identifier, e.message); end
try, h = quiver(g, x, y, y, y); fprintf('quiver g: %s\n', h.Parent.Type); catch e, fprintf('quiver g: %s | %s\n', e.identifier, e.message); end
try, h = contour(g, magic(3)); fprintf('contour g: %s\n', class(h)); catch e, fprintf('contour g: %s | %s\n', e.identifier, e.message); end
try, h = mesh(g, magic(3)); fprintf('mesh g: %s\n', h.Parent.Type); catch e, fprintf('mesh g: %s | %s\n', e.identifier, e.message); end
try, h = surf(g, magic(3)); fprintf('surf g: %s\n', h.Parent.Type); catch e, fprintf('surf g: %s | %s\n', e.identifier, e.message); end
try, h = scatter3(g, x, y, y); fprintf('scatter3 g: %s\n', h.Parent.Type); catch e, fprintf('scatter3 g: %s | %s\n', e.identifier, e.message); end
try, h = barh(g, y); fprintf('barh g: %s\n', h.Parent.Type); catch e, fprintf('barh g: %s | %s\n', e.identifier, e.message); end
try, h = semilogy(g, x, y); fprintf('semilogy g: %s\n', h.Parent.Type); catch e, fprintf('semilogy g: %s | %s\n', e.identifier, e.message); end
try, h = pcolor(g, magic(3)); fprintf('pcolor g: %s\n', h.Parent.Type); catch e, fprintf('pcolor g: %s | %s\n', e.identifier, e.message); end
try, h = image(g, 'CData', magic(3)); fprintf('image g props: %s\n', h.Parent.Type); catch e, fprintf('image g props: %s | %s\n', e.identifier, e.message); end
try, h = stem3(g, x, y, y); fprintf('stem3 g: %s\n', h.Parent.Type); catch e, fprintf('stem3 g: %s | %s\n', e.identifier, e.message); end
try, h = fill3(g, x, y, y, 'r'); fprintf('fill3 g: %s\n', h.Parent.Type); catch e, fprintf('fill3 g: %s | %s\n', e.identifier, e.message); end
disp('done');
