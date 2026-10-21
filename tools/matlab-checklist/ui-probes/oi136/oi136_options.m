% oi136_options - open items 136 and 137: which makers take any property of the object they make
% as a trailing 'Name', value option, and which take a group or a transform as their first
% argument. Headless in R2025b (run-probe.ps1) and in JGraph (jgraph.exe -batch oi136_options.m).
% Each case prints one line: what it made (count, Type, Tag, UserData, Parent's Type) or the error.
set(groot, 'DefaultFigureVisible', 'off');
f = figure;
ax = axes(f);
hold(ax, 'on');
x = 1:3; y = [2 1 3]; z = [1 2 1];
Z = [1 2; 3 4];
C = magic(3);
o = {'Tag', 't', 'UserData', 7};

disp('--- makers, axes first, Tag and UserData');
run('plot', @() plot(ax, x, y, o{:}));
run('plot spec', @() plot(ax, x, y, 'r-o', o{:}));
run('plot y', @() plot(ax, y, o{:}));
run('plot y spec', @() plot(ax, y, 'g', o{:}));
run('plot groups', @() plot(ax, x, y, 'r', x, z, 'b', o{:}));
run('plot groups nospec', @() plot(ax, x, y, x, z, o{:}));
run('plot3', @() plot3(ax, x, y, z, o{:}));
run('plot3 spec', @() plot3(ax, x, y, z, 'k:', o{:}));
run('line', @() line(ax, x, y, o{:}));
run('line 3', @() line(ax, x, y, z, o{:}));
run('stairs', @() stairs(ax, x, y, o{:}));
run('stairs spec', @() stairs(ax, x, y, 'r', o{:}));
run('stem', @() stem(ax, x, y, o{:}));
run('stem filled', @() stem(ax, x, y, 'filled', o{:}));
run('stem3', @() stem3(ax, x, y, z, o{:}));
run('scatter', @() scatter(ax, x, y, o{:}));
run('scatter sz c', @() scatter(ax, x, y, 20, 'r', o{:}));
run('scatter filled', @() scatter(ax, x, y, 'filled', o{:}));
run('scatter3', @() scatter3(ax, x, y, z, o{:}));
run('bar', @() bar(ax, y, o{:}));
run('bar xy', @() bar(ax, x, y, o{:}));
run('bar width', @() bar(ax, y, 0.5, o{:}));
run('bar stacked', @() bar(ax, [y; z], 'stacked', o{:}));
run('barh', @() barh(ax, y, o{:}));
run('area', @() area(ax, x, y, o{:}));
run('errorbar', @() errorbar(ax, x, y, z, o{:}));
run('fill', @() fill(ax, x, y, 'r', o{:}));
run('patch', @() patch(ax, x, y, 'r', o{:}));
run('patch c', @() patch(ax, x, y, [1 2 3], o{:}));
run('patch xyz', @() patch(ax, x, y, z, 'r', o{:}));
run('rectangle', @() rectangle(ax, 'Position', [0 0 1 1], o{:}));
run('light', @() light(ax, o{:}));
run('text', @() text(ax, 1, 2, 'hi', o{:}));
run('surface', @() surface(ax, [0 1; 0 1], [0 0; 1 1], Z, o{:}));
run('surf', @() surf(ax, [0 1; 0 1], [0 0; 1 1], Z, o{:}));
run('mesh', @() mesh(ax, [0 1; 0 1], [0 0; 1 1], Z, o{:}));
run('image', @() image(ax, C, o{:}));
run('imagesc', @() imagesc(ax, C, o{:}));
run('contour', @() contour(ax, C, o{:}));
run('contourf', @() contourf(ax, C, o{:}));
run('quiver', @() quiver(ax, x, y, z, y, o{:}));
run('histogram', @() histogram(ax, [1 2 2 3 3 3], o{:}));
run('semilogy', @() semilogy(ax, x, y, o{:}));
run('pcolor', @() pcolor(ax, C, o{:}));

disp('--- makers, no axes, Tag and UserData');
cla(ax);
run('plot', @() plot(x, y, o{:}));
run('line', @() line(x, y, o{:}));
run('scatter', @() scatter(x, y, o{:}));
run('bar', @() bar(y, o{:}));
run('patch', @() patch(x, y, 'r', o{:}));
run('rectangle', @() rectangle('Position', [0 0 1 1], o{:}));
run('light', @() light(o{:}));
run('stairs', @() stairs(x, y, o{:}));
run('stem', @() stem(x, y, o{:}));
run('area', @() area(x, y, o{:}));

disp('--- other properties as options');
cla(ax);
run('plot Visible Clipping', @() plot(ax, x, y, 'Visible', 'off', 'Clipping', 'off', 'Tag', 'v'));
run('plot SeriesIndex', @() plot(ax, x, y, 'SeriesIndex', 3, 'Tag', 'si'));
run('plot XData', @() plot(ax, x, y, 'XData', [5 6 7], 'Tag', 'xd'));
run('scatter MarkerFaceAlpha', @() scatter(ax, x, y, 'MarkerFaceAlpha', 0.5, 'Tag', 'mfa'));
run('bar FaceColor', @() bar(ax, y, 'FaceColor', 'r', 'Tag', 'fc'));
run('bar BarWidth', @() bar(ax, y, 'BarWidth', 0.3, 'Tag', 'bw'));
run('stem BaseValue', @() stem(ax, x, y, 'BaseValue', 1, 'Tag', 'bv'));
run('patch FaceAlpha', @() patch(ax, x, y, 'r', 'FaceAlpha', 0.5, 'Tag', 'fa'));
run('rectangle Curvature', @() rectangle(ax, 'Position', [0 0 1 1], 'Curvature', 1, 'Tag', 'cu'));
run('light Visible', @() light(ax, 'Visible', 'off', 'Tag', 'lv'));
run('line Parent', @() line(x, y, 'Parent', ax, 'Tag', 'lp'));
fprintf('plot SeriesIndex value: %s\n', v2s(get(findobj(ax, 'Tag', 'si'), 'SeriesIndex')));
fprintf('plot XData value: %s\n', v2s(get(findobj(ax, 'Tag', 'xd'), 'XData')));
fprintf('plot Visible Clipping: %s %s\n', get(findobj(ax, 'Tag', 'v'), 'Visible'), get(findobj(ax, 'Tag', 'v'), 'Clipping'));
fprintf('scatter MarkerFaceAlpha: %s\n', v2s(get(findobj(ax, 'Tag', 'mfa'), 'MarkerFaceAlpha')));
fprintf('bar BarWidth: %s\n', v2s(get(findobj(ax, 'Tag', 'bw'), 'BarWidth')));
fprintf('stem BaseValue: %s\n', v2s(get(findobj(ax, 'Tag', 'bv'), 'BaseValue')));
fprintf('patch FaceAlpha: %s\n', v2s(get(findobj(ax, 'Tag', 'fa'), 'FaceAlpha')));
fprintf('rectangle Curvature: %s\n', v2s(get(findobj(ax, 'Tag', 'cu'), 'Curvature')));

disp('--- names that are not right');
run('plot unknown', @() plot(ax, x, y, 'Foo', 1));
run('plot partial', @() plot(ax, x, y, 'Ta', 'pt'));
run('plot partial2', @() plot(ax, x, y, 'UserD', 3));
run('plot ambiguous', @() plot(ax, x, y, 'Mark', 1));
run('plot odd', @() plot(ax, x, y, 'Tag'));
run('plot readonly', @() plot(ax, x, y, 'Type', 'x'));
run('plot bad value', @() plot(ax, x, y, 'Visible', 3));
run('plot spec after', @() plot(ax, x, y, 'Tag', 'a', 'r'));
run('plot group after', @() plot(ax, x, y, 'Tag', 'a', x, y));
run('line unknown', @() line(ax, x, y, 'Foo', 1));
run('scatter unknown', @() scatter(ax, x, y, 'Foo', 1));
run('bar unknown', @() bar(ax, y, 'Foo', 1));
run('patch unknown', @() patch(ax, x, y, 'r', 'Foo', 1));
run('rectangle unknown', @() rectangle(ax, 'Foo', 1));
run('light unknown', @() light(ax, 'Foo', 1));
run('stem unknown', @() stem(ax, x, y, 'Foo', 1));
run('stairs unknown', @() stairs(ax, x, y, 'Foo', 1));

disp('--- a group or a transform first');
cla(ax);
g = hggroup(ax);
t = hgtransform(ax);
run('line g', @() line(g, [0 1], [1 0], 'Tag', 'lg'));
run('line g 3', @() line(g, [0 1], [1 0], [0 0], 'Tag', 'lg3'));
run('line g props', @() line(g, 'XData', [0 1], 'YData', [1 0], 'Tag', 'lgp'));
run('line t', @() line(t, [0 1], [1 0], 'Tag', 'lt'));
run('text g', @() text(g, 1, 2, 'hi', 'Tag', 'tg'));
run('text t 3', @() text(t, 1, 2, 3, 'hi', 'Tag', 'tt'));
run('patch g', @() patch(g, x, y, 'r', 'Tag', 'pg'));
run('patch t', @() patch(t, x, y, 'r', 'Tag', 'pt'));
run('surface g', @() surface(g, [0 1; 0 1], [0 0; 1 1], Z, 'Tag', 'sg'));
run('surface t Z', @() surface(t, Z, 'Tag', 'st'));
run('rectangle g', @() rectangle(g, 'Position', [0 0 1 1], 'Tag', 'rg'));
run('rectangle t', @() rectangle(t, 'Position', [0 0 1 1], 'Tag', 'rt'));
run('image g', @() image(g, C, 'Tag', 'ig'));
run('image t', @() image(t, 'CData', C, 'Tag', 'it'));
run('light g', @() light(g, 'Tag', 'lig'));
run('light t', @() light(t, 'Tag', 'lit'));
run('hggroup g', @() hggroup(g, 'Tag', 'gg'));
run('hgtransform g', @() hgtransform(g, 'Tag', 'tg2'));
run('plot g', @() plot(g, x, y));
run('scatter g', @() scatter(g, x, y));
run('bar g', @() bar(g, y));
run('surf g', @() surf(g, Z));
run('fill g', @() fill(g, x, y, 'r'));
run('imagesc g', @() imagesc(g, C));
run('stairs g', @() stairs(g, x, y));
fprintf('children of g: %d, of t: %d\n', numel(g.Children), numel(t.Children));
disp('done');

function run(name, make)
try
    h = make();
    if isempty(h)
        fprintf('%-26s ok empty\n', name);
        return;
    end
    fprintf('%-26s ok n=%d type=%s tag=%s ud=%s parent=%s\n', name, numel(h), get(h(1), 'Type'), ...
        get(h(1), 'Tag'), v2s(get(h(1), 'UserData')), get(get(h(1), 'Parent'), 'Type'));
catch e
    fprintf('%-26s err %s | %s\n', name, e.identifier, strrep(e.message, newline, ' '));
end
end

function s = v2s(v)
if ischar(v)
    s = ['''' v ''''];
elseif isnumeric(v) || islogical(v)
    s = mat2str(v);
else
    s = ['<' class(v) '>'];
end
end
