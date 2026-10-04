function u1_uicontrol
% U1 probe: what R2025b's uicontrol (text, edit, pushbutton) holds, takes and refuses, in a
% classic figure that is never shown. Run headless: run-probe.ps1 -Name u1_uicontrol.
f = figure('Visible', 'off', 'Position', [400 400 350 250]);
fprintf('figure Visible=%s class=%s\n', char(f.Visible), class(f));

%% defaults per style
for style = {'text', 'edit', 'pushbutton'}
    h = uicontrol(f, 'Style', style{1});
    fprintf('== default %s\n', style{1});
    show(h, {'Position', 'BackgroundColor', 'ForegroundColor', 'FontName', 'FontSize', 'FontUnits', ...
        'FontWeight', 'FontAngle', 'HorizontalAlignment', 'Enable', 'Visible', 'Value', 'String', ...
        'Max', 'Min', 'Units', 'Tooltip', 'Callback', 'Tag', 'UserData', 'Type', 'InnerPosition', ...
        'OuterPosition', 'ListboxTop', 'SliderStep', 'Interruptible', 'BusyAction', 'HandleVisibility', ...
        'KeyPressFcn', 'KeyReleaseFcn', 'ButtonDownFcn', 'CreateFcn', 'DeleteFcn', 'BeingDeleted', 'Children', 'CData', ...
        'ContextMenu'});
    delete(h);
end
h = uicontrol(f, 'Style', 'text');
names = fieldnames(get(h));
fprintf('get names (%d): %s\n', numel(names), strjoin(names', ','));
s = set(h);
fprintf('set names (%d): %s\n', numel(fieldnames(s)), strjoin(fieldnames(s)', ','));
fprintf('set HorizontalAlignment options: %s\n', strjoin(s.HorizontalAlignment', '|'));
fprintf('set FontWeight options: %s\n', strjoin(s.FontWeight', '|'));
fprintf('set FontAngle options: %s\n', strjoin(s.FontAngle', '|'));
fprintf('set Enable options: %s\n', strjoin(s.Enable', '|'));
fprintf('set FontUnits options: %s\n', strjoin(s.FontUnits', '|'));
fprintf('set Style options: %s\n', strjoin(s.Style', '|'));
try, fprintf('TooltipString=%s\n', v2s(get(h, 'TooltipString'))); catch e, fprintf('TooltipString ERR %s\n', e.identifier); end
try, fprintf('UIContextMenu=%s\n', v2s(get(h, 'UIContextMenu'))); catch e, fprintf('UIContextMenu ERR %s\n', e.identifier); end
try, fprintf('Extent=%s\n', v2s(get(h, 'Extent'))); catch e, fprintf('Extent ERR %s\n', e.identifier); end

%% String coercions
probes = {'''abc''', 'abc'; '"abc"', "abc"; 'cellstr', {'a', 'bc'}; 'cellcol', {'a'; 'bc'}; ...
    'strarray', ["a", "bc"]; 'charmat', ['ab'; 'cd']; 'empty char', ''; 'empty []', []; ...
    'number 5', 5; 'number row', [1 2 3]; 'logical', true; 'empty cell', {}; 'mixed cell', {'a', 1}; ...
    'string empty', ""; 'string missing', string(missing); 'cell of string', {"a"}; 'int8', int8(65)};
for k = 1:size(probes, 1)
    try
        set(h, 'String', probes{k, 2});
        v = get(h, 'String');
        fprintf('String <- %s : %s\n', probes{k, 1}, v2s(v));
    catch e
        fprintf('String <- %s : ERR %s | %s\n', probes{k, 1}, e.identifier, oneline(e.message));
    end
end
h.String = 'dot set'; fprintf('dot String: %s\n', v2s(h.String));
fprintf('style abbreviations:\n');
for ab = {'push', 'pushb', 'Edit', 'TEXT', 'popup', 'te', 'e', 'p', 'check', 'radio', 'toggle', 'list', 'fr', 'sl'}
    try
        g = uicontrol(f, 'Style', ab{1}); fprintf('  %s -> %s\n', ab{1}, g.Style); delete(g);
    catch e
        fprintf('  %s -> ERR %s | %s\n', ab{1}, e.identifier, oneline(e.message));
    end
end

%% value words, case and abbreviations
words = {'HorizontalAlignment', {'left', 'LEFT', 'l', 'ri', 'center', 'centre', 'bogus', 5}; ...
    'FontWeight', {'bold', 'b', 'demi', 'light', 'normal', 'Bold', 'heavy'}; ...
    'FontAngle', {'italic', 'oblique', 'i', 'normal', 'slanted'}; ...
    'Enable', {'off', 'inactive', 'on', 'ON', 'in', true, false, 1, 0, 'yes'}; ...
    'Visible', {'off', 'on', true, 0, 'maybe'}; ...
    'FontUnits', {'pixels', 'normalized', 'points', 'inches', 'centimeters', 'px'}; ...
    'Units', {'normalized', 'pixels', 'characters', 'points', 'bogus'}; ...
    'Interruptible', {'off', 'on', false, 'nah'}; ...
    'BusyAction', {'cancel', 'queue', 'c', 'drop'}; ...
    'HandleVisibility', {'off', 'callback', 'on', 'c', 'maybe'}};
for w = 1:size(words, 1)
    prop = words{w, 1};
    for k = 1:numel(words{w, 2})
        val = words{w, 2}{k};
        try
            set(h, prop, val);
            fprintf('%s <- %s : %s\n', prop, v2s(val), v2s(get(h, prop)));
        catch e
            fprintf('%s <- %s : ERR %s | %s\n', prop, v2s(val), e.identifier, oneline(e.message));
        end
    end
end
set(h, 'Units', 'pixels');

%% colours, numbers and positions
colours = {[0 0.45 0.74], 'r', 'red', '#FF8000', '#f80', [2 0 0], [0.5 0.5], 'none', 'bogus', [1;0;0], uint8([255 0 0]), "blue", [-0.1 0 0]};
for prop = {'ForegroundColor', 'BackgroundColor'}
    for k = 1:numel(colours)
        try
            set(h, prop{1}, colours{k});
            v = get(h, prop{1});
            fprintf('%s <- %s : %s  (exact %s)\n', prop{1}, v2s(colours{k}), v2s(v), sprintf('%.17g ', v));
        catch e
            fprintf('%s <- %s : ERR %s | %s\n', prop{1}, v2s(colours{k}), e.identifier, oneline(e.message));
        end
    end
end
for val = {10, 0, -1, 2.5, [10 12], 'big', NaN, Inf, int8(9)}
    try
        set(h, 'FontSize', val{1}); fprintf('FontSize <- %s : %s\n', v2s(val{1}), v2s(h.FontSize));
    catch e
        fprintf('FontSize <- %s : ERR %s | %s\n', v2s(val{1}), e.identifier, oneline(e.message));
    end
end
for val = {[30 180 100 20], [1 2 3], [0 0 0 0], [10 10 -5 20], [10 10 5 -20], [1.5 2.5 3.5 4.5], 'abc', [NaN 1 1 1], [1 2 3 4 5], [1;2;3;4], int16([1 2 3 4])}
    try
        set(h, 'Position', val{1}); fprintf('Position <- %s : %s\n', v2s(val{1}), v2s(h.Position));
    catch e
        fprintf('Position <- %s : ERR %s | %s\n', v2s(val{1}), e.identifier, oneline(e.message));
    end
end
for val = {5, -1, [1 2], 'x', true, [], int8(3)}
    try
        set(h, 'Value', val{1}); fprintf('Value <- %s : %s\n', v2s(val{1}), v2s(h.Value));
    catch e
        fprintf('Value <- %s : ERR %s | %s\n', v2s(val{1}), e.identifier, oneline(e.message));
    end
end
for prop = {'Max', 'Min'}
    for val = {2, -3, 'x', [1 2]}
        try
            set(h, prop{1}, val{1}); fprintf('%s <- %s : %s\n', prop{1}, v2s(val{1}), v2s(get(h, prop{1})));
        catch e
            fprintf('%s <- %s : ERR %s | %s\n', prop{1}, v2s(val{1}), e.identifier, oneline(e.message));
        end
    end
end
for val = {'tip', "tip2", {'a', 'b'}, 5, ''}
    try
        set(h, 'Tooltip', val{1}); fprintf('Tooltip <- %s : %s\n', v2s(val{1}), v2s(h.Tooltip));
    catch e
        fprintf('Tooltip <- %s : ERR %s | %s\n', v2s(val{1}), e.identifier, oneline(e.message));
    end
end
for val = {'Arial', "Courier New", 5, ''}
    try
        set(h, 'FontName', val{1}); fprintf('FontName <- %s : %s\n', v2s(val{1}), v2s(h.FontName));
    catch e
        fprintf('FontName <- %s : ERR %s | %s\n', v2s(val{1}), e.identifier, oneline(e.message));
    end
end
try, set(h, 'Bogus', 1); catch e, fprintf('unknown prop: ERR %s | %s\n', e.identifier, oneline(e.message)); end
try, x = h.Bogus; catch e, fprintf('unknown get: ERR %s | %s\n', e.identifier, oneline(e.message)); end
try, set(h, 'Type', 'x'); catch e, fprintf('set Type: ERR %s | %s\n', e.identifier, oneline(e.message)); end
try, set(h, 'Style', 'bogus'); catch e, fprintf('set Style bogus: ERR %s | %s\n', e.identifier, oneline(e.message)); end
set(h, 'Style', 'edit'); fprintf('restyle to edit: Style=%s Bg=%s\n', h.Style, v2s(h.BackgroundColor));
set(h, 'Style', 'pushbutton'); fprintf('restyle to push: Style=%s Bg=%s\n', h.Style, v2s(h.BackgroundColor));
g = uicontrol(f, 'Style', 'text'); g.BackgroundColor = [1 0 0]; g.Style = 'edit';
fprintf('restyle after explicit colour: Bg=%s\n', v2s(g.BackgroundColor)); delete(g);
g = uicontrol(f, 'Style', 'edit'); g.Style = 'text';
fprintf('edit->text Bg=%s\n', v2s(g.BackgroundColor)); delete(g);

%% callbacks: forms and refusals
cbs = {@(s, e) disp(1), 'disp(2)', {@(s, e, a) disp(a), 3}, {'disp', 4}, '', [], 5, {5}, {}, {@disp}, "disp(7)", struct('a', 1)};
for k = 1:numel(cbs)
    try
        set(h, 'Callback', cbs{k}); v = get(h, 'Callback');
        fprintf('Callback <- %s : %s (class %s)\n', v2s(cbs{k}), v2s(v), class(v));
    catch e
        fprintf('Callback <- %s : ERR %s | %s\n', v2s(cbs{k}), e.identifier, oneline(e.message));
    end
end
for prop = {'KeyPressFcn', 'ButtonDownFcn', 'DeleteFcn', 'CreateFcn', 'KeyReleaseFcn'}
    for k = [1 2 3 7]
        try
            set(h, prop{1}, cbs{k}); fprintf('%s <- %s : %s\n', prop{1}, v2s(cbs{k}), v2s(get(h, prop{1})));
        catch e
            fprintf('%s <- %s : ERR %s | %s\n', prop{1}, v2s(cbs{k}), e.identifier, oneline(e.message));
        end
    end
end
for prop = {'KeyPressFcn', 'WindowKeyPressFcn', 'WindowButtonDownFcn', 'SizeChangedFcn', 'CloseRequestFcn', 'ButtonDownFcn', 'DeleteFcn'}
    for k = [1 2 3 7]
        try
            set(f, prop{1}, cbs{k}); fprintf('figure %s <- %s : %s\n', prop{1}, v2s(cbs{k}), v2s(get(f, prop{1})));
        catch e
            fprintf('figure %s <- %s : ERR %s | %s\n', prop{1}, v2s(cbs{k}), e.identifier, oneline(e.message));
        end
    end
end
set(f, 'KeyPressFcn', '', 'WindowKeyPressFcn', '', 'WindowButtonDownFcn', '', 'SizeChangedFcn', '', 'ButtonDownFcn', '', 'DeleteFcn', '');
set(f, 'CloseRequestFcn', 'closereq');
fprintf('figure default CloseRequestFcn: %s\n', v2s(get(groot, 'DefaultFigureCloseRequestFcn')));

%% callbacks run by the script: CreateFcn, DeleteFcn; event data of a Callback called by hand
g = uicontrol(f, 'Style', 'pushbutton', 'CreateFcn', @(s, e) fprintf('CreateFcn ran: class(e)=%s isempty=%d type=%s\n', class(e), isempty(e), s.Type));
set(g, 'DeleteFcn', @(s, e) fprintf('DeleteFcn ran: class(e)=%s\n', class(e)));
delete(g);
g = uicontrol(f, 'Style', 'pushbutton', 'CreateFcn', 'disp(''char CreateFcn ran'')');
g.DeleteFcn = {@(s, e, a) fprintf('cell DeleteFcn ran with %d, class(e)=%s\n', a, class(e)), 42};
delete(g);

%% parents, children, delete
a = axes(f);
c1 = uicontrol(f, 'Style', 'text', 'String', 'one');
c2 = uicontrol('Parent', f, 'Style', 'edit', 'String', 'two');
kids = f.Children;
fprintf('figure Children (%d): %s\n', numel(kids), strjoin(arrayfun(@(k) [k.Type ':' tagof(k)], kids, 'UniformOutput', false)', ','));
fprintf('Parent of c1 is f: %d\n', isequal(c1.Parent, f));
fprintf('findobj Style edit: %d ; findobj Type uicontrol: %d\n', numel(findobj(f, 'Style', 'edit')), numel(findobj(f, 'Type', 'uicontrol')));
fprintf('isgraphics=%d ishandle=%d isvalid=%d class=%s\n', isgraphics(c1), ishandle(c1), isvalid(c1), class(c1));
fprintf('isa uicontrol=%d isa handle=%d\n', isa(c1, 'matlab.ui.control.UIControl'), isa(c1, 'handle'));
try, uicontrol(a, 'Style', 'text'); catch e, fprintf('axes parent: ERR %s | %s\n', e.identifier, oneline(e.message)); end
try, uicontrol(c1, 'Style', 'text'); catch e, fprintf('uicontrol parent: ERR %s | %s\n', e.identifier, oneline(e.message)); end
try, uicontrol('Parent', 7777, 'Style', 'text'); catch e, fprintf('bad parent number: ERR %s | %s\n', e.identifier, oneline(e.message)); end
try, uicontrol('Style'); catch e, fprintf('odd pairs: ERR %s | %s\n', e.identifier, oneline(e.message)); end
try, uicontrol(f, 5); catch e, fprintf('numeric name: ERR %s | %s\n', e.identifier, oneline(e.message)); end
st = struct('Style', 'text', 'String', 'from struct');
try, c3 = uicontrol(f, st); fprintf('struct form: %s %s\n', c3.Style, c3.String); delete(c3); catch e, fprintf('struct form: ERR %s | %s\n', e.identifier, oneline(e.message)); end
try, c3 = uicontrol(f, 'text'); fprintf('bare word: %s\n', c3.Style); delete(c3); catch e, fprintf('bare word: ERR %s | %s\n', e.identifier, oneline(e.message)); end
c3 = uicontrol(f, 'Style', 'text', 'Visible', 'off'); fprintf('Visible off -> %s\n', char(c3.Visible)); delete(c3);
delete(c1);
fprintf('after delete: isgraphics=%d isvalid=%d\n', isgraphics(c1), isvalid(c1));
try, c1.String = 'x'; catch e, fprintf('set on deleted: ERR %s | %s\n', e.identifier, oneline(e.message)); end
try, x = get(c1, 'String'); catch e, fprintf('get on deleted: ERR %s | %s\n', e.identifier, oneline(e.message)); end
close(f, 'force');

%% no parent: makes a figure, and the current figure
close all force
n0 = numel(findall(groot, 'Type', 'figure'));
u = uicontrol('Style', 'text', 'String', 'orphan');
fprintf('no parent: figures %d -> %d, parent is gcf %d, gcf visible=%s\n', n0, numel(findall(groot, 'Type', 'figure')), isequal(u.Parent, gcf), char(gcf().Visible));
close all force

%% CloseRequestFcn as text other than closereq
f2 = figure('Visible', 'off', 'CloseRequestFcn', 'disp(''veto by text'')');
close(f2);
fprintf('after close with text CloseRequestFcn: alive=%d\n', isgraphics(f2));
f2.CloseRequestFcn = {@(s, e, msg) disp(msg), 'veto by cell'};
close(f2);
fprintf('after close with cell CloseRequestFcn: alive=%d\n', isgraphics(f2));
f2.CloseRequestFcn = @(s, e) fprintf('handle CloseRequestFcn class(e)=%s\n', class(e));
close(f2);
fprintf('after close with handle CloseRequestFcn: alive=%d\n', isgraphics(f2));
delete(f2);
fprintf('after delete: alive=%d\n', isgraphics(f2));

%% event data class
fprintf('ActionData superclasses: %s\n', strjoin(superclasses('matlab.ui.eventdata.ActionData')', ','));
m = ?matlab.ui.eventdata.ActionData;
fprintf('ActionData props: %s\n', strjoin(arrayfun(@(p) p.Name, m.PropertyList, 'UniformOutput', false)', ','));
end

function show(h, names)
for k = 1:numel(names)
    try
        fprintf('  %s = %s\n', names{k}, v2s(get(h, names{k})));
    catch e
        fprintf('  %s : ERR %s\n', names{k}, e.identifier);
    end
end
end

function t = tagof(k)
if isprop(k, 'String'), t = char(string(k.String)); else, t = ''; end
end

function s = oneline(s)
s = regexprep(strtrim(s), '\s+', ' ');
end

function s = v2s(v)
if ischar(v)
    if size(v, 1) <= 1, s = ['''' v '''']; else, s = sprintf('char %s %s', mat2str(size(v)), mat2str(v)); end
elseif isstring(v)
    if isscalar(v), if ismissing(v), s = 'string <missing>'; else, s = ['"' char(v) '"']; end, else, s = sprintf('string %s [%s]', mat2str(size(v)), strjoin(v, ',')); end
elseif iscell(v)
    parts = cellfun(@v2s, v, 'UniformOutput', false);
    s = sprintf('cell %s {%s}', mat2str(size(v)), strjoin(parts(:)', ','));
elseif isa(v, 'function_handle')
    s = ['@' func2str(v)];
elseif isa(v, 'matlab.lang.OnOffSwitchState')
    s = sprintf('OnOff %s', char(v));
elseif isnumeric(v) || islogical(v)
    if isempty(v), s = sprintf('%s %s', class(v), mat2str(size(v))); else, s = sprintf('%s %s', class(v), mat2str(v, 6)); end
elseif isstruct(v)
    s = sprintf('struct %s', strjoin(fieldnames(v)', ','));
elseif isa(v, 'matlab.graphics.Graphics') || isa(v, 'handle')
    if isempty(v), s = sprintf('%s empty', class(v)); else, s = sprintf('<%s %s>', class(v), mat2str(size(v))); end
else
    s = sprintf('<%s>', class(v));
end
end
