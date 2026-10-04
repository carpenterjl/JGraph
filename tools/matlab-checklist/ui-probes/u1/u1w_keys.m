function u1w_keys
% U1 WINDOW probe (needs a display; opens figure windows and types into them with java.awt.Robot —
% run only with the user's leave, and with hands off the keyboard and mouse for about 30 s).
%   1. Who hears a key while a uicontrol edit field has the keyboard, and in which order:
%      the component's KeyPressFcn, the figure's KeyPressFcn, the figure's WindowKeyPressFcn.
%   2. The same with the figure itself holding the keyboard.
%   3. The event data a key press, a window button press and a wheel turn carry (class, EventName,
%      fields), and what a click on a pushbutton hands its Callback.
%   4. When an edit field's Callback fires: on Enter, and on a click elsewhere after typing.
global LOG
LOG = {};
cleanup = onCleanup(@() printLog()); %#ok<NASGU>
import java.awt.Robot
import java.awt.event.KeyEvent
import java.awt.event.InputEvent
robot = Robot();
robot.setAutoDelay(40);

f = figure('Name', 'u1w_keys', 'NumberTitle', 'off', 'Position', [300 300 420 260], 'MenuBar', 'none', 'ToolBar', 'none');
e1 = uicontrol(f, 'Style', 'edit', 'Position', [20 200 160 26], 'Tag', 'e1');
e2 = uicontrol(f, 'Style', 'edit', 'Position', [200 200 160 26], 'Tag', 'e2');
b = uicontrol(f, 'Style', 'pushbutton', 'String', 'Go', 'Position', [20 140 100 30], 'Tag', 'b');
t = uicontrol(f, 'Style', 'text', 'String', 'label', 'Position', [20 90 100 20], 'Tag', 't');
set(e1, 'KeyPressFcn', @(s, e) note('e1.KeyPressFcn', e), 'KeyReleaseFcn', @(s, e) note('e1.KeyReleaseFcn', e), ...
    'Callback', @(s, e) note(sprintf('e1.Callback String=[%s]', s.String), e));
set(e2, 'Callback', @(s, e) note(sprintf('e2.Callback String=[%s]', s.String), e));
set(b, 'Callback', @(s, e) note('b.Callback', e), 'KeyPressFcn', @(s, e) note('b.KeyPressFcn', e));
set(f, 'KeyPressFcn', @(s, e) note('fig.KeyPressFcn', e), 'KeyReleaseFcn', @(s, e) note('fig.KeyReleaseFcn', e), ...
    'WindowKeyPressFcn', @(s, e) note('fig.WindowKeyPressFcn', e), 'WindowKeyReleaseFcn', @(s, e) note('fig.WindowKeyReleaseFcn', e), ...
    'WindowButtonDownFcn', @(s, e) note(sprintf('fig.WindowButtonDownFcn SelectionType=%s CurrentObject=%s', f.SelectionType, typeOf(f.CurrentObject)), e), ...
    'WindowButtonUpFcn', @(s, e) note('fig.WindowButtonUpFcn', e), ...
    'WindowScrollWheelFcn', @(s, e) note('fig.WindowScrollWheelFcn', e), ...
    'ButtonDownFcn', @(s, e) note('fig.ButtonDownFcn', e));
drawnow; pause(1.5);
fp = getpixelposition(f);
scr = get(groot, 'ScreenSize');
% Screen point of a figure-relative MATLAB pixel (x right, y up, 1-based) for the robot (y down),
% scaled from MATLAB's 1/96-inch pixels to whatever the robot counts in.
javaScreen = java.awt.Toolkit.getDefaultToolkit().getScreenSize();
scale = double(javaScreen.width) / scr(3);
fprintf('screen %s, java %dx%d, scale %.4g\n', mat2str(scr), javaScreen.width, javaScreen.height, scale);
at = @(x, y) round(scale * [fp(1) + x - 1.5, scr(4) - (fp(2) + y - 1) + 0.5]);

section('1. edit field e1 focused, type a then Return');
uicontrol(e1); drawnow; pause(0.5);
click(robot, at(100, 213)); pause(0.5);
key(robot, KeyEvent.VK_A); pause(0.5);
key(robot, KeyEvent.VK_ENTER); pause(1);

section('2. type b in e1, then click e2 (focus moves)');
key(robot, KeyEvent.VK_B); pause(0.3);
click(robot, at(280, 213)); pause(1);

section('3. type c in e2, then click the button');
key(robot, KeyEvent.VK_C); pause(0.3);
click(robot, at(70, 155)); pause(1);

section('4. button focused, press space');
key(robot, KeyEvent.VK_SPACE); pause(1);

section('5. click the figure background, press shift+x');
click(robot, at(300, 40)); pause(0.5);
robot.keyPress(KeyEvent.VK_SHIFT); key(robot, KeyEvent.VK_X); robot.keyRelease(KeyEvent.VK_SHIFT); pause(1);

section('6. wheel over the figure');
xy = at(300, 40); robot.mouseMove(xy(1), xy(2)); pause(0.2);
robot.mouseWheel(2); pause(1);

section('7. right click on the background');
xy = at(300, 40); robot.mouseMove(xy(1), xy(2)); pause(0.2);
robot.mousePress(InputEvent.BUTTON3_DOWN_MASK); robot.mouseRelease(InputEvent.BUTTON3_DOWN_MASK); pause(1);

drawnow;
delete(f);
end

function printLog()
global LOG
fprintf('%s\n', LOG{:});
end

function section(text)
global LOG
LOG{end + 1} = ['== ' text];
end

function note(who, e)
global LOG
line = sprintf('%s: class=%s', who, class(e));
if isobject(e) && isprop(e, 'EventName')
    line = sprintf('%s EventName=%s', line, e.EventName);
    for p = {'Character', 'Key', 'Modifier', 'VerticalScrollCount', 'VerticalScrollAmount', 'Button', 'IntersectionPoint'}
        if isprop(e, p{1})
            v = e.(p{1});
            if iscell(v), v = strjoin(v, '+'); end
            if isnumeric(v), v = mat2str(v); end
            if ischar(v) && isscalar(v) && double(v) < 32, v = sprintf('char(%d)', double(v)); end
            line = sprintf('%s %s=[%s]', line, p{1}, v);
        end
    end
    line = sprintf('%s props=%s', line, strjoin(properties(e)', ','));
end
LOG{end + 1} = line;
end

function s = typeOf(h)
if isempty(h), s = '[]'; else, s = [h.Type ':' h.Tag]; end
end

function key(robot, code)
robot.keyPress(code);
robot.keyRelease(code);
end

function click(robot, xy)
import java.awt.event.InputEvent
robot.mouseMove(xy(1), xy(2));
pause(0.15);
robot.mousePress(InputEvent.BUTTON1_DOWN_MASK);
robot.mouseRelease(InputEvent.BUTTON1_DOWN_MASK);
end
