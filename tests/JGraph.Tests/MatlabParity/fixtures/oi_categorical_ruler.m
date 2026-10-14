% record: -noFigureWindows
% Open item 120 (ADR 0233): R2025b's CategoricalRuler, in the MATLAB dialect. A categorical handed to
% bar, barh, plot, scatter, stem, area, stairs, errorbar, line, swarmchart, boxchart, xline, xregion or
% text puts its categories on the ruler the values stand along, category k at position k; the chart's
% data, the axes' XLim and XTick read back as categoricals and XTickLabel as a column of names; limits
% and ticks are written as categories, never as numbers; a held axes adds new categories at the end,
% a categorical onto a numeric ruler that holds data is refused, and numbers onto a categorical ruler
% are positions; histogram and pie of a categorical count its categories; ruler2num and num2ruler
% convert. Lines marked div=ADR0233 are a handle's class name (a double here), the turned labels of
% a crowded ruler, axis() over mixed rulers, loglog, and the property listings.
close all force
f = figure('Visible', 'off', 'Position', [100 100 560 420]); ax = axes(f);
c3 = categorical({'x', 'y', 'z'});
b = bar(ax, c3, [1 2 3]); drawnow;
u9b_chk('b_xdata', @() b.XData); u9b_chk('b_ydata', @() b.YData); u9b_chk('b_xend', @() b.XEndPoints); u9b_chk('b_yend', @() b.YEndPoints);
u9b_chk('b_xtick', @() ax.XTick); u9b_chk('b_xticklabel', @() ax.XTickLabel); u9b_chk('b_xlim', @() ax.XLim); u9b_chk('b_xlimmode', @() ax.XLimMode); u9b_chk('b_xtickmode', @() ax.XTickMode);
u9b_chk('b_cats', @() ax.XAxis.Categories); u9b_chk('b_limits', @() ax.XAxis.Limits); u9b_chk('b_tickvalues', @() ax.XAxis.TickValues);
u9b_chk('b_r2n', @() ruler2num(ax.XLim, ax.XAxis)); u9b_chk('b_r2n_one', @() ruler2num(categorical({'y'}), ax.XAxis)); u9b_chk('b_r2n_cell', @() ruler2num({'z'}, ax.XAxis));
u9b_chk('b_n2r', @() num2ruler(2, ax.XAxis)); u9b_chk('b_n2r_frac', @() num2ruler(2.4, ax.XAxis)); u9b_chk('b_n2r_out', @() num2ruler(7, ax.XAxis));
u9b_chk('b_ylim', @() ax.YLim); u9b_chk('b_ytick', @() ax.YTick);
u9b_chk('b_get_xlim', @() get(ax, 'XLim')); u9b_chk('b_get_xdata', @() get(b, 'XData'));
u9b_chk('b_ticklabel_rot', @() ax.XTickLabelRotation);
u9b_chk('b_barwidth', @() b.BarWidth);
u9b_chk('set_xlim_cat', @() setp(ax, 'XLim', categorical({'x', 'y'}))); u9b_chk('set_xlim_cat_tick', @() ax.XTick); u9b_chk('set_xlim_cat_mode', @() ax.XLimMode);
u9b_chk('set_xlim_cellstr', @() setp(ax, 'XLim', {'y', 'z'})); u9b_chk('set_xlim_string', @() setp(ax, 'XLim', ["x" "z"]));
u9b_chk('set_xlim_rev', @() setp(ax, 'XLim', categorical({'z', 'x'})));
u9b_chk('set_xlim_unknown', @() setp(ax, 'XLim', categorical({'x', 'q'})));
u9b_chk('set_xlim_num', @() setp(ax, 'XLim', [1 2]));
u9b_chk('set_xlim_auto', @() setp(ax, 'XLimMode', 'auto')); u9b_chk('set_xlim_auto_lim', @() ax.XLim);
u9b_chk('xlim_fn', @() xlimfn(ax, categorical({'y', 'z'}))); u9b_chk('xlim_fn_read', @() xlim(ax));
xlim(ax, 'auto');
u9b_chk('set_xtick_cat', @() setp(ax, 'XTick', categorical({'x', 'z'}))); u9b_chk('set_xtick_cat_label', @() ax.XTickLabel); u9b_chk('set_xtick_mode', @() ax.XTickMode);
u9b_chk('set_xtick_cell', @() setp(ax, 'XTick', {'y'}));
u9b_chk('set_xtick_num', @() setp(ax, 'XTick', [1 2]));
u9b_chk('set_xtick_empty', @() setp(ax, 'XTick', []));
ax.XTickMode = 'auto';
u9b_chk('set_xticklabel', @() setp(ax, 'XTickLabel', {'A', 'B', 'C'})); u9b_chk('set_xticklabel_tick', @() ax.XTick);
ax.XTickLabelMode = 'auto';
u9b_chk('set_cats', @() setp(ax.XAxis, 'Categories', {'z', 'y', 'x'})); u9b_chk('set_cats_label', @() ax.XTickLabel); u9b_chk('set_cats_xdata', @() b.XData); u9b_chk('set_cats_lim', @() ax.XLim);
u9b_chk('set_cats_drop', @() setp(ax.XAxis, 'Categories', {'z', 'y'}));
u9b_chk('set_cats_add', @() setp(ax.XAxis, 'Categories', {'z', 'y', 'x', 'w'})); u9b_chk('set_cats_add_lim', @() ax.XLim); u9b_chk('set_cats_add_tick', @() ax.XTick);
u9b_chk('xdata_write', @() setp(b, 'XData', categorical({'z', 'y', 'x'}))); u9b_chk('xdata_write_label', @() ax.XTickLabel);
u9b_chk('xdata_write_num', @() setp(b, 'XData', [1 2 3]));
u9b_chk('xdata_write_new', @() setp(b, 'XData', categorical({'p', 'q', 'r'}))); u9b_chk('xdata_write_new_cats', @() ax.XAxis.Categories);
cla(ax, 'reset'); b2 = bar(ax, categorical({'a', 'b'}, {'a', 'b', 'c'}), [1 2]);
u9b_chk('unused_cats', @() ax.XAxis.Categories); u9b_chk('unused_tick', @() ax.XTick); u9b_chk('unused_lim', @() ax.XLim);
cla(ax, 'reset'); b3 = bar(ax, categorical({'a', ''}), [1 2]);
u9b_chk('undef_cats', @() ax.XAxis.Categories); u9b_chk('undef_tick', @() ax.XTick); u9b_chk('undef_lim', @() ax.XLim); u9b_chk('undef_xdata', @() b3.XData);
cla(ax, 'reset'); nm120 = arrayfun(@(k) sprintf('cat%02d', k), 1:30, 'UniformOutput', false);
bar(ax, categorical(nm120), 1:30); drawnow;
u9b_chk('many_tick_n', @() numel(ax.XTick)); u9b_chk('many_tick', @() ax.XTick); u9b_chk('many_lim', @() ax.XLim); u9b_chkdiv('many_rot', @() oi_try( @() ax.XTickLabelRotation), 'ADR0233');
cla(ax, 'reset'); bar(ax, categorical({'b', 'a', 'c'}), [1 2 3]);
u9b_chk('order_cats', @() ax.XAxis.Categories);
cla(ax, 'reset'); bar(ax, categorical({'b', 'a', 'c'}, {'c', 'b', 'a'}), [1 2 3]);
u9b_chk('order_valueset', @() ax.XAxis.Categories);
cla(ax, 'reset'); bar(ax, categorical({'lo', 'hi'}, {'lo', 'hi'}, 'Ordinal', true), [1 2]);
u9b_chk('ordinal_cats', @() ax.XAxis.Categories); u9b_chk('ordinal_xdata', @() isordinal(ax.Children(1).XData));
cla(ax, 'reset'); bb = bar(ax, categorical({'x', 'y'}), [1 2; 3 4]);
u9b_chk('grp_n', @() numel(bb)); u9b_chk('grp_xdata', @() bb(2).XData); u9b_chk('grp_xend', @() bb(2).XEndPoints); u9b_chk('grp_lim', @() ax.XLim);
cla(ax, 'reset'); bs = bar(ax, categorical({'x', 'y'}), [1 2; 3 4], 'stacked'); u9b_chk('stk_xend', @() bs(1).XEndPoints);
cla(ax, 'reset'); pl = plot(ax, categorical({'x', 'y'}), [1 2]); drawnow;
u9b_chk('pl_xdata', @() pl.XData); u9b_chk('pl_xtick', @() ax.XTick); u9b_chk('pl_xlim', @() ax.XLim); u9b_chk('pl_cats', @() ax.XAxis.Categories); u9b_chk('pl_ylim', @() ax.YLim);
cla(ax, 'reset'); pl3 = plot(ax, categorical({'x', 'y', 'x'}), [1 2 3], 'o-'); u9b_chk('pl_rep_xdata', @() pl3.XData); u9b_chk('pl_rep_cats', @() ax.XAxis.Categories);
cla(ax, 'reset'); plm = plot(ax, categorical({'x'; 'y'}), [1 2; 3 4]); u9b_chk('pl_mat_n', @() numel(plm)); u9b_chk('pl_mat_xdata', @() plm(2).XData);
cla(ax, 'reset'); pp = plot(ax, [1 2 3], categorical({'x', 'y', 'x'})); u9b_chk('ply_ydata', @() pp.YData); u9b_chk('ply_ytick', @() ax.YTick); u9b_chk('ply_ylim', @() ax.YLim); u9b_chk('ply_xlim', @() ax.XLim); u9b_chk('ply_ycats', @() ax.YAxis.Categories);
cla(ax, 'reset'); pc = plot(ax, categorical({'x', 'y'}), categorical({'p', 'q'})); u9b_chk('plxy_x', @() pc.XData); u9b_chk('plxy_y', @() pc.YData);
cla(ax, 'reset'); sc = scatter(ax, categorical({'x', 'y'}), [1 2]); u9b_chk('sc_xdata', @() sc.XData); u9b_chk('sc_lim', @() ax.XLim);
cla(ax, 'reset'); st = stem(ax, categorical({'x', 'y'}), [1 2]); u9b_chk('st_xdata', @() st.XData); u9b_chk('st_lim', @() ax.XLim);
cla(ax, 'reset'); ar = area(ax, categorical({'x', 'y'}), [1 2]); u9b_chk('ar_xdata', @() ar.XData); u9b_chk('ar_lim', @() ax.XLim);
cla(ax, 'reset'); e = errorbar(ax, categorical({'x', 'y'}), [1 2], [0.1 0.1]); u9b_chk('eb_xdata', @() e.XData); u9b_chk('eb_lim', @() ax.XLim);
cla(ax, 'reset'); ln = line(ax, categorical({'x', 'y'}), [1 2]); u9b_chk('line_xdata', @() ln.XData);
cla(ax, 'reset'); sw = swarmchart(ax, categorical({'x', 'y', 'x'}), [1 2 3]); u9b_chk('sw_xdata', @() sw.XData); u9b_chk('sw_lim', @() ax.XLim);
cla(ax, 'reset'); bx = boxchart(ax, categorical({'x', 'y', 'x'}), [1 2 3]); u9b_chk('bx_xdata', @() bx.XData); u9b_chk('bx_lim', @() ax.XLim); u9b_chk('bx_tick', @() ax.XTick);
cla(ax, 'reset'); bh = barh(ax, categorical({'x', 'y'}), [1 2]); u9b_chk('bh_xdata', @() bh.XData); u9b_chk('bh_ytick', @() ax.YTick); u9b_chk('bh_ylim', @() ax.YLim); u9b_chk('bh_xlim', @() ax.XLim);
cla(ax, 'reset'); st3 = stairs(ax, categorical({'x', 'y'}), [1 2]); u9b_chk('stairs_xdata', @() st3.XData);
cla(ax, 'reset'); u9b_chk('plot3_cat', @() class(plot3(ax, categorical({'x', 'y'}), [1 2], [3 4]).XData));
cla(ax, 'reset'); u9b_chkdiv('fill_cat', @() oi_try( @() class(fill(ax, categorical({'x', 'y', 'x'}), [1 2 3], 'r'))), 'ADR0233');
cla(ax, 'reset'); u9b_chkdiv('patch_cat', @() oi_try( @() class(patch(ax, categorical({'x', 'y', 'x'}), [1 2 3], 'r'))), 'ADR0233');
cla(ax, 'reset'); u9b_chk('semilogy_cat', @() class(semilogy(ax, categorical({'x', 'y'}), [1 2]).XData));
cla(ax, 'reset'); u9b_chkdiv('loglog_cat', @() oi_try( @() class(loglog(ax, categorical({'x', 'y'}), [1 2]).XData)), 'ADR0233');
cla(ax, 'reset'); hold(ax, 'on'); bar(ax, c3, [1 2 3]); u9b_chk('mix_num_on_cat', @() class(plot(ax, [1 2], [3 4]).XData)); u9b_chk('mix_num_on_cat_cats', @() ax.XAxis.Categories);
u9b_chk('mix_cat_y_on_num', @() class(plot(ax, [1 2], categorical({'a', 'b'})).YData));
hold(ax, 'off'); cla(ax, 'reset'); hold(ax, 'on'); plot(ax, [1 2], [3 4]); u9b_chk('mix_cat_on_num', @() class(bar(ax, c3, [1 2 3]).XData));
hold(ax, 'off'); cla(ax, 'reset'); plot(ax, [1 2], [3 4]); u9b_chk('mix_cat_on_num_nohold', @() class(bar(ax, c3, [1 2 3]).XData)); u9b_chk('mix_nohold_cats', @() ax.XAxis.Categories);
cla(ax, 'reset'); bar(ax, c3, [1 2 3]); u9b_chk('mix_replace_num', @() class(plot(ax, [1 2], [3 4]).XData)); u9b_chkdiv('mix_replace_ruler', @() oi_try( @() class(ax.XAxis)), 'ADR0233'); u9b_chk('mix_replace_xlim', @() ax.XLim);
cla(ax, 'reset'); u9b_chkdiv('mix_ruler_after_reset', @() oi_try( @() class(ax.XAxis)), 'ADR0233'); u9b_chk('mix_lim_after_reset', @() ax.XLim);
cla(ax, 'reset'); hold(ax, 'on'); bar(ax, c3, [1 2 3]); plot(ax, categorical({'q', 'x'}), [3 4]); u9b_chk('hold_add_cats', @() ax.XAxis.Categories); u9b_chk('hold_add_lim', @() ax.XLim); u9b_chk('hold_add_tick', @() ax.XTick);
plot(ax, categorical({'a'}, {'a', 'zz'}), 1); u9b_chk('hold_add_unused', @() ax.XAxis.Categories);
hold(ax, 'off');
cla(ax, 'reset'); bar(ax, c3, [1 2 3]); hold(ax, 'on'); xl = xline(ax, categorical({'y'})); u9b_chk('xline_value', @() xl.Value); u9b_chk('xline_cell', @() class(xline(ax, {'z'}).Value)); u9b_chk('xline_num_on_cat', @() xline(ax, 2).Value);
xr = xregion(ax, categorical({'x'}), categorical({'y'})); u9b_chk('xregion_value', @() xr.Value);
tx = text(ax, categorical({'y'}), 2, 'lbl'); u9b_chk('text_pos', @() tx.Position);
hold(ax, 'off');
cla(ax, 'reset'); bar(ax, c3, [1 2 3]); u9b_chkdiv('yline_on_cat', @() oi_try( @() class(yline(ax, 1))), 'ADR0233');
cla(ax, 'reset'); hh = histogram(ax, categorical({'x', 'y', 'x'})); drawnow;
u9b_chk('h_values', @() hh.Values); u9b_chk('h_cats', @() hh.Categories); u9b_chk('h_data', @() hh.Data); u9b_chk('h_bw', @() hh.BarWidth); u9b_chk('h_norm', @() hh.Normalization); u9b_chk('h_ndb', @() hh.NumDisplayBins); u9b_chk('h_order', @() hh.DisplayOrder);
u9b_chk('h_xtick', @() ax.XTick); u9b_chk('h_xlim', @() ax.XLim); u9b_chk('h_cats_ax', @() ax.XAxis.Categories); u9b_chk('h_ylim', @() ax.YLim); u9b_chk('h_orient', @() hh.Orientation);
u9b_chk('h_binedges', @() hh.BinEdges); u9b_chk('h_numbins', @() hh.NumBins); u9b_chk('h_bincounts', @() hh.BinCounts);
u9b_chkdiv('h_props', @() oi_try( @() strjoin(fieldnames(get(hh))', ',')), 'ADR0233');
u9b_chk('h_set_norm', @() setp(hh, 'Normalization', 'probability')); u9b_chk('h_set_norm_vals', @() hh.Values);
u9b_chk('h_set_order', @() setp(hh, 'DisplayOrder', 'descend')); u9b_chk('h_set_order_cats', @() hh.Categories); u9b_chk('h_set_order_ax', @() ax.XAxis.Categories);
u9b_chk('h_set_bw', @() setp(hh, 'BarWidth', 0.5));
cla(ax, 'reset'); hh2 = histogram(ax, categorical({'x', 'y', 'x', 'z'}), {'y', 'x'}); u9b_chk('h_sub_vals', @() hh2.Values); u9b_chk('h_sub_cats', @() hh2.Categories); u9b_chk('h_sub_ax', @() ax.XAxis.Categories);
cla(ax, 'reset'); hh3 = histogram(ax, categorical({'x', 'y', ''})); u9b_chk('h_undef_vals', @() hh3.Values); u9b_chk('h_undef_cats', @() hh3.Categories); u9b_chk('h_undef_ax', @() ax.XAxis.Categories);
cla(ax, 'reset'); hh4 = histogram(ax, 'Categories', {'a', 'b'}, 'BinCounts', [3 5]); u9b_chk('h_counts_vals', @() hh4.Values); u9b_chk('h_counts_data', @() hh4.Data);
cla(ax, 'reset'); hh5 = histogram(ax, categorical({'x', 'y', 'x'}), 'Orientation', 'horizontal'); u9b_chk('h_horiz_ytick', @() ax.YTick); u9b_chk('h_horiz_ycats', @() ax.YAxis.Categories);
cla(ax, 'reset'); hh6 = histogram(ax, categorical({'x', 'y', 'x'}, {'x', 'y', 'w'})); u9b_chk('h_unused_vals', @() hh6.Values); u9b_chk('h_unused_cats', @() hh6.Categories);
cla(ax, 'reset'); hh7 = histogram(ax, categorical({'x', 'y', 'x'}), 'Normalization', 'pdf'); u9b_chk('h_pdf_vals', @() hh7.Values);
cla(ax, 'reset'); hh8 = histogram(ax, categorical({'x', 'y', 'x', 'z', 'z', 'z'}), 'DisplayOrder', 'ascend'); u9b_chk('h_asc_cats', @() hh8.Categories); u9b_chk('h_asc_vals', @() hh8.Values);
cla(ax, 'reset'); hh9 = histogram(ax, categorical({'x', 'y', 'x', 'z', 'z', 'z'}), 'NumDisplayBins', 2); u9b_chk('h_ndb2_cats', @() hh9.Categories); u9b_chk('h_ndb2_vals', @() hh9.Values); u9b_chk('h_ndb2_other', @() hh9.OthersValue); u9b_chk('h_ndb2_showothers', @() hh9.ShowOthers);
cla(ax, 'reset'); hh10 = histogram(ax, categorical({'x', 'y', 'x', 'z', 'z', 'z'}), 'ShowOthers', 'on', 'NumDisplayBins', 2); u9b_chk('h_others_cats', @() hh10.Categories); u9b_chk('h_others_ax', @() ax.XAxis.Categories);
cla(ax, 'reset'); u9b_chk('h_numbins_cat', @() class(histogram(ax, categorical({'x'}), 3)));
u9b_chkdiv('h_binmethod_cat', @() oi_try( @() class(histogram(ax, categorical({'x'}), 'BinMethod', 'auto'))), 'ADR0233');
u9b_chk('histcounts_cat', @() histcounts(categorical({'x', 'y', 'x'})));
u9b_chk('histcounts_cat2', @() histcounts(categorical({'x', 'y', 'x'}), {'y'}));
[nn, cc] = histcounts(categorical({'x', 'y', 'x'})); u9b_chk('histcounts_cats_out', @() cc);
cla(ax, 'reset'); hp = pie(ax, categorical({'x', 'y', 'x'}));
u9b_chk('pie_n', @() numel(hp)); u9b_chk('pie_labels', @() strjoin(arrayfun(@(h) h.String, hp(2:2:end), 'UniformOutput', false), ','));
cla(ax, 'reset'); hp2 = pie(ax, categorical({'a', 'b', 'c', 'a'})); u9b_chk('pie3_labels', @() strjoin(arrayfun(@(h) h.String, hp2(2:2:end), 'UniformOutput', false), ','));
cla(ax, 'reset'); hp3 = pie(ax, categorical({'a', 'b'}, {'a', 'b', 'c'})); u9b_chk('pie_unused_n', @() numel(hp3)); u9b_chk('pie_unused_labels', @() strjoin(arrayfun(@(h) h.String, hp3(2:2:end), 'UniformOutput', false), ','));
cla(ax, 'reset'); hp4 = pie(ax, categorical({'a', ''})); u9b_chk('pie_undef_n', @() numel(hp4)); u9b_chk('pie_undef_labels', @() strjoin(arrayfun(@(h) h.String, hp4(2:2:end), 'UniformOutput', false), ','));
cla(ax, 'reset'); u9b_chk('pie_explode_n', @() numel(pie(ax, categorical({'a', 'b', 'a'}), {'b'})));
cla(ax, 'reset'); u9b_chk('pie_explode_num', @() numel(pie(ax, categorical({'a', 'b', 'b'}), [0 1])));
cla(ax, 'reset'); u9b_chk('pie_lbls', @() pielbl(ax));
u9b_chk('pie_text_cat_1', @() pietext(categorical({'a', 'b', 'b', 'c', 'c', 'c'})));
u9b_chk('pie_text_cat_round', @() pietext(categorical({'a', 'b', 'b', 'b', 'b', 'b', 'b'})));
u9b_chk('pie3_cat', @() numel(pie3(ax, categorical({'a', 'b', 'b'}))));
cla(ax, 'reset'); hm = heatmap(f, table(categorical({'x'; 'y'; 'x'}), categorical({'p'; 'q'; 'p'}), 'VariableNames', {'a', 'b'}), 'a', 'b');
u9b_chk('hm_xlabels', @() hm.XDisplayLabels); u9b_chk('hm_ylabels', @() hm.YDisplayLabels); u9b_chk('hm_xdata', @() hm.XData); u9b_chk('hm_xdata_class', @() class(hm.XData));
hm2 = heatmap(f, [1 2; 3 4]); u9b_chk('hm_num_xlabels', @() hm2.XDisplayLabels); u9b_chk('hm_num_xdata', @() hm2.XData);
close all force
f2 = figure('Visible', 'off'); ax2 = axes(f2);
u9b_chkdiv('bar_cell_x', @() oi_try( @() class(bar(ax2, {'a', 'b'}, [1 2]))), 'ADR0233');
u9b_chkdiv('plot_string_x', @() oi_try( @() class(plot(ax2, ["a" "b"], [1 2]).XData)), 'ADR0233');
u9b_chk('bar_string_x', @() class(bar(ax2, ["a" "b"], [1 2]).XData));
cla(ax2, 'reset'); bar(ax2, categorical({'x', 'y'}), [1 2]); u9b_chk('xticklabels_fn', @() xticklabels(ax2)); u9b_chk('xticks_fn', @() xticks(ax2));
u9b_chk('xticks_set_fn', @() xtfn(ax2, categorical({'y'})));
u9b_chkdiv('axis_fn', @() oi_try( @() axis(ax2)), 'ADR0233');
u9b_chk('axis_tight', @() axtight(ax2));
u9b_chkdiv('get_ruler_props', @() oi_try( @() strjoin(fieldnames(get(ax2.XAxis))', ',')), 'ADR0233');
u9b_chkdiv('ruler_tlf', @() oi_try( @() ax2.XAxis.TickLabelFormat), 'ADR0233');
u9b_chkdiv('zoom_set_num_via_axis', @() oi_try( @() axset(ax2, [0 3 0 3])), 'ADR0233');
close all force



function pr(label, f)
try
    v = f();
    fprintf('%s\t%s\t%s\t%s\n', label, class(v), mat2str(size(v)), s(v));
catch e
    fprintf('%s\tERR\t%s\t%s\n', label, e.identifier, strrep(e.message, newline, ' '));
end
end

function t = s(v)
if iscategorical(v)
    cs = cellstr(v); t = strjoin(reshape(cs, 1, []), ',');
elseif isnumeric(v) || islogical(v)
    t = mat2str(v, 10);
elseif ischar(v)
    t = strrep(v, newline, '\n');
elseif isstring(v)
    v(ismissing(v)) = "<missing>"; t = strjoin(reshape(v, 1, []), ',');
elseif iscell(v)
    t = strjoin(cellfun(@(x) s(x), reshape(v, 1, []), 'UniformOutput', false), '|');
elseif isstruct(v)
    t = strjoin(fieldnames(v)', ',');
elseif istable(v)
    t = strjoin(v.Properties.VariableNames, ',');
else
    t = class(v);
end
end

function x = setp(h, name, v)
set(h, name, v); x = get(h, name);
end

function x = xlimfn(ax, v)
xlim(ax, v); x = ax.XLimMode;
end

function x = xtfn(ax, v)
xticks(ax, v); x = ax.XTick;
end

function x = axtight(ax)
axis(ax, 'tight'); x = ax.XLim;
end

function x = axset(ax, v)
axis(ax, v); x = ax.XLim;
end

function x = pielbl(ax)
h = pie(ax, categorical({'a', 'b', 'b'}), {'L1', 'L2'}); x = strjoin(arrayfun(@(t) t.String, h(2:2:end), 'UniformOutput', false), ',');
end

function x = pietext(c)
f = figure('Visible', 'off'); h = pie(axes(f), c); x = strjoin(arrayfun(@(t) t.String, h(2:2:end), 'UniformOutput', false), ','); close(f);
end

function r = oi_try(fn)
% The value fn answers, or the identifier of the error it raises, as one line (a divergence row).
try
    r = fn();
catch err
    r = err.identifier;
end
end
