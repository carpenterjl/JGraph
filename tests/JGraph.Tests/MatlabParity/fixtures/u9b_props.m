% record: -noFigureWindows
% U9b of the app-building plan (ADR 0208): uihtml's properties. As made in a classic figure (F) and a
% uifigure (U) - its Type, the names it answers to and can be written by, and every default - and
% then one battery of values written to each property of a fresh one in a uifigure, each line saying
% what the property reads as afterwards, or R2025b's refusal. Probe u9b_matrix.
skip = {'Parent', 'ContextMenu', 'Layout', 'UserData'};
for q = 1:2
    if q == 1
        p = figure('Visible', 'off', 'Position', [100 100 560 420]); w = 'F';
    else
        p = uifigure('Visible', 'off', 'Position', [100 100 560 420]); w = 'U';
    end
    h = uihtml(p);
    names = sort(fieldnames(get(h)));
    fprintf('CHK|%s_type|%s|exact\n', w, get(h, 'Type'));
    fprintf('CHK|%s_get_names|%s|exact\n', w, strjoin(names', ' '));
    fprintf('CHK|%s_set_names|%s|exact\n', w, strjoin(sort(fieldnames(set(h)))', ' '));
    for n = 1:numel(names)
        if ~any(strcmp(names{n}, skip))
            fprintf('CHK|%s_default_%s|%s|exact\n', w, names{n}, u9b_text(get(h, names{n})));
        end
    end
    sv = set(h);
    words = fieldnames(sv);
    for n = 1:numel(words)
        if ~isempty(sv.(words{n}))
            fprintf('CHK|%s_words_%s|%s|exact\n', w, words{n}, u9b_text(sv.(words{n})));
        end
    end
    fprintf('CHK|%s_userdata_empty|%d|exact\n', w, isempty(get(h, 'UserData')));
    fprintf('CHK|%s_contextmenu_empty|%d|exact\n', w, isempty(get(h, 'ContextMenu')));
    fprintf('CHK|%s_parent_type|%s|exact\n', w, get(get(h, 'Parent'), 'Type'));
    fprintf('CHK|%s_flags|%d %d %d %d %d %d %d %d %d|exact\n', w, isprop(h, 'Units'), isprop(h, 'Enable'), isprop(h, 'FontSize'), ...
        isprop(h, 'BackgroundColor'), isgraphics(h), ishghandle(h), isvalid(h), ishandle(h), isa(h, 'matlab.ui.control.HTML'));
    delete(p);
end

uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
d1 = datetime(2024, 1, 15);
battery = {'on', 'off', true, false, 1, 0, -1, 2.5, [1 2], [10 20 30 40], [1 2 3], 'abc', "str", {'a', 'b'}, ["p"; "q"], ...
    [], '', {}, NaN, Inf, @sin, struct('a', 1), int8(3), single(2.5), 'left', 'auto', ['ab'; 'cd'], "", string(missing), ...
    {1, 2}, {@sin, 1}, magic(3), 'pixels', 'ON', 'Off', d1, 1 + 2i, {'a'; 'b'}, uint8([10 20 30]), ...
    '<p>hi</p>', 'missing.html', 'missing.HTM', 'https://www.mathworks.com', 'www.mathworks.com', 'mailto:a@b.c', ...
    'ftp://x', 'data:text/html,<b>x</b>', 'missing.txt', '  missing.html', 'u9b_json.m', [0 0 0], 'red', zeros(1, 0)};
props = {'Data', 'HTMLSource', 'DataChangedFcn', 'HTMLEventReceivedFcn', 'Tooltip', 'Position', 'InnerPosition', 'OuterPosition', ...
    'Visible', 'Tag', 'HandleVisibility', 'BusyAction', 'Interruptible', 'Enable', 'Type', 'BeingDeleted'};
for n = 1:numel(props)
    for k = 1:numel(battery)
        h = uihtml(uf);
        u9b_try(sprintf('set_%s_%02d', props{n}, k), h, props{n}, battery{k});
        delete(h);
    end
end

% dot writes, and what keeps a string
h = uihtml(uf);
u9b_chk('dot_source_string', @() setdot(h, 'HTMLSource', "<b>s</b>"));
u9b_chk('dot_data_string', @() setdot(h, 'Data', "s"));
u9b_chk('dot_data_cell_string', @() setdot(h, 'Data', {"s"}));
u9b_chk('set_data_string', @() setget(h, 'Data', "t"));
u9b_chk('dot_tooltip_string', @() setdot(h, 'Tooltip', "t"));
u9b_chk('dot_data_struct', @() setdot(h, 'Data', struct('a', {1, 2})));
u9b_chk('get_bogus', @() get(h, 'Bogus'));
u9b_chk('set_bogus', @() setget(h, 'Bogus', 1));
u9b_chk('get_units', @() get(h, 'Units'));
u9b_chk('callback_cell', @() setget(h, 'DataChangedFcn', {@disp, 1}));
u9b_chk('callback_text', @() setget(h, 'HTMLEventReceivedFcn', 'disp(1)'));
u9b_chk('position_after', @() setget(h, 'Position', [10 20 300 200], 'Position', 'InnerPosition', 'OuterPosition'));
delete(uf);

function v = setdot(h, name, value)
h.(name) = value;
v = h.(name);
end

function v = setget(h, name, value, varargin)
set(h, name, value);
if isempty(varargin)
    v = get(h, name);
else
    v = cellfun(@(n) get(h, n), varargin, 'UniformOutput', false);
end
end
