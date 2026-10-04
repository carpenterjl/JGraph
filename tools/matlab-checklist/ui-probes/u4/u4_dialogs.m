function u4_dialogs
% U4 probe (headless): the non-blocking classic dialogs in R2025b - msgbox and its three wrappers,
% dialog and waitbar - their argument forms, refusals and object trees.
say('ScreenPixelsPerInch=%g ScreenSize=%s', get(0, 'ScreenPixelsPerInch'), mat2str(get(0, 'ScreenSize')));
say('FactoryUicontrolFontSize=%g FactoryUicontrolFontName=%s', get(0, 'FactoryUicontrolFontSize'), get(0, 'FactoryUicontrolFontName'));

% --- dialog
d = dialog;
dumpFigure('dialog', d);
say('  ButtonDownFcn=[%s] PaperPositionMode=%s DockControls=%s Pointer=%s', d.ButtonDownFcn, d.PaperPositionMode, char(d.DockControls), d.Pointer);
say('  gcf is it=%d  findobj sees it=%d findall sees it=%d', isequal(gcf, d), ~isempty(findobj(0, 'Type', 'figure')), ~isempty(findall(0, 'Type', 'figure')));
close all force; delete(findall(0, 'Type', 'figure'));
d = dialog('Name', 'N', 'WindowStyle', 'normal', 'Position', [10 20 300 100], 'Visible', 'off', 'Tag', 't', 'Color', [1 0 0], 'Resize', 'on');
dumpFigure('dialog with options', d);
delete(d);
attempt('dialog(''Name'')', @() dialog('Name'));
attempt('dialog(''NoSuch'', 1)', @() dialog('NoSuch', 1));
attempt('dialog("Name", "s")', @() delete(dialog("Name", "s")));
attempt('dialog(''windowstyle'', ''bad'')', @() dialog('windowstyle', 'bad'));
delete(findall(0, 'Type', 'figure'));
f = figure('Visible', 'off');
for w = {'normal', 'modal', 'docked', 'alwaysontop'}
    attempt(['figure WindowStyle ' w{1}], @() set(f, 'WindowStyle', w{1}));
    say('  -> %s', get(f, 'WindowStyle'));
end
set(f, 'WindowStyle', 'normal');
delete(f);

% --- msgbox forms
cases = {
    'one arg',            @() msgbox('Hello there')
    'title',              @() msgbox('Hello there', 'Title')
    'modal',              @() msgbox('Hello there', 'Title', 'modal')
    'warn icon',          @() msgbox('Hello there', 'Title', 'warn')
    'help icon',          @() msgbox('Hello there', 'Title', 'help')
    'error icon',         @() msgbox('Hello there', 'Title', 'error')
    'none icon',          @() msgbox('Hello there', 'Title', 'none')
    'icon and modal',     @() msgbox('Hello there', 'Title', 'error', 'modal')
    'bad icon',           @() msgbox('Hello there', 'Title', 'bogus')
    'two lines cell',     @() msgbox({'First line', 'Second line is longer'}, 'T')
    'char matrix',        @() msgbox(['ab '; 'cde'], 'T')
    'newline in text',    @() msgbox(sprintf('one\ntwo'), 'T')
    'string scalar',      @() msgbox("A string", "T")
    'string array',       @() msgbox(["a" "b"], "T")
    'empty text',         @() msgbox('', 'T')
    'long text',          @() msgbox(repmat('word ', 1, 40), 'T')
    'five lines icon',    @() msgbox({'1', '2', '3', '4', '5'}, 'T', 'warn')
    'struct mode',        @() msgbox('x', 'T', struct('WindowStyle', 'modal', 'Interpreter', 'tex'))
    'struct mode icon',   @() msgbox('x', 'T', 'help', struct('WindowStyle', 'non-modal', 'Interpreter', 'none'))
    'replace',            @() msgbox('x', 'T', 'replace')
    'custom icon',        @() msgbox('x', 'T', 'custom', rand(8, 8, 3))
    'custom icon cmap',   @() msgbox('x', 'T', 'custom', magic(4), gray(16))
    'custom, no data',    @() msgbox('x', 'T', 'custom')
    'custom, cell data',  @() msgbox('x', 'T', 'custom', {1})
    'five args not custom', @() msgbox('x', 'T', 'warn', magic(4), gray(16))
    'six args',           @() msgbox('x', 'T', 'custom', magic(4), gray(16), 'modal')
    'no args',            @() msgbox()
    'seven args',         @() msgbox('x', 'T', 'custom', magic(4), gray(16), 'modal', 1)
    'numeric text',       @() msgbox(5)
    'numeric title',      @() msgbox('x', 5)
    'bad struct',         @() msgbox('x', 'T', struct('WindowStyle', 'modal'))
    'bad interpreter',    @() msgbox('x', 'T', struct('WindowStyle', 'modal', 'Interpreter', 'bogus'))
    'two outputs',        @() twoOut(@() msgbox('x'))
    'errordlg none',      @() errordlg()
    'errordlg text',      @() errordlg('Bad value')
    'errordlg title',     @() errordlg('Bad value', 'Oops')
    'errordlg modal',     @() errordlg('Bad value', 'Oops', 'modal')
    'errordlg on',        @() errordlg('Bad value', 'Oops', 'on')
    'errordlg cell',      @() errordlg({'a', 'b'}, 'Oops')
    'errordlg number',    @() errordlg(5)
    'errordlg 4 args',    @() errordlg('a', 'b', 'modal', 4)
    'warndlg none',       @() warndlg()
    'warndlg text',       @() warndlg('Careful')
    'warndlg modal',      @() warndlg('Careful', 'W', 'modal')
    'warndlg struct',     @() warndlg('Careful', 'W', struct('WindowStyle', 'modal', 'Interpreter', 'tex'))
    'helpdlg none',       @() helpdlg()
    'helpdlg text',       @() helpdlg('Help text')
    'helpdlg title',      @() helpdlg('Help text', 'H')
    'helpdlg 3 args',     @() helpdlg('Help text', 'H', 'modal')
    };
for k = 1:size(cases, 1)
    name = cases{k, 1};
    lastwarn('');
    try
        r = cases{k, 2}();
        [msg, id] = lastwarn;
        if ~isempty(msg), say('== %s: warned [%s] %s', name, id, msg); end
        say('== %s: class=%s size=%s', name, class(r), mat2str(size(r)));
        if isa(r, 'matlab.ui.Figure') && isvalid(r)
            dumpFigure(name, r);
        end
    catch e
        say('== %s: ERR [%s] %s', name, e.identifier, strrep(e.message, newline, ' / '));
    end
    delete(findall(0, 'Type', 'figure'));
end

% --- replace: the same name and tag is reused; non-modal makes another
a = msgbox('first', 'Same');
b = msgbox('second', 'Same');
say('non-modal twice: same=%d count=%d', isequal(a, b), numel(findall(0, 'Type', 'figure')));
c = msgbox('third', 'Same', 'replace');
say('replace: same as first=%d same as second=%d count=%d valid a=%d b=%d', isequal(c, a), isequal(c, b), numel(findall(0, 'Type', 'figure')), isvalid(a), isvalid(b));
tx = findall(c, 'Type', 'text');
say('  text now=[%s]', char(string(tx.String)));
c2 = msgbox('fourth', 'Same', 'modal');
say('modal reuses too: same=%d count=%d style=%s', isequal(c2, c), numel(findall(0, 'Type', 'figure')), c2.WindowStyle);
h1 = helpdlg('h1', 'HT'); h2 = helpdlg('h2', 'HT');
say('helpdlg twice: same=%d', isequal(h1, h2));
e1 = errordlg('e1', 'ET'); e2 = errordlg('e2', 'ET');
say('errordlg twice: same=%d', isequal(e1, e2));
e3 = errordlg('e3', 'ET', 'on');
say('errordlg on: reuses=%d', isequal(e3, e1) || isequal(e3, e2));
delete(findall(0, 'Type', 'figure'));

% --- the OK button and the keys
m = msgbox('press', 'P');
ok = findall(m, 'Tag', 'OKButton');
say('OK Callback=[%s] class=%s KeyPressFcn class=%s fig KeyPressFcn class=%s', ok.Callback, class(ok.Callback), class(ok.KeyPressFcn), class(m.KeyPressFcn));
say('fig CloseRequestFcn=[%s] DeleteFcn=[%s] ButtonDownFcn=[%s]', m.CloseRequestFcn, m.DeleteFcn, m.ButtonDownFcn);
tx = findall(m, 'Type', 'text');
say('text: Interpreter=%s VerticalAlignment=%s FontWeight=%s Color=%s class(String)=%s', tx.Interpreter, tx.VerticalAlignment, tx.FontWeight, mat2str(tx.Color), class(tx.String));
say('text Extent(points)=%s', mat2str(tx.Extent, 5));
delete(m);

% --- uiwait(msgbox) ended by a timer that deletes it
m = msgbox('wait', 'W');
tm = timer('StartDelay', 0.5, 'TimerFcn', @(~, ~) delete(m)); start(tm);
t = tic; uiwait(m); say('uiwait(msgbox): %.1fs valid=%d', toc(t), isvalid(m)); delete(tm);

% --- waitbar
w = waitbar(0.25, 'Working...');
dumpFigure('waitbar', w);
hs = getappdata(w, 'TMWWaitbar_handles');
say('  handles fields: %s', strjoin(fieldnames(hs)', ','));
say('  value appdata=%g', getappdata(w, 'TMWWaitbar_value'));
ttl = get(hs.axes, 'Title');
say('  title=[%s] FontSize=%g FontName=%s Units=%s Visible=%s', ttl.String, ttl.FontSize, ttl.FontName, ttl.Units, char(ttl.Visible));
pi_ = findall(w, 'Type', 'uiprogressindicator');
say('  indicator class=%s', class(pi_));
try, disp(get(pi_)); catch e, say('  get(indicator) ERR %s', e.message); end
say('  figure BusyAction=%s Interruptible=%s CloseRequestFcn=[%s]', w.BusyAction, char(w.Interruptible), w.CloseRequestFcn);
r = waitbar(0.5, w);
say('update returns same=%d value=%g indicator Value=%g', isequal(r, w), getappdata(w, 'TMWWaitbar_value'), pi_.Value);
waitbar(0.75, w, 'New message');
say('message now=[%s] Value=%g', ttl.String, pi_.Value);
waitbar(2, w); say('x=2 clamps: %g', pi_.Value);
waitbar(-1, w); say('x=-1 clamps: %g', pi_.Value);
r = waitbar(0.3);
say('waitbar(x) updates the existing one: same=%d Value=%g', isequal(r, w), pi_.Value);
attempt('waitbar()', @() waitbar());
attempt('waitbar(''a'')', @() waitbar('a'));
attempt('waitbar([1 2])', @() waitbar([1 2]));
attempt('waitbar(0.5, 5.5)', @() waitbar(0.5, 5.5));
attempt('waitbar(0.5, w, 5)', @() waitbar(0.5, w, 5));
say('  still valid=%d', isvalid(w));
delete(findall(0, 'Type', 'figure'));
w = waitbar(0, 'Named', 'Name', 'Progress', 'Color', [1 1 1]);
say('waitbar with options: Name=%s Color=%s', w.Name, mat2str(w.Color));
delete(w);
w = waitbar(0.1, 'Cancel me', 'CreateCancelBtn', 'disp(1)');
dumpFigure('waitbar with cancel', w);
say('  CloseRequestFcn=[%s]', w.CloseRequestFcn);
delete(w);
w = waitbar(0.1, {'two', 'lines'});
dumpFigure('waitbar cell message', w);
delete(w);
w = waitbar(0.1, repmat('a long message ', 1, 12));
dumpFigure('waitbar long message', w);
delete(w);
lastwarn('');
w = waitbar(0.1, 'x', 'NoSuchProp', 1);
[msg, id] = lastwarn; say('waitbar bad option: warned [%s] %s valid=%d', id, msg, isvalid(w));
delete(w);
attempt('waitbar odd options', @() waitbar(0.1, 'x', 'Name'));
delete(findall(0, 'Type', 'figure'));
w = waitbar(0.4);
say('waitbar(x) alone: title=[%s]', get(get(findall(w, 'Type', 'axes'), 'Title'), 'String'));
delete(w);
w1 = waitbar(0.1, 'a'); w2 = waitbar(0.2, 'b');
say('two waitbars: distinct=%d', ~isequal(w1, w2));
delete([w1 w2]);
w = waitbar(0.1, 'x', 'Visible', 'off');
say('waitbar Visible off: %s', char(w.Visible));
delete(w);

% --- the blocking ones refuse here, before or after their own argument checks?
attempt('questdlg()', @() questdlg());
attempt('questdlg(''a'')', @() questdlg('a'));
attempt('questdlg 7 args', @() questdlg('a', 'b', 'c', 'd', 'e', 'f', 'g'));
attempt('inputdlg()', @() inputdlg());
attempt('inputdlg bad numlines', @() inputdlg({'a', 'b'}, 't', [1 2 3]));
attempt('inputdlg 6 args', @() inputdlg('a', 't', 1, {''}, 'on', 6));
attempt('listdlg()', @() listdlg());
attempt('listdlg odd', @() listdlg('ListString'));
attempt('listdlg unknown', @() listdlg('Bogus', 1));
attempt('listdlg no list', @() listdlg('Name', 'x'));
attempt('listdlg ok', @() listdlg('ListString', {'a'}));
attempt('uigetfile', @() uigetfile());
attempt('uiputfile', @() uiputfile());
attempt('uigetdir', @() uigetdir());
attempt('uisetcolor', @() uisetcolor());
attempt('uisetfont', @() uisetfont());
attempt('uiopen', @() uiopen());
attempt('uisave', @() uisave());
attempt('uiload', @() uiload());
attempt('uigetfile 5 outputs', @() fiveOut(@() uigetfile()));
attempt('exportapp(f)', @() exportapp(figure('Visible', 'off'), fullfile(tempdir, 'u4_exportapp.png')));
attempt('exportapp()', @() exportapp());
delete(findall(0, 'Type', 'figure'));
end

function varargout = twoOut(fn)
[a, b] = fn();
varargout = {a, b};
end

function fiveOut(fn)
[a, b, c, d, e] = fn(); %#ok<ASGLU>
end

function dumpFigure(label, h)
say('   [%s] figure: Name=[%s] WindowStyle=%s Resize=%s Units=%s Position=%s Color=%s Visible=%s Tag=[%s] HandleVisibility=%s IntegerHandle=%s NumberTitle=%s MenuBar=%s ToolBar=%s', label, ...
    h.Name, h.WindowStyle, char(h.Resize), h.Units, mat2str(h.Position, 6), mat2str(h.Color, 4), ...
    char(h.Visible), h.Tag, h.HandleVisibility, char(h.IntegerHandle), char(h.NumberTitle), h.MenuBar, h.ToolBar);
say('   pixels=%s', mat2str(getpixelposition(h), 6));
ad = getappdata(h); fn = fieldnames(ad);
if ~isempty(fn), say('   appdata: %s', strjoin(fn', ', ')); end
walk(h, '   ');
end

function walk(h, ind)
kids = allchild(h);
for i = numel(kids):-1:1
    c = kids(i);
    s = sprintf('%s- %s', ind, c.Type);
    if isprop(c, 'Style'), s = [s ' Style=' c.Style]; end %#ok<*AGROW>
    if isprop(c, 'Tag') && ~isempty(c.Tag), s = [s ' Tag=' c.Tag]; end
    if isprop(c, 'Units'), s = [s ' Units=' c.Units]; end
    if isprop(c, 'Position'), s = [s ' Position=' mat2str(c.Position, 6)]; end
    if isprop(c, 'String') && ~strcmp(c.Type, 'text')
        v = c.String; if iscell(v), v = strjoin(v, '|'); end
        if ischar(v) && size(v, 1) <= 1, s = [s ' String="' v '"']; end
    end
    if isprop(c, 'FontSize'), s = sprintf('%s FontSize=%g', s, c.FontSize); end
    if isprop(c, 'FontName'), s = [s ' FontName=' c.FontName]; end
    if isprop(c, 'HorizontalAlignment'), s = [s ' HAlign=' c.HorizontalAlignment]; end
    if strcmp(c.Type, 'image'), s = [s ' CData=' mat2str(size(c.CData)) ' class=' class(c.CData) ' AlphaData=' mat2str(size(c.AlphaData))]; end
    if strcmp(c.Type, 'axes'), s = [s ' XLim=' mat2str(c.XLim) ' YLim=' mat2str(c.YLim) ' YDir=' c.YDir ' Visible=' char(c.Visible)]; end
    if strcmp(c.Type, 'text')
        v = c.String; if iscell(v), v = strjoin(v, '|'); elseif size(v, 1) > 1, v = strjoin(cellstr(v), '|'); end
        s = [s ' class(String)=' class(c.String) ' size=' mat2str(size(c.String)) ' String="' v '" Extent=' mat2str(c.Extent, 5)];
    end
    if isprop(c, 'Value') && ~isprop(c, 'Style'), s = sprintf('%s Value=%g', s, c.Value); end
    say('%s', s);
    if ~strcmp(c.Type, 'uimenu') && ~strcmp(c.Type, 'uiprogressindicator'), walk(c, [ind '  ']); end
end
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
