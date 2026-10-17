% record: -noFigureWindows
% Open item 77 (ADR 0240): a MATLAB .fig written by JGraph's savefig - the struct tree every MATLAB
% release rebuilds a figure from, with JGraph's own document beside it. helpers/oi77_jgraph.fig is
% helpers/oi77_figure.m's figure as JGraph wrote it: R2025b reads the tree, JGraph reads the
% document, and the rows hold the two readings to each other. The same figure is then saved and
% reopened live in each engine. Probes oi77_shapes, oi77_hand, oi77_verify.
hp = fullfile(pwd, 'helpers');


% --- the file JGraph wrote ------------------------------------------------------------------------------
h = openfig(fullfile(hp, 'oi77_jgraph.fig'), 'invisible');
u2_chk('file_current', @() isequal(gcf, h));
u2_chk('file_visible', @() char(h.Visible));
look('file', h);
delete(h);

% --- the same figure saved and reopened here ------------------------------------------------------------
f = oi77_figure();
tmp = [tempname '.fig'];
savefig(f, tmp);
u2_chk('live_saved', @() isfile(tmp));
close(f);
h = openfig(tmp, 'invisible');
look('live', h);
delete(h);
delete(tmp);

function look(p, h)
u2_chk([p '_figure'], @() {h.Name, h.Tag, round(h.Color, 2), h.Position, char(h.NumberTitle)}); % colours are eight bits here
u2_chk([p '_axes'], @() get(findobj(h, 'Type', 'axes'), 'Tag'));
L = findobj(h, 'Tag', 'left');
u2_chk([p '_left'], @() {round(L.Position, 4), L.Title.String, L.XLabel.String, L.YLabel.String, char(L.XGrid), L.XLim, L.XLimMode, L.YLimMode});
l1 = findobj(h, 'Tag', 'first');
u2_chk([p '_first'], @() {l1.Color, l1.LineStyle, l1.Marker, l1.LineWidth, l1.XData, l1.YData});
l2 = findobj(h, 'Tag', 'second');
u2_chk([p '_second'], @() {l2.Color, l2.LineStyle, l2.YData});
u2_chk([p '_line_order'], @() get(findobj(L, 'Type', 'line'), 'Tag'));
tx = findobj(h, 'Type', 'text', 'String', 'note'); % a text's Tag does not survive this build's document (open item 140)
u2_chk([p '_note'], @() {tx.String, tx.Position, tx.Color});
lg = findobj(h, 'Type', 'legend');
u2_chk([p '_legend'], @() {numel(lg), lg.String, lg.Location});
R = findobj(h, 'Tag', 'right');
im = findobj(R, 'Type', 'image');
u2_chk([p '_image'], @() {size(im.CData), im.CData(1, :), R.CLim});
u9b_chkdiv([p '_image_position'], @() round(R.Position, 4), '0230'); % an outside colour bar does not shrink the axes here (ADR 0230's recorded divergence)
cb = findobj(h, 'Type', 'colorbar');
u2_chk([p '_colorbar'], @() {numel(cb), cb.Limits, cb.Location});
u2_chk([p '_colormap'], @() {size(h.Colormap), round(h.Colormap(end, :), 4)});
C = findobj(h, 'Tag', 'charts');
u2_chk([p '_charts'], @() {round(C.Position, 4), char(C.Box), get(C.Children, 'Type')});
b = findobj(h, 'Tag', 'bar');
u2_chk([p '_bar'], @() {b.YData, b.FaceColor});
s = findobj(h, 'Tag', 'scatter');
u2_chk([p '_scatter'], @() {s.XData, s.YData, unique(s.SizeData), s.MarkerFaceColor});
st = findobj(h, 'Tag', 'stairs');
u2_chk([p '_stairs'], @() {st.YData, st.Color});
S = findobj(h, 'Tag', 'shapes');
pa = findobj(h, 'Tag', 'patch');
u2_chk([p '_patch'], @() {pa.FaceColor, pa.FaceAlpha, pa.Vertices(:, 1:2), pa.Faces});
u2_chk([p '_shapes'], @() {S.XLim, S.YLim, numel(S.Children)}); % the rectangle comes back a patch from this build's document (open item 140)
ed = findobj(h, 'Tag', 'ed');
u2_chk([p '_edit'], @() {ed.Style, ed.String, ed.Position, ed.Units});
pop = findobj(h, 'Tag', 'pop');
u2_chk([p '_popup'], @() {pop.Style, pop.String, pop.Value});
tb = findall(h, 'String', 'box');
u2_chk([p '_textbox'], @() {numel(tb), tb.String});
if strcmp(p, 'file')
    u9b_chkdiv([p '_textbox_position'], @() round(tb.Position, 4), '0240'); % FitBoxToText is not carried into the tree (open item 139)
else
    u2_chk([p '_textbox_position'], @() round(tb.Position, 4));
end
end
