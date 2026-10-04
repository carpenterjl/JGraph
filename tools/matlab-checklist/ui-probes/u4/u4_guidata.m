function u4_guidata
% U4 probe (headless): guihandles, guidata and the application data verbs in R2025b.
f = figure('Visible', 'off', 'Tag', 'mainFig');
p = uipanel(f, 'Tag', 'panel1');
a = uicontrol(f, 'Style', 'edit', 'Tag', 'nameEdit');
b = uicontrol(p, 'Style', 'pushbutton', 'Tag', 'go');
b2 = uicontrol(f, 'Style', 'pushbutton', 'Tag', 'go');
uicontrol(f, 'Style', 'text', 'Tag', 'not a name');
uicontrol(f, 'Style', 'text', 'Tag', '1abc');
uicontrol(f, 'Style', 'text');
hid = uicontrol(f, 'Style', 'text', 'Tag', 'hidden', 'HandleVisibility', 'off');
ax = axes(f, 'Tag', 'ax1');
ln = line(ax, [0 1], [0 1], 'Tag', 'ln1');
tx = text(ax, 0, 0, 'x', 'Tag', 'tx1');

h = guihandles(f);
names = fieldnames(h);
say('guihandles(f): %s', strjoin(names', ','));
for k = 1:numel(names)
    v = h.(names{k});
    say('  %s: class=%s size=%s', names{k}, class(v), mat2str(size(v)));
end
say('go order: first is b2=%d, second is b=%d', h.go(1) == b2, h.go(2) == b);
h2 = guihandles(b);
say('guihandles(control) same fields=%d', isequal(fieldnames(h2), names));
h3 = guihandles(ln);
say('guihandles(line) same fields=%d', isequal(fieldnames(h3), names));
g = figure('Visible', 'off');
he = guihandles(g);
say('guihandles(empty fig): class=%s size=%s isempty=%d', class(he), mat2str(size(he)), isempty(he));
say('guihandles() uses gcf: class=%s', class(guihandles()));
delete(g);
attempt('guihandles(5.5)', @() guihandles(5.5));
attempt('guihandles([f f])', @() guihandles([f f]));
attempt('guihandles(''a'')', @() guihandles('a'));
attempt('guihandles(0)', @() guihandles(0));
attempt('guihandles(f, 1)', @() guihandles(f, 1));

% --- guidata
say('guidata(f) before: class=%s size=%s', class(guidata(f)), mat2str(size(guidata(f))));
s.a = 1; s.b = 'two';
guidata(f, s);
say('guidata(f) after: %s', mat2str(guidata(f).a));
say('guidata(b) from a child: %s', guidata(b).b);
say('guidata(ln) from a line: %s', guidata(ln).b);
say('appdata names: %s', strjoin(fieldnames(getappdata(f))', ','));
say('isappdata UsedByGUIData_m=%d', isappdata(f, 'UsedByGUIData_m'));
s.a = 5;
say('stored copy is independent: %d', guidata(f).a);
guidata(b, 42);
say('guidata set through a child: %d', guidata(f));
guidata(f, []);
say('guidata(f, []) removes: isappdata=%d value class=%s size=%s', isappdata(f, 'UsedByGUIData_m'), class(guidata(f)), mat2str(size(guidata(f))));
guidata(f, '');
say('guidata(f, '''') : isappdata=%d', isappdata(f, 'UsedByGUIData_m'));
guidata(f, {});
say('guidata(f, {}) : isappdata=%d', isappdata(f, 'UsedByGUIData_m'));
guidata(f, 0);
say('guidata(f, 0) : isappdata=%d', isappdata(f, 'UsedByGUIData_m'));
attempt('guidata()', @() guidata());
attempt('guidata(5.5)', @() guidata(5.5));
attempt('guidata(0)', @() guidata(0));
attempt('guidata([f f])', @() guidata([f f]));
attempt('guidata(''a'', 1)', @() guidata('a', 1));
attempt('guidata(f, 1, 2)', @() guidata(f, 1, 2));
attempt('x = guidata(f, 1)', @() nout1(@() guidata(f, 1)));
d = uicontrol(f); delete(d);
attempt('guidata(deleted)', @() guidata(d));

% --- application data
setappdata(f, 'one', 1);
setappdata(f, 'two', 'b');
say('getappdata(f): %s', strjoin(fieldnames(getappdata(f))', ','));
say('getappdata(f, ''one'')=%d', getappdata(f, 'one'));
v = getappdata(f, 'none');
say('getappdata(f, ''none''): class=%s size=%s', class(v), mat2str(size(v)));
say('isappdata none=%d one=%d', isappdata(f, 'none'), isappdata(f, 'one'));
attempt('rmappdata(f, ''none'')', @() rmappdata(f, 'none'));
attempt('rmappdata(f, ''one'')', @() rmappdata(f, 'one'));
say('after rm: %s', strjoin(fieldnames(getappdata(f))', ','));
attempt('setappdata(f, 5, 1)', @() setappdata(f, 5, 1));
attempt('setappdata(f, ''not a name'', 1)', @() setappdata(f, 'not a name', 1));
say('  names: %s', strjoin(fieldnames(getappdata(f))', ','));
attempt('getappdata(5.5, ''a'')', @() getappdata(5.5, 'a'));
attempt('setappdata(5.5, ''a'', 1)', @() setappdata(5.5, 'a', 1));
attempt('isappdata(5.5, ''a'')', @() isappdata(5.5, 'a'));
attempt('rmappdata(5.5, ''a'')', @() rmappdata(5.5, 'a'));
attempt('setappdata(f, ''a'')', @() setappdata(f, 'a'));
setappdata(0, 'rootdata', 7);
say('root appdata: %d isappdata=%d', getappdata(0, 'rootdata'), isappdata(0, 'rootdata'));
rmappdata(0, 'rootdata');
say('root appdata removed: %d', isappdata(0, 'rootdata'));
setappdata(b, 'onbutton', 3);
say('appdata on a control: %d; on its figure: %d', getappdata(b, 'onbutton'), isappdata(f, 'onbutton'));
e = getappdata(a);
say('getappdata(no data): class=%s fields=%d size=%s', class(e), numel(fieldnames(e)), mat2str(size(e)));
setappdata(f, {'x1', 'x2'}, {10, 20});
say('cell names: x1=%d x2=%d', getappdata(f, 'x1'), getappdata(f, 'x2'));
attempt('rmappdata(f, {''x1'', ''x2''})', @() rmappdata(f, {'x1', 'x2'}));
say('  after: %d %d', isappdata(f, 'x1'), isappdata(f, 'x2'));
delete(f);
end

function x = nout1(fn)
x = fn();
end

function attempt(label, fn)
lastwarn('');
try
    fn();
    [msg, id] = lastwarn;
    if isempty(msg)
        say('%s: ok', label);
    else
        say('%s: ok, warned [%s] %s', label, id, msg);
    end
catch e
    say('%s: ERR [%s] %s', label, e.identifier, strrep(e.message, newline, ' / '));
end
end

function say(fmt, varargin)
fprintf([fmt '\n'], varargin{:});
end
