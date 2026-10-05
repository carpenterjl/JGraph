function u5_grid
% U5 probe: uigridlayout - forms, tracks, auto-placement, Layout options, positions once the
% (asynchronous) layout of an invisible uifigure has settled. Headless.
uf = uifigure('Visible', 'off', 'Position', [100 100 600 400]);
warm = uilabel(uf); settle(warm); delete(warm);

%% forms
tryp('form parent', @() class(uigridlayout(uf)));
delete(allchild(uf));
tryp('form parent size', @() tracks(uigridlayout(uf, [3 4])));
tryp('form size row vector 1x1', @() tracks(uigridlayout(uf, [1 1])));
tryp('form size scalar', @() tracks(uigridlayout(uf, 3)));
tryp('form size three', @() tracks(uigridlayout(uf, [1 2 3])));
tryp('form size zero', @() tracks(uigridlayout(uf, [0 2])));
tryp('form size fraction', @() tracks(uigridlayout(uf, [1.5 2])));
tryp('form size negative', @() tracks(uigridlayout(uf, [-1 2])));
tryp('form size text', @() tracks(uigridlayout(uf, 'a')));
tryp('form size column', @() tracks(uigridlayout(uf, [2; 3])));
tryp('form size then pairs', @() tracks(uigridlayout(uf, [2 3], 'RowHeight', {10, 20})));
tryp('form size then pairs mismatch', @() tracks(uigridlayout(uf, [2 3], 'RowHeight', {10, 20, 30})));
tryp('form pairs', @() tracks(uigridlayout(uf, 'ColumnWidth', {'fit', 5})));
tryp('form named parent', @() tracks(uigridlayout('Parent', uf)));
tryp('form odd', @() uigridlayout(uf, 'RowHeight'));
tryp('form unknown', @() uigridlayout(uf, 'Bogus', 1));
tryp('form bad parent number', @() uigridlayout(5.5));
tryp('form parent label', @() uigridlayout(uilabel(uf)));
tryp('form struct', @() tracks(uigridlayout(uf, struct('RowSpacing', 3))));
delete(allchild(uf));
tryp('form second grid in figure', @() two(uf));
delete(allchild(uf));
tryp('form grid in panel', @() class(get(uigridlayout(uipanel(uf)), 'Parent')));
tryp('form grid in grid', @() class(get(uigridlayout(uigridlayout(uf)), 'Parent')));
tryp('form grid in buttongroup', @() class(get(uigridlayout(uibuttongroup(uf)), 'Parent')));
delete(allchild(uf));
f = figure('Visible', 'off');
tryp('form grid in classic figure', @() class(get(uigridlayout(f), 'Parent')));
tryp('form grid in classic panel', @() class(get(uigridlayout(uipanel(f)), 'Parent')));
tryp('form uicontrol in grid', @() get(uicontrol(uigridlayout(uf)), 'Position'));
tryp('form axes in grid', @() class(axes(uigridlayout(uf))));
delete(f); delete(allchild(uf));

%% tracks: what is kept
g = uigridlayout(uf);
vals = {{'1x', 'fit', 40, '2x'}, [30 40], ["fit" "1x"], {'1x'}, '1x', 'fit', 50, {}, [], {'0x'}, {'1.5x'}, {'x'}, {' 2x'}, {'2X'}, {'FIT'}, ...
    {0}, {-5}, {Inf}, {NaN}, {'bogus'}, {1, 'fit'; 2, 3}, {'1x'; '2x'}, {int8(5)}, {true}, {"2x"}, "3x", {'10'}, {'10px'}, {[1 2]}, {single(2.5)}, {'1e1x'}, {'-1x'}, {'.5x'}};
for k = 1:numel(vals)
    tryp(sprintf('RowHeight <- %s', v2s(vals{k})), @() setget(g, 'RowHeight', vals{k}));
    tryp(sprintf('ColumnWidth <- %s', v2s(vals{k})), @() setget(g, 'ColumnWidth', vals{k}));
end
delete(g);
g = uigridlayout(uf);
pv = {5, [1 2], [1 2 3 4], [1 2 3 4]', -1, [1 2 3 -4], 'a', {1, 2, 3, 4}, [], NaN(1, 4), [Inf 1 1 1], int8([1 2 3 4]), true, [1.5 2.5 3.5 4.5]};
for k = 1:numel(pv)
    tryp(sprintf('Padding <- %s', v2s(pv{k})), @() setget(g, 'Padding', pv{k}));
end
sv = {5, 0, -1, 2.5, [1 2], 'a', [], NaN, Inf, int8(4), true};
for k = 1:numel(sv)
    tryp(sprintf('RowSpacing <- %s', v2s(sv{k})), @() setget(g, 'RowSpacing', sv{k}));
    tryp(sprintf('ColumnSpacing <- %s', v2s(sv{k})), @() setget(g, 'ColumnSpacing', sv{k}));
end
delete(g);

%% auto-placement
g = uigridlayout(uf, [2 2]);
c = gobjects(1, 6);
for k = 1:6
    c(k) = uibutton(g, 'Text', sprintf('b%d', k));
    fprintf('auto child %d: Row=%s Column=%s | tracks %s\n', k, mat2str(c(k).Layout.Row), mat2str(c(k).Layout.Column), tracks(g));
end
fprintf('auto children order (Children): %s\n', strjoin(arrayfun(@(h) h.Text, g.Children', 'UniformOutput', false), ' '));
delete(c(2));
n = uibutton(g, 'Text', 'n');
fprintf('after deleting b2, new child: Row=%s Column=%s\n', mat2str(n.Layout.Row), mat2str(n.Layout.Column));
c(1).Layout.Row = 2; c(1).Layout.Column = 2;
n2 = uibutton(g, 'Text', 'n2');
fprintf('after moving b1 to (2,2), new child: Row=%s Column=%s | tracks %s\n', mat2str(n2.Layout.Row), mat2str(n2.Layout.Column), tracks(g));
delete(g);
g = uigridlayout(uf, [2 2]);
a = uibutton(g); a.Layout.Row = [1 2]; a.Layout.Column = [1 2];
b = uibutton(g);
fprintf('after a spanning child, next: Row=%s Column=%s | tracks %s\n', mat2str(b.Layout.Row), mat2str(b.Layout.Column), tracks(g));
delete(g);
g = uigridlayout(uf, [1 3]);
a = uibutton(g); a.Layout.Column = 3;
b = uibutton(g);
fprintf('after first moved to col 3, next: Row=%s Column=%s | tracks %s\n', mat2str(b.Layout.Row), mat2str(b.Layout.Column), tracks(g));
delete(g);
g = uigridlayout(uf, [2 2]);
a = uibutton(g, 'Layout', matlab.ui.layout.GridLayoutOptions('Row', 2, 'Column', 2));
fprintf('Layout in ctor: Row=%s Column=%s\n', mat2str(a.Layout.Row), mat2str(a.Layout.Column));
b = uibutton(g);
fprintf('  next: Row=%s Column=%s\n', mat2str(b.Layout.Row), mat2str(b.Layout.Column));
delete(g);
g = uigridlayout(uf, [2 2]);
p = uipanel(g); ax = uiaxes(g); gg = uigridlayout(g); bgp = uibuttongroup(g);
fprintf('containers placed: panel %s/%s axes %s/%s grid %s/%s group %s/%s\n', mat2str(p.Layout.Row), mat2str(p.Layout.Column), ...
    mat2str(ax.Layout.Row), mat2str(ax.Layout.Column), mat2str(gg.Layout.Row), mat2str(gg.Layout.Column), mat2str(bgp.Layout.Row), mat2str(bgp.Layout.Column));
delete(g);

%% Layout options
g = uigridlayout(uf, [3 3]);
b = uibutton(g);
fprintf('Layout class: %s | fields: %s\n', class(b.Layout), strjoin(fieldnames(b.Layout)', ' '));
fprintf('Layout disp props: %s\n', strjoin(properties(b.Layout)', ' '));
lv = {2, [1 2], [2 3], [2 1], [1 3], 0, -1, 1.5, 5, [1 5], [1 2 3], 'a', [], {1}, true, int8(2), NaN, Inf, [2 2], single(3), "2", [0 1]};
for k = 1:numel(lv)
    tryp(sprintf('Layout.Row <- %s', v2s(lv{k})), @() setrow(b, lv{k}));
    fprintf('   tracks %s\n', tracks(g));
    g.RowHeight = {'1x', '1x', '1x'};
    tryp(sprintf('Layout.Column <- %s', v2s(lv{k})), @() setcol(b, lv{k}));
    g.ColumnWidth = {'1x', '1x', '1x'};
end
tryp('Layout <- options object', @() setlayout(b, matlab.ui.layout.GridLayoutOptions('Row', 3, 'Column', [1 2])));
tryp('Layout <- struct', @() setlayout(b, struct('Row', 1, 'Column', 1)));
tryp('Layout <- []', @() setlayout(b, []));
tryp('Layout <- number', @() setlayout(b, 5));
tryp('Layout <- LayoutOptions.empty', @() setlayout(b, matlab.ui.layout.LayoutOptions.empty));
tryp('GridLayoutOptions()', @() showopt(matlab.ui.layout.GridLayoutOptions()));
tryp('GridLayoutOptions bad name', @() matlab.ui.layout.GridLayoutOptions('Bogus', 1));
tryp('GridLayoutOptions Row 0', @() matlab.ui.layout.GridLayoutOptions('Row', 0));
lone = uibutton(uf);
fprintf('non-grid child Layout: class %s size %s\n', class(lone.Layout), mat2str(size(lone.Layout)));
tryp('non-grid child Layout <- options', @() setlayout(lone, matlab.ui.layout.GridLayoutOptions('Row', 2, 'Column', 2)));
tryp('non-grid child Layout.Row <- 2', @() setrow(lone, 2));
lone.Parent = g;
fprintf('moved into grid: Row=%s Column=%s\n', mat2str(lone.Layout.Row), mat2str(lone.Layout.Column));
lone.Parent = uf;
fprintf('moved out of grid: class %s size %s | Position %s\n', class(lone.Layout), mat2str(size(lone.Layout)), mat2str(lone.Position));
g2 = uigridlayout(uipanel(uf), [1 1]);
b.Layout.Row = 3; b.Layout.Column = 3;
b.Parent = g2;
fprintf('moved to another grid from (3,3): Row=%s Column=%s | tracks %s\n', mat2str(b.Layout.Row), mat2str(b.Layout.Column), tracks(g2));
delete(allchild(uf));

%% shrinking tracks under children
g = uigridlayout(uf, [3 3]);
b = uibutton(g); b.Layout.Row = 3; b.Layout.Column = 3;
g.RowHeight = {'1x'}; g.ColumnWidth = {'1x', '1x'};
fprintf('tracks cut under a child at (3,3): Row=%s Column=%s | tracks %s\n', mat2str(b.Layout.Row), mat2str(b.Layout.Column), tracks(g));
settle(b);
fprintf('   its Position %s Visible %s\n', mat2str(b.Position, 8), char(b.Visible));
delete(g);

%% positions
g = uigridlayout(uf);
settle(uilabel(g));
fprintf('grid in 600x400 uifigure: Position %s Inner %s Outer %s\n', mat2str(g.Position), mat2str(g.InnerPosition), mat2str(g.OuterPosition));
tryp('grid Position <- [10 10 50 50]', @() setget(g, 'Position', [10 10 50 50]));
[m, id] = lastwarn; fprintf('   lastwarn %s | %s\n', id, oneline(m));
delete(g);

g = uigridlayout(uf, [3 3], 'RowHeight', {'1x', '2x', 50}, 'ColumnWidth', {100, '1x', 'fit'});
h = gobjects(1, 9);
for k = 1:9, h(k) = uibutton(g, 'Text', sprintf('button %d', k)); end
settle(h(9));
for k = 1:9, fprintf('3x3 mixed child %d: %s\n', k, mat2str(h(k).Position, 8)); end
h(1).Layout.Column = [1 3]; delete(h(2)); delete(h(3)); settle(h(1));
fprintf('span cols 1-3: %s\n', mat2str(h(1).Position, 8));
h(4).Layout.Row = [2 3]; delete(h(7)); settle(h(4));
fprintf('span rows 2-3: %s\n', mat2str(h(4).Position, 8));
h(9).Visible = 'off'; settle(h(8)); pause(0.5); drawnow;
fprintf('with the only fit-column child hidden: child 6 %s child 9 %s\n', mat2str(h(6).Position, 8), mat2str(h(9).Position, 8));
delete(h(9)); delete(h(6)); settle(h(8)); pause(0.5); drawnow;
fprintf('with the fit column empty: child 5 %s child 8 %s\n', mat2str(h(5).Position, 8), mat2str(h(8).Position, 8));
delete(g);

% overflow: fixed tracks larger than the figure
g = uigridlayout(uf, [2 2], 'RowHeight', {300, 300}, 'ColumnWidth', {400, '1x'});
h = gobjects(1, 4); for k = 1:4, h(k) = uibutton(g); end
settle(h(4));
for k = 1:4, fprintf('overflow child %d: %s\n', k, mat2str(h(k).Position, 8)); end
g.Scrollable = 'on'; pause(0.5); drawnow; pause(0.3);
for k = 1:4, fprintf('overflow scrollable child %d: %s\n', k, mat2str(h(k).Position, 8)); end
delete(g);

% two children in one cell, and an empty grid's fit row
g = uigridlayout(uf, [2 1], 'RowHeight', {'fit', '1x'});
a = uibutton(g); b = uilabel(g, 'Text', {'one', 'two', 'three'}); b.Layout.Row = 1;
c = uibutton(g); c.Layout.Row = 2;
settle(c);
fprintf('two in one fit cell: button %s label %s below %s\n', mat2str(a.Position, 8), mat2str(b.Position, 8), mat2str(c.Position, 8));
delete(g);

% fit row holding a child spanning two rows
g = uigridlayout(uf, [3 1], 'RowHeight', {'fit', 'fit', '1x'});
a = uilistbox(g); a.Layout.Row = [1 2];
c = uibutton(g); c.Layout.Row = 3;
settle(c);
fprintf('span over two fit rows: list %s below %s\n', mat2str(a.Position, 8), mat2str(c.Position, 8));
delete(g);

% nested: a fit row holding a grid, and a panel with a grid
g = uigridlayout(uf, [2 1], 'RowHeight', {'fit', '1x'}, 'ColumnWidth', {'fit'});
inner = uigridlayout(g, [1 2], 'ColumnWidth', {'fit', 'fit'}, 'RowHeight', {'fit'});
x = uilabel(inner, 'Text', 'Name'); y = uieditfield(inner);
z = uibutton(g);
settle(z);
fprintf('nested fit grid: inner %s label %s field %s below %s\n', mat2str(inner.Position, 8), mat2str(x.Position, 8), mat2str(y.Position, 8), mat2str(z.Position, 8));
delete(g);
g = uigridlayout(uf, [1 2], 'ColumnWidth', {'fit', '1x'});
p = uipanel(g, 'Title', 'Design');
pg = uigridlayout(p, [2 1], 'RowHeight', {'fit', 'fit'});
x = uilabel(pg, 'Text', 'Type'); y = uidropdown(pg);
z = uibutton(g);
settle(z);
fprintf('panel in fit column: panel %s grid %s label %s dropdown %s right %s\n', mat2str(p.Position, 8), mat2str(pg.Position, 8), mat2str(x.Position, 8), mat2str(y.Position, 8), mat2str(z.Position, 8));
fprintf('   panel inner %s\n', mat2str(p.InnerPosition, 8));
delete(g);

% uiaxes in a grid
g = uigridlayout(uf, [1 2]);
ax = uiaxes(g); z = uibutton(g);
settle(z); pause(1); drawnow;
fprintf('uiaxes in grid: Position %s Outer %s Units %s\n', mat2str(ax.Position, 8), mat2str(ax.OuterPosition, 8), ax.Units);
tryp('uiaxes in grid Position <-', @() setget(ax, 'Position', [1 1 50 50]));
[m, id] = lastwarn; fprintf('   lastwarn %s | %s\n', id, oneline(m));
delete(g);

% a grid in a panel, and in a classic figure
p = uipanel(uf, 'Position', [20 20 300 200], 'Title', 'T');
g = uigridlayout(p, [1 1]); b = uibutton(g); settle(b);
fprintf('grid in titled 300x200 panel: grid %s button %s\n', mat2str(g.Position, 8), mat2str(b.Position, 8));
delete(p);

%% a grid child's Position, Visible and the like
g = uigridlayout(uf, [1 1]); b = uibutton(g); settle(b);
lastwarn('');
tryp('child Position <-', @() setget(b, 'Position', [5 5 50 50]));
[m, id] = lastwarn; fprintf('   lastwarn %s | %s\n', id, oneline(m));
lastwarn('');
tryp('child InnerPosition <-', @() setget(b, 'InnerPosition', [5 5 50 50]));
[m, id] = lastwarn; fprintf('   lastwarn %s | %s\n', id, oneline(m));
delete(g);

%% fit sizes of text, to calibrate the measure
g = uigridlayout(uf, [1 1], 'RowHeight', {'fit'}, 'ColumnWidth', {'fit'});
texts = {'a', 'W', 'Label', 'iiii', 'MMMM', 'The quick brown fox', '0123456789', ' ', ''};
for k = 1:numel(texts)
    for fs = [12 20]
        for bold = 0:1
            l = uilabel(g, 'Text', texts{k}, 'FontSize', fs);
            if bold, l.FontWeight = 'bold'; end
            p = settle(l);
            fprintf('fit label [%s] size %d bold %d: %s\n', texts{k}, fs, bold, mat2str(p(3:4), 8));
            delete(l);
        end
    end
end
fonts = {'Arial', 'Courier New', 'Times New Roman', 'Segoe UI'};
for k = 1:numel(fonts)
    l = uilabel(g, 'Text', 'The quick brown fox', 'FontName', fonts{k}); p = settle(l);
    fprintf('fit label in %s: %s\n', fonts{k}, mat2str(p(3:4), 8));
    delete(l);
end
comps = u5_makers();
for k = 1:size(comps, 1)
    if any(strcmp(comps{k, 1}, {'RadioButton', 'ToggleButton'})), continue; end
    for fs = [12 20]
        c = comps{k, 2}(g);
        try, c.FontSize = fs; catch, end %#ok<NOCOM>
        p = settle(c);
        fprintf('fit %s size %d: %s\n', comps{k, 1}, fs, mat2str(p(3:4), 8));
        delete(c);
    end
end
b = uibutton(g, 'Text', 'A much longer button text'); p = settle(b); fprintf('fit long button: %s\n', mat2str(p(3:4), 8)); delete(b);
b = uicheckbox(g, 'Text', 'A much longer check box'); p = settle(b); fprintf('fit long checkbox: %s\n', mat2str(p(3:4), 8)); delete(b);
b = uidropdown(g, 'Items', {'Short', 'A much longer item text'}); p = settle(b); fprintf('fit long dropdown: %s\n', mat2str(p(3:4), 8)); delete(b);
b = uieditfield(g, 'Value', 'Some text typed in'); p = settle(b); fprintf('fit edit with text: %s\n', mat2str(p(3:4), 8)); delete(b);
b = uilistbox(g, 'Items', {'A much longer item text', 'b'}); p = settle(b); fprintf('fit long listbox: %s\n', mat2str(p(3:4), 8)); delete(b);
b = uilabel(g, 'Text', {'two', 'lines'}); p = settle(b); fprintf('fit two-line label: %s\n', mat2str(p(3:4), 8)); delete(b);
b = uibutton(g, 'Text', {'two', 'lines'}); p = settle(b); fprintf('fit two-line button: %s\n', mat2str(p(3:4), 8)); delete(b);
b = uihyperlink(g, 'Text', 'A much longer link text'); p = settle(b); fprintf('fit long hyperlink: %s\n', mat2str(p(3:4), 8)); delete(b);
b = uibutton(g, 'state', 'Text', 'Longer state button'); p = settle(b); fprintf('fit long state button: %s\n', mat2str(p(3:4), 8)); delete(b);
delete(uf);
end

function s = tracks(g)
s = sprintf('R{%s} C{%s}', one(g.RowHeight), one(g.ColumnWidth));
end

function s = one(c)
parts = cellfun(@(v) char(string(v)), c, 'UniformOutput', false);
s = strjoin(parts, ',');
end

function s = two(uf)
a = uigridlayout(uf); b = uigridlayout(uf);
s = sprintf('%d children, valid %d %d', numel(uf.Children), isvalid(a), isvalid(b));
end

function v = setget(h, name, value)
set(h, name, value);
v = get(h, name);
end

function v = setrow(b, value)
b.Layout.Row = value;
v = sprintf('Row=%s Column=%s', mat2str(b.Layout.Row), mat2str(b.Layout.Column));
end

function v = setcol(b, value)
b.Layout.Column = value;
v = sprintf('Row=%s Column=%s', mat2str(b.Layout.Row), mat2str(b.Layout.Column));
end

function v = setlayout(b, value)
b.Layout = value;
v = showopt(b.Layout);
end

function v = showopt(o)
if isempty(o)
    v = sprintf('%s empty %s', class(o), mat2str(size(o)));
else
    v = sprintf('%s Row=%s Column=%s', class(o), mat2str(o.Row), mat2str(o.Column));
end
end

function p = settle(h)
% Waits for the asynchronous layout: until the position has been the same for three looks.
p0 = h.Position; same = 0; t = tic;
while toc(t) < 8 && same < 4
    drawnow; pause(0.15);
    p = h.Position;
    if isequal(p, p0), same = same + 1; else, same = 0; p0 = p; end
end
p = h.Position;
end
