% record: -noFigureWindows
% Open item 119 (ADR 0238): the ticks a short linear ruler takes, as R2025b scores them. The rule is
% a score per band of the ruler's pixels (coverage, a penalty per tick under or over a target, an
% allowance when both limits sit on ticks), the bands' edges measured at 8, 10 and 13 points and the
% count caps beyond them at every five pixels, fitted to probe_119b, probe_119c and probe_119d
% (open-items scratch): 45 lengths each way at 10 points, ranges slid and stretched at 60 and 140
% pixels, the capped region to 500 pixels, the automatic font against the written one, the polar r
% ruler at 21 radii. The axes keep their automatic font: within the scored bands it leaves R2025b's
% ticks where the nominal 10 points put them. Lines marked div=ADR0238 are where R2025b reads the
% sign of the limits on the shortest rulers, prefers a step by its leading digit on the polar r
% ruler, reads a landing limit as off its tick in its own double arithmetic ([0 0.55] at 270
% pixels), or moves a band's edge under a shrunk 9-point font (165 pixels up) or a written 13 at
% the edge of a band. Numbers are rounded to nine decimals (R2025b's tick values carry noise).
close all force
ranges = {[0 1], [0 2], [0 3], [0 4], [0 5], [0 6], [0 7], [0 8], [0 9], [0 10], [0 12], [0 13], [0 15], [0 17], ...
    [0 20], [0 21], [0 25], [0 30], [0 35], [0 45], [0 50], [0 55], [0 60], [0 90], [0 100], [0 0.7], [0 0.55], ...
    [0 0.3], [0 0.15], [0 0.123], [0.123 0.987], [1 10], [2 9], [-1 1], [-3 11], [-5 5], [1.5 3.5], [10 11.3], ...
    [12345 12399], [0 0.0004], [0.5 9.5], [1 99], [-2 7], [-0.3 0.9], [3 17], [0.3 2.7], [-15 85], [2.5 7.5]};
% The axes keep their default font: R2025b's 10 points is the length JGraph's default 11 reads
% the bands at, and writing 10 here would move every JGraph ruler up a band.
f = figure('Visible', 'off', 'Position', [50 50 900 700]); ax = axes(f, 'Units', 'pixels'); plot(ax, [0 1], [0 1]);

% --- square axes: the x and y rulers through the bands
for W = [60 120 140 180 200 250]
    ax.Position = [80 80 W W]; drawnow;
    for k = 1:numel(ranges)
        r = ranges{k}; ax.XLim = r; ax.YLim = r; drawnow;
        sr_chk(sprintf('sq%d_x_%s', W, sr_name(r)), @() sr_r(ax.XTick));
        sr_chk(sprintf('sq%d_y_%s', W, sr_name(r)), @() sr_r(ax.YTick));
    end
end

% --- a tall axes: the y ruler alone through its upper bands
for H = [75 100 120 140 165 190 215 240 270]
    ax.Position = [80 80 400 H]; drawnow;
    for k = [1 2 3 5 6 8 9 10 12 13 15 16 17 22 26 27 28 30 31 32 36 39]
        r = ranges{k}; ax.YLim = r; drawnow;
        sr_chk(sprintf('tall%d_y_%s', H, sr_name(r)), @() sr_r(ax.YTick));
    end
end

% --- a range sliding under a 60 and a 140 pixel x ruler
ax.Position = [80 80 140 140]; drawnow;
for lo = [-4 -3.875 -2 -1.875 -1 0 0.125 1 2.125 3]
    ax.XLim = [lo lo + 5]; drawnow;
    sr_chk(sprintf('slide140_%s', sr_name([lo lo + 5])), @() sr_r(ax.XTick));
end
ax.Position = [80 80 60 60]; drawnow;
for lo = [-4 -3.875 -2 -1.875 -1 0 0.125 1 2.125 3]
    ax.XLim = [lo lo + 5]; drawnow;
    sr_chk(sprintf('slide60_%s', sr_name([lo lo + 5])), @() sr_r(ax.XTick));
end

% --- a written font moves the bands
ax.Position = [80 80 180 180];
for fs = [8 13]
    ax.FontSize = fs; drawnow;
    for k = [1 3 5 6 9 10 13 16 17 22 26 31]
        r = ranges{k}; ax.XLim = r; ax.YLim = r; drawnow;
        sr_chk(sprintf('fs%d_x_%s', fs, sr_name(r)), @() sr_r(ax.XTick));
        sr_chk(sprintf('fs%d_y_%s', fs, sr_name(r)), @() sr_r(ax.YTick));
    end
end
close(f);

% --- automatic limits on a short ruler follow the same score
f = figure('Visible', 'off', 'Position', [50 50 900 700]);
for W = [60 140 200]
    ax = axes(f, 'Units', 'pixels', 'Position', [80 80 W W]);
    for p = {[0 7], [0 13], [-3 11], [0.123 0.987], [2 97], [-1 1000], [0.5 9.5], [1 10]}
        plot(ax, 1:10, linspace(p{1}(1), p{1}(2), 10)); drawnow;
        sr_chk(sprintf('auto%d_%s', W, sr_name(p{1})), @() sr_r([ax.YLim ax.YTick]));
    end
    delete(ax);
end
close(f);

% --- the polar r ruler, the same at every radius
for S = [150 300 500]
    f = figure('Visible', 'off', 'Position', [50 50 900 700]); pax = polaraxes(f, 'Units', 'pixels'); pax.Position = [20 20 S S]; drawnow;
    for k = [1 2 3 5 6 9 10 12 13 16 17 20 22 24 26 30 31 32 36 39]
        r = ranges{k}; pax.RLim = r; drawnow;
        sr_chk(sprintf('polar%d_%s', S, sr_name(r)), @() sr_r(pax.RTick));
    end
    close(f);
end
close all force

function s = sr_name(r)
s = strrep(strrep(sprintf('%g_%g', r(1), r(2)), '-', 'm'), '.', 'p');
end

function v = sr_r(v)
v = round(v, 9);
end

function sr_chk(name, fn)
% u9b_chk, or u9b_chkdiv for a label this fixture records as a divergence of ADR 0238.
persistent divs
if isempty(divs)
    divs = sr_divs();
end
if any(strcmp(name, divs))
    u9b_chkdiv(name, fn, 'ADR0238');
else
    u9b_chk(name, fn);
end
end

function d = sr_divs()
d = {'fs13_x_0_0p7', 'polar150_0_21', 'polar150_0_25', 'polar150_0_55', 'polar150_12345_12399', 'polar300_0_21', 'polar300_0_25', 'polar300_0_55', 'polar300_12345_12399', 'polar500_0_21', 'polar500_0_25', 'polar500_0_55', 'polar500_12345_12399', 'slide60_m1p875_3p125', 'slide60_m3p875_1p125', 'sq60_x_0p3_2p7', 'sq60_x_12345_12399', 'sq60_x_m0p3_0p9', 'sq60_x_m15_85', 'sq60_x_m2_7', 'sq60_y_0p3_2p7', 'sq60_y_12345_12399', 'sq60_y_m0p3_0p9', 'sq60_y_m15_85', 'sq60_y_m2_7', 'tall165_y_0_0p7', 'tall165_y_0p123_0p987', 'tall270_y_0_0p55'};
end
