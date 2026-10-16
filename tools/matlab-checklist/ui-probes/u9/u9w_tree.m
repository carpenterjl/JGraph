function u9w_tree
% U9/U8 WINDOW probe, second round (open items 59 and 63; needs a display — run only with the
% user's leave; the robot clicks for about forty seconds, hands off). The uitest gestures refuse
% trees, the colour picker, a table's headers and its check-box cells (u9w_events, u8w_events),
% so the robot clicks them by position and the Clicked events' own Location lines calibrate it:
%   1. A tree: select a node, collapse and expand by the chevron, retype a node's text
%      (Editable), and a checkbox tree's leaf and branch ticks.
%   2. The colour picker: open its palette (captured from the screen), pick a swatch.
%   3. A table in row and in column selection: a click on a row header and a column header.
%   4. A check-box cell's tick (CellEditCallback for a logical column).
% Run: run-probe.ps1 -Name u9w_tree -WithWindows -TimeoutSec 300
global LOG
LOG = {};
cleanup = onCleanup(@() printLog()); %#ok<NASGU>
here = fileparts(mfilename('fullpath'));
import java.awt.Robot
import java.awt.event.InputEvent
import java.awt.event.KeyEvent
robot = Robot();
robot.setAutoDelay(35);
n = @(who) @(s, e) note(who, s, e);

fig = uifigure('Name', 'u9w_tree', 'Position', [200 200 640 500]);
tr = uitree(fig, 'Position', [40 120 180 220], 'Editable', 'on', ...
    'SelectionChangedFcn', n('tree.SelectionChangedFcn'), 'NodeExpandedFcn', n('tree.NodeExpandedFcn'), ...
    'NodeCollapsedFcn', n('tree.NodeCollapsedFcn'), 'NodeTextChangedFcn', n('tree.NodeTextChangedFcn'), ...
    'ClickedFcn', n('tree.ClickedFcn'), 'DoubleClickedFcn', n('tree.DoubleClickedFcn'));
a = uitreenode(tr, 'Text', 'Alpha');
uitreenode(a, 'Text', 'Alpha one');
uitreenode(a, 'Text', 'Alpha two');
b = uitreenode(tr, 'Text', 'Beta');
uitreenode(b, 'Text', 'Beta one');
expand(tr);
ct = uitree(fig, 'checkbox', 'Position', [260 120 180 220], ...
    'CheckedNodesChangedFcn', n('ctree.CheckedNodesChangedFcn'), 'SelectionChangedFcn', n('ctree.SelectionChangedFcn'), ...
    'ClickedFcn', n('ctree.ClickedFcn'));
g = uitreenode(ct, 'Text', 'Gamma');
uitreenode(g, 'Text', 'Gamma one');
uitreenode(g, 'Text', 'Gamma two');
expand(ct);
cp = uicolorpicker(fig, 'Position', [480 420 40 22], 'ValueChangedFcn', n('colour.ValueChangedFcn'));
drawnow; pause(2);
figure(fig); drawnow; pause(1);

scr = get(groot, 'ScreenSize');
javaScreen = java.awt.Toolkit.getDefaultToolkit().getScreenSize();
scale = double(javaScreen.width) / scr(3);
fp = fig.Position;
at = @(x, y) round(scale * [fp(1) + x - 1, scr(4) - (fp(2) + y)]);
    function click(what, x, y, count)
        if nargin < 4, count = 1; end
        LOG{end + 1} = sprintf('== click %s at [%g %g]', what, x, y);
        p = at(x, y);
        robot.mouseMove(p(1), p(2)); pause(0.2);
        for c = 1:count
            robot.mousePress(InputEvent.BUTTON1_MASK); robot.mouseRelease(InputEvent.BUTTON1_MASK);
            if count > 1, pause(0.08); end
        end
        pause(1); drawnow;
    end
    function keys(what, codes)
        LOG{end + 1} = sprintf('== type %s', what);
        for c = codes
            robot.keyPress(c); robot.keyRelease(c);
            pause(0.08);
        end
        pause(1); drawnow;
    end

% Tree rows from its top (y 340): about 22 px each; text from x 70, chevron near x 52.
click('Alpha text (select)', 90, 329);
click('Alpha chevron (collapse)', 52, 329);
click('Alpha chevron (expand)', 52, 329);
click('Beta text (select)', 90, 263);
click('Beta text again (begin edit)', 90, 263, 2);
pause(0.5);
keys('Bravo and Enter', [KeyEvent.VK_BACK_SPACE KeyEvent.VK_BACK_SPACE KeyEvent.VK_BACK_SPACE KeyEvent.VK_BACK_SPACE ...
    KeyEvent.VK_B KeyEvent.VK_R KeyEvent.VK_A KeyEvent.VK_V KeyEvent.VK_O KeyEvent.VK_ENTER]);
LOG{end + 1} = sprintf('Beta text now [%s]', b.Text);

% Checkbox tree: chevron ~272, box ~292, text ~310; Gamma row 1 (y 329), Gamma one row 2 (y 307).
click('Gamma one checkbox (leaf tick)', 292, 307);
click('Gamma checkbox (branch tick)', 292, 329);
click('Gamma one checkbox (untick under a ticked branch)', 292, 307);

% The colour picker: open the palette, capture it, pick a swatch, Escape if still open.
click('colour picker swatch (open the palette)', 500, 431);
pause(1);
try
    rect = java.awt.Rectangle(round(scale * (fp(1) + 300)), round(scale * (scr(4) - fp(2) - fp(4))), round(scale * 340), round(scale * 500));
    cap = robot.createScreenCapture(rect);
    javax.imageio.ImageIO.write(cap, 'png', java.io.File(fullfile(here, 'u9w_palette.png')));
    LOG{end + 1} = 'palette capture written: u9w_palette.png';
catch e
    LOG{end + 1} = sprintf('ERR capture %s %s', e.identifier, oneline(e.message));
end
click('a palette swatch (just under the picker)', 500, 395);
pause(0.5);
robot.keyPress(KeyEvent.VK_ESCAPE); robot.keyRelease(KeyEvent.VK_ESCAPE);
pause(0.5);
LOG{end + 1} = sprintf('colour Value now %s', mat2str(cp.Value, 6));
delete(fig);

% A second figure: a table's row and column selection, and a check-box cell.
f2 = uifigure('Name', 'u9w_tree2', 'Position', [200 200 640 400]);
t = uitable(f2, 'Position', [40 80 320 240], 'Data', {1, 'a', true; 2, 'b', false; 3, 'c', true}, ...
    'ColumnWidth', {60, 60, 60}, 'ColumnEditable', true, 'SelectionType', 'row', ...
    'CellEditCallback', n('t.CellEditCallback'), 'CellSelectionCallback', n('t.CellSelectionCallback'), ...
    'SelectionChangedFcn', n('t.SelectionChangedFcn'), 'ClickedFcn', n('t.ClickedFcn'), ...
    'DisplayDataChangedFcn', n('t.DisplayDataChangedFcn'));
drawnow; pause(2);
figure(f2); drawnow; pause(1);
fp = f2.Position;

% Table top at y 320; header ~25 px; rows ~22 px; row headers ~32 px wide; columns 60 px.
click('row 2 header (row selection)', 55, 262);
LOG{end + 1} = sprintf('t.Selection now %s', mat2str(t.Selection));
t.SelectionType = 'column';
drawnow; pause(0.5);
click('column 2 header (column selection)', 132, 284);
LOG{end + 1} = sprintf('t.Selection now %s', mat2str(t.Selection));
t.SelectionType = 'cell';
drawnow; pause(0.5);
click('check box cell [2 3] (tick off->on)', 192, 262);
LOG{end + 1} = sprintf('t.Data now: %s', strjoin(cellfun(@(v) mat2str(v), t.Data(:)', 'UniformOutput', false), ','));
delete(f2);
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
end
LOG{end + 1} = line;
end

function s = v2s(v)
if ischar(v), s = ['''' v ''''];
elseif isstring(v), s = ['"' char(strjoin(v, '|')) '"'];
elseif isnumeric(v) || islogical(v), s = mat2str(v, 8);
elseif iscell(v), s = ['{' strjoin(cellfun(@v2s, v(:)', 'UniformOutput', false), ',') '}'];
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
