% U0: grid 'fit' sizes and grid geometry, waiting for the (asynchronous) layout of an invisible uifigure.
uf = uifigure('Visible','off','Position',[100 100 800 900]);
probe = uilabel(uf); settle(probe, [100 100 31 22]); delete(probe); % warm the view
makers = {
  'uilabel',        @(p) uilabel(p)
  'uilabel_long',   @(p) uilabel(p,'Text','A much longer label text')
  'uilabel_2line',  @(p) uilabel(p,'Text',{'two','lines'})
  'uibutton',       @(p) uibutton(p)
  'uibutton_long',  @(p) uibutton(p,'Text','A much longer button text')
  'uibutton_state', @(p) uibutton(p,'state')
  'uieditfield',    @(p) uieditfield(p)
  'uieditfield_num',@(p) uieditfield(p,'numeric')
  'uitextarea',     @(p) uitextarea(p)
  'uidropdown',     @(p) uidropdown(p)
  'uidropdown_long',@(p) uidropdown(p,'Items',{'Short','A much longer item text'})
  'uilistbox',      @(p) uilistbox(p)
  'uicheckbox',     @(p) uicheckbox(p)
  'uicheckbox_long',@(p) uicheckbox(p,'Text','A much longer check box')
  'uislider',       @(p) uislider(p)
  'uispinner',      @(p) uispinner(p)
  'uiswitch',       @(p) uiswitch(p)
  'uiknob',         @(p) uiknob(p)
  'uigauge',        @(p) uigauge(p)
  'uilamp',         @(p) uilamp(p)
  'uidatepicker',   @(p) uidatepicker(p)
  'uiimage',        @(p) uiimage(p)
  'uihyperlink',    @(p) uihyperlink(p)
  'uicolorpicker',  @(p) uicolorpicker(p)
  'uitable',        @(p) uitable(p)
  'uitree',         @(p) uitree(p)
  'uipanel',        @(p) uipanel(p)
  'uiaxes',         @(p) uiaxes(p)
};
for k = 1:size(makers,1)
    g = uigridlayout(uf,[1 1],'RowHeight',{'fit'},'ColumnWidth',{'fit'});
    h = makers{k,2}(g);
    p0 = h.Position;
    [p, ok, dt] = settle(h, p0);
    say('fit %-17s %s  (from %s, settled=%d after %.1fs)', makers{k,1}, mat2str(p,8), mat2str(p0), ok, dt);
    delete(g);
end
% Geometry: 3x2 grid with mixed tracks inside 800x900
g = uigridlayout(uf,[3 2],'RowHeight',{'fit',100,'1x'},'ColumnWidth',{'1x','2x'});
a = uilabel(g,'Text','row1'); b = uibutton(g); c = uieditfield(g); d = uibutton(g); e1 = uilabel(g); f1 = uibutton(g);
settle(f1, f1.Position);
for h = [a b c d e1 f1], say('grid %s Row=%d Col=%d Position=%s', class(h), h.Layout.Row, h.Layout.Column, mat2str(h.Position,8)); end
g.Padding = [5 6 7 8]; g.RowSpacing = 3; g.ColumnSpacing = 4; settle(f1, f1.Position);
for h = [a f1], say('after Padding [5 6 7 8], spacing 3/4: %s Position=%s', class(h), mat2str(h.Position,8)); end
b.Layout.Row = 5; settle(b, b.Position);
say('Layout.Row=5: RowHeight=%s, b Position=%s', cellstr2(g.RowHeight), mat2str(b.Position,8));
delete(uf);

function [p, ok, dt] = settle(h, p0)
    t = tic; ok = false;
    while toc(t) < 15
        drawnow; pause(0.1);
        if ~isequal(h.Position, p0), ok = true; pause(0.3); drawnow; break; end
    end
    p = h.Position; dt = toc(t);
end
function s = cellstr2(c)
    s = strjoin(cellfun(@(v) char(string(v)), c, 'UniformOutput', false), ',');
end
function say(fmt, varargin)
    fprintf([fmt '\n'], varargin{:});
end
