function u5w_events
% U5 WINDOW probe (open item 51; needs a display — run only with the user's leave):
% the event data each uifigure component's callback receives, driven by the App Testing
% Framework's gestures, which refuse invisible figures. Records for every event its class and
% every property with its value, who runs and in which order: a button's push, a state button's,
% a checkbox's and a radio pair's press, an edit field's typing (ValueChangingFcn per keystroke,
% then ValueChangedFcn), a numeric field's and a spinner's typing and arrows, a slider's and a
% range slider's drag, a drop-down's opening and choice, a list's click, double click and choice,
% an image's and a hyperlink's click, and a text area's typing.
% Run: run-probe.ps1 -Name u5w_events -WithWindows -TimeoutSec 300
global LOG
LOG = {};
cleanup = onCleanup(@() printLog()); %#ok<NASGU>
tc = matlab.uitest.TestCase.forInteractiveUse;
fig = uifigure('Name', 'u5w_events', 'Position', [200 200 560 520]);
n = @(who) @(s, e) note(who, s, e);

btn = uibutton(fig, 'Text', 'Push', 'Position', [20 470 90 22], 'ButtonPushedFcn', n('btn.ButtonPushedFcn'));
st = uibutton(fig, 'state', 'Text', 'State', 'Position', [130 470 90 22], 'ValueChangedFcn', n('state.ValueChangedFcn'));
cb = uicheckbox(fig, 'Text', 'Check', 'Position', [240 470 90 22], 'ValueChangedFcn', n('check.ValueChangedFcn'));
bg = uibuttongroup(fig, 'Position', [350 420 180 80], 'SelectionChangedFcn', n('group.SelectionChangedFcn'));
r1 = uiradiobutton(bg, 'Text', 'First', 'Position', [10 40 100 22]);
r2 = uiradiobutton(bg, 'Text', 'Second', 'Position', [10 10 100 22]);
ef = uieditfield(fig, 'Position', [20 430 120 22], 'ValueChangedFcn', n('edit.ValueChangedFcn'), 'ValueChangingFcn', n('edit.ValueChangingFcn'));
nf = uieditfield(fig, 'numeric', 'Position', [160 430 90 22], 'ValueChangedFcn', n('num.ValueChangedFcn'));
try
    nf.ValueChangingFcn = n('num.ValueChangingFcn');
    LOG{end + 1} = 'numeric field takes a ValueChangingFcn';
catch e
    LOG{end + 1} = sprintf('numeric ValueChangingFcn refused: %s %s', e.identifier, oneline(e.message));
end
sp = uispinner(fig, 'Position', [270 430 70 22], 'ValueChangedFcn', n('spin.ValueChangedFcn'), 'ValueChangingFcn', n('spin.ValueChangingFcn'));
sl = uislider(fig, 'Position', [20 390 200 3], 'ValueChangedFcn', n('slider.ValueChangedFcn'), 'ValueChangingFcn', n('slider.ValueChangingFcn'));
rs = uislider(fig, 'range', 'Position', [280 390 200 3], 'ValueChangedFcn', n('range.ValueChangedFcn'), 'ValueChangingFcn', n('range.ValueChangingFcn'));
dd = uidropdown(fig, 'Items', {'A', 'B', 'C'}, 'Position', [20 310 90 22], 'ValueChangedFcn', n('drop.ValueChangedFcn'), ...
    'DropDownOpeningFcn', n('drop.DropDownOpeningFcn'), 'ClickedFcn', n('drop.ClickedFcn'));
de = uidropdown(fig, 'Editable', 'on', 'Items', {'red', 'green'}, 'Position', [130 310 90 22], 'ValueChangedFcn', n('dropedit.ValueChangedFcn'));
lb = uilistbox(fig, 'Items', {'one', 'two', 'three', 'four'}, 'Position', [20 190 110 100], ...
    'ValueChangedFcn', n('list.ValueChangedFcn'), 'ClickedFcn', n('list.ClickedFcn'), 'DoubleClickedFcn', n('list.DoubleClickedFcn'));
im = uiimage(fig, 'Position', [160 190 80 60], 'ImageClickedFcn', n('image.ImageClickedFcn'));
hl = uihyperlink(fig, 'Text', 'Link', 'URL', '', 'Position', [260 210 80 22], 'HyperlinkClickedFcn', n('link.HyperlinkClickedFcn'));
ta = uitextarea(fig, 'Position', [360 190 150 100], 'ValueChangedFcn', n('area.ValueChangedFcn'), 'ValueChangingFcn', n('area.ValueChangingFcn'));
drawnow; pause(1.5);

gesture('press button', @() tc.press(btn));
gesture('press state button', @() tc.press(st));
gesture('press state button again', @() tc.press(st));
gesture('press checkbox', @() tc.press(cb));
gesture('press radio 2', @() tc.press(r2));
gesture('press radio 2 again (already selected)', @() tc.press(r2));
gesture('press radio 1', @() tc.press(r1));
gesture('type ab into the edit field', @() tc.type(ef, 'ab'));
gesture('type 42.5 into the numeric field', @() tc.type(nf, 42.5));
gesture('type 7 into the spinner', @() tc.type(sp, 7));
gesture('spinner up (press sp up)', @() tc.press(sp, 'up'));
gesture('spinner down (press sp down)', @() tc.press(sp, 'down'));
gesture('drag the slider 0 to 30', @() tc.drag(sl, 0, 30));
gesture('drag the range slider thumb 20 to 60', @() tc.drag(rs, 20, 60));
gesture('choose B in the dropdown', @() tc.choose(dd, 'B'));
gesture('type mauve into the editable dropdown', @() tc.type(de, 'mauve'));
gesture('choose two in the list', @() tc.choose(lb, 'two'));
gesture('choose item 3 in the list by index', @() tc.choose(lb, 3));
gesture('press the image', @() tc.press(im));
gesture('press the hyperlink', @() tc.press(hl));
gesture('type two lines into the text area', @() tc.type(ta, sprintf('line1\nline2')));

% A double click on the list: no gesture does it, so the robot clicks the second row twice.
% The figure is fronted first and the pointer stays off the keyboard's way.
gesture('front the figure', @() figure(fig));
pause(1);
try
    import java.awt.Robot
    import java.awt.event.InputEvent
    robot = Robot();
    robot.setAutoDelay(30);
    fp = fig.Position;
    scr = get(groot, 'ScreenSize');
    javaScreen = java.awt.Toolkit.getDefaultToolkit().getScreenSize();
    scale = double(javaScreen.width) / scr(3);
    % Second row of the list: 20 px in, two rows (~22 px each) down from its top at y = 290.
    x = fp(1) + 60;
    y = scr(4) - (fp(2) + 290 - 33);
    p = round(scale * [x, y]);
    LOG{end + 1} = '== robot double click on list row 2';
    robot.mouseMove(p(1), p(2)); pause(0.2);
    robot.mousePress(InputEvent.BUTTON1_MASK); robot.mouseRelease(InputEvent.BUTTON1_MASK);
    pause(0.08);
    robot.mousePress(InputEvent.BUTTON1_MASK); robot.mouseRelease(InputEvent.BUTTON1_MASK);
    pause(1); drawnow;
catch e
    LOG{end + 1} = sprintf('ERR robot %s %s', e.identifier, oneline(e.message));
end

delete(fig);
end

function gesture(name, run)
global LOG
LOG{end + 1} = ['== ' name];
try
    run();
    drawnow;
    pause(0.7);
catch e
    LOG{end + 1} = sprintf('ERR %s %s', e.identifier, oneline(e.message));
end
end

function note(who, s, e)
global LOG
line = sprintf('%s: class=%s', who, class(e));
if isprop(s, 'Value')
    line = sprintf('%s s.Value=%s', line, v2s(s.Value));
end
if isobject(e)
    try
        for p = properties(e)'
            if strcmp(p{1}, 'Source'), continue; end
            line = sprintf('%s %s=%s', line, p{1}, v2s(e.(p{1})));
        end
    catch inner
        line = sprintf('%s PROPS-ERR %s', line, inner.message);
    end
elseif isstruct(e)
    line = sprintf('%s struct %s', line, strjoin(fieldnames(e)', ','));
end
LOG{end + 1} = line;
end

function s = v2s(v)
if ischar(v), s = ['''' v ''''];
elseif isstring(v), s = ['"' char(strjoin(v, '|')) '"'];
elseif isnumeric(v) || islogical(v), s = mat2str(v, 8);
elseif iscell(v), s = ['{' strjoin(cellfun(@v2s, v, 'UniformOutput', false), ',') '}'];
elseif isa(v, 'matlab.graphics.Graphics'), s = ['<' class(v) '>'];
elseif isdatetime(v), s = ['dt ' char(string(v))];
elseif isobject(v)
    try
        parts = {};
        for p = properties(v)'
            parts{end + 1} = sprintf('%s=%s', p{1}, v2s(v.(p{1}))); %#ok<AGROW>
        end
        s = ['<' class(v) ' ' strjoin(parts, ' ') '>'];
    catch
        s = ['<' class(v) '>'];
    end
else, s = ['<' class(v) '>'];
end
end

function s = oneline(s)
s = regexprep(strtrim(s), '\s+', ' ');
end

function printLog()
global LOG
fprintf('%s\n', LOG{:});
end
