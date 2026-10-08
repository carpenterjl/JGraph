% u11_open - opens the .fig files u11_figs and u11_guide wrote, and prints what came back. Runs in
% R2025b and in JGraph; the two outputs are diffed. Headless.
here = fileparts(mfilename('fullpath'));
out = fullfile(here, 'figs');
global U11LOG %#ok<GVMIS>
U11LOG = {};

fprintf('--- plots\n');
pf = fullfile(out, 'u11_plots.fig');
h = openfig(pf, 'invisible');
fprintf('class=%s Visible=%s Name=%s NumberTitle=%s Color=%s\n', class(h), h.Visible, h.Name, h.NumberTitle, mat2str(h.Color, 4));
fprintf('Position=%s FileName ok=%d\n', mat2str(h.Position), strcmp(h.FileName, pf));
axs = findobj(h, 'Type', 'axes');
fprintf('axes count=%d\n', numel(axs));
for i = 1:numel(axs)
    a = axs(i);
    fprintf('axes %d: Position=%s title=%s xlabel=%s ylabel=%s XGrid=%s YDir=%s XLimMode=%s\n', i, mat2str(a.Position, 4), ...
        a.Title.String, a.XLabel.String, a.YLabel.String, a.XGrid, a.YDir, a.XLimMode);
    k = cellstr(get(a.Children, 'Type'));
    k = k(~ismember(k, {'legend', 'colorbar'}));
    fprintf('  children: %s\n', strjoin(k', ','));
end
ln = findobj(h, 'Tag', 'first');
fprintf('first line: Color=%s LineStyle=%s Marker=%s LineWidth=%g XData=%s YData=%s\n', mat2str(ln.Color), ln.LineStyle, ln.Marker, ln.LineWidth, mat2str(ln.XData), mat2str(ln.YData));
l2 = findobj(h, 'Type', 'line', 'LineStyle', '--');
fprintf('second line: n=%d Color=%s LineStyle=%s\n', numel(l2), mat2str(l2.Color, 4), l2.LineStyle);
tx = findobj(h, 'Type', 'text', 'String', 'note');
fprintf('note text: n=%d Position=%s Color=%s\n', numel(tx), mat2str(tx.Position, 4), mat2str(tx.Color, 4));
im = findobj(h, 'Type', 'image');
fprintf('image: n=%d CData=%s CDataMapping=%s\n', numel(im), mat2str(im.CData), im.CDataMapping);
lg = findobj(h, 'Type', 'legend');
fprintf('legend: n=%d String=%s\n', numel(lg), strjoin(lg.String, '|'));
cb = findobj(h, 'Type', 'colorbar');
fprintf('colorbar: n=%d\n', numel(cb));
fprintf('Colormap=%s\n', mat2str(size(h.Colormap)));
delete(h);

fprintf('--- gui\n');
h = openfig(fullfile(out, 'u11_gui.fig'), 'invisible');
fprintf('Name=%s MenuBar=%s Tag=%s Position=%s\n', h.Name, h.MenuBar, h.Tag, mat2str(h.Position));
p = findobj(h, 'Tag', 'panel1');
fprintf('panel: Title=%s Units=%s Position=%s children=%s\n', p.Title, p.Units, mat2str(p.Position, 4), strjoin(get(p.Children, 'Tag')', ','));
bg = findobj(h, 'Tag', 'bg');
fprintf('bg: Title=%s SelectedObject=%s children=%s\n', bg.Title, bg.SelectedObject.Tag, strjoin(get(bg.Children, 'Tag')', ','));
pop = findobj(h, 'Tag', 'pop');
fprintf('pop: Style=%s Value=%d String=%s Position=%s\n', pop.Style, pop.Value, strjoin(pop.String', '|'), mat2str(pop.Position));
lb = findobj(h, 'Tag', 'lb');
fprintf('lb: Max=%g Value=%s\n', lb.Max, mat2str(lb.Value));
sl = findobj(h, 'Tag', 'sl');
fprintf('sl: Units=%s Position=%s Min=%g Max=%g Value=%g\n', sl.Units, mat2str(sl.Position, 4), sl.Min, sl.Max, sl.Value);
chk = findobj(h, 'Tag', 'chk');
fprintf('chk: Value=%g String=%s\n', chk.Value, chk.String);
go = findobj(h, 'Tag', 'go');
fprintf('go: Style=%s Callback=%s\n', go.Style, go.Callback);
ed = findobj(h, 'Tag', 'ed');
fprintf('ed: String=%s BackgroundColor=%s ContextMenu=%s\n', ed.String, mat2str(ed.BackgroundColor), strjoin(cellstr(get(ed.ContextMenu.Children, 'Tag'))', ','));
txt = findobj(h, 'Tag', 'txt');
fprintf('txt: String=%s FontWeight=%s HorizontalAlignment=%s\n', strjoin(txt.String', '|'), txt.FontWeight, txt.HorizontalAlignment);
mf = findobj(h, 'Tag', 'mfile');
mo = findobj(h, 'Tag', 'mopen');
fprintf('menus: %s / %s Accelerator=%s MenuSelectedFcn=%s\n', mf.Text, mo.Text, mo.Accelerator, func2str(mo.MenuSelectedFcn));
t = findobj(h, 'Tag', 'tbl');
fprintf('table: Data=%s ColumnName=%s Position=%s\n', mat2str(t.Data), strjoin(t.ColumnName', ','), mat2str(t.Position));
fprintf('figure children: %s\n', strjoin(get(h.Children, 'Tag')', ','));
delete(h);

fprintf('--- surf\n');
U11LOG = {};
h = openfig(fullfile(out, 'u11_surf.fig'));
fprintf('Visible=%s creates=%s\n', h.Visible, strjoin(U11LOG, ','));
a = findobj(h, 'Type', 'axes');
fprintf('View=%s XGrid=%s children=%s\n', mat2str(a.View), a.XGrid, strjoin(cellstr(get(a.Children, 'Type'))', ','));
s = findobj(h, 'Type', 'surface');
fprintf('surface: ZData size=%s\n', mat2str(size(s.ZData)));
pa = findobj(h, 'Type', 'patch');
fprintf('patch: FaceColor=%s FaceAlpha=%g\n', mat2str(pa.FaceColor), pa.FaceAlpha);
delete(h);

fprintf('--- old forms\n');
h = openfig(fullfile(out, 'u11_hgsave.fig'), 'invisible');
fprintf('hgsave: lines=%d Name=%s\n', numel(findobj(h, 'Type', 'line')), h.Name);
delete(h);
h = openfig(fullfile(out, 'u11_hgsonly.fig'), 'invisible');
fprintf('hgS only: lines=%d YData=%s\n', numel(findobj(h, 'Type', 'line')), mat2str(get(findobj(h, 'Type', 'line'), 'YData')));
delete(h);
h = openfig(fullfile(out, 'u11_labels7.fig'), 'invisible');
a = findobj(h, 'Type', 'axes');
fprintf('labels7: title=%s xlabel=%s ylabel=%s XScale=%s XLim=%s YLimMode=%s legend=%s\n', a.Title.String, a.XLabel.String, a.YLabel.String, a.XScale, mat2str(a.XLim), a.YLimMode, strjoin(get(findobj(h, 'Type', 'legend'), 'String'), '|'));
delete(h);
h = openfig(fullfile(out, 'u11_labels.fig'), 'invisible');
a = findobj(h, 'Type', 'axes');
fprintf('labels: title=%s xlabel=%s ylabel=%s XScale=%s XLim=%s YLimMode=%s legend=%s\n', a.Title.String, a.XLabel.String, a.YLabel.String, a.XScale, mat2str(a.XLim), a.YLimMode, strjoin(get(findobj(h, 'Type', 'legend'), 'String'), '|'));
delete(h);
