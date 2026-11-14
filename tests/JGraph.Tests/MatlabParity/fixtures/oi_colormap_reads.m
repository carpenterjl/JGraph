% record: -noFigureWindows
% Open item 293 (ADR 0361): what colormap, f.Colormap, ax.Colormap and get(gca, 'Colormap') read
% back after each way of setting a map; a generator named without a size taking the current
% figure's height (colormap('hot') after a 5-row map is hot(5)); an unaddressed colormap setting
% the figure, and a figure's map, however set, taking every axes back from a map of its own; c =
% colormap(...) answering the map; colormap('default') and 'name(k)'; and a bare prism recolouring
% the current axes' lines while flag does not. Probes cm1 and cm2 (agent V's scratch).
close all force

f = figure('Visible', 'off');
pm('a_fig_default', f.Colormap);
ax = axes(f);
pm('a_gca_default', get(gca, 'Colormap'));
pm('a_ax_default', ax.Colormap);
pm('a_read_default', colormap);
pm('a_hot_default', hot);
colormap(jet(5));
pm('b_fig_jet5', f.Colormap);
pm('b_gca_jet5', get(gca, 'Colormap'));
pm('b_read_jet5', colormap);
pm('b_hot_after_jet5', hot);
colormap('hot');
pm('b_read_hot', colormap);
pm('b_fig_hot', f.Colormap);
pm('b_ax_hot', ax.Colormap);
c = colormap('gray');
pm('b_answer_gray', c);
colormap(ax, 'copper');
pm('c_ax_copper', ax.Colormap);
pm('c_fig_after_ax', f.Colormap);
pm('c_read_ax', colormap(ax));
pm('c_read_fig', colormap(f));
pm('c_read_bare', colormap);
colormap(f, parula(7));
pm('c_fig_parula7', f.Colormap);
pm('c_ax_follows_fig', ax.Colormap);
pm('c_bone_height', bone);
ax.Colormap = summer(3);
pm('d_ax_summer3', ax.Colormap);
colormap('default');
pm('d_fig_default_again', f.Colormap);
pm('d_ax_default_again', ax.Colormap);
c = colormap('hot(3)');
pm('d_answer_hot3', c);
pm('d_fig_hot3', f.Colormap);
close(f);

f = figure('Visible', 'off');
ax = axes(f);
mode = @() ax.ColormapMode;
u9b_chk('m_mode_fresh', mode);
colormap(ax, 'copper');
u9b_chk('m_mode_after_ax', mode);
colormap(f, parula(7));
u9b_chk('m_mode_after_fig', mode);
u9b_chk('m_rows_after_fig', @() size(ax.Colormap, 1));
ax.Colormap = summer(3);
u9b_chk('m_mode_after_property', mode);
f.Colormap = hot(4);
u9b_chk('m_mode_after_fig_property', mode);
u9b_chk('m_rows_after_fig_property', @() size(ax.Colormap, 1));
ax.Colormap = summer(3);
set(f, 'Colormap', gray(6));
u9b_chk('m_rows_after_set', @() size(ax.Colormap, 1));
ax.Colormap = summer(3);
colormap(gray(9));
u9b_chk('m_rows_after_bare', @() [size(ax.Colormap, 1) size(f.Colormap, 1)]);
ax2 = axes(f, 'Position', [0.1 0.1 0.2 0.2]);
u9b_chk('m_new_axes_rows', @() size(ax2.Colormap, 1));
colormap(ax2, jet(3));
colormap(f, 'hot');
u9b_chk('m_two_axes_after_fig', @() [size(ax.Colormap, 1) size(ax2.Colormap, 1) size(f.Colormap, 1)]);
c = colormap(ax2, 'gray');
u9b_chk('m_answer_named_on_axes', @() [size(ax2.Colormap, 1) size(c, 1)]);
close(f);

% The measured contradiction of the documented rule: an axes that chose its own map follows the
% figure's next one.
surf(peaks(8));
colormap(gca, 'jet');
colormap(gcf, 'hot');
u9b_chk('surf_axes_follows', @() max(max(abs(get(gca, 'Colormap') - get(gcf, 'Colormap')))));
close all force

f = figure('Visible', 'off');
plot(1:3); hold on; plot(2:4); plot(3:5); hold off;
prism;
ch = get(gca, 'Children');
u9b_chk('prism_bare_colours', @() round(vertcat(ch.Color), 2)); % a line's colour is held at eight bits here
pm('prism_bare_fig', f.Colormap);
cla;
plot(1:3); hold on; plot(2:4); hold off;
ch = get(gca, 'Children');
before = vertcat(ch.Color);
flag;
u9b_chk('flag_bare_leaves_lines', @() isequal(vertcat(ch.Color), before));
p = prism(4);
pm('prism_4', p);
p = prism;
pm('prism_answer', p);
close(f);

function pm(key, m)
if isempty(m)
    fprintf('CHK|%s|%s|exact\n', key, mat2str(size(m)));
    return
end
rows = unique([1 min(2, size(m, 1)) size(m, 1)]);
fprintf('CHK|%s|%s %s|exact\n', key, mat2str(size(m)), strtrim(sprintf('%.17g ', m(rows, :))));
fprintf('CHK|%s_sum|%.10g|abs=1e-6\n', key, sum(m(:)));
end
