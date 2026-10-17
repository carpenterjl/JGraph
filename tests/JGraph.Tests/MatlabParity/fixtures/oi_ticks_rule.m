% record: -noFigureWindows
% Open item 99 (ADR 0231): the ticks a linear ruler chooses and the limits an auto-scaling ruler
% takes around them, as R2025b chooses them, in both dialects. The rule was fitted to probe_99
% (77 ranges at six figure sizes), probe_99b (a fine sweep of ranges at fourteen ruler lengths and
% four fonts, automatic limits, resizes and charts) and probe_99c (short rulers, how far an aligned
% limit reaches, padded limits, 3-D views) in the open-items scratch. Lines marked div=ADR0231 are
% where R2025b's own floating point decides a tie or a snap, where its axes font shrinks with a
% small figure, or where a uiaxes counts differently. Numbers are rounded to nine decimals, because
% R2025b's tick values carry its own floating-point noise (1.3999999999999999 for 1.4).
close all force

% --- ticks inside written limits on the default axes (434 by 342 pixels)
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
ax = axes(f); plot(ax, [0 1], [0 1]); drawnow;
ranges = {[0 1], [0 2], [0 3], [0 4], [0 5], [0 7], [0 8], [0 9], [0 10], [0 11], [0 12], [0 13], [0 15], ...
    [0 16], [0 19], [0 20], [0 23], [0 25], [0 35], [0 45], [0 50], [0 55], [0 75], [0 90], [0 100], ...
    [0 150], [0 200], [0 1000], [0 50000], [0 2e6], [1 3], [1 7], [2 97], [-1 1], [-3 11], [0 0.0004], ...
    [0 0.001], [0.123 0.987], [-5 5], [10 20], [100 101], [0 2.5], [0 0.25], [0 1.5], [0 0.3], [0 0.7], ...
    [3 4], [1 1.5], [-20 20], [-50 50], [0 150000], [0 9999], [0 10000], [-30000 30000]};
for k = 1:numel(ranges)
    r = ranges{k}; ax.XLim = r; ax.YLim = r; drawnow;
    u9b_chk(sprintf('x_%d_%s', k, oi_name(r)), @() oi_r(ax.XTick));
    u9b_chk(sprintf('y_%d_%s', k, oi_name(r)), @() oi_r(ax.YTick));
end
delete(ax);

% --- automatic limits are aligned to the ticks (tickaligned, the default in both dialects)
pairs = {[0 1], [0 0.7], [0 7], [0 13], [0 23], [0 9999], [0.123 0.987], [2 97], [-3 11], [-5 5], [-50 50], ...
    [0 1.5], [0 15], [0 0.0004], [1 1], [5 5], [0 0], [1 1.0001], [-1e-7 1e-7], [3 4], [1e4 2e4], [0.5 9.5], ...
    [1 10], [1 100], [0 2e6], [12345 12399], [-1 1e3], [0.05 0.95], [-0.95 0.95], [10 11.3], [-7 2], [0 1e-3], ...
    [99 101], [-1 0], [0.5 1.5]};
for k = 1:numel(pairs)
    p = pairs{k};
    ax = axes(f); plot(ax, 1:10, linspace(p(1), p(2), 10)); drawnow;
    u9b_chk(sprintf('auto_%d_%s_lim', k, oi_name(p)), @() oi_r(ax.YLim));
    u9b_chk(sprintf('auto_%d_%s_tick', k, oi_name(p)), @() oi_r(ax.YTick));
    delete(ax);
end
u9b_chk('auto_method', @() ax.YLimitMethod);
ax = axes(f); plot(ax, 1:10, linspace(-0.2, 0.9, 10)); drawnow;
u9b_chk('auto_neg_lim', @() oi_r(ax.YLim));
u9b_chk('auto_neg_tick', @() oi_r(ax.YTick));
delete(ax);
for n = [7 100 2 50]
    ax = axes(f); plot(ax, 1:n, linspace(0.123, 0.987, n)); drawnow;
    u9b_chk(sprintf('auto_x_%d_lim', n), @() oi_r(ax.XLim));
    u9b_chk(sprintf('auto_x_%d_tick', n), @() oi_r(ax.XTick));
    delete(ax);
end
ax = axes(f); plot(ax, 1, 5); drawnow;
u9b_chk('one_point', @() oi_r([ax.XLim ax.YLim]));
u9b_chk('one_point_ticks', @() oi_r([ax.XTick ax.YTick]));
delete(ax);
ax = axes(f); plot(ax, [1 2], [5 5]); drawnow;
u9b_chk('two_points', @() oi_r([ax.XLim ax.YLim]));
delete(ax);
ax = axes(f); plot(ax, [1 2], [1e6 1e6]); drawnow;
u9b_chk('big_flat', @() oi_r(ax.YLim));
delete(ax);
ax = axes(f); drawnow;
u9b_chk('empty_axes', @() oi_r([ax.XLim ax.YLim ax.XTick]));
delete(ax);
ax = axes(f); plot(ax, 1:10, linspace(0.123, 0.987, 10)); hold(ax, 'on'); plot(ax, 1:10, linspace(-3, 11, 10)); drawnow;
u9b_chk('two_lines', @() oi_r([ax.XLim ax.YLim]));
u9b_chk('two_lines_ticks', @() oi_r(ax.YTick));
ax.XLim = [0.5 9.5]; drawnow;
u9b_chk('written_x', @() oi_r(ax.XTick));
delete(ax);
ax = axes(f); semilogy(ax, 1:10, logspace(0.3, 3.7, 10)); drawnow;
u9b_chk('semilogy', @() oi_r([ax.YLim ax.YTick]));
delete(ax);

% --- the other limit methods
for k = [4 13 26 30 23 2 22 9 33 10 21 1 20 35]
    p = pairs{k};
    ax = axes(f); plot(ax, 1:10, linspace(p(1), p(2), 10)); ax.YLimitMethod = 'padded'; drawnow;
    if any(k == [1 20 33 35])
        u9b_chkdiv(sprintf('padded_%d_%s', k, oi_name(p)), @() oi_r(ax.YLim), 'ADR0231');
    else
        u9b_chk(sprintf('padded_%d_%s', k, oi_name(p)), @() oi_r(ax.YLim));
    end
    ax.YLimitMethod = 'tight'; drawnow;
    u9b_chk(sprintf('tight_%d_%s', k, oi_name(p)), @() oi_r(ax.YLim));
    delete(ax);
end
ax = axes(f); plot(ax, 1:10, 5 * ones(1, 10)); ax.YLimitMethod = 'tight'; drawnow;
u9b_chk('tight_flat', @() oi_r(ax.YLim));
delete(ax);

% --- the font and the figure size enter the count
ax = axes(f); plot(ax, 1:10, linspace(0.123, 0.987, 10)); ax.FontSize = 14; drawnow;
u9b_chk('font14', @() oi_r([ax.XLim ax.YLim]));
u9b_chk('font14_ticks', @() oi_r(ax.XTick));
delete(ax);
ax = axes(f); plot(ax, 1:10, linspace(0.123, 0.987, 10)); ax.FontSize = 8; drawnow;
u9b_chk('font8', @() oi_r([ax.XLim ax.XTick]));
delete(ax);
ax = axes(f); plot(ax, 1:10, linspace(-3, 11, 10)); drawnow;
u9b_chk('resize_0', @() oi_r([ax.YLim ax.YTick]));
f.Position = [100 100 280 210]; drawnow;
u9b_chk('resize_1', @() oi_r([ax.YLim ax.YTick ax.XLim ax.XTick]));
u9b_chk('resize_1_mode', @() ax.YLimMode);
f.Position = [100 100 560 420]; drawnow;
u9b_chk('resize_2', @() oi_r([ax.YLim ax.YTick]));
ax.YLim = [-3 11]; drawnow;
u9b_chk('resize_3', @() oi_r([ax.YLim ax.YTick]));
u9b_chk('resize_3_mode', @() ax.YLimMode);
delete(ax);
sizes = {[1120 840], [200 800], [280 210]};
for s = 1:numel(sizes)
    f.Position = [100 100 sizes{s}]; drawnow;
    for k = [1 2 4 7 9 12 23 30]
        p = pairs{k};
        ax = axes(f); plot(ax, 1:10, linspace(p(1), p(2), 10)); drawnow;
        name = sprintf('size_%d_%d_%s', s, k, oi_name(p));
        if s == 3 && any(k == [2 4 30])
            u9b_chkdiv(name, @() oi_r([ax.XLim ax.XTick ax.YLim ax.YTick]), 'ADR0231');
        else
            u9b_chk(name, @() oi_r([ax.XLim ax.XTick ax.YLim ax.YTick]));
        end
        delete(ax);
    end
end
f.Position = [100 100 560 420]; drawnow;
ax = subplot(2, 2, 1, 'Parent', f); plot(ax, 1:10, linspace(0.123, 0.987, 10)); drawnow;
u9b_chk('subplot', @() oi_r([ax.XLim ax.XTick ax.YLim ax.YTick])); % the font shrinks with the subplot and R2025b scores the short x ruler (open item 119, ADR 0238)
delete(ax);

% --- 3-D: each ruler by the run of its edge across or up the page
ax = axes(f); plot3(ax, linspace(0.123, 0.987, 10), linspace(2, 97, 10), linspace(-3, 11, 10)); drawnow;
u9b_chk('p3_lims', @() oi_r([ax.XLim ax.YLim ax.ZLim]));
u9b_chk('p3_ticks', @() oi_r([ax.XTick ax.YTick ax.ZTick]));
views = {[0 90], [90 0], [0 0], [45 45], [-20 10]};
for v = 1:numel(views)
    view(ax, views{v}); drawnow;
    u9b_chk(sprintf('p3_view_%d_lims', v), @() oi_r([ax.XLim ax.YLim ax.ZLim]));
    u9b_chk(sprintf('p3_view_%d_ticks', v), @() oi_r([ax.XTick ax.YTick ax.ZTick]));
end
delete(ax);
close(f);

% --- a uiaxes counts as R2025b's own, which is a divergence to keep
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
ua = uiaxes(uf); plot(ua, 1:10, linspace(0.123, 0.987, 10)); drawnow;
u9b_chkdiv('uiaxes', @() oi_r([ua.XLim ua.XTick]), 'ADR0231');
close(uf);

function s = oi_name(r)
s = sprintf('%g_%g', r(1), r(2));
s = strrep(strrep(strrep(s, '-', 'm'), '.', 'p'), '+', '');
end

function v = oi_r(v)
v = round(v, 9);
end
