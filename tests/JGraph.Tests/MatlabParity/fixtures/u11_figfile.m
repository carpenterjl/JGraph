% record: -noFigureWindows
% U11 of the app-building plan (ADR 0210): MATLAB figure files opened by openfig and hgload - the
% subsystem form R2025b's savefig writes (hgS_080000 empty), the struct form hgsave writes, and a
% file with the struct tree alone - their objects, the options, the refusals, and the order the
% objects' CreateFcns run in. The files are R2025b's own, written by ui-probes/u11/u11_makefix.m.
% Probes u11_figs, u11_hgm, u11_open, u11_lte.
hp = fullfile(pwd, 'helpers');
u11_log();

% --- R2025b's savefig: two axes, lines, labels, a legend, a text, an image and a colour bar
pf = fullfile(hp, 'u11_plots.fig');
h = openfig(pf, 'invisible');
u9b_chk('plots_figure', @() {h.Visible, h.Name, h.NumberTitle, h.Color, h.Position, h.Tag});
u9b_chk('plots_filename', @() strcmp(h.FileName, pf));
u9b_chk('plots_current', @() isequal(gcf, h));
L = findobj(h, 'Tag', 'left');
R = findobj(h, 'Tag', 'right');
u9b_chk('left_axes', @() {round(L.Position, 6), L.Title.String, L.XLabel.String, L.YLabel.String, L.XGrid, L.YGrid, L.XLimMode});
u9b_chk('right_axes', @() {round(R.Position, 6), R.YDir, R.XLim, R.YLim, R.XLimMode, R.CLim});
ln = findobj(h, 'Tag', 'first');
u9b_chk('first_line', @() {ln.Color, ln.LineStyle, ln.Marker, ln.LineWidth, ln.XData, ln.YData});
l2 = findobj(h, 'Tag', 'second');
u9b_chk('second_line', @() {l2.Color, l2.LineStyle, l2.YData});
u9b_chk('line_order', @() get(findobj(L, 'Type', 'line'), 'Tag'));
tx = findobj(h, 'Tag', 'note');
u9b_chk('note_text', @() {tx.String, tx.Position, tx.Color});
im = findobj(h, 'Type', 'image');
u9b_chk('image', @() {im.CData, im.CDataMapping});
lg = findobj(h, 'Type', 'legend');
u9b_chk('legend', @() {numel(lg), lg.String});
u9b_chk('colorbar', @() numel(findobj(h, 'Type', 'colorbar')));
u9b_chk('colormap', @() {size(h.Colormap), h.Colormap(end, :)});
delete(h);

% --- the classic controls, a panel, a button group, menus, a context menu and a table
h = openfig(fullfile(hp, 'u11_gui.fig'), 'invisible');
u9b_chk('gui_figure', @() {h.Name, h.MenuBar, h.Tag, h.Position});
p = findobj(h, 'Tag', 'panel1');
u9b_chk('panel', @() {p.Title, p.Units, p.Position, get(p.Children, 'Tag')});
bg = findobj(h, 'Tag', 'bg');
u9b_chk('buttongroup', @() {bg.Title, bg.SelectedObject.Tag, get(bg.Children, 'Tag')});
pop = findobj(h, 'Tag', 'pop');
u9b_chk('popup', @() {pop.Style, pop.Value, pop.String, pop.Position});
lb = findobj(h, 'Tag', 'lb');
u9b_chk('listbox', @() {lb.Max, lb.Value, lb.String});
sl = findobj(h, 'Tag', 'sl');
u9b_chk('slider', @() {sl.Units, sl.Position, sl.Min, sl.Max, sl.Value});
chk = findobj(h, 'Tag', 'chk');
u9b_chk('checkbox', @() {chk.Value, chk.String});
go = findobj(h, 'Tag', 'go');
u9b_chk('push_callback', @() {go.Style, go.Callback});
ed = findobj(h, 'Tag', 'ed');
u9b_chk('edit', @() {ed.String, ed.BackgroundColor, get(ed.ContextMenu.Children, 'Tag')});
txt = findobj(h, 'Tag', 'txt');
u9b_chk('text_control', @() {txt.String, txt.FontWeight, txt.HorizontalAlignment});
mf = findobj(h, 'Tag', 'mfile');
mo = findobj(h, 'Tag', 'mopen');
u9b_chk('menus', @() {mf.Text, mo.Text, mo.Accelerator});
mo.MenuSelectedFcn(mo, []);
u9b_chk('menu_callback_captured', @() u11_log());
t = findobj(h, 'Tag', 'tbl');
u9b_chk('table', @() {t.Data, t.ColumnName, t.Position});
delete(h);

% --- a surface, a patch and a line in a 3-D view, saved invisible, with CreateFcns
h = openfig(fullfile(hp, 'u11_surf.fig'));
u9b_chk('surf_visible', @() h.Visible);
u9b_chkdiv('surf_creates', @() u11_log(), '0210');
a = findobj(h, 'Type', 'axes');
u9b_chk('surf_axes', @() {a.View, get(a.Children, 'Type')});
u9b_chk('surface', @() size(get(findobj(h, 'Type', 'surface'), 'ZData')));
pa = findobj(h, 'Tag', 'tri');
u9b_chk('patch', @() {pa.FaceColor, pa.FaceAlpha});
delete(h);

% --- a log axis, a limit, labels and a legend, in both forms
for name = {'u11_labels', 'u11_labels7'}
    h = openfig(fullfile(hp, [name{1} '.fig']), 'invisible');
    a = findobj(h, 'Type', 'axes');
    u9b_chk([name{1} '_axes'], @() {a.Title.String, a.XLabel.String, a.YLabel.String, a.XScale, a.XLim, a.XLimMode, a.YLimMode, a.XGrid});
    u9b_chk([name{1} '_legend'], @() get(findobj(h, 'Type', 'legend'), 'String'));
    u9b_chk([name{1} '_line'], @() get(findobj(h, 'Type', 'line'), 'YData'));
    delete(h);
end

% --- the order CreateFcns run in, and the children's order, in each form
u11_log();
h = openfig(fullfile(hp, 'u11_order.fig'), 'invisible');
u9b_chkdiv('order_savefig', @() u11_log(), '0210');
u9b_chk('order_savefig_children', @() get(h.Children, 'Tag'));
delete(h);
h = openfig(fullfile(hp, 'u11_order7.fig'), 'invisible');
u9b_chkdiv('order_hgsave', @() u11_log(), '0210');
delete(h);
h = openfig(fullfile(hp, 'u11_order7s.fig'), 'invisible');
u9b_chk('order_struct_only', @() u11_log());
u9b_chk('order_struct_children', @() get(h.Children, 'Tag'));
u9b_chk('order_struct_axes', @() get(get(findobj(h, 'Tag', 'A1'), 'Children'), 'Tag'));
u9b_chk('order_struct_panel', @() get(get(findobj(h, 'Tag', 'P1'), 'Children'), 'Tag'));
delete(h);

% --- the options and the refusals
h1 = openfig(pf, 'invisible');
h2 = openfig(pf, 'reuse', 'invisible');
h3 = openfig(pf, 'new', 'invisible');
u9b_chk('reuse', @() [isequal(h1, h2), isequal(h1, h3)]);
delete([h1 h3]);
h = openfig(pf, 'reuse');
u9b_chk('reuse_none_open', @() {isgraphics(h), h.Visible});
delete(h);
h = openfig(fullfile(hp, 'u11_surf.fig'), 'visible');
u9b_chk('visible_option', @() h.Visible);
delete(h);
u11_log();
h = openfig(fullfile(hp, 'u11_plots'), 'INVISIBLE');
u9b_chk('no_extension', @() {h.Name, h.Visible});
delete(h);
u9b_chk('bad_option', @() openfig(pf, 'bogus'));
u9b_chk('missing', @() openfig('u11_nope.fig'));
u9b_chk('missing_no_extension', @() openfig('u11_nope'));
nf = [tempname '.mat'];
x = 1; %#ok<NASGU>
save(nf, 'x');
u9b_chk('not_a_figure', @() openfig(nf));
delete(nf);

% --- hgload
[h, old] = hgload(pf);
u9b_chk('hgload', @() {h.Visible, h.Name, class(old), size(old)});
delete(h);
[h, old] = hgload(pf, struct('Visible', 'off', 'Name', 'renamed'));
u9b_chk('hgload_props', @() {h.Visible, h.Name, old});
delete(h);

