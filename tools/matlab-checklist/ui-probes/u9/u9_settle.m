function u9_settle
% U9 probe (open item 64): what the view settles for a knob, a gauge, a discrete knob and a
% switch — whether the tick counts depend on the component's size, the OuterPosition each kind
% answers at a fixed Position under different labels and fonts, and the warning a Position write
% with the wrong aspect raises. Headless, as u9_behave's TICKS section was: the view settles
% without a window.
uf = uifigure('Position', [80 80 900 620]);
warm = uiknob(uf);
drawnow; pause(2); delete(warm);

%% 1. Do the settled ticks depend on the size? (u9_behave recorded one size per range)
for sz = [20 30 40 60 90 120 200]
    h = uiknob(uf, 'Position', [100 100 sz sz]);
    settle(2);
    show(sprintf('SIZETICKS knob sz=%d lim=[0 100]', sz), h);
    delete(h);
end
for sz = [20 40 60 120]
    h = uiknob(uf, 'Position', [100 100 sz sz], 'Limits', [0 1000]);
    settle(2);
    show(sprintf('SIZETICKS knob sz=%d lim=[0 1000]', sz), h);
    delete(h);
end
gauges = {'gauge', {}; 'ninety', {'ninetydegree'}; 'semi', {'semicircular'}; 'linear', {'linear'}};
for q = 1:size(gauges, 1)
    for sz = [40 80 120 240]
        h = uigauge(uf, gauges{q, 2}{:});
        h.Position = [100 100 sz max(20, round(sz * heightRatio(gauges{q, 1})))];
        settle(2);
        show(sprintf('SIZETICKS %s sz=%d lim=[0 100]', gauges{q, 1}, sz), h);
        delete(h);
    end
end

%% 2. More ranges for the tick rule (u9_behave holds 36; these probe its seams)
more = {[0 2000], [0 5000], [0 1e4], [0 13], [0 17], [0 20], [0 33], [0 70], [0 90], [0 99], ...
    [0 101], [0 365], [3 300], [12 24], [0.1 0.9], [1 1.5], [-0.5 0.5], [1e-4 1e-3], [0 12345], [20 20000]};
kinds = {'knob', @() uiknob(uf); 'gauge', @() uigauge(uf); 'linear', @() uigauge(uf, 'linear'); ...
    'ninety', @() uigauge(uf, 'ninetydegree'); 'semi', @() uigauge(uf, 'semicircular')};
for q = 1:size(kinds, 1)
    for r = 1:numel(more)
        h = kinds{q, 2}();
        h.Limits = more{r};
        settle(1);
        show(sprintf('TICKS %s %s', kinds{q, 1}, mat2str(more{r})), h);
        delete(h);
    end
end

%% 3. OuterPosition at a fixed Position: the view's text measurement
outer('kn default', uiknob(uf, 'Position', [200 200 60 60]));
outer('kn long labels', uiknob(uf, 'Position', [200 200 60 60], 'MajorTickLabels', {'Minimum', 'Maximum', 'Middle', 'x', 'y', 'z'}));
outer('kn Limits [0 1000]', uiknob(uf, 'Position', [200 200 60 60], 'Limits', [0 1000]));
outer('kn Limits [0 1e6]', uiknob(uf, 'Position', [200 200 60 60], 'Limits', [0 1e6]));
outer('kn FontSize 8', uiknob(uf, 'Position', [200 200 60 60], 'FontSize', 8));
outer('kn FontSize 20', uiknob(uf, 'Position', [200 200 60 60], 'FontSize', 20));
outer('kn sz 30', uiknob(uf, 'Position', [200 200 30 30]));
outer('kn sz 120', uiknob(uf, 'Position', [200 200 120 120]));
outer('kn no labels', uiknob(uf, 'Position', [200 200 60 60], 'MajorTickLabels', {}));
outer('kn no ticks', uiknob(uf, 'Position', [200 200 60 60], 'MajorTicks', []));
outer('dk default', uiknob(uf, 'discrete', 'Position', [200 200 60 60]));
outer('dk long items', uiknob(uf, 'discrete', 'Position', [200 200 60 60], 'Items', {'Alpha', 'Beta', 'Gamma', 'Delta Epsilon'}));
outer('dk 2 items', uiknob(uf, 'discrete', 'Position', [200 200 60 60], 'Items', {'a', 'b'}));
outer('dk FontSize 20', uiknob(uf, 'discrete', 'Position', [200 200 60 60], 'FontSize', 20));
outer('sw default', uiswitch(uf, 'Position', [200 200 45 20]));
outer('sw long', uiswitch(uf, 'Position', [200 200 45 20], 'Items', {'Stopped', 'Running'}));
outer('sw FontSize 20', uiswitch(uf, 'Position', [200 200 45 20], 'FontSize', 20));
outer('sw vertical', uiswitch(uf, 'Position', [200 200 20 45], 'Orientation', 'vertical'));
outer('ro default', uiswitch(uf, 'rocker', 'Position', [200 200 20 45]));
outer('ro horizontal', uiswitch(uf, 'rocker', 'Position', [200 200 45 20], 'Orientation', 'horizontal'));
outer('to default', uiswitch(uf, 'toggle', 'Position', [200 200 20 45]));

%% 4. The aspect warning a Position write raises, and what is stored
aspect('kn Position <- [10 10 100 30]', @() uiknob(uf), [10 10 100 30]);
aspect('kn Position <- [10 10 30 100]', @() uiknob(uf), [10 10 30 100]);
aspect('kn InnerPosition <- [10 10 100 30]', @() uiknob(uf), [10 10 100 30], 'InnerPosition');
aspect('dk Position <- [10 10 100 30]', @() uiknob(uf, 'discrete'), [10 10 100 30]);
aspect('sw Position <- [10 10 100 10]', @() uiswitch(uf), [10 10 100 10]);
aspect('sw Position <- [10 10 10 100]', @() uiswitch(uf), [10 10 10 100]);
aspect('ro Position <- [10 10 100 10]', @() uiswitch(uf, 'rocker'), [10 10 100 10]);
aspect('ga Position <- [10 10 100 30]', @() uigauge(uf), [10 10 100 30]);
aspect('ninety Position <- [10 10 100 30]', @() uigauge(uf, 'ninetydegree'), [10 10 100 30]);
aspect('semi Position <- [10 10 100 30]', @() uigauge(uf, 'semicircular'), [10 10 100 30]);
aspect('lamp Position <- [10 10 100 30]', @() uilamp(uf), [10 10 100 30]);
delete(uf);
end

function r = heightRatio(kind)
switch kind
    case 'gauge', r = 1;
    case 'ninety', r = 1;
    case 'semi', r = 65 / 120;
    case 'linear', r = 40 / 120;
end
end

function show(label, h)
fprintf('%s : major=%s labels={%s} minorN=%d minorFirst=%s\n', label, mat2str(h.MajorTicks, 10), ...
    strjoin(h.MajorTickLabels, ','), numel(h.MinorTicks), mat2str(h.MinorTicks(1:min(4, end)), 10));
end

function outer(label, h)
settle(1.5);
fprintf('OUTER %s : Position=%s Inner=%s Outer=%s\n', label, mat2str(h.Position, 8), mat2str(h.InnerPosition, 8), mat2str(h.OuterPosition, 8));
delete(h);
end

function aspect(label, make, rect, name)
if nargin < 4
    name = 'Position';
end
h = make();
settle(0.5);
lastwarn('', '');
try
    set(h, name, rect);
    drawnow;
    [msg, id] = lastwarn;
    settle(1);
    fprintf('ASPECT %s : warn id=[%s] msg=[%s] Position=%s Outer=%s\n', label, id, strtrim(msg), mat2str(h.Position, 8), mat2str(h.OuterPosition, 8));
catch e
    fprintf('ASPECT %s : ERR %s | %s\n', label, e.identifier, regexprep(strtrim(e.message), '\s+', ' '));
end
delete(h);
end

function settle(seconds)
drawnow;
t0 = tic;
while toc(t0) < seconds
    drawnow;
    pause(0.2);
end
end
