function u8w_events
% U8 WINDOW probe (open item 59; needs a display — run only with the user's leave):
%   1. A uitable's CellEditCallback event after text over a number (a number, and no number),
%      text over text, a ticked check box and a picked ColumnFormat choice; what NewData and
%      Error hold in each.
%   2. Which of CellSelectionCallback and SelectionChangedFcn runs first and what each is told,
%      for a cell, a row and a column selection.
%   3. A MenuSelectedFcn's event on a menu bar entry and in a context menu, and the order round
%      ContextMenuOpeningFcn.
%   4. A toggle tool's OnCallback/OffCallback against ClickedCallback, and a push tool's.
%   5. The text a table writes for numbers under each ColumnFormat word (exportapp picture).
%   6. A classic figure's tab rectangle: the strip and border, measured from a getframe.
% Run: run-probe.ps1 -Name u8w_events -WithWindows -TimeoutSec 420
global LOG
LOG = {};
cleanup = onCleanup(@() printLog()); %#ok<NASGU>
here = fileparts(mfilename('fullpath'));
tc = matlab.uitest.TestCase.forInteractiveUse;
fig = uifigure('Name', 'u8w_events', 'Position', [200 200 640 480]);
n = @(who) @(s, e) note(who, s, e);

t = uitable(fig, 'Position', [20 200 420 240], ...
    'Data', {1.5, 'abc', true, 'two'; 42, 'def', false, 'one'; -7, 'ghi', true, 'three'}, ...
    'ColumnName', {'Num', 'Text', 'Tick', 'Pick'}, ...
    'ColumnFormat', {[], [], 'logical', {'one', 'two', 'three'}}, ...
    'ColumnEditable', true, ...
    'CellEditCallback', n('t.CellEditCallback'), ...
    'CellSelectionCallback', n('t.CellSelectionCallback'), ...
    'SelectionChangedFcn', n('t.SelectionChangedFcn'), ...
    'ClickedFcn', n('t.ClickedFcn'), ...
    'DoubleClickedFcn', n('t.DoubleClickedFcn'), ...
    'DisplayDataChangedFcn', n('t.DisplayDataChangedFcn'));

mFile = uimenu(fig, 'Text', 'Probe');
mOpen = uimenu(mFile, 'Text', 'Open', 'MenuSelectedFcn', n('menu.Open.MenuSelectedFcn'));
mFile.MenuSelectedFcn = n('menu.Probe.MenuSelectedFcn');
cm = uicontextmenu(fig);
cm.ContextMenuOpeningFcn = n('cm.ContextMenuOpeningFcn');
cmi = uimenu(cm, 'Text', 'Do it', 'MenuSelectedFcn', n('cm.DoIt.MenuSelectedFcn'));
lab = uilabel(fig, 'Text', 'right-click me', 'Position', [480 400 120 22], 'ContextMenu', cm);

tb = uitoolbar(fig);
pt = uipushtool(tb, 'ClickedCallback', n('push.ClickedCallback'), 'Tooltip', 'push');
tt = uitoggletool(tb, 'ClickedCallback', n('toggle.ClickedCallback'), ...
    'OnCallback', n('toggle.OnCallback'), 'OffCallback', n('toggle.OffCallback'), 'Tooltip', 'toggle');
drawnow; pause(1.5);

%% 1 + 2: cells
gesture('choose cell [1 1]', @() tc.choose(t, [1 1]));
gesture('type 99 into cell [1 1]', @() tc.type(t, [1 1], '99'));
gesture('type xyz into numeric cell [2 1]', @() tc.type(t, [2 1], 'xyz'));
gesture('type new over text cell [1 2]', @() tc.type(t, [1 2], 'new'));
gesture('type 5 over text cell [2 2]', @() tc.type(t, [2 2], '5'));
gesture('press the check box cell [2 3]', @() tc.press(t, [2 3]));
gesture('choose three in the pick cell [2 4]', @() tc.choose(t, [2 4], 'three'));
gesture('choose cells [1 1; 2 1]', @() tc.choose(t, [1 1; 2 1]));
LOG{end + 1} = sprintf('t.Data now: %s', v2s(t.Data));
LOG{end + 1} = sprintf('t.Selection now: %s', mat2str(t.Selection));

gesture('row selection: set SelectionType row', @() set(t, 'SelectionType', 'row'));
gesture('choose row 2', @() tc.choose(t, 2));
gesture('column selection: set SelectionType column', @() set(t, 'SelectionType', 'column'));
gesture('choose column 3', @() tc.choose(t, 3));

%% 3: menus
gesture('press the Open menu entry', @() tc.press(mOpen));
gesture('context menu on the label: choose Do it', @() tc.chooseContextMenu(lab, cmi));

%% 4: toolbar tools
gesture('press the push tool', @() tc.press(pt));
gesture('press the toggle tool (goes down)', @() tc.press(tt));
LOG{end + 1} = sprintf('toggle State now: %s', v2s(tt.State));
gesture('press the toggle tool (comes up)', @() tc.press(tt));
LOG{end + 1} = sprintf('toggle State now: %s', v2s(tt.State));

%% 5: the text each ColumnFormat writes (picture, read by eye)
f2 = uifigure('Name', 'u8w_formats', 'Position', [100 100 1180 360]);
formats = {'short', 'long', 'shorte', 'longe', 'shortg', 'longg', 'bank', '+', 'rat', 'numeric', 'char', 'logical'};
numbers = [pi; 1234567.891; -0.5; 0.001; 1/3; 0; 1e10];
data = cell(numel(numbers), numel(formats));
for c = 1:numel(formats)
    for r = 1:numel(numbers)
        data{r, c} = numbers(r);
    end
end
t2 = uitable(f2, 'Position', [10 10 1160 340], 'Data', data, 'ColumnFormat', formats, 'ColumnName', formats);
drawnow; pause(2);
try
    exportapp(f2, fullfile(here, 'u8w_formats.png'));
    LOG{end + 1} = 'formats picture written: u8w_formats.png';
catch e
    LOG{end + 1} = sprintf('ERR exportapp %s %s', e.identifier, oneline(e.message));
end
delete(f2);

%% 6: a classic figure's tab rectangle, from its pixels
cf = figure('Name', 'u8w_tab', 'NumberTitle', 'off', 'Position', [150 150 420 320], 'MenuBar', 'none', 'ToolBar', 'none', 'Color', [0 0 1]);
tg = uitabgroup(cf, 'Units', 'pixels', 'Position', [20 20 300 200]);
t1 = uitab(tg, 'Title', 'One', 'BackgroundColor', [1 0 0]);
uitab(tg, 'Title', 'Two', 'BackgroundColor', [0 1 0]);
drawnow; pause(2);
try
    frame = getframe(cf);
    img = frame.cdata;
    [fh, fw, ~] = size(img);
    cfp = cf.Position;
    LOG{end + 1} = sprintf('classic figure Position=%s frame=%dx%d (scale %.4g)', mat2str(cfp), fw, fh, fw / cfp(3));
    red = img(:, :, 1) > 200 & img(:, :, 2) < 90 & img(:, :, 3) < 90;
    cols = find(any(red, 1));
    rows = find(any(red, 2));
    if ~isempty(cols)
        LOG{end + 1} = sprintf('tab One content in frame pixels: x=[%d %d] y=[%d %d] (y from the top)', cols(1), cols(end), rows(1), rows(end));
    else
        LOG{end + 1} = 'tab One content: no red pixels found';
    end
    LOG{end + 1} = sprintf('tabgroup Position=%s InnerPosition(t1)=%s', mat2str(tg.Position), mat2str(t1.InnerPosition));
    imwrite(img, fullfile(here, 'u8w_tab.png'));
    LOG{end + 1} = 'tab picture written: u8w_tab.png';
catch e
    LOG{end + 1} = sprintf('ERR getframe %s %s', e.identifier, oneline(e.message));
end
delete(cf);
delete(fig);
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
elseif isa(v, 'matlab.graphics.Graphics'), s = ['<' class(v) '>'];
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
