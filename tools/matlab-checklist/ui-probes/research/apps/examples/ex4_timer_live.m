% EX4_TIMER_LIVE  Timer-driven live plot: a fixedRate timer appends samples to a
% line while the script blocks in waitfor(fig). A toggle pauses, the close
% request stops and deletes the timer. Typical of DAQ / serial "scope" labs.

fig = figure('Name', 'Live signal', 'NumberTitle', 'off', 'Position', [300 300 700 400]);
ax = axes(fig, 'Position', [0.08 0.2 0.88 0.72]);
hLine = animatedline(ax, 'Color', [0 0.45 0.74], 'MaximumNumPoints', 400);
ax.XLim = [0 10];
ax.YLim = [-1.5 1.5];
xlabel(ax, 't (s)');
grid(ax, 'on');

pauseBtn = uicontrol(fig, 'Style', 'togglebutton', 'String', 'Pause', ...
    'Units', 'normalized', 'Position', [0.08 0.03 0.15 0.08]);
rateTxt = uicontrol(fig, 'Style', 'text', 'String', '', ...
    'Units', 'normalized', 'Position', [0.30 0.03 0.40 0.06]);

state.t0 = tic;
state.n = 0;
fig.UserData = state;

tmr = timer('ExecutionMode', 'fixedRate', 'Period', 0.05, 'BusyMode', 'drop', ...
    'TimerFcn', @(t, ~) tick(t, fig, ax, hLine, pauseBtn, rateTxt), ...
    'ErrorFcn', @(t, evt) fprintf(2, 'timer error: %s\n', evt.Data.message), ...
    'Name', 'liveSignalTimer');

fig.CloseRequestFcn = @(src, ~) shutdown(src, tmr);
start(tmr);

waitfor(fig);                         % script stays here until the window is closed
disp('Live view closed; timer stopped and deleted.');

function tick(t, fig, ax, hLine, pauseBtn, rateTxt)
if ~isvalid(fig) || pauseBtn.Value == 1
    return
end
s = fig.UserData;
tt = toc(s.t0);
s.n = s.n + 1;
addpoints(hLine, tt, sin(2*pi*0.5*tt) + 0.2*randn);
if tt > ax.XLim(2)
    ax.XLim = ax.XLim + 5;
end
rateTxt.String = sprintf('%d samples, %.1f /s (tasks %d)', s.n, s.n/tt, t.TasksExecuted);
fig.UserData = s;
drawnow limitrate
end

function shutdown(fig, tmr)
if isvalid(tmr)
    stop(tmr);
    delete(tmr);
end
delete(fig);
end
