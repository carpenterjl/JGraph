% record: -noFigureWindows
% U1 of the app-building plan (ADR 0198): the classic uicontrol in a figure that is never shown —
% its defaults, R2025b's coercions and refusals, every callback form, the event data a script can
% make fire, the figure's children, and a text CloseRequestFcn that vetoes the close.
f = figure('Visible', 'off', 'Position', [400 400 350 250]);
fprintf('CHK|figure_visible|%s|exact\n', char(get(f, 'Visible')));

% --- defaults, per style ---------------------------------------------------------------------------
styles = {'text', 'edit', 'pushbutton', 'listbox', 'popupmenu'};
props = {'Position', 'BackgroundColor', 'ForegroundColor', 'FontName', 'FontSize', 'FontUnits', ...
    'FontWeight', 'FontAngle', 'HorizontalAlignment', 'Enable', 'Value', 'String', 'Max', 'Min', ...
    'Units', 'Tooltip', 'Callback', 'Type', 'ListboxTop', 'SliderStep', 'KeyPressFcn', 'InnerPosition', ...
    'OuterPosition', 'Tag', 'BusyAction'};
for s = 1:numel(styles)
    h = uicontrol(f, 'Style', styles{s});
    fprintf('CHK|default_%s_Style|%s|exact\n', styles{s}, get(h, 'Style'));
    fprintf('CHK|default_%s_Visible|%s|exact\n', styles{s}, char(get(h, 'Visible')));
    for p = 1:numel(props)
        fprintf('CHK|default_%s_%s|%s|exact\n', styles{s}, props{p}, u1_show(get(h, props{p})));
    end
    delete(h);
end

% --- String --------------------------------------------------------------------------------------
h = uicontrol(f, 'Style', 'text');
strings = {'abc', "abc", {'a', 'bc'}, {'a'; 'bc'}, ["a", "bc"], ['ab'; 'cd'], '', [], 5, [1 2 3], ...
    {}, {'a', 1}, "", {"a"}, int8(65), 2.5};
for k = 1:numel(strings)
    set(h, 'String', strings{k});
    fprintf('CHK|string_%d|%s|exact\n', k, u1_show(get(h, 'String')));
end
h.String = 'dot set';
fprintf('CHK|string_dot|%s|exact\n', u1_show(h.String));
try
    set(h, 'String', true);
    fprintf('CHK|string_logical|accepted|exact\n');
catch err
    fprintf('CHK|string_logical|%s|exact\n', err.identifier);
    fprintf('CHK|string_logical_msg|%s|exact\n', strrep(regexprep(err.message, '\s+', ' '), '|', '/'));
end

% --- words: case and unique prefixes -------------------------------------------------------------
for ab = {'push', 'Edit', 'TEXT', 'popup', 'te', 'e', 'p', 'check', 'fr', 'sl'}
    try
        g = uicontrol(f, 'Style', ab{1});
        fprintf('CHK|style_%s|%s|exact\n', ab{1}, g.Style);
        delete(g);
    catch err
        fprintf('CHK|style_%s|%s|exact\n', ab{1}, err.identifier);
        fprintf('CHK|style_%s_msg|%s|exact\n', ab{1}, strrep(regexprep(err.message, '\s+', ' '), '|', '/'));
    end
end
words = {'HorizontalAlignment', {'left', 'LEFT', 'l', 'ri', 'center', 'centre', 5}; ...
    'FontWeight', {'bold', 'b', 'demi', 'light', 'normal', 'heavy'}; ...
    'FontAngle', {'italic', 'oblique', 'i', 'normal', 'slanted'}; ...
    'Enable', {'off', 'inactive', 'on', 'ON', 'in', true, 'yes'}; ...
    'Visible', {'off', 'on', true, 0, 'maybe'}; ...
    'FontUnits', {'normalized', 'points', 'px'}};
for w = 1:size(words, 1)
    for k = 1:numel(words{w, 2})
        try
            set(h, words{w, 1}, words{w, 2}{k});
            fprintf('CHK|word_%s_%d|%s|exact\n', words{w, 1}, k, char(get(h, words{w, 1})));
        catch err
            fprintf('CHK|word_%s_%d|%s|exact\n', words{w, 1}, k, err.identifier);
            fprintf('CHK|word_%s_%d_msg|%s|exact\n', words{w, 1}, k, strrep(regexprep(err.message, '\s+', ' '), '|', '/'));
        end
    end
end
set(h, 'FontUnits', 'points', 'FontSize', 8);

% --- colours, sizes, positions, values -----------------------------------------------------------
colours = {[0 0.45 0.74], 'r', 'red', '#FF8000', '#f80', [2 0 0], [0.5 0.5], 'none', 'bogus', [1; 0; 0], uint8([255 0 0]), "blue"};
for k = 1:numel(colours)
    try
        set(h, 'ForegroundColor', colours{k});
        fprintf('CHK|colour_%d|%s|exact\n', k, u1_show(get(h, 'ForegroundColor')));
    catch err
        fprintf('CHK|colour_%d|%s|exact\n', k, err.identifier);
        fprintf('CHK|colour_%d_msg|%s|exact\n', k, strrep(regexprep(err.message, '\s+', ' '), '|', '/'));
    end
end
sizes = {10, 0, -1, 2.5, [10 12], 'big', NaN, Inf, int8(9)};
for k = 1:numel(sizes)
    try
        set(h, 'FontSize', sizes{k});
        fprintf('CHK|fontsize_%d|%s|exact\n', k, u1_show(h.FontSize));
    catch err
        fprintf('CHK|fontsize_%d|%s|exact\n', k, err.identifier);
        fprintf('CHK|fontsize_%d_msg|%s|exact\n', k, strrep(regexprep(err.message, '\s+', ' '), '|', '/'));
    end
end
positions = {[30 180 100 20], [1 2 3], [0 0 0 0], [10 10 -5 20], [1.5 2.5 3.5 4.5], 'abc', [NaN 1 1 1], [1; 2; 3; 4], int16([1 2 3 4])};
for k = 1:numel(positions)
    try
        set(h, 'Position', positions{k});
        fprintf('CHK|position_%d|%s|exact\n', k, u1_show(h.Position));
    catch err
        fprintf('CHK|position_%d|%s|exact\n', k, err.identifier);
        fprintf('CHK|position_%d_msg|%s|exact\n', k, strrep(regexprep(err.message, '\s+', ' '), '|', '/'));
    end
end
values = {5, -1, [1 2], 'x', true, [], int8(3)};
for k = 1:numel(values)
    try
        set(h, 'Value', values{k});
        fprintf('CHK|value_%d|%s|exact\n', k, u1_show(h.Value));
    catch err
        fprintf('CHK|value_%d|%s|exact\n', k, err.identifier);
    end
end
for prop = {'Max', 'Min'}
    for val = {2, 'x', [1 2]}
        try
            set(h, prop{1}, val{1});
            fprintf('CHK|%s_%s|%s|exact\n', prop{1}, class(val{1}), u1_show(get(h, prop{1})));
        catch err
            fprintf('CHK|%s_%s_%d|%s|exact\n', prop{1}, class(val{1}), numel(val{1}), err.identifier);
        end
    end
end
tips = {'tip', "tip2", {'a', 'b'}, 5, ''};
for k = 1:numel(tips)
    try
        set(h, 'Tooltip', tips{k});
        fprintf('CHK|tooltip_%d|%s|exact\n', k, u1_show(h.Tooltip));
    catch err
        fprintf('CHK|tooltip_%d|%s|exact\n', k, err.identifier);
    end
end
fprintf('CHK|tooltipstring|%s|exact\n', u1_show(get(h, 'TooltipString')));
for val = {'Arial', "Courier New", 5, ''}
    try
        set(h, 'FontName', val{1});
        fprintf('CHK|fontname_%s|%s|exact\n', class(val{1}), u1_show(h.FontName));
    catch err
        fprintf('CHK|fontname_%s_%d|%s|exact\n', class(val{1}), numel(val{1}), err.identifier);
    end
end
try, set(h, 'Bogus', 1); catch err, fprintf('CHK|unknown_set|%s|exact\n', err.identifier); fprintf('CHK|unknown_set_msg|%s|exact\n', err.message); end
try, set(h, 'Units', 'normalized'); units = h.Units; catch err, units = 'refused'; end
fprintf('CHK|units_normalized|%s|exact\n', units);
set(h, 'Units', 'pixels');
try, set(h, 'Units', 'bogus'); catch err, fprintf('CHK|units_bogus|%s|exact\n', err.identifier); end

% --- the style decides the default background, until one is set -----------------------------------
g = uicontrol(f, 'Style', 'text');
g.Style = 'edit';
fprintf('CHK|restyle_text_edit|%s|exact\n', u1_show(g.BackgroundColor));
g.BackgroundColor = [1 0 0];
g.Style = 'text';
fprintf('CHK|restyle_after_set|%s|exact\n', u1_show(g.BackgroundColor));
delete(g);

% --- callbacks: the three forms and the refusal ---------------------------------------------------
cbs = {@(s, e) disp(1), 'disp(2)', {@(s, e, a) disp(a), 3}, {'disp', 4}, '', [], 5, {5}, {}, {@disp}, "disp(7)"};
for k = 1:numel(cbs)
    try
        set(h, 'Callback', cbs{k});
        fprintf('CHK|callback_%d|%s|exact\n', k, u1_show(get(h, 'Callback')));
    catch err
        fprintf('CHK|callback_%d|%s|exact\n', k, err.identifier);
        fprintf('CHK|callback_%d_msg|%s|exact\n', k, strrep(regexprep(err.message, '\s+', ' '), '|', '/'));
    end
end
for prop = {'KeyPressFcn', 'ButtonDownFcn', 'DeleteFcn', 'CreateFcn', 'KeyReleaseFcn'}
    for k = [2 3 7]
        try
            set(h, prop{1}, cbs{k});
            fprintf('CHK|cb_%s_%d|%s|exact\n', prop{1}, k, u1_show(get(h, prop{1})));
        catch err
            fprintf('CHK|cb_%s_%d|%s|exact\n', prop{1}, k, err.identifier);
        end
    end
end
set(h, 'DeleteFcn', '');
for prop = {'KeyPressFcn', 'WindowKeyPressFcn', 'WindowButtonDownFcn', 'SizeChangedFcn', 'ButtonDownFcn'}
    for k = [2 3 7]
        try
            set(f, prop{1}, cbs{k});
            fprintf('CHK|figure_cb_%s_%d|%s|exact\n', prop{1}, k, u1_show(get(f, prop{1})));
        catch err
            fprintf('CHK|figure_cb_%s_%d|%s|exact\n', prop{1}, k, err.identifier);
        end
    end
    set(f, prop{1}, '');
end
fprintf('CHK|figure_closereq_default|%s|exact\n', u1_show(get(f, 'CloseRequestFcn')));

% --- what a script can make fire: CreateFcn and DeleteFcn, in each form --------------------------
global U1LOG
U1LOG = {};
g = uicontrol(f, 'Style', 'pushbutton', 'CreateFcn', @(s, e) u1_log(sprintf('create %s %d %s', class(e), isempty(e), s.Type)));
set(g, 'DeleteFcn', @(s, e) u1_log(sprintf('delete %s %s %d', class(e), e.EventName, isa(e, 'event.EventData'))));
delete(g);
g = uicontrol(f, 'Style', 'pushbutton', 'CreateFcn', 'u1_log(''text create'')');
g.DeleteFcn = {@(s, e, a) u1_log(sprintf('cell delete %d %s', a, class(e))), 42};
delete(g);
for k = 1:numel(U1LOG)
    fprintf('CHK|fired_%d|%s|exact\n', k, U1LOG{k});
end

% --- children, parents, search, deletion ---------------------------------------------------------
delete(h);
a = axes(f);
c1 = uicontrol(f, 'Style', 'text', 'String', 'one');
c2 = uicontrol('Parent', f, 'Style', 'edit', 'String', 'two');
kids = get(f, 'Children');
types = cell(1, numel(kids));
for k = 1:numel(kids)
    types{k} = get(kids(k), 'Type');
end
fprintf('CHK|children|%s|exact\n', strjoin(types, ','));
fprintf('CHK|children_first_is_newest|%d|exact\n', kids(1) == c2);
fprintf('CHK|parent|%d|exact\n', get(c1, 'Parent') == f);
fprintf('CHK|findobj_style|%d|exact\n', numel(findobj(f, 'Style', 'edit')));
fprintf('CHK|findobj_type|%d|exact\n', numel(findobj(f, 'Type', 'uicontrol')));
fprintf('CHK|isgraphics|%d|exact\n', isgraphics(c1));
fprintf('CHK|class|%s|div=ADR0198\n', class(c1));
try, uicontrol(a, 'Style', 'text'); catch err, fprintf('CHK|parent_axes|%s|exact\n', err.identifier); fprintf('CHK|parent_axes_msg|%s|exact\n', err.message); end
try, uicontrol(c1, 'Style', 'text'); catch err, fprintf('CHK|parent_uicontrol|%s|exact\n', err.identifier); end
try, uicontrol('Parent', 7777, 'Style', 'text'); catch err, fprintf('CHK|parent_number|%s|exact\n', err.identifier); end
try, uicontrol('Style'); catch err, fprintf('CHK|odd_pairs|%s|exact\n', err.identifier); end
try, uicontrol(f, 5); catch err, fprintf('CHK|numeric_name|%s|exact\n', err.identifier); end
try, uicontrol(f, 'text'); catch err, fprintf('CHK|bare_word|%s|exact\n', err.identifier); end
c3 = uicontrol(f, struct('Style', 'text', 'String', 'from struct'));
fprintf('CHK|struct_form|%s %s|exact\n', c3.Style, c3.String);
delete(c3);
delete(c1);
fprintf('CHK|deleted_isgraphics|%d|exact\n', isgraphics(c1));
try, c1.String = 'x'; catch err, fprintf('CHK|set_on_deleted|%s|div=ADR0198\n', err.identifier); end

% --- a text CloseRequestFcn other than closereq vetoes the close ---------------------------------
U1LOG = {};
f.CloseRequestFcn = 'u1_log(''veto by text'')';
close(f);
fprintf('CHK|veto_text_alive|%d|exact\n', isgraphics(f));
f.CloseRequestFcn = {@(s, e, msg) u1_log(msg), 'veto by cell'};
close(f);
fprintf('CHK|veto_cell_alive|%d|exact\n', isgraphics(f));
f.CloseRequestFcn = @(s, e) u1_log(sprintf('handle %s %s', class(e), e.EventName));
close(f);
fprintf('CHK|veto_handle_alive|%d|exact\n', isgraphics(f));
for k = 1:numel(U1LOG)
    fprintf('CHK|veto_%d|%s|exact\n', k, U1LOG{k});
end
f.CloseRequestFcn = 'closereq';
fprintf('CHK|closereq_reads|%s|exact\n', u1_show(f.CloseRequestFcn));
close(f);
fprintf('CHK|closed|%d|exact\n', isgraphics(f));
