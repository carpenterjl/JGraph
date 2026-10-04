% EX5_WAITFOR_VALUE  Two blocking idioms in one script:
%  (1) a polling loop that runs until a Stop toggle is pressed (drawnow lets the
%      button's state change get processed), and
%  (2) waitfor(obj, 'Value', v) to block until a control reaches a value.
% Both appear constantly in measurement/control lab scripts.

fig = figure('Name', 'Acquire', 'NumberTitle', 'off', 'Position', [300 300 500 300]);
ax = axes(fig, 'Position', [0.1 0.3 0.85 0.6]);
h = plot(ax, NaN, NaN, '.-');
stopBtn = uicontrol(fig, 'Style', 'togglebutton', 'String', 'Stop', ...
    'Units', 'normalized', 'Position', [0.1 0.05 0.2 0.1]);
goBtn = uicontrol(fig, 'Style', 'togglebutton', 'String', 'Save && exit', ...
    'Units', 'normalized', 'Position', [0.4 0.05 0.3 0.1], 'Enable', 'off');

% (1) poll until Stop
k = 0;
y = [];
while ishghandle(stopBtn) && get(stopBtn, 'Value') == 0
    k = k + 1;
    y(end+1) = sum(rand(1, 12)) - 6; %#ok<SAGROW>
    set(h, 'XData', 1:k, 'YData', y);
    drawnow                                  % processes the toggle's click
    pause(0.02);
end

if ~ishghandle(fig)
    return
end
set(stopBtn, 'Enable', 'off');
set(goBtn, 'Enable', 'on');
title(ax, sprintf('%d samples, mean %.3f - press Save & exit', k, mean(y)));

% (2) block until the second toggle reaches Value == 1 (or the figure closes)
waitfor(goBtn, 'Value', 1);

if ishghandle(fig)
    save(fullfile(tempdir, 'acquire_result.mat'), 'y');
    close(fig);
end
