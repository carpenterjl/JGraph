% U0: root screen values, units conversions, grid 'fit' sizes, Layout.Row out of range,
% focus/uialert/uiprogressdlg on an invisible figure, and the waitbar's tree.
r = groot;
say('ScreenSize=%s ScreenPixelsPerInch=%g MonitorPositions=%s Units=%s', mat2str(r.ScreenSize), r.ScreenPixelsPerInch, mat2str(r.MonitorPositions), r.Units);
for u = {'pixels','points','inches','centimeters','normalized','characters'}
    set(r,'Units',u{1}); say('  root ScreenSize in %s = %s', u{1}, mat2str(get(r,'ScreenSize'),8));
end
set(r,'Units','pixels');

% --- units on a classic figure and a uicontrol
f = figure('Units','pixels','Position',[100 100 560 420]);
c = uicontrol(f,'Style','pushbutton','Units','pixels','Position',[20 30 100 40],'String','abc');
for u = {'pixels','points','inches','centimeters','normalized','characters'}
    c.Units = u{1}; say('uicontrol Position in %-11s = %s', u{1}, mat2str(c.Position,8));
    f.Units = u{1}; say('figure    Position in %-11s = %s', u{1}, mat2str(f.Position,8));
end
c.Units = 'characters'; c.Position = [1 1 1 1]; c.Units = 'pixels'; say('1x1 characters in pixels = %s', mat2str(c.Position,8));
f.Units = 'pixels';
p = uipanel(f,'Units','pixels','Position',[50 60 200 150],'Title','P');
q = uicontrol(p,'Style','text','Units','normalized','Position',[0 0 1 1]);
q.Units = 'pixels'; say('normalized [0 0 1 1] in a 200x150 titled panel = %s', mat2str(q.Position,8));
p.BorderType = 'none'; p.Title = ''; drawnow; q.Units='normalized'; q.Position=[0 0 1 1]; q.Units='pixels';
say('normalized [0 0 1 1] in a 200x150 untitled borderless panel = %s', mat2str(q.Position,8));
% uicontrol Extent for a few strings
c.Units='pixels'; c.Style='text';
for s = {'a','abc','Hello World','MMMMMMMMMM'}
    c.String = s{1}; say('Extent text "%s" = %s', s{1}, mat2str(c.Extent,8));
end
c.Style='pushbutton'; c.String='OK'; say('Extent pushbutton "OK" = %s', mat2str(c.Extent,8));
delete(f);

% --- grid 'fit' sizes for each component's default
uf = uifigure('Visible','off','Position',[100 100 800 900]);
makers = {
  'uilabel',        @(p) uilabel(p)
  'uibutton',       @(p) uibutton(p)
  'uibutton_state', @(p) uibutton(p,'state')
  'uieditfield',    @(p) uieditfield(p)
  'uieditfield_num',@(p) uieditfield(p,'numeric')
  'uitextarea',     @(p) uitextarea(p)
  'uidropdown',     @(p) uidropdown(p)
  'uilistbox',      @(p) uilistbox(p)
  'uicheckbox',     @(p) uicheckbox(p)
  'uiradiobutton_inbg', []
  'uislider',       @(p) uislider(p)
  'uispinner',      @(p) uispinner(p)
  'uiswitch',       @(p) uiswitch(p)
  'uiknob',         @(p) uiknob(p)
  'uigauge',        @(p) uigauge(p)
  'uilamp',         @(p) uilamp(p)
  'uidatepicker',   @(p) uidatepicker(p)
  'uiimage',        @(p) uiimage(p)
  'uihyperlink',    @(p) uihyperlink(p)
  'uitable',        @(p) uitable(p)
  'uitree',         @(p) uitree(p)
  'uipanel',        @(p) uipanel(p)
  'uiaxes',         @(p) uiaxes(p)
  'uicolorpicker',  @(p) uicolorpicker(p)
  'uispinner_long', @(p) uispinner(p,'Value',123456789)
  'uilabel_long',   @(p) uilabel(p,'Text','A much longer label text')
  'uibutton_long',  @(p) uibutton(p,'Text','A much longer button text')
};
for k = 1:size(makers,1)
    if isempty(makers{k,2}), continue; end
    g = uigridlayout(uf,[1 1],'RowHeight',{'fit'},'ColumnWidth',{'fit'});
    try
        h = makers{k,2}(g);
        drawnow;
        say('fit %-18s Position=%s (default Position when unparented-to-grid: see get dumps)', makers{k,1}, mat2str(h.Position));
    catch e
        say('fit %-18s ERROR %s', makers{k,1}, e.message);
    end
    delete(g);
end
% Grid geometry: 3x2 grid with mixed tracks inside an 800x900 figure
g = uigridlayout(uf,[3 2],'RowHeight',{'fit',100,'1x'},'ColumnWidth',{'1x','2x'});
a = uilabel(g,'Text','row1'); b = uibutton(g); cbx = uieditfield(g); d = uibutton(g); e1 = uilabel(g); f1 = uibutton(g);
drawnow;
for h = [a b cbx d e1 f1], say('grid child %s Row=%s Col=%s Position=%s', class(h), mat2str(h.Layout.Row), mat2str(h.Layout.Column), mat2str(h.Position)); end
% Layout.Row out of range
try
    b.Layout.Row = 5; drawnow;
    say('Layout.Row=5 in a 3-row grid: ok, RowHeight now %s, Position=%s', strjoin(cellfun(@num2strOrChar, g.RowHeight, 'UniformOutput', false), ','), mat2str(b.Position));
catch e
    say('Layout.Row=5: ERROR %s | %s', e.identifier, e.message);
end
try
    a.Layout.Column = [1 2]; drawnow; say('Column span [1 2]: Position=%s', mat2str(a.Position));
catch e
    say('Column span: ERROR %s | %s', e.identifier, e.message);
end
% Auto-placement past the grid's cells
g2 = uigridlayout(uf,[1 1]);
x1 = uibutton(g2); x2 = uibutton(g2); x3 = uibutton(g2); drawnow;
say('auto-placed 3 into 1x1: rows=%d cols=%d, x3 Row=%d Col=%d', numel(g2.RowHeight), numel(g2.ColumnWidth), x3.Layout.Row, x3.Layout.Column);
delete(g2); delete(g);

% --- focus / uialert / uiprogressdlg / uiconfirm on an invisible uifigure
ed = uieditfield(uf);
try, focus(ed); say('focus on invisible: ok'); catch e, say('focus on invisible: %s | %s', e.identifier, e.message); end
try, uialert(uf,'msg','title'); say('uialert on invisible: ok'); catch e, say('uialert on invisible: %s | %s', e.identifier, e.message); end
try, d = uiprogressdlg(uf,'Title','T','Message','M','Value',0.3); say('uiprogressdlg on invisible: ok class=%s Value=%g', class(d), d.Value); close(d); catch e, say('uiprogressdlg on invisible: %s | %s', e.identifier, e.message); end
try, s = uiconfirm(uf,'Sure?','T'); say('uiconfirm on invisible: returned %s', s); catch e, say('uiconfirm on invisible: %s | %s', e.identifier, e.message); end
delete(uf);

% --- waitbar tree (its bar is a component in R2025b)
w = waitbar(0.25,'Working...');
kids = allchild(w);
for i = 1:numel(kids)
    k = kids(i); say('waitbar child %s class=%s', k.Type, class(k));
end
pi1 = findall(w,'Type','uiprogressindicator');
if ~isempty(pi1), say('uiprogressindicator Value=%g Position=%s Units? %d', pi1.Value, mat2str(pi1.Position), isprop(pi1,'Units')); end
say('findobj(w,''Type'',''patch'') count=%d', numel(findobj(w,'Type','patch')));
ax = findall(w,'Type','axes'); say('waitbar axes Title="%s"', ax.Title.String);
waitbar(0.6, w, 'Later'); say('after update: indicator Value=%g, title="%s"', pi1.Value, ax.Title.String);
props = properties(pi1); say('uiprogressindicator props: %s', strjoin(props', ' '));
delete(w);

function s = num2strOrChar(v)
    if ischar(v) || isstring(v), s = char(v); else, s = num2str(v); end
end
function say(fmt, varargin)
    fprintf([fmt '\n'], varargin{:});
end
