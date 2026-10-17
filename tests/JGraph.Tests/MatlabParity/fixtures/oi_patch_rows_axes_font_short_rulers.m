% record: -noFigureWindows
% Open items 114, 118 and 119 (ADR 0232), in both dialects. 114: a patch answers XData, YData and
% ZData as columns (one per face), Vertices as n-by-2 when flat, CData as the ink 1-by-1-by-3 when
% it has no colour data, a column per face or vertex otherwise; a write of a vector XData is one
% face, of a matrix a face per column, and the vertex list grows with NaN; a text's default
% FontSize is 10. 118: the axes font shrinks with the axes' own pixel box (8.5 from 132 px wide or
% 106 high, 9 from 180/144, 9.5 from 228/182, 10 from 276/222, else 8), the title and labels at
% 1.1 of it, a legend and colorbar at 0.9, while FontSizeMode stays auto; a manual FontSize holds.
% 119: the tick count caps of ADR 0231 refitted at a fixed font so short rulers, the polar r ruler
% (caps 7 and 5, whatever the size) and the colorbar (the axes caps at its own length and 0.9 of
% the font) count as R2025b does. Lines marked div=ADR0232 are where its text ink is
% [0.1294 0.1294 0.1294] and this build's is 0.251, where a polar axes' Units cannot be pixels here,
% or where R2025b's own double arithmetic drops a landing tick; the short-ruler rows that scored
% density under ~200 px were lifted by ADR 0238 (open item 119), which also marks the one row where
% a shrunk 9-point font moves a band's edge (sq217_y_0_0p15). Tick values are rounded to nine decimals.
close all force

% --- 114: patch reads
f = figure('Visible', 'off'); ax = axes(f);
p = patch(ax, [0 1 1 0], [0 0 1 1], 'r');
oi_chk('patch_xdata', @() p.XData); oi_chk('patch_ydata', @() p.YData); oi_chk('patch_zdata', @() p.ZData);
oi_chk('patch_cdata', @() p.CData); oi_chk('patch_vertices', @() p.Vertices); oi_chk('patch_faces', @() p.Faces);
oi_chk('patch_fvcd', @() p.FaceVertexCData); oi_chk('patch_facecolor', @() p.FaceColor);
p2 = patch(ax, [0;1;1;0], [0;0;1;1], [1;2;3;4]);
oi_chk('patchcol_xdata', @() p2.XData); oi_chk('patchcol_cdata', @() p2.CData); oi_chk('patchcol_vertices', @() p2.Vertices);
oi_chk('patchcol_fvcd', @() p2.FaceVertexCData); oi_chk('patchcol_facecolor', @() p2.FaceColor);
p2c = patch(ax, [0 1 1 0], [0 0 1 1], 5);
oi_chk('patchscalarc_cdata', @() p2c.CData); oi_chk('patchscalarc_fvcd', @() p2c.FaceVertexCData); oi_chk('patchscalarc_facecolor', @() p2c.FaceColor);
p2d = patch(ax, [0 1 1 0], [0 0 1 1], [0.2 0.4 0.6]);
oi_chk('patchrgb_cdata', @() p2d.CData); oi_chk('patchrgb_fvcd', @() p2d.FaceVertexCData); oi_chk('patchrgb_facecolor', @() p2d.FaceColor);
p3 = patch(ax, [0 1 1 0], [0 0 1 1], [0 0 1 1], 'g');
oi_chk('patchz_zdata', @() p3.ZData); oi_chk('patchz_vertices', @() p3.Vertices); oi_chk('patchz_xdata', @() p3.XData);
p4 = patch(ax, [0 1; 1 2; 1 2], [0 0; 0 0; 1 1], 'b');
oi_chk('patchm_xdata', @() p4.XData); oi_chk('patchm_faces', @() p4.Faces); oi_chk('patchm_vertices', @() p4.Vertices);
oi_chk('patchm_cdata', @() p4.CData); oi_chk('patchm_fvcd', @() p4.FaceVertexCData);
p4b = patch(ax, [0 1; 1 2; 1 2], [0 0; 0 0; 1 1], [1 2]);
oi_chk('patchm2_cdata', @() p4b.CData); oi_chk('patchm2_fvcd', @() p4b.FaceVertexCData); oi_chk('patchm2_facecolor', @() p4b.FaceColor);
p4c = patch(ax, [0 1; 1 2; 1 2], [0 0; 0 0; 1 1], [1 2; 3 4; 5 6]);
oi_chk('patchm3_cdata', @() p4c.CData); oi_chk('patchm3_fvcd', @() p4c.FaceVertexCData); oi_chk('patchm3_facecolor', @() p4c.FaceColor);
p5 = patch(ax, 'Faces', [1 2 3], 'Vertices', [0 0; 1 0; 1 1], 'FaceColor', 'y');
oi_chk('patchfv_xdata', @() p5.XData); oi_chk('patchfv_zdata', @() p5.ZData); oi_chk('patchfv_vertices', @() p5.Vertices);
oi_chk('patchfv_cdata', @() p5.CData); oi_chk('patchfv_fvcd', @() p5.FaceVertexCData);
p6 = patch(ax, 'Faces', [1 2 3], 'Vertices', [0 0 0; 1 0 0; 1 1 0], 'FaceColor', 'y');
oi_chk('patchfv3_zdata', @() p6.ZData); oi_chk('patchfv3_vertices', @() p6.Vertices);
p6b = patch(ax, 'Faces', [1 2 3; 2 3 4], 'Vertices', [0 0; 1 0; 1 1; 0 1], 'FaceVertexCData', [1; 2; 3; 4], 'FaceColor', 'interp');
oi_chk('patchfv4_xdata', @() p6b.XData); oi_chk('patchfv4_cdata', @() p6b.CData); oi_chk('patchfv4_fvcd', @() p6b.FaceVertexCData);
p6c = patch(ax, 'Faces', [1 2 3; 2 3 4], 'Vertices', [0 0; 1 0; 1 1; 0 1], 'FaceVertexCData', [1; 2], 'FaceColor', 'flat');
oi_chk('patchfv5_cdata', @() p6c.CData); oi_chk('patchfv5_fvcd', @() p6c.FaceVertexCData);
p6d = patch(ax, 'XData', [0 1 1], 'YData', [0 0 1]);
oi_chk('patchxy_xdata', @() p6d.XData); oi_chk('patchxy_vertices', @() p6d.Vertices); oi_chk('patchxy_faces', @() p6d.Faces);
oi_chk('patchxy_cdata', @() p6d.CData); oi_chk('patchxy_facecolor', @() p6d.FaceColor);
p6e = patch(ax, 'XData', [0;1;1], 'YData', [0;0;1], 'ZData', [1;1;1]);
oi_chk('patchxyz_zdata', @() p6e.ZData); oi_chk('patchxyz_vertices', @() p6e.Vertices);

% --- 114: patch writes
pl = patch(ax, [0 1 1 0; 2 3 3 2]', [0 0 1 1; 0 0 1 1]', 'r');
oi_chk('patch2f_xdata', @() pl.XData); oi_chk('patch2f_faces', @() pl.Faces); oi_chk('patch2f_vertices', @() size(pl.Vertices));
oi_chk('patch2f_written_row', @() xwrite(pl, [5 6 7 8])); oi_chk('patch2f_written_col', @() xwrite(pl, [5; 6; 7; 8]));
oi_chk('patch2f_written_mat', @() xwrite(pl, [5 6 7 8; 1 2 3 4]'));
pm = patch(ax, [0 1 1], [0 0 1], 'r');
oi_chk('patch_vert3', @() vwrite(pm, [0 0 1; 1 0 1; 1 1 1])); oi_chk('patch_vert_grow', @() vwrite(pm, [0 0; 1 0; 1 1; 0 1]));
oi_chk('patch_faces_written', @() facewrite(pm, [1 2 3; 1 3 4]));
pn = patch(ax, [0 1 1], [0 0 1], 'r');
oi_chk('patch_xdata_grow', @() xwrite(pn, [0 1 1 0])); oi_chk('patch_xdata_grow_ydata', @() pn.YData);
oi_chk('patch_xdata_grow_vertices', @() pn.Vertices); oi_chk('patch_xdata_grow_faces', @() pn.Faces);
po = patch(ax, [0 1 1], [0 0 1], 'r');
oi_chk('patch_cdata_write_rgb', @() cwrite(po, [0.1 0.2 0.3])); oi_chk('patch_cdata_write_scalar', @() cwrite(po, 7));
oi_chk('patch_cdata_write_col', @() cwrite(po, [1; 2; 3])); oi_chk('patch_cdata_write_row', @() cwrite(po, [1 2 3]));
oi_chk('patch_fvcd_write_col', @() fwrite(po, [4; 5; 6])); oi_chk('patch_fvcd_write_rgb', @() fwrite(po, [0.1 0.2 0.3]));
oi_chk('patch_fvcd_write_rgb_rows', @() fwrite(po, [0.1 0.2 0.3; 0.4 0.5 0.6; 0.7 0.8 0.9]));

% --- 114: pie wedges, fill, text
cla(ax); h = pie(ax, [1 2 3]);
oi_chk('pie_xdata_size', @() size(h(1).XData)); oi_chk('pie_zdata', @() h(1).ZData); oi_chk('pie_vertices_size', @() size(h(1).Vertices));
oi_chk('pie_cdata', @() h(1).CData); oi_chk('pie_faces_size', @() size(h(1).Faces));
oi_chk('pie_cat', @() size([h(1).XData; h(3).XData])); oi_chk('pie_cat2', @() size([h(1).XData h(3).XData]));
oi_chk('pie_text_fontsize', @() h(2).FontSize);
oi_chk('pie_text_color', @() h(2).Color);
cla(ax); hf = fill(ax, [0 1 1], [0 0 1], 'r');
oi_chk('fill_xdata', @() hf.XData); oi_chk('fill_cdata', @() hf.CData); oi_chk('fill_vertices', @() hf.Vertices); oi_chk('fill_faces', @() hf.Faces);
hf2 = fill(ax, [0 1 1], [0 0 1], [1; 2; 3]);
oi_chk('fillc_cdata', @() hf2.CData); oi_chk('fillc_fvcd', @() hf2.FaceVertexCData); oi_chk('fillc_facecolor', @() hf2.FaceColor);
hf3 = fill(ax, [0 1 1; 2 3 3]', [0 0 1; 0 0 1]', 'g');
oi_chk('fillm_n', @() numel(hf3)); oi_chk('fillm_xdata', @() hf3(1).XData);
cla(ax); h3 = fill3(ax, [0 1 1], [0 0 1], [0 0 1], 'r');
oi_chk('fill3_zdata', @() h3.ZData); oi_chk('fill3_vertices', @() h3.Vertices); oi_chk('fill3_xdata', @() h3.XData);
cla(ax); t = text(ax, 0.5, 0.5, 'hi');
oi_chk('text_fontsize', @() t.FontSize); oi_chk('text_fontsizemode', @() t.FontSizeMode);
oi_chk('text_color', @() t.Color);
oi_chk('factory_text_fs', @() get(groot, 'FactoryTextFontSize'));
close(f);

% --- 118: the axes font by the axes' own pixel box
f = figure('Visible', 'off', 'Position', [50 50 900 700]); ax = axes(f, 'Units', 'pixels'); ax.Position = [60 60 500 400]; drawnow;
full = ax.FontSize; % 10 in R2025b, 11 here (the base is ADR 0231's recorded divergence): the bands are ratios to it
for W = [100 130 132 150 178 180 200 226 228 250 274 276 300]
    ax.Position = [60 60 W 400]; drawnow;
    oi_chk(sprintf('font_w%d', W), @() oi_r(ax.FontSize / full));
end
for H = [80 104 106 120 142 144 160 180 182 200 220 222 260]
    ax.Position = [60 60 500 H]; drawnow;
    oi_chk(sprintf('font_h%d', H), @() oi_r(ax.FontSize / full));
end
for W = [120 160 200 280]
    for H = [90 130 190 230]
        ax.Position = [60 60 W H]; drawnow;
        oi_chk(sprintf('font_w%d_h%d', W, H), @() oi_r(ax.FontSize / full));
    end
end
oi_chk('font_mode', @() ax.FontSizeMode);
ax.Position = [60 60 150 120]; title(ax, 'T'); xlabel(ax, 'x'); drawnow;
oi_chk('font_small_title', @() oi_r(ax.Title.FontSize / ax.FontSize)); oi_chk('font_small_xlabel', @() oi_r(ax.XLabel.FontSize / ax.FontSize)); oi_chk('font_small_ticklabel', @() oi_r(ax.FontSize / full));
lg = legend(ax, 'a'); cb = colorbar(ax); drawnow;
oi_chk('font_small_legend', @() oi_r(lg.FontSize / ax.FontSize)); oi_chk('font_small_colorbar', @() oi_r(cb.FontSize / ax.FontSize));
oi_chk('font_small_legend_mode', @() lg.FontSizeMode); oi_chk('font_small_colorbar_mode', @() cb.FontSizeMode);
ax.FontSize = 12; drawnow;
oi_chk('font_manual', @() ax.FontSize); oi_chk('font_manual_mode', @() ax.FontSizeMode);
oi_chk('font_manual_legend', @() lg.FontSize); oi_chk('font_manual_colorbar', @() cb.FontSize);
ax.FontSizeMode = 'auto'; drawnow;
oi_chk('font_auto_again', @() oi_r(ax.FontSize / full));
close(f);
for sz = {[240 180], [320 240], [200 160], [400 300], [300 200], [560 420]}
    f = figure('Visible', 'off', 'Position', [50 50 sz{1}]); ax = axes(f); plot(ax, 1:10, linspace(0, 0.7, 10)); drawnow;
    nm = sprintf('fig_%d_%d', sz{1}(1), sz{1}(2));
    oi_chk([nm '_fs'], @() oi_r(ax.FontSize / full));
    oi_chk([nm '_xt'], @() oi_r(ax.XTick)); oi_chk([nm '_yt'], @() oi_r(ax.YTick));
    oi_chk([nm '_ylim'], @() oi_r(ax.YLim));
    close(f);
end
f = figure('Visible', 'off', 'Position', [50 50 560 420]); ax = subplot(3, 3, 5); plot(ax, 1:10, linspace(0, 0.7, 10)); drawnow;
oi_chk('sub33_fs', @() oi_r(ax.FontSize / full)); oi_chk('sub33_xt', @() oi_r(ax.XTick)); oi_chk('sub33_yt', @() oi_r(ax.YTick));
close(f);
uf = uifigure('Visible', 'off', 'Position', [50 50 240 180]); ua = uiaxes(uf, 'Position', [10 10 150 120]); plot(ua, 1:3); drawnow;
oi_chk('uiaxes_small_fs', @() ua.FontSize);
close(uf);

% --- 119: short rulers (the automatic font shrinks with them in both engines)
f = figure('Visible', 'off', 'Position', [50 50 900 700]); ax = axes(f, 'Units', 'pixels'); plot(ax, [0 1], [0 1]);
ranges = {[0 1], [0 2], [0 3], [0 4], [0 5], [0 6], [0 7], [0 8], [0 10], [0 12], [0 15], [0 20], [0 21], [0 25], [0 30], ...
    [0 50], [0 100], [0 0.7], [0 0.5], [0 0.3], [0 0.15], [0.123 0.987], [1 10], [-1 1], [-3 11], [-5 5], [10 11.3], [0 0.0004]};
for W = [60 100 140 180 217 255]
    ax.Position = [80 80 W W]; drawnow;
    for k = 1:numel(ranges)
        r = ranges{k}; ax.XLim = r; ax.YLim = r; drawnow;
        oi_chk(sprintf('sq%d_x_%s', W, oi_name(r)), @() oi_r(ax.XTick));
        oi_chk(sprintf('sq%d_y_%s', W, oi_name(r)), @() oi_r(ax.YTick));
    end
end
close(f);

% --- 119: the polar r ruler and the colorbar
f = figure('Visible', 'off'); pax = polaraxes(f); drawnow;
oi_chk('polar_default_rt', @() oi_r(pax.RTick)); oi_chk('polar_default_tt', @() oi_r(pax.ThetaTick));
for k = [1 2 5 7 10 12 14 16 17 18 20 21 22 24 25 26 27]
    r = ranges{k}; pax.RLim = r; drawnow;
    oi_chk(sprintf('polar_%s', oi_name(r)), @() oi_r(pax.RTick));
end
close(f);
f = figure('Visible', 'off', 'Position', [50 50 280 210]); pax = polaraxes(f); drawnow;
for k = [5 7 12 14 18 21]
    r = ranges{k}; pax.RLim = r; drawnow;
    oi_chk(sprintf('polar_small_%s', oi_name(r)), @() oi_r(pax.RTick));
end
close(f);
for sz = {[560 420], [280 210], [900 700]}
    f = figure('Visible', 'off', 'Position', [50 50 sz{1}]); ax = axes(f); imagesc(ax, magic(4)); cb = colorbar(ax); drawnow;
    nm = sprintf('cb_%d_%d', sz{1}(1), sz{1}(2));
    for lim = {[0 1], [0 7], [0 13], [0 21], [0 0.7], [1 16], [-3 11], [0 100]}
        ax.CLim = lim{1}; drawnow;
        oi_chk(sprintf('%s_%s', nm, oi_name(lim{1})), @() oi_r(cb.Ticks));
    end
    cb.Location = 'southoutside'; drawnow;
    for lim = {[0 7], [0 13], [0 21], [0 0.7]}
        ax.CLim = lim{1}; drawnow;
        oi_chk(sprintf('%s_south_%s', nm, oi_name(lim{1})), @() oi_r(cb.Ticks));
    end
    close(f);
end
close all force

function oi_chk(name, fn)
% u9b_chk, or u9b_chkdiv for a label this fixture records as a divergence of ADR 0232.
persistent divs
if isempty(divs)
    divs = oi_divs();
end
if any(strcmp(name, divs))
    u9b_chkdiv(name, fn, 'ADR0232');
else
    u9b_chk(name, fn);
end
end

function d = oi_divs()
% The labels R2025b answers differently: its ink is [0.1294 0.1294 0.1294] where this build keeps
% 0.251 (open item 105); a two-face patch keeps stale vertices after a column write; a uiaxes'
% font is 12 there and 11 here; a subplot's box is a few pixels wider there; and the short-ruler
% and polar tick counts under R2025b's density score (open item 119, still open).
d = {'fig_300_200_ylim', 'fig_300_200_yt', 'fill_cdata', 'font_auto_again', 'font_small_title', 'font_small_xlabel', 'patch2f_written_col', 'patch_cdata', 'patch_fvcd', 'patchfv_cdata', 'patchfv_fvcd', 'patchm_cdata', 'patchm_fvcd', 'patchrgb_cdata', 'patchrgb_fvcd', 'patchxy_cdata', 'patchxy_facecolor', 'pie_text_color', 'polar_0_25', 'polar_small_0_25', 'sq100_y_0_0p5', 'sq180_x_0_0p5', 'sq217_y_0_0p15', 'sub33_fs', 'text_color', 'uiaxes_small_fs'}; % OI_DIVS
end

function out = xwrite(p, v)
p.XData = v; out = sprintf('%s %s %s %s', mat2str(size(p.XData)), mat2str(p.XData), mat2str(p.Faces), mat2str(size(p.Vertices)));
end

function out = vwrite(p, v)
p.Vertices = v; out = sprintf('%s %s %s %s', mat2str(size(p.Vertices)), mat2str(p.XData), mat2str(p.ZData), mat2str(p.Faces));
end

function out = facewrite(p, v)
p.Faces = v; out = sprintf('%s %s %s', mat2str(p.Faces), mat2str(p.XData), mat2str(size(p.Vertices)));
end

function out = cwrite(p, v)
p.CData = v; out = sprintf('%s %s %s', mat2str(size(p.CData)), mat2str(squeeze(p.CData)'), mat2str(size(p.FaceVertexCData)));
end

function out = fwrite(p, v)
p.FaceVertexCData = v; out = sprintf('%s %s %s', mat2str(size(p.FaceVertexCData)), mat2str(size(p.CData)), mat2str(squeeze(p.CData)'));
end

function s = oi_name(r)
s = sprintf('%g_%g', r(1), r(2));
s = strrep(strrep(strrep(s, '-', 'm'), '.', 'p'), '+', '');
end

function v = oi_r(v)
v = round(v, 9);
end
