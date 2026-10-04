function u3w_clicks
% U3 WINDOW probe (needs a display; opens one figure window and clicks in it with java.awt.Robot —
% run only with the user's leave, and with hands off the mouse and keyboard for about a minute).
% Before every click it asks Windows which window is in front and stops unless it is its own figure.
%   1. What a click does to each uicontrol style: the Value its Callback sees, and the event data.
%   2. A button group: SelectionChangedFcn's event data, and whether the button's own Callback runs
%      and in which order; a click on the button already selected.
%   3. ButtonDownFcn: on an inactive control, on a disabled one, and for a right click on an
%      enabled one; and whether WindowButtonDownFcn runs for those.
% Run: run-probe.ps1 -Name u3w_clicks -WithWindows
global LOG
LOG = {};
cleanup = onCleanup(@() printLog()); %#ok<NASGU>
import java.awt.Robot
import java.awt.event.InputEvent
robot = Robot();
robot.setAutoDelay(40);
title = 'u3w_clicks';
here = fileparts(mfilename('fullpath'));

f = figure('Name', title, 'NumberTitle', 'off', 'Position', [300 250 480 440], 'MenuBar', 'none', 'ToolBar', 'none');
set(f, 'WindowButtonDownFcn', @(s, e) note(sprintf('fig.WindowButtonDownFcn SelectionType=%s CurrentObject=%s', f.SelectionType, tagOf(f.CurrentObject)), e), ...
    'ButtonDownFcn', @(s, e) note('fig.ButtonDownFcn', e));
cb = @(s, e) note(sprintf('%s.Callback Value=%s SelectionType=%s gco=%s', s.Tag, mat2str(s.Value), f.SelectionType, tagOf(gco)), e);
bd = @(s, e) note(sprintf('%s.ButtonDownFcn Value=%s SelectionType=%s', s.Tag, mat2str(s.Value), f.SelectionType), e);
uicontrol(f, 'Style', 'togglebutton', 'Tag', 'toggle', 'String', 'Toggle', 'Position', [20 390 100 30], 'Callback', cb, 'ButtonDownFcn', bd);
uicontrol(f, 'Style', 'checkbox', 'Tag', 'check', 'String', 'Check', 'Min', 2, 'Max', 7, 'Value', 2, 'Position', [140 390 100 30], 'Callback', cb, 'ButtonDownFcn', bd);
uicontrol(f, 'Style', 'radiobutton', 'Tag', 'radio', 'String', 'Radio', 'Position', [260 390 100 30], 'Callback', cb, 'ButtonDownFcn', bd);
uicontrol(f, 'Style', 'slider', 'Tag', 'slider', 'Position', [20 350 200 20], 'Callback', cb, 'ButtonDownFcn', bd);
uicontrol(f, 'Style', 'listbox', 'Tag', 'list', 'String', {'one', 'two', 'three', 'four'}, 'Position', [20 230 120 100], 'Callback', cb, 'ButtonDownFcn', bd);
g = uibuttongroup(f, 'Units', 'pixels', 'Position', [260 220 200 150], 'Tag', 'group', 'Title', 'Group', ...
    'SelectionChangedFcn', @(s, e) note(sprintf('group.SelectionChangedFcn Old=%s New=%s selected=%s', tagOf(e.OldValue), tagOf(e.NewValue), tagOf(s.SelectedObject)), e));
uicontrol(g, 'Style', 'radiobutton', 'Tag', 'g1', 'String', 'First', 'Position', [10 100 100 22], 'Callback', cb);
uicontrol(g, 'Style', 'radiobutton', 'Tag', 'g2', 'String', 'Second', 'Position', [10 70 100 22], 'Callback', cb);
uicontrol(g, 'Style', 'togglebutton', 'Tag', 'g3', 'String', 'Third', 'Position', [10 20 100 30], 'Callback', cb);
uicontrol(f, 'Style', 'text', 'Tag', 'inactive', 'String', 'inactive text', 'Enable', 'inactive', 'Position', [20 180 120 20], 'Callback', cb, 'ButtonDownFcn', bd);
uicontrol(f, 'Style', 'pushbutton', 'Tag', 'off', 'String', 'Off', 'Enable', 'off', 'Position', [160 175 100 28], 'Callback', cb, 'ButtonDownFcn', bd);
uicontrol(f, 'Style', 'pushbutton', 'Tag', 'push', 'String', 'Push', 'Position', [20 130 100 28], 'Callback', cb, 'ButtonDownFcn', bd);
uicontrol(f, 'Style', 'text', 'Tag', 'text', 'String', 'enabled text', 'Position', [160 135 100 20], 'Callback', cb, 'ButtonDownFcn', bd);
uicontrol(f, 'Style', 'frame', 'Tag', 'frame', 'Position', [300 130 100 40], 'Callback', cb, 'ButtonDownFcn', bd);
uicontrol(f, 'Style', 'pushbutton', 'Tag', 'inactivepush', 'String', 'Inactive', 'Enable', 'inactive', 'Position', [20 90 100 28], 'Callback', cb, 'ButtonDownFcn', bd);
uicontrol(f, 'Style', 'popupmenu', 'Tag', 'popup', 'String', {'red', 'green', 'blue'}, 'Position', [160 60 120 25], 'Callback', cb, 'ButtonDownFcn', bd);
drawnow; pause(2);
figure(f); drawnow; pause(1);

fp = getpixelposition(f);
scr = get(groot, 'ScreenSize');
javaScreen = java.awt.Toolkit.getDefaultToolkit().getScreenSize();
scale = double(javaScreen.width) / scr(3);
fprintf('screen %s, java %dx%d, scale %.4g, figure %s\n', mat2str(scr), javaScreen.width, javaScreen.height, scale, mat2str(fp));
at = @(x, y) round(scale * [fp(1) + x - 1.5, scr(4) - (fp(2) + y - 1) + 0.5]);
left = InputEvent.BUTTON1_MASK;
right = InputEvent.BUTTON3_MASK;

    function press(what, x, y, button)
        % One click at a figure pixel — only while this figure is the window in front.
        [~, front] = system(sprintf('powershell -NoProfile -ExecutionPolicy Bypass -File "%s"', fullfile(here, 'fgtitle.ps1')));
        front = strtrim(front);
        if ~strcmp(front, title)
            LOG{end + 1} = sprintf('STOPPED before [%s]: the window in front is "%s"', what, front);
            error('u3w:foreground', 'The window in front is "%s", not the probe''s figure; nothing more is clicked.', front);
        end
        LOG{end + 1} = sprintf('--- %s', what);
        p = at(x, y);
        robot.mouseMove(p(1), p(2)); pause(0.15);
        robot.mousePress(button); robot.mouseRelease(button);
        pause(0.9); drawnow;
    end

press('toggle: click', 70, 405, left);
press('toggle: click again', 70, 405, left);
press('checkbox (Min 2 Max 7): click', 150, 405, left);
press('checkbox: click again', 150, 405, left);
press('radio outside a group: click', 270, 405, left);
press('radio: click again', 270, 405, left);
press('slider: right arrow', 212, 360, left);
press('slider: trough right of the thumb', 150, 360, left);
press('list: second row', 60, 303, left);
press('list: third row', 60, 288, left);
press('list: third row again', 60, 288, left);
press('group: second radio', 280, 300, left);
press('group: second radio again', 280, 300, left);
press('group: toggle', 320, 255, left);
press('group: toggle again', 320, 255, left);
press('group: first radio', 280, 330, left);
press('inactive text: click', 60, 190, left);
press('disabled button: click', 210, 189, left);
press('inactive push button: click', 70, 104, left);
press('enabled text: click', 210, 145, left);
press('frame: click', 350, 150, left);
press('enabled push button: right click', 70, 144, right);
press('enabled push button: click', 70, 144, left);
press('figure background: click', 440, 30, left);
% Last, because an open list could swallow a later click.
press('popup: open', 220, 72, left);
press('popup: the row below the box', 220, 40, left);
pause(0.5);
h = findobj(f, 'Type', 'uicontrol');
for k = numel(h):-1:1
    LOG{end + 1} = sprintf('end %s Value=%s', h(k).Tag, mat2str(h(k).Value)); %#ok<AGROW>
end
LOG{end + 1} = sprintf('end group selected=%s', tagOf(g.SelectedObject));
delete(f);
end

function note(what, e)
global LOG
if isempty(e)
    LOG{end + 1} = sprintf('%s | event: []', what);
    return;
end
names = properties(e);
parts = cell(1, numel(names));
for k = 1:numel(names)
    v = e.(names{k});
    if ischar(v), parts{k} = sprintf('%s=''%s''', names{k}, v);
    elseif isnumeric(v), parts{k} = sprintf('%s=%s', names{k}, mat2str(v, 5));
    elseif isempty(v), parts{k} = sprintf('%s=<empty %s>', names{k}, class(v));
    else, parts{k} = sprintf('%s=<%s>', names{k}, class(v));
    end
end
LOG{end + 1} = sprintf('%s | event: %s {%s}', what, class(e), strjoin(parts, ', '));
end

function s = tagOf(h)
if isempty(h), s = '(none)'; else, s = sprintf('%s:%s', get(h, 'Type'), get(h, 'Tag')); end
end

function printLog()
global LOG
for k = 1:numel(LOG)
    fprintf('%s\n', LOG{k});
end
end
