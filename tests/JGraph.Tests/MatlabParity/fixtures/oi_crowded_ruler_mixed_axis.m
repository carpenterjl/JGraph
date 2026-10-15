% record: -noFigureWindows
% Open item 126 (ADR 0234): the categorical ruler's leftovers, in the MATLAB dialect. axis over a
% categorical and a numeric ruler refuses as Mixed2D or Mixed3D (three outputs too), loglog and its
% kind over a categorical chart refuse a log scale, a categorical histogram refuses the numeric
% histogram's names and the numeric one the categorical one's, the CategoricalRuler and categorical
% Histogram answer R2025b's property lists and refuse the dot reads, writes, set and get they lack,
% fill and patch take categorical x, text and friends refuse categorical x, a ruler's colour and
% handle visibility, axis tight and padded on numeric axes, and a crowded ruler's labels turn by
% themselves to 30, 45 or 90 degrees by R2025b's measured rule. Lines marked div=ADR0234 are a
% handle's class and superclasses (a double here), a ruler's colour, the property values a listing
% prints, LimitMethod (a JGraph extra on a ruler), the limits axis padded chooses, and the plot box
% under turned labels.
f = figure('Visible', 'off'); ax = axes(f);
c3 = categorical({'a', 'b', 'c'});
bar(ax, c3, [1 2 3]);
u9b_chk('ax_query', @() msgof(@() axis(ax))); u9b_chk('ax_set', @() msgof(@() axis(ax, [0 3 0 3]))); u9b_chk('ax_set6', @() msgof(@() axis(ax, [0 3 0 3 0 1])));
u9b_chk('ax_tight', @() msgof(@() axis(ax, 'tight'))); u9b_chk('ax_tight_lim', @() axlims(ax, 'tight')); u9b_chk('ax_equal', @() msgof(@() axis(ax, 'equal')));
u9b_chk('ax_off', @() msgof(@() axis(ax, 'off'))); u9b_chk('ax_on', @() msgof(@() axis(ax, 'on'))); u9b_chk('ax_auto', @() msgof(@() axis(ax, 'auto')));
u9b_chk('ax_auto_lim', @() axlims(ax, 'auto')); u9b_chk('ax_manual', @() msgof(@() axis(ax, 'manual'))); u9b_chk('ax_square', @() msgof(@() axis(ax, 'square')));
u9b_chk('ax_normal', @() msgof(@() axis(ax, 'normal'))); u9b_chk('ax_fill', @() msgof(@() axis(ax, 'fill'))); u9b_chk('ax_ij', @() msgof(@() axis(ax, 'ij')));
u9b_chk('ax_xy', @() msgof(@() axis(ax, 'xy'))); u9b_chk('ax_image', @() msgof(@() axis(ax, 'image'))); u9b_chk('ax_vis3d', @() msgof(@() axis(ax, 'vis3d')));
u9b_chk('ax_padded', @() msgof(@() axis(ax, 'padded'))); u9b_chkdiv('ax_padded_lim', @() oi_try( @() axlims(ax, 'padded')), 'ADR0234'); u9b_chk('ax_tickaligned', @() msgof(@() axis(ax, 'tickaligned')));
u9b_chk('ax_tight_out', @() msgof(@() axout(ax))); u9b_chk('ax_two', @() msgof(@() axis(ax, 'tight', 'equal'))); u9b_chk('ax_auto_x', @() msgof(@() axis(ax, 'auto x')));
u9b_chk('ax_style_out', @() msgof(@() axstyle(ax))); u9b_chk('ax_gca', @() axgca(c3));
u9b_chk('ax_cat_cat', @() axcatcat(c3)); u9b_chk('ax_barh', @() axbarh(c3)); u9b_chk('ax_3d', @() ax3d(c3)); u9b_chk('ax_3d_set', @() ax3dset(c3));
u9b_chk('ax_num_only', @() axnum()); u9b_chk('ax_after_cla', @() axcla(c3)); u9b_chk('ax_xlim_num', @() msgof(@() xlim(ax, [0 3])));
u9b_chk('ax_msg_set', @() axmsg(ax));
u9b_chk('ll_cat', @() msgof(@() loglog(ax, categorical({'x', 'y'}), [1 2]))); u9b_chk('ll_cat_y', @() msgof(@() loglog(ax, [1 2], categorical({'x', 'y'}))));
u9b_chk('ll_after', @() llafter(c3)); u9b_chk('slx_cat', @() msgof(@() semilogx(ax, categorical({'x', 'y'}), [1 2])));
u9b_chk('sly_cat', @() slycat()); u9b_chk('sly_cat_y', @() msgof(@() semilogy(ax, [1 2], categorical({'x', 'y'}))));
u9b_chk('slx_cat_y', @() slxcaty()); u9b_chk('scale_log', @() msgof(@() setlog(c3))); u9b_chk('ruler_scale_log', @() msgof(@() rulerlog(c3)));
u9b_chk('ll_held', @() msgof(@() llheld(c3))); u9b_chk('ll_cat_both', @() msgof(@() loglog(ax, categorical({'x', 'y'}), categorical({'u', 'v'}))));
u9b_chk('ll_scale_after_cat', @() llscale(c3));
cla(ax, 'reset');
u9b_chk('hb_binmethod', @() msgof(@() histogram(ax, c3, 'BinMethod', 'auto'))); u9b_chk('hb_numbins', @() msgof(@() histogram(ax, c3, 'NumBins', 3)));
u9b_chk('hb_binedges', @() msgof(@() histogram(ax, c3, 'BinEdges', [0 1 2]))); u9b_chk('hb_binwidth', @() msgof(@() histogram(ax, c3, 'BinWidth', 1)));
u9b_chk('hb_binlimits', @() msgof(@() histogram(ax, c3, 'BinLimits', [0 1]))); u9b_chk('hb_norm', @() hbval(ax, c3, 'Normalization', 'probability'));
u9b_chk('hb_order', @() hbcats(ax, c3, 'DisplayOrder', 'descend')); u9b_chk('hb_numdisplay', @() hbcats(ax, c3, 'NumDisplayBins', 2));
u9b_chk('hb_showothers', @() hbcats(ax, c3, 'ShowOthers', 'on')); u9b_chk('hb_stairs', @() msgof(@() histogram(ax, c3, 'DisplayStyle', 'stairs')));
u9b_chk('hb_horizontal', @() hbh(ax, c3)); u9b_chk('hb_bincounts_mode', @() msgof(@() histogram(ax, c3, 'BinCountsMode', 'manual')));
u9b_chk('hb_bogus', @() msgof(@() histogram(ax, c3, 'Bogus', 1))); u9b_chk('hb_set_binwidth', @() msgof(@() hbset(ax, c3, 'BinWidth', 1)));
u9b_chk('hb_set_numbins', @() msgof(@() hbset(ax, c3, 'NumBins', 2))); u9b_chk('hb_set_binedges', @() msgof(@() hbset(ax, c3, 'BinEdges', [1 2])));
u9b_chk('hb_get_numbins', @() msgof(@() hbget(ax, c3, 'NumBins'))); u9b_chk('hb_dot_binmethod', @() msgof(@() hbdot(ax, c3)));
u9b_chk('hb_num_categories', @() msgof(@() hbnumcats(ax))); u9b_chk('hb_num_showothers', @() msgof(@() histogram(ax, [1 2 3], 'ShowOthers', 'on')));
cla(ax, 'reset'); bar(ax, c3, [1 2 3]);
u9b_chk('pl_ruler', @() strjoin(fieldnames(get(ax.XAxis))', ',')); u9b_chk('pl_ruler_num', @() strjoin(fieldnames(get(ax.YAxis))', ','));
u9b_chkdiv('pv_ruler', @() oi_try( @() deep(get(ax.XAxis))), 'ADR0234'); u9b_chkdiv('pv_ruler_num', @() oi_try( @() deep(get(ax.YAxis))), 'ADR0234');
u9b_chk('pl_ruler_set', @() strjoin(fieldnames(set(ax.XAxis))', ',')); u9b_chk('pl_ruler_num_set', @() strjoin(fieldnames(set(ax.YAxis))', ','));
cla(ax, 'reset'); hh = histogram(ax, c3);
u9b_chk('pl_hist', @() strjoin(fieldnames(get(hh))', ',')); u9b_chkdiv('pv_hist', @() oi_try( @() deep(get(hh))), 'ADR0234');
u9b_chk('pl_hist_set', @() strjoin(fieldnames(set(hh))', ','));
cla(ax, 'reset'); hn = histogram(ax, [1 2 2 3]);
u9b_chk('pl_hist_num', @() strjoin(fieldnames(get(hn))', ',')); u9b_chkdiv('pv_hist_num', @() oi_try( @() deep(get(hn))), 'ADR0234');
u9b_chk('pl_hist_cat_ruler', @() strjoin(fieldnames(get(ax.XAxis))', ','));
cla(ax, 'reset'); hh = histogram(ax, c3);
u9b_chk('pl_hist_ruler', @() strjoin(fieldnames(get(ax.XAxis))', ',')); u9b_chk('pl_hist_yruler', @() strjoin(fieldnames(get(ax.YAxis))', ','));
cla(ax, 'reset'); bar(ax, c3, [1 2 3]);
u9b_chk('rd_tlf', @() msgof(@() ax.XAxis.TickLabelFormat)); u9b_chk('rd_exponent', @() msgof(@() ax.XAxis.Exponent));
u9b_chk('rd_exponentmode', @() msgof(@() ax.XAxis.ExponentMode)); u9b_chkdiv('rd_limitmethod', @() oi_try( @() msgof(@() ax.XAxis.LimitMethod)), 'ADR0234');
u9b_chk('rd_secondary', @() msgof(@() ax.XAxis.SecondaryLabel)); u9b_chk('rd_scale', @() ax.XAxis.Scale); u9b_chk('rd_minor', @() ax.XAxis.MinorTick);
u9b_chk('rd_num_categories', @() msgof(@() ax.YAxis.Categories)); u9b_chk('wr_tlf', @() msgof(@() setr(ax.XAxis, 'TickLabelFormat', '%g')));
u9b_chk('wr_exponent', @() msgof(@() setr(ax.XAxis, 'Exponent', 2))); u9b_chk('wr_num_categories', @() msgof(@() setr(ax.YAxis, 'Categories', {'a'})));
u9b_chk('wr_set_tlf', @() msgof(@() set(ax.XAxis, 'TickLabelFormat', '%g'))); u9b_chk('rd_get_tlf', @() msgof(@() get(ax.XAxis, 'TickLabelFormat')));
u9b_chk('rd_ax_xaxis_class', @() class(ax.XAxis.Limits)); u9b_chk('rd_minorvalues', @() msgof(@() ax.XAxis.MinorTickValues));
u9b_chk('wr_minor_on', @() msgof(@() setr(ax.XAxis, 'MinorTick', 'on')));
f2 = figure('Visible', 'off'); ax2 = axes(f2);
u9b_chk('plot_string_x', @() msgof(@() plot(ax2, ["a" "b"], [1 2]))); u9b_chk('plot_string_x_noax', @() msgof(@() plotnoax()));
u9b_chk('plot_string_y', @() msgof(@() plot(ax2, [1 2], ["a" "b"]))); u9b_chk('plot_string_one', @() msgof(@() plot(ax2, "a")));
u9b_chk('plot_cellstr_x', @() msgof(@() plot(ax2, {'a', 'b'}, [1 2]))); u9b_chk('plot_char_x', @() msgof(@() plot(ax2, 'ab', [1 2])));
u9b_chk('bar_cell_x', @() barcell(ax2)); u9b_chk('bar_cell_x_lim', @() barcelllim(ax2)); u9b_chk('scatter_string_x', @() msgof(@() scatter(ax2, ["a" "b"], [1 2])));
u9b_chk('fill_cat', @() fillcat(ax2)); u9b_chk('patch_cat', @() patchcat(ax2)); u9b_chk('fill_cat_ruler', @() fillruler(ax2));
u9b_chk('area_string_x', @() msgof(@() area(ax2, ["a" "b"], [1 2]))); u9b_chk('stem_cell_x', @() msgof(@() stem(ax2, {'a', 'b'}, [1 2])));
u9b_chk('line_string_x', @() msgof(@() line(ax2, ["a" "b"], [1 2])));


f = figure('Visible', 'off'); ax = axes(f);
c3 = categorical({'a', 'b', 'c'});
bar(ax, c3, [1 2 3]);
u9b_chkdiv('cr_class', @() oi_try( @() class(ax.XAxis)), 'ADR0234'); u9b_chkdiv('cr_super', @() oi_try( @() superclasses(ax.XAxis)'), 'ADR0234');
u9b_chk('cr_isa_ruler', @() isa(ax.XAxis, 'matlab.graphics.axis.decorator.Ruler'));
u9b_chk('cr_get_tlf', @() msgof(@() get(ax.XAxis, 'TickLabelFormat')));
u9b_chk('cr_set_bogus', @() msgof(@() set(ax.XAxis, 'Bogus', 1)));
u9b_chk('nr_set_bogus', @() msgof(@() set(ax.YAxis, 'Bogus', 1))); u9b_chk('nr_get_bogus', @() msgof(@() get(ax.YAxis, 'Bogus')));
u9b_chk('nr_dot_bogus', @() msgof(@() ax.YAxis.Bogus));
u9b_chkdiv('cr_color', @() oi_try( @() ax.XAxis.Color), 'ADR0234'); u9b_chkdiv('ax_xcolor', @() oi_try( @() ax.XColor), 'ADR0234'); u9b_chk('cr_hv', @() ax.XAxis.HandleVisibility);
u9b_chk('nr_hv', @() ax.YAxis.HandleVisibility); u9b_chk('cr_minorvalues', @() class(ax.XAxis.MinorTickValues));
u9b_chk('cr_minorvalues_size', @() size(ax.XAxis.MinorTickValues));
u9b_chk('cr_props', @() strjoin(properties(ax.XAxis)', ','));
cla(ax, 'reset');
h = histogram(ax, c3);
u9b_chkdiv('ch_class', @() oi_try( @() class(h)), 'ADR0234'); u9b_chk('ch_type', @() h.Type); u9b_chkdiv('ch_super', @() oi_try( @() superclasses(h)'), 'ADR0234');
u9b_chk('ch_findobj', @() numel(findobj(ax, 'Type', 'categoricalhistogram')));
u9b_chk('ch_findobj_hist', @() numel(findobj(ax, 'Type', 'histogram')));
u9b_chkdiv('ch_counts_manual', @() oi_try( @() chcounts(ax, c3)), 'ADR0234');
u9b_chk('ch_set_bincounts', @() chsetcounts(ax, c3));
u9b_chk('ch_ishandle', @() ishghandle(h));
cla(ax, 'reset'); plot(ax, [1 2], [3 4]);
u9b_chk('ax3_num', @() axis3(ax)); u9b_chk('ax3_num_ij', @() axis3ij(ax));
u9b_chk('ax_one_out', @() axis(ax));
u9b_chk('ax_out_tight', @() msgof(@() axout(ax)));
cla(ax, 'reset'); plot(ax, [1 2 3], [2 5 4]);
u9b_chk('num_tight', @() tightlim(ax, 'tight')); u9b_chkdiv('num_padded', @() oi_try( @() tightlim(ax, 'padded')), 'ADR0234');
u9b_chk('num_tickaligned', @() tightlim(ax, 'tickaligned')); u9b_chk('num_auto', @() tightlim(ax, 'auto'));
u9b_chk('num_limitmethod_tight', @() limmethod(ax, 'tight')); u9b_chk('num_limitmethod_padded', @() limmethod(ax, 'padded'));
cla(ax, 'reset'); bar(ax, [1 2 3]);
u9b_chkdiv('bar_padded', @() oi_try( @() tightlim(ax, 'padded')), 'ADR0234'); u9b_chk('bar_tight', @() tightlim(ax, 'tight'));
cla(ax, 'reset'); bar(ax, c3, [1 2 3]);
u9b_chkdiv('cbar_padded', @() oi_try( @() tightlim(ax, 'padded')), 'ADR0234'); u9b_chk('cbar_tight', @() tightlim(ax, 'tight'));
u9b_chk('cbar_limitmethod', @() limmethod(ax, 'padded'));
u9b_chk('cbar_axis_fill', @() msgof(@() axis(ax, 'fill'))); u9b_chk('cbar_axis_auto_y', @() axautoy(ax));
u9b_chk('cbar_axis_vis3d', @() msgof(@() axis(ax, 'vis3d')));
u9b_chk('cbar_axis_manual_modes', @() axmanual(ax));
u9b_chk('rot30cat', @() rotof(30, 3, [100 100 560 420])); u9b_chk('rot12cat5', @() rotof(12, 5, [100 100 560 420]));
u9b_chk('rot10cat5', @() rotof(10, 5, [100 100 560 420])); u9b_chk('rot8cat8', @() rotof(8, 8, [100 100 560 420]));
u9b_chk('rot_num40', @() rotnum(40)); u9b_chk('rot_num20', @() rotnum(20)); u9b_chk('rot_num12', @() rotnum(12));
u9b_chkdiv('rot_box30', @() oi_try( @() boxof(30, 12, [100 100 560 420])), 'ADR0234');
u9b_chkdiv('rot_box_label', @() oi_try( @() boxlabel(12, 12)), 'ADR0234');
u9b_chk('rot_mode_after_set', @() rotmode());
u9b_chk('rot_title_long', @() rottitle());
u9b_chk('rot_fontsize', @() rotfs());
u9b_chk('rot_ylabels', @() rotbarh());


u9b_chk('turn_digits', @() turns(arrayfun(@(k) sprintf('%04d', k), 1:10, 'UniformOutput', false), 10, [379 381 299 301 149 151]));
u9b_chk('turn_short', @() turns(arrayfun(@(k) sprintf('%02d', k), 1:10, 'UniformOutput', false), 10, [229 231 149 151]));
u9b_chk('turn_long', @() turns(arrayfun(@(k) sprintf('%010d', k), 1:10, 'UniformOutput', false), 10, [829 831]));
u9b_chk('turn_mmm', @() turns(arrayfun(@(k) sprintf('mmm%02d', k), 1:10, 'UniformOutput', false), 10, [569 571]));
u9b_chk('turn_iii', @() turns(arrayfun(@(k) sprintf('iii%02d', k), 1:10, 'UniformOutput', false), 10, [319 321]));
u9b_chk('turn_cat', @() turns(arrayfun(@(k) sprintf('cat%d', k), 1:10, 'UniformOutput', false), 10, [409 411]));
u9b_chk('turn_fs8', @() turns(arrayfun(@(k) sprintf('%04d', k), 1:10, 'UniformOutput', false), 8, [299 301 239 241 119 121]));
u9b_chk('turn_fs12', @() turns(arrayfun(@(k) sprintf('%04d', k), 1:10, 'UniformOutput', false), 12, [449 451 339 341 169 171]));
u9b_chk('turn_fs14', @() turns(arrayfun(@(k) sprintf('%04d', k), 1:10, 'UniformOutput', false), 14, [529 531 419 421 209 211]));
u9b_chk('turn_fs20', @() turns(arrayfun(@(k) sprintf('%04d', k), 1:10, 'UniformOutput', false), 20, [749 751 599 601 299 301]));
u9b_chk('turn_fs28', @() turns(arrayfun(@(k) sprintf('%04d', k), 1:10, 'UniformOutput', false), 28, [1049 1051 839 841 419 421]));
u9b_chk('turn_tie_cat8', @() tiecat(8)); u9b_chk('turn_tie_cat5', @() tiecat(5)); u9b_chk('turn_tie_cat31', @() tiecat(31));
u9b_chk('turn_tie_cat32', @() tiecat(32)); u9b_chk('turn_tie_num8', @() tienum(8)); u9b_chk('turn_tie_num20', @() tienum(20));
u9b_chk('turn_room_per_tick', @() roomnum());



function m = msgof(f)
try
    f();
    m = 'no error';
catch e
    m = [e.identifier ' | ' e.message];
end
end

function out = axlims(ax, mode)
axis(ax, mode);
out = {class(ax.XLim), ax.YLim, ax.XLimMode, ax.YLimMode};
end

function out = axout(ax)
out = axis(ax, 'tight');
end

function out = axstyle(ax)
[m, v, d] = axis(ax); %#ok<ASGLU>
out = m;
end

function out = axgca(c3)
f = figure('Visible', 'off');
bar(c3, [1 2 3]);
out = msgof(@() axis);
close(f);
end

function out = axcatcat(c3)
f = figure('Visible', 'off'); ax = axes(f);
scatter(ax, c3, c3);
out = msgof(@() axis(ax));
close(f);
end

function out = axbarh(c3)
f = figure('Visible', 'off'); ax = axes(f);
barh(ax, c3, [1 2 3]);
out = msgof(@() axis(ax));
close(f);
end

function out = ax3d(c3)
f = figure('Visible', 'off'); ax = axes(f);
plot3(ax, c3, [1 2 3], [1 2 3]);
out = msgof(@() axis(ax));
close(f);
end

function out = ax3dset(c3)
f = figure('Visible', 'off'); ax = axes(f);
plot3(ax, c3, [1 2 3], [1 2 3]);
out = msgof(@() axis(ax, [0 1 0 1 0 1]));
close(f);
end

function out = axnum()
f = figure('Visible', 'off'); ax = axes(f);
plot(ax, [1 2], [3 4]);
out = axis(ax);
close(f);
end

function out = axcla(c3)
f = figure('Visible', 'off'); ax = axes(f);
bar(ax, c3, [1 2 3]);
cla(ax);
out = msgof(@() axis(ax));
close(f);
end

function out = axmsg(ax)
try
    axis(ax, [0 3 0 3]);
    out = 'no error';
catch e
    out = e.message;
end
end

function out = llafter(c3)
f = figure('Visible', 'off'); ax = axes(f);
try
    loglog(ax, c3, [1 2 3]);
catch
end
out = {ax.XScale, ax.YScale, numel(ax.Children), class(ax.XLim)};
close(f);
end

function out = slycat()
f = figure('Visible', 'off'); ax = axes(f);
h = semilogy(ax, categorical({'x', 'y'}), [1 2]);
out = {class(h.XData), ax.YScale, ax.XScale};
close(f);
end

function out = slxcaty()
f = figure('Visible', 'off'); ax = axes(f);
try
    h = semilogx(ax, [1 2], categorical({'x', 'y'}));
    out = {class(h.YData), ax.XScale, ax.YScale};
catch e
    out = [e.identifier ' | ' e.message];
end
close(f);
end

function setlog(c3)
f = figure('Visible', 'off'); ax = axes(f);
bar(ax, c3, [1 2 3]);
ax.XScale = 'log';
close(f);
end

function rulerlog(c3)
f = figure('Visible', 'off'); ax = axes(f);
bar(ax, c3, [1 2 3]);
ax.XAxis.Scale = 'log';
close(f);
end

function llheld(c3)
f = figure('Visible', 'off'); ax = axes(f);
bar(ax, c3, [1 2 3]);
hold(ax, 'on');
loglog(ax, [1 2], [3 4]);
close(f);
end

function out = llscale(c3)
f = figure('Visible', 'off'); ax = axes(f);
loglog(ax, [1 2], [3 4]);
hold(ax, 'on');
try
    bar(ax, c3, [1 2 3]);
    out = 'no error';
catch e
    out = [e.identifier ' | ' e.message];
end
close(f);
end

function out = hbval(ax, c3, name, value)
h = histogram(ax, c3, name, value);
out = h.Values;
end

function out = hbcats(ax, c3, name, value)
h = histogram(ax, [c3 c3(1)], name, value);
out = {h.Categories, h.Values};
end

function out = hbh(ax, c3)
h = histogram(ax, c3, 'Orientation', 'horizontal');
out = {h.Orientation, class(ax.YAxis.Limits), class(ax.XAxis.Limits)};
end

function hbset(ax, c3, name, value)
h = histogram(ax, c3);
set(h, name, value);
end

function out = hbget(ax, c3, name)
h = histogram(ax, c3);
out = get(h, name);
end

function out = hbdot(ax, c3)
h = histogram(ax, c3);
out = h.BinMethod;
end

function out = hbnumcats(ax)
h = histogram(ax, [1 2 3]);
out = h.Categories;
end

function setr(r, name, value)
r.(name) = value;
end

function out = deep(st)
f = fieldnames(st);
out = '';
for k = 1:numel(f)
    v = st.(f{k});
    out = [out f{k} '=' class(v) mat2str(size(v)) ':' txt(v) ';']; %#ok<AGROW>
end
end

function t = txt(v)
if isnumeric(v) || islogical(v)
    if numel(v) > 12
        t = '...';
    else
        t = mat2str(v, 6);
    end
elseif ischar(v)
    t = v;
elseif iscell(v)
    t = strjoin(cellfun(@txt, v(:)', 'UniformOutput', false), '|');
elseif isstring(v)
    t = strjoin(v(:)', ',');
elseif iscategorical(v)
    t = strjoin(cellstr(v(:))', ',');
elseif isa(v, 'matlab.lang.OnOffSwitchState')
    t = char(v);
elseif isa(v, 'function_handle')
    t = func2str(v);
else
    t = class(v);
end
end

function plotnoax()
f = figure('Visible', 'off');
plot(["a" "b"], [1 2]);
close(f);
end

function out = barcell(ax)
b = bar(ax, {'a', 'b'}, [1 2]);
out = {class(b.XData), cellstr(b.XData), class(ax.XLim)};
end

function out = barcelllim(ax)
cla(ax, 'reset');
bar(ax, {'b', 'a'}, [1 2]);
out = {cellstr(ax.XLim), cellstr(ax.XTick)'};
end

function out = fillcat(ax)
cla(ax, 'reset');
p = fill(ax, categorical({'x', 'y', 'x'}), [1 2 3], 'r');
out = {class(p.XData), size(p.XData), class(ax.XLim), cellstr(ax.XTick)'};
end

function out = patchcat(ax)
cla(ax, 'reset');
p = patch(ax, categorical({'x', 'y', 'x'}), [1 2 3], 'r');
out = {class(p.XData), size(p.XData), class(ax.XLim), cellstr(ax.XLim)};
end

function out = fillruler(ax)
cla(ax, 'reset');
fill(ax, categorical({'x', 'y', 'x'}), [1 2 3], 'r');
out = {ax.XAxis.Categories', mat2str(ruler2num(ax.XLim, ax.XAxis))};
end

function t = s(v)
if iscategorical(v)
    cs = cellstr(v); t = strjoin(reshape(cs, 1, []), ',');
elseif isnumeric(v) || islogical(v)
    t = mat2str(v, 6);
elseif ischar(v)
    t = strrep(v, newline, '\n');
elseif isstring(v)
    t = strjoin(reshape(v, 1, []), ',');
elseif iscell(v)
    t = strjoin(cellfun(@(x) s(x), reshape(v, 1, []), 'UniformOutput', false), '|');
elseif isa(v, 'matlab.lang.OnOffSwitchState')
    t = char(v);
else
    t = class(v);
end
end

function out = chcounts(ax, c3)
cla(ax, 'reset');
h = histogram(ax, 'Categories', {'a', 'b', 'c'}, 'BinCounts', [3 2 1], 'BinCountsMode', 'manual');
out = {h.Values, h.BinCountsMode, class(h)};
end

function out = chsetcounts(ax, c3)
cla(ax, 'reset');
h = histogram(ax, c3);
h.BinCounts = [5 6 7];
out = {h.Values, h.BinCountsMode};
end

function out = axis3(ax)
[m, v, d] = axis(ax);
out = {m, v, d};
end

function out = axis3ij(ax)
axis(ax, 'ij'); axis(ax, 'manual');
[m, v, d] = axis(ax);
out = {m, v, d};
axis(ax, 'xy'); axis(ax, 'auto');
end

function out = tightlim(ax, mode)
axis(ax, mode);
out = {ax.XLim, ax.YLim, ax.XLimMode, ax.YLimMode};
axis(ax, 'auto');
end

function out = limmethod(ax, mode)
axis(ax, mode);
out = {ax.XLimitMethod, ax.YLimitMethod};
axis(ax, 'tickaligned');
end

function out = axautoy(ax)
ylim(ax, [0 10]);
axis(ax, 'auto y');
out = {ax.YLim, ax.YLimMode, ax.XLimMode};
end

function out = axmanual(ax)
axis(ax, 'manual');
out = {ax.XLimMode, ax.YLimMode, ax.ZLimMode};
axis(ax, 'auto');
end

function out = rotof(n, L, pos)
f = figure('Visible', 'off', 'Position', pos); ax = axes(f);
names = arrayfun(@(k) sprintf(['c%0' num2str(L - 1) 'd'], k), 1:n, 'UniformOutput', false);
bar(ax, categorical(names, names), 1:n);
out = {ax.XTickLabelRotation, ax.XTickLabelRotationMode, ax.XAxis.TickLabelRotation, ax.YTickLabelRotation};
close(f);
end

function out = rotnum(n)
f = figure('Visible', 'off', 'Position', [100 100 560 420]); ax = axes(f);
plot(ax, 1:n, 1:n); xticks(ax, 1:n);
out = {ax.XTickLabelRotation, ax.XTickLabelRotationMode};
close(f);
end

function out = boxof(n, L, pos)
f = figure('Visible', 'off', 'Position', pos); ax = axes(f);
names = arrayfun(@(k) sprintf(['%0' num2str(L) 'd'], k), 1:n, 'UniformOutput', false);
bar(ax, categorical(names, names), 1:n);
out = {ax.XTickLabelRotation, ax.InnerPosition, ax.TightInset};
close(f);
end

function out = boxlabel(n, L)
f = figure('Visible', 'off', 'Position', [100 100 560 420]); ax = axes(f);
names = arrayfun(@(k) sprintf(['%0' num2str(L) 'd'], k), 1:n, 'UniformOutput', false);
bar(ax, categorical(names, names), 1:n); xlabel(ax, 'x'); title(ax, 't');
out = {ax.XTickLabelRotation, ax.InnerPosition, ax.TightInset};
close(f);
end

function out = rotmode()
f = figure('Visible', 'off', 'Position', [100 100 560 420]); ax = axes(f);
names = arrayfun(@(k) sprintf('c%02d', k), 1:30, 'UniformOutput', false);
bar(ax, categorical(names, names), 1:30);
a1 = ax.XTickLabelRotation;
ax.XTickLabelRotation = 0;
a2 = ax.XTickLabelRotationMode;
ax.XTickLabelRotationMode = 'auto';
a3 = ax.XTickLabelRotation;
out = {a1, a2, a3};
close(f);
end

function out = rottitle()
f = figure('Visible', 'off', 'Position', [100 100 560 420]); ax = axes(f);
names = arrayfun(@(k) sprintf('label%02d', k), 1:12, 'UniformOutput', false);
plot(ax, categorical(names, names), 1:12);
out = {ax.XTickLabelRotation, round(ax.InnerPosition, 6)};
close(f);
end

function out = rotfs()
f = figure('Visible', 'off', 'Position', [100 100 560 420]); ax = axes(f);
names = arrayfun(@(k) sprintf('c%02d', k), 1:20, 'UniformOutput', false);
bar(ax, categorical(names, names), 1:20); ax.FontSize = 14;
out = {ax.XTickLabelRotation, round(ax.InnerPosition, 6)};
close(f);
end

function out = rotbarh()
f = figure('Visible', 'off', 'Position', [100 100 560 420]); ax = axes(f);
names = arrayfun(@(k) sprintf('c%02d', k), 1:40, 'UniformOutput', false);
barh(ax, categorical(names, names), 1:40);
out = {ax.XTickLabelRotation, ax.YTickLabelRotation};
close(f);
end

function out = turns(names, fs, widths)
% the angle at each axes width, for bars over names at fs points
f = figure('Visible', 'off', 'Position', [100 100 1500 500]); ax = axes(f);
bar(ax, categorical(names, names), 1:numel(names));
ax.FontSize = fs; ax.Units = 'pixels';
out = zeros(1, numel(widths));
for k = 1:numel(widths)
    ax.Position = [20 60 widths(k) 400];
    out(k) = ax.XTickLabelRotation;
end
close(f);
end

function out = tiecat(n)
% n bars with room per tick exactly 38, 30 and 15 pixels: the thresholds of four digits at 10 points
names = arrayfun(@(k) sprintf('%04d', k), 1:n, 'UniformOutput', false);
f = figure('Visible', 'off', 'Position', [10 10 1700 500]); ax = axes(f);
bar(ax, categorical(names, names), 1:n);
ax.FontSize = 10; ax.Units = 'pixels';
out = zeros(1, 3); T = [38 30 15];
for k = 1:3
    if n * T(k) > 1650, out(k) = NaN; continue, end
    ax.Position = [20 60 n * T(k) 400];
    out(k) = ax.XTickLabelRotation;
end
close(f);
end

function out = tienum(n)
% the same ties on a numeric ruler with ticks 1..n under limits [0.5 n+0.5]
names = arrayfun(@(k) sprintf('%04d', k), 1:n, 'UniformOutput', false);
f = figure('Visible', 'off', 'Position', [10 10 1700 500]); ax = axes(f);
plot(ax, 1:n, 1:n);
ax.XLim = [0.5 n + 0.5]; ax.XTick = 1:n; ax.XTickLabel = names;
ax.FontSize = 10; ax.Units = 'pixels';
out = zeros(1, 3); T = [38 30 15];
for k = 1:3
    ax.Position = [20 60 n * T(k) 400];
    out(k) = ax.XTickLabelRotation;
end
close(f);
end

function out = roomnum()
% ticks 1..9 under limits [0 10]: the room is the ruler shared among the ticks, not their distance
names = arrayfun(@(k) sprintf('%04d', k), 1:9, 'UniformOutput', false);
f = figure('Visible', 'off', 'Position', [10 10 1700 500]); ax = axes(f);
plot(ax, 1:9, 1:9);
ax.XLim = [0 10]; ax.XTick = 1:9; ax.XTickLabel = names;
ax.FontSize = 10; ax.Units = 'pixels';
out = zeros(1, 2); W = [299 271];
for k = 1:2
    ax.Position = [20 60 W(k) 400];
    out(k) = ax.XTickLabelRotation;
end
close(f);
end

function r = oi_try(fn)
% The value fn answers, or the identifier of the error it raises, as one line (a divergence row).
try
    r = fn();
catch err
    r = err.identifier;
end
end
