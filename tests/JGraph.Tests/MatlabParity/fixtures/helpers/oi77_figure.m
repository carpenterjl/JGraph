function f = oi77_figure()
% OI77_FIGURE The figure behind the oi_fig_struct_form fixture (open item 77, ADR 0240): lines with
% a title, labels, a legend and a text; an image with a colour bar; a bar, a scatter and a stair
% chart; a patch and a rectangle; two classic controls; a text box. helpers/oi77_jgraph.fig is this
% figure as JGraph's savefig writes it (FigFileWriteTests writes it with JGRAPH_OI77_OUT set); the
% fixture also saves and reopens it live in each engine. plot's 'Tag' option is set afterwards
% because this build's plot refuses it among its options (open item 136).
f = figure('Visible', 'off', 'Name', 'oi77', 'NumberTitle', 'off', 'Position', [100 100 640 400], 'Color', [1 1 0.9], 'Tag', 'oi77');
ax1 = subplot(2, 2, 1);
l1 = plot(ax1, 1:5, [1 4 2 5 3], 'r-o', 'LineWidth', 2);
set(l1, 'Tag', 'first');
hold(ax1, 'on');
l2 = plot(ax1, 1:5, [2 2 3 3 4], 'b--');
set(l2, 'Tag', 'second');
title(ax1, 'Top'); xlabel(ax1, 'X'); ylabel(ax1, 'Y');
legend(ax1, 'a', 'b', 'Location', 'northwest');
grid(ax1, 'on');
text(ax1, 2, 3, 'note', 'Color', [0 0.6 0], 'Tag', 'note');
set(ax1, 'Tag', 'left', 'XLim', [0 6]);
ax2 = subplot(2, 2, 2);
imagesc(ax2, magic(4));
colorbar(ax2);
set(ax2, 'Tag', 'right');
colormap(f, hot(16));
ax3 = subplot(2, 2, 3);
hold(ax3, 'on');
b = bar(ax3, [3 1 2], 'FaceColor', [0.2 0.6 0.2]);
set(b, 'Tag', 'bar');
s = scatter(ax3, 1:3, [2 3 1], 60, 'filled', 'MarkerFaceColor', [0.8 0 0]);
set(s, 'Tag', 'scatter');
st = stairs(ax3, 1:4, [1 3 2 4], 'Color', [0 0 0.8]);
set(st, 'Tag', 'stairs');
set(ax3, 'Tag', 'charts', 'Box', 'on');
ax4 = subplot(2, 2, 4);
hold(ax4, 'on');
p = patch(ax4, 'Faces', [1 2 3; 1 3 4], 'Vertices', [0 0; 1 0; 1 1; 0 1], 'FaceColor', 'g', 'FaceAlpha', 0.5);
set(p, 'Tag', 'patch');
r = rectangle(ax4, 'Position', [0.2 0.2 0.3 0.3], 'Curvature', 0.5, 'FaceColor', [1 1 0]);
set(r, 'Tag', 'rect');
set(ax4, 'Tag', 'shapes', 'XLim', [0 1], 'YLim', [0 1]);
uicontrol(f, 'Style', 'edit', 'String', 'abc', 'Position', [10 10 80 24], 'Tag', 'ed');
uicontrol(f, 'Style', 'popupmenu', 'String', {'one', 'two', 'three'}, 'Value', 2, 'Position', [100 10 100 24], 'Tag', 'pop');
annotation(f, 'textbox', [0.4 0.01 0.2 0.05], 'String', 'box', 'Tag', 'tbx');
end
