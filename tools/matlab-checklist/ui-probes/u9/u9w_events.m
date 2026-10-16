function u9w_events
% U9 WINDOW probe (open items 63 and 64; needs a display — run only with the user's leave):
%   1. A knob's ValueChangingFcn under a drag and its ValueChangedFcn; a switch's, a rocker's and
%      a discrete knob's ValueChangedFcn; a date picker's and a colour picker's; each event's
%      class and every property.
%   2. A tree's SelectionChangedFcn, NodeExpandedFcn, NodeCollapsedFcn, CheckedNodesChangedFcn,
%      ClickedFcn and DoubleClickedFcn with their TreeInteraction, as far as gestures reach.
%   3. ContextMenuOpeningFcn on a component, and its order round MenuSelectedFcn.
%   4. Item 64's cross-check: a knob's and a ninety-degree gauge's settled MajorTicks in a shown
%      window, against the headless recordings.
%   5. Pictures of a knob, the gauges, the switches, a tree and the colour picker (exportapp),
%      and the colour picker's open palette (a screen capture, which exportapp cannot take).
% Run: run-probe.ps1 -Name u9w_events -WithWindows -TimeoutSec 420
global LOG
LOG = {};
cleanup = onCleanup(@() printLog()); %#ok<NASGU>
here = fileparts(mfilename('fullpath'));
tc = matlab.uitest.TestCase.forInteractiveUse;
fig = uifigure('Name', 'u9w_events', 'Position', [200 200 640 520]);
n = @(who) @(s, e) note(who, s, e);

kn = uiknob(fig, 'Position', [40 400 60 60], 'ValueChangedFcn', n('knob.ValueChangedFcn'), 'ValueChangingFcn', n('knob.ValueChangingFcn'));
dk = uiknob(fig, 'discrete', 'Position', [160 400 60 60], 'Items', {'Low', 'Medium', 'High'}, 'ValueChangedFcn', n('dknob.ValueChangedFcn'));
sw = uiswitch(fig, 'Position', [280 420 45 20], 'ValueChangedFcn', n('switch.ValueChangedFcn'));
ro = uiswitch(fig, 'rocker', 'Position', [360 400 20 45], 'ValueChangedFcn', n('rocker.ValueChangedFcn'));
tg = uiswitch(fig, 'toggle', 'Position', [420 400 20 45], 'ValueChangedFcn', n('toggle.ValueChangedFcn'));
dp = uidatepicker(fig, 'Position', [40 340 130 22], 'ValueChangedFcn', n('date.ValueChangedFcn'));
cp = uicolorpicker(fig, 'Position', [200 340 40 22], 'ValueChangedFcn', n('colour.ValueChangedFcn'));
tr = uitree(fig, 'Position', [40 120 170 190], 'SelectionChangedFcn', n('tree.SelectionChangedFcn'), ...
    'NodeExpandedFcn', n('tree.NodeExpandedFcn'), 'NodeCollapsedFcn', n('tree.NodeCollapsedFcn'), ...
    'ClickedFcn', n('tree.ClickedFcn'), 'DoubleClickedFcn', n('tree.DoubleClickedFcn'));
a = uitreenode(tr, 'Text', 'Alpha');
a1 = uitreenode(a, 'Text', 'Alpha one');
uitreenode(a, 'Text', 'Alpha two');
b = uitreenode(tr, 'Text', 'Beta');
uitreenode(b, 'Text', 'Beta one');
ct = uitree(fig, 'checkbox', 'Position', [240 120 170 190], 'CheckedNodesChangedFcn', n('ctree.CheckedNodesChangedFcn'), ...
    'SelectionChangedFcn', n('ctree.SelectionChangedFcn'));
ca = uitreenode(ct, 'Text', 'Gamma');
ca1 = uitreenode(ca, 'Text', 'Gamma one');
uitreenode(ca, 'Text', 'Gamma two');
cm = uicontextmenu(fig);
cm.ContextMenuOpeningFcn = n('cm.ContextMenuOpeningFcn');
cmi = uimenu(cm, 'Text', 'Act', 'MenuSelectedFcn', n('cm.Act.MenuSelectedFcn'));
kn.ContextMenu = cm;
drawnow; pause(1.5);

%% 1: scales and pickers
gesture('drag the knob 0 to 60', @() tc.drag(kn, 0, 60));
gesture('choose High on the discrete knob', @() tc.choose(dk, 'High'));
gesture('press the switch', @() tc.press(sw));
gesture('choose On on the rocker', @() tc.choose(ro, 'On'));
gesture('press the toggle switch', @() tc.press(tg));
gesture('type a date into the date picker', @() tc.type(dp, datetime(2026, 3, 5)));
gesture('choose a date in the date picker', @() tc.choose(dp, datetime(2026, 7 , 14)));
gesture('choose red on the colour picker', @() tc.choose(cp, [1 0 0]));
gesture('press the colour picker (open its palette)', @() tc.press(cp));
pause(1);
try
    % The open palette, from the screen: exportapp cannot see a popup.
    import java.awt.Robot
    robot = Robot();
    scr = get(groot, 'ScreenSize');
    javaScreen = java.awt.Toolkit.getDefaultToolkit().getScreenSize();
    scale = double(javaScreen.width) / scr(3);
    fp = fig.Position;
    rect = java.awt.Rectangle(round(scale * (fp(1) - 40)), round(scale * (scr(4) - fp(2) - fp(4) - 40)), round(scale * (fp(3) + 300)), round(scale * (fp(4) + 320)));
    cap = robot.createScreenCapture(rect);
    file = java.io.File(fullfile(here, 'u9w_palette.png'));
    javax.imageio.ImageIO.write(cap, 'png', file);
    LOG{end + 1} = 'palette capture written: u9w_palette.png';
catch e
    LOG{end + 1} = sprintf('ERR capture %s %s', e.identifier, oneline(e.message));
end
gesture('press Escape to shut the palette', @() closePalette(fig));

%% 2: trees
gesture('choose node Alpha one', @() tc.choose(tr, a1));
gesture('collapse node Alpha', @() tc.collapse(tr, a));
gesture('expand node Alpha', @() tc.expand(tr, a));
gesture('collapse all (no node argument)', @() tc.collapse(tr));
gesture('expand all', @() tc.expand(tr));
gesture('choose checkbox node Gamma one (check)', @() tc.choose(ct, ca1));
gesture('press checkbox node Gamma (check whole branch?)', @() tc.press(ct, ca));
gesture('press checkbox node Gamma one', @() tc.press(ct, ca1));

%% 3: the context menu on the knob
gesture('context menu on the knob: choose Act', @() tc.chooseContextMenu(kn, cmi));

%% 4: settled ticks in a shown window (cross-check of the headless recordings)
k2 = uiknob(fig, 'Position', [480 400 60 60]);
g2 = uigauge(fig, 'ninetydegree', 'Position', [480 260 90 90]);
drawnow;
t0 = tic;
while toc(t0) < 3, drawnow; pause(0.2); end
LOG{end + 1} = sprintf('WINDOW TICKS knob [0 100] : major=%s labels={%s}', mat2str(k2.MajorTicks, 10), strjoin(k2.MajorTickLabels, ','));
LOG{end + 1} = sprintf('WINDOW TICKS ninety [0 100] : major=%s labels={%s}', mat2str(g2.MajorTicks, 10), strjoin(g2.MajorTickLabels, ','));
k2.Limits = [0 1000];
t0 = tic;
while toc(t0) < 2, drawnow; pause(0.2); end
LOG{end + 1} = sprintf('WINDOW TICKS knob [0 1000] : major=%s labels={%s}', mat2str(k2.MajorTicks, 10), strjoin(k2.MajorTickLabels, ','));

% Does the aspect coercion warn when a window is up? (It is silent headless, u9_settle.)
lastwarn('', '');
k2.Position = [480 400 100 30];
drawnow; pause(1); drawnow;
[wmsg, wid] = lastwarn;
LOG{end + 1} = sprintf('WINDOW ASPECT knob <- [.. 100 30] : warn id=[%s] msg=[%s] Position=%s', wid, oneline(wmsg), mat2str(k2.Position, 8));
delete(fig);

%% 5: the looks, one picture
f2 = uifigure('Name', 'u9w_looks', 'Position', [150 150 760 520]);
uiknob(f2, 'Position', [50 400 60 60]);
uiknob(f2, 'discrete', 'Position', [170 400 60 60]);
uigauge(f2, 'Position', [280 380 120 120]);
uigauge(f2, 'ninetydegree', 'Position', [430 390 90 90]);
uigauge(f2, 'semicircular', 'Position', [550 400 120 65]);
uigauge(f2, 'linear', 'Position', [50 300 120 40]);
uiswitch(f2, 'Position', [220 310 45 20]);
uiswitch(f2, 'rocker', 'Position', [310 290 20 45]);
uiswitch(f2, 'toggle', 'Position', [370 290 20 45]);
uidatepicker(f2, 'Position', [430 300 130 22]);
uicolorpicker(f2, 'Position', [590 300 40 22], 'Value', [0.2 0.4 0.9]);
t2 = uitree(f2, 'Position', [50 60 180 200]);
n1 = uitreenode(t2, 'Text', 'Alpha');
uitreenode(n1, 'Text', 'Alpha one');
uitreenode(t2, 'Text', 'Beta');
expand(t2);
t3 = uitree(f2, 'checkbox', 'Position', [260 60 180 200]);
n3 = uitreenode(t3, 'Text', 'Gamma');
uitreenode(n3, 'Text', 'Gamma one');
expand(t3);
t3.CheckedNodes = n3;
uiknob(f2, 'Position', [500 80 90 90], 'Limits', [0 1000], 'Value', 400);
drawnow; pause(3);
try
    exportapp(f2, fullfile(here, 'u9w_looks.png'));
    LOG{end + 1} = 'looks picture written: u9w_looks.png';
catch e
    LOG{end + 1} = sprintf('ERR exportapp %s %s', e.identifier, oneline(e.message));
end
delete(f2);
end

function closePalette(fig)
import java.awt.Robot
import java.awt.event.KeyEvent
robot = Robot();
figure(fig);
pause(0.3);
robot.keyPress(KeyEvent.VK_ESCAPE);
robot.keyRelease(KeyEvent.VK_ESCAPE);
pause(0.5);
end

function gesture(name, run)
global LOG
LOG{end + 1} = ['== ' name];
try
    run();
    drawnow;
    pause(0.8);
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
    line = sprintf('%s struct:', line);
    for p = fieldnames(e)'
        if strcmp(p{1}, 'Source'), continue; end
        line = sprintf('%s %s=%s', line, p{1}, v2s(e.(p{1})));
    end
end
LOG{end + 1} = line;
end

function s = v2s(v)
if ischar(v), s = ['''' v ''''];
elseif isstring(v), s = ['"' char(strjoin(v, '|')) '"'];
elseif isnumeric(v) || islogical(v), s = mat2str(v, 8);
elseif iscell(v), s = ['{' strjoin(cellfun(@v2s, v(:)', 'UniformOutput', false), ',') '}'];
elseif isdatetime(v)
    if isempty(v), s = 'dt empty'; elseif isnat(v), s = 'NaT'; else, s = ['dt ' char(string(v, 'yyyy-MM-dd HH:mm:ss'))]; end
elseif isa(v, 'matlab.graphics.Graphics')
    if isempty(v), s = sprintf('<%s empty %s>', class(v), mat2str(size(v)));
    elseif isscalar(v) && isprop(v, 'Text'), s = sprintf('<%s ''%s''>', class(v), char(string(v.Text)));
    else, s = sprintf('<%s %s>', class(v), mat2str(size(v)));
    end
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
