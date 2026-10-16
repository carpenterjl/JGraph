function u5_ticks2
% U5 probe (open item 49): the automatic MajorTicks a uislider settles on, over many Limits and
% track widths. Headless: the view runs and re-ticks without a window (u5_forms recorded settled
% ticks under -noFigureWindows). One batch per range holds a slider of every width; each batch
% waits a fixed three seconds after drawnow before reading.
ranges = {
    [0 100], [0 1], [-1000 1000], [0 7], [0.001 0.002], [0.01 0.99], [1 12], [0 1e6], [-5 5], ...
    [0 255], [20 20000], [0 50], [30 50], [0 3], [0 10], [0 15], [0 25], [0 40], [0 60], ...
    [0 150], [0 300], [0 500], [0 1000], [-1 1], [5 6], [0 0.5], [100 200], [-100 0], ...
    [0 12345], [0.5 2.5], [0 2], [0 4], [0 5], [0 6], [0 8], [0 9], [0 11], [0 13], [0 17], ...
    [0 20], [0 24], [0 30], [0 33], [0 70], [0 90], [0 99], [0 101], [0 120], [0 200], ...
    [0 365], [0 2000], [0 5000], [0 1e4], [0 1e5], [0 1e7], [1e6 2e6], [-1e6 0], [0.1 0.9], ...
    [0.2 0.8], [1 1.5], [2 3], [7 11], [-7 3], [-0.5 0.5], [1e-4 1e-3], [1 100], [3 300], [12 24]};
widths = [40 60 80 100 120 150 200 250 300 400 600 800];

uf = fresh([]);
for r = 1:numel(ranges)
    if mod(r, 20) == 0
        uf = fresh(uf);
    end
    lim = ranges{r};
    hs = cell(1, numel(widths));
    for k = 1:numel(widths)
        hs{k} = uislider(uf, 'Position', [10, 10 + 42 * k, widths(k), 3], 'Limits', lim);
    end
    stable(hs);
    for k = 1:numel(widths)
        show(sprintf('TICKS lim=%s w=%d', mat2str(lim), widths(k)), hs{k});
        delete(hs{k});
    end
end

% Vertical sliders: the track is the height.
uf = fresh(uf);
heights = [60 100 150 300 500];
vranges = {[0 100], [0 1], [1 12], [0 1e6], [0 7], [0 255]};
for r = 1:numel(vranges)
    hs = cell(1, numel(heights));
    for k = 1:numel(heights)
        hs{k} = uislider(uf, 'Orientation', 'vertical', 'Position', [10 + 60 * k, 10, 3, heights(k)], 'Limits', vranges{r});
    end
    stable(hs);
    for k = 1:numel(heights)
        show(sprintf('VTICKS lim=%s h=%d', mat2str(vranges{r}), heights(k)), hs{k});
        delete(hs{k});
    end
end

% A range slider's automatic ticks, for a cross-check with the plain slider's.
for lim = {[0 100], [1 12], [0 1e6]}
    for w = [100 150 300]
        s = uislider(uf, 'range', 'Position', [10 60 w 3], 'Limits', lim{1});
        stable({s});
        show(sprintf('RTICKS lim=%s w=%d', mat2str(lim{1}), w), s);
        delete(s);
    end
end
delete(uf);
end

function show(label, s)
fprintf('%s : major=%s labels={%s} minorN=%d minorFirst=%s\n', label, mat2str(s.MajorTicks, 10), ...
    strjoin(s.MajorTickLabels, ','), numel(s.MinorTicks), mat2str(s.MinorTicks(1:min(4, end)), 10));
end

function stable(hs)
% Waits until every slider's ticks have stopped changing for 1.5 s (the view re-ticks in
% stages), up to 15 s.
drawnow;
t0 = tic;
last = snapshot(hs);
quiet = tic;
while toc(t0) < 15 && toc(quiet) < 1.5
    pause(0.25);
    drawnow;
    now_ = snapshot(hs);
    if ~isequal(now_, last)
        last = now_;
        quiet = tic;
    end
end
end

function s = snapshot(hs)
s = cellfun(@(h) {h.MajorTicks, h.MinorTicks}, hs, 'UniformOutput', false);
end

function uf = fresh(old)
if ~isempty(old)
    delete(old);
end
uf = uifigure('Position', [80 80 900 620]);
warm = uislider(uf, 'Position', [10 10 150 3]);
drawnow;
pause(2);
delete(warm);
end
