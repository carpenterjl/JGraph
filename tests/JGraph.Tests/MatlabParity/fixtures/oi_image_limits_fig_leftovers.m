% record: -noFigureWindows
% Open items 138 and 139 (ADR 0241). 138: image and imagesc set the axes' limits by hand to the
% pixel edges, half a pixel beyond the first and last centre (one column given two ends spans
% twice their distance); the low-level form, a held axes and an empty image leave them automatic;
% the automatic limits over an image, alone or beside a line or a second image, are the ordinary
% tick-aligned rule over the union (probes oi138_image, oi138_auto). 139: hgsave's struct tree has
% no place for a colour bar's label, a legend's title or an axes' subtitle (probe oi139_shapes):
% helpers/oi139_matlab_tree.fig is R2025b's own hgsave stripped to the tree, read here and there
% without them; each engine's own save keeps them.
hp = fullfile(pwd, 'helpers');

% --- 138: the limits the image family sets ------------------------------------------------------------
lim('im_sc', @() imagesc(magic(4)));
lim('im_image', @() image(magic(4)));
lim('im_xy', @() image([2 5], [10 40], magic(4)));
lim('im_scalar', @() image(3, 7, magic(4)));
lim('im_one', @() image(7));
lim('im_3x5', @() imagesc(ones(3, 5)));
lim('im_down', @() image([5 2], [1 4], magic(4)));
lim('im_uneven', @() image([1 2 10], [1 4], magic(4)));
lim('im_column', @() image([2 5], [1 4], (1:4)'));
lim('im_rgb', @() image(rand(4, 4, 3)));
lim('im_low', @() image('CData', magic(4)));
lim('im_low_xdata', @() image('XData', [2 5], 'CData', magic(4)));
lim('im_clims', @() imagesc(magic(4), [0 20]));
lim('im_named', @() imagesc(gca, magic(4)));
lim('im_vectors', @() imagesc(1:5, 1:3, ones(3, 5)));
lim('im_empty', @() image([]));
lim('im_set_cdata', @() then(@() image(magic(4)), @() set(findobj(gca, 'Type', 'image'), 'CData', ones(2, 7))));
lim('im_plot_after', @() then(@() imagesc(magic(4)), @() addplot(0:8, 0:8)));
lim('im_into_held', @() then(@() hold(gca, 'on'), @() imagesc(magic(4))));
modes('im_held', @() then(@() plot(1:10, (1:10) * 2), @() hold_image()));

% --- 138: the automatic limits over an image ----------------------------------------------------------
lim('au_alone', @() then(@() imagesc(magic(4)), @() xlim('auto')));
lim('au_modes', @() then(@() imagesc(magic(4)), @() set(gca, 'XLimMode', 'auto', 'YLimMode', 'auto')));
lim('au_tight', @() then(@() imagesc(magic(4)), @() axis('tight')));
lim('au_image', @() then(@() imagesc(magic(4)), @() axis('image')));
lim('au_uneven', @() then(@() image([1 2 10], [1 4], magic(4)), @() xlim('auto')));
lim('au_inside', @() with_line([1 3], [1 2]));
lim('au_47', @() with_line([1 4.7], [1 2]));
lim('au_08', @() with_line(0:8, 0:8));
lim('au_y6', @() with_line([1 2], [0 6]));
lim('au_7', @() with_line([1 7], [1 2]));
lim('au_12', @() with_line([1 12], [1 2]));
lim('au_neg', @() with_line([-2 2], [1 2]));
lim('au_two', @() with(@() image([6 9], [1 4], magic(4))));

% --- 139: each engine's own save keeps the leftovers ----------------------------------------------------
f = figure('Visible', 'off', 'Tag', 'leftovers');
ax = axes(f);
plot(ax, 1:5, [1 4 2 5 3]);
hold(ax, 'on');
plot(ax, 1:5, [2 2 3 3 4]);
title(ax, 'Main');
subtitle(ax, 'Below the main');
xlabel(ax, 'X');
lg = legend(ax, 'a', 'b');
title(lg, 'Series');
set(ax, 'Tag', 'left');
g = figure('Visible', 'off', 'Tag', 'heat');
ax2 = axes(g);
imagesc(ax2, magic(4));
cb = colorbar(ax2);
cb.Label.String = 'Heat';
set(ax2, 'Tag', 'right');
u9b_chkdiv('rt_legend_title_class', @() class(lg.Title), '0241'); % a text object there, text here (open item 141)
tmp1 = [tempname '.fig'];
tmp2 = [tempname '.fig'];
u9b_chkdiv('rt_save_warning', @() save_warning(f, tmp1), '0241'); % this build names what MATLAB's copy leaves out
savefig(g, tmp2);
close(f);
close(g);
h = openfig(tmp1, 'invisible');
L = findobj(h, 'Tag', 'left');
u2_chk('rt_title', @() L.Title.String);
u2_chk('rt_subtitle', @() L.Subtitle.String);
u2_chk('rt_xlabel', @() L.XLabel.String);
lg = findobj(h, 'Type', 'legend');
u2_chk('rt_legend', @() lg.String);
u2_chk('rt_legend_title', @() legend_title(lg));
delete(h);
h = openfig(tmp2, 'invisible');
cb = findobj(h, 'Type', 'colorbar');
u2_chk('rt_cb_label', @() cb.Label.String);
u2_chk('rt_cb_limits', @() cb.Limits);
R = findobj(h, 'Tag', 'right');
u2_chk('rt_right_limits', @() {R.XLim, R.XLimMode, R.YLim, R.YLimMode});
delete(h);
delete(tmp1);
delete(tmp2);

% --- 139: R2025b's own tree, read in both engines without them -----------------------------------------
h = openfig(fullfile(hp, 'oi139_matlab_tree.fig'), 'invisible');
L = findobj(h, 'Tag', 'left');
u2_chk('tree_title', @() L.Title.String);
u2_chk('tree_subtitle', @() L.Subtitle.String);
u2_chk('tree_xlabel', @() L.XLabel.String);
u2_chk('tree_lines', @() numel(findobj(L, 'Type', 'line')));
lg = findobj(h, 'Type', 'legend');
u2_chk('tree_legend', @() {numel(lg), lg.String});
u2_chk('tree_legend_title', @() legend_title(lg));
cb = findobj(h, 'Type', 'colorbar');
u2_chk('tree_cb_label', @() {numel(cb), cb.Label.String});
u2_chk('tree_cb_limits', @() cb.Limits);
R = findobj(h, 'Tag', 'right');
u2_chk('tree_right_limits', @() {R.XLim, R.XLimMode, R.YLim, R.YLimMode});
delete(h);

function lim(name, make)
f = figure('Visible', 'off');
make();
ax = gca;
u2_chk(name, @() {ax.XLim, ax.XLimMode, ax.YLim, ax.YLimMode, ax.YDir});
close(f);
end

function modes(name, make)
f = figure('Visible', 'off');
make();
ax = gca;
u2_chk(name, @() {ax.XLimMode, ax.YLimMode, ax.YDir});
close(f);
end

function then(a, b)
a();
b();
end

function addplot(x, y)
hold on;
plot(x, y);
end

function hold_image()
hold on;
imagesc(magic(4));
end

function with_line(x, y)
with(@() plot(x, y));
end

function with(make)
imagesc(magic(4));
hold on;
make();
axis auto;
end

function t = legend_title(lg)
% A legend's Title is a text object in R2025b and text here (open item 141): the words either way.
t = lg.Title;
if ~ischar(t) && ~isstring(t)
    t = t.String;
end
t = char(t);
end

function id = save_warning(f, file)
lastwarn('');
savefig(f, file);
[~, id] = lastwarn;
end
