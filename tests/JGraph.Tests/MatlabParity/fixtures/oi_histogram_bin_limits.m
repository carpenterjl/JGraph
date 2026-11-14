% record: -noFigureWindows
% Open item 271 (ADR 0361): histcounts' and histogram's edges under named limits, transcribed from
% R2025b's histcounts.m (its BinLimits branch), binpickerbl.m and integerrule.m; histcounts2's
% limits; the integer and Freedman-Diaconis rules without limits; and a histogram whose BinLimits
% or BinLimitsMode is set afterwards, which keeps its bin width (or its count, when its bins are not
% evenly spread) and reads 'manual'. Edges print whole when short, else five of them; every value
% to 17 digits. Probes hp1 and hp2 (agent V's scratch).
close all force

x = sqrt(2)*erfinv(2*((1:30000)-0.5)/30000-1);
y = mod((1:5000)*0.618034, 1)*7 - 2;
z = round(10*sin(1:400));

% histcounts under limits, every rule.
meths = {'auto', 'scott', 'fd', 'sturges', 'sqrt', 'integers'};
for k = 1:numel(meths)
    pe(['hc_x_' meths{k}], hce(x, 'BinLimits', [-1 2], 'BinMethod', meths{k}));
    pe(['hc_y_' meths{k}], hce(y, 'BinLimits', [-1.3 2.7], 'BinMethod', meths{k}));
    pe(['hc_z_' meths{k}], hce(z, 'BinLimits', [-3.5 4], 'BinMethod', meths{k}));
    pe(['hc_zi_' meths{k}], hce(z, 'BinLimits', [-3 4], 'BinMethod', meths{k}));
end
pe('hc_auto_default', hce(x, 'BinLimits', [-1 2]));
pe('hc_nb7', hce(x, 'BinLimits', [-1 2], 'NumBins', 7));
pe('hc_w07', hce(x, 'BinLimits', [-1 2], 'BinWidth', 0.7));
pe('hc_w05', hce(x, 'BinLimits', [-1 2], 'BinWidth', 0.5));
pe('hc_w5', hce(x, 'BinLimits', [-1 2], 'BinWidth', 5));
pe('hc_w_tiny', hce([1 2 3], 'BinLimits', [0 1], 'BinWidth', 1e-9));
pe('hc_empty_auto', hce(x, 'BinLimits', [10 12]));
pe('hc_one_auto', hce([1 1 1 5], 'BinLimits', [0.5 3]));
pe('hc_one_fd', hce([1 1 1 5], 'BinLimits', [0.5 3], 'BinMethod', 'fd'));
pe('hc_scott_constant', hce([1 2 3 1.5], 'BinLimits', [1.4 1.6]));
pe('hc_eq_nb', hce([1 2 3], 'BinLimits', [2 2], 'NumBins', 3));
pe('hc_eq_auto', hce([1 2 3], 'BinLimits', [2 2]));
pe('hc_fd_outliers', hce([zeros(1, 100) 1:10], 'BinLimits', [-1 12], 'BinMethod', 'fd'));
pe('hc_int_big', hce([0 1000 3 7], 'BinLimits', [-2 1001.5], 'BinMethod', 'integers'));
pe('hc_int_huge', hce([0 100000 3 7], 'BinLimits', [-2 100001], 'BinMethod', 'integers'));

% Without limits: the integer rule's unit bins and its power-of-ten ones, and fd's IQR guard.
pe('nl_int_big', hce([0 1000 3 7], 'BinMethod', 'integers'));
pe('nl_int_huge', hce([0 100000 3 7], 'BinMethod', 'integers'));
pe('nl_int_frac', hce([0.5 10.2 3 7], 'BinMethod', 'integers'));
pe('nl_auto_z', hce(z));
pe('nl_fd_outliers', hce([zeros(1, 100) 1:10], 'BinMethod', 'fd'));
pe('nl_fd_x', hce(x, 'BinMethod', 'fd'));
pe('nl_fd_y', hce(y, 'BinMethod', 'fd'));
pe('nl_n7', hce(x, 7));

% histcounts2 under limits: a rule reads the pairs inside both limits.
[~, xe, ye] = histcounts2(x(1:6:30000), y, 'XBinLimits', [-1 2]);
pe('hc2_x', xe); pe('hc2_y', ye);
[~, xe, ye] = histcounts2(x(1:5000), y, 'XBinLimits', [-1 2], 'YBinLimits', [0 3]);
pe('hc2_both_x', xe); pe('hc2_both_y', ye);
[~, xe, ye] = histcounts2(x(1:5000), y, 'XBinLimits', [-1 2], 'YBinLimits', [0 3], 'BinMethod', 'fd');
pe('hc2_fd_x', xe); pe('hc2_fd_y', ye);

% A histogram's BinLimits set afterwards, and BinLimitsMode.
f = figure('Visible', 'off');
h = histogram(x); ph('h_auto_made', h);
h = histogram(x, 7); ph('h_n7_made', h);
h = histogram(x, [-4 -1 0 2 5]); ph('h_edges_made', h);
h = histogram(x, 'BinWidth', 0.7); ph('h_w07_made', h);
h = histogram(x, 'BinLimits', [-1 2]); ph('h_mk_auto', h);
h = histogram(x, 'BinLimits', [-1 2], 'BinMethod', 'sqrt'); ph('h_mk_sqrt', h);
h = histogram(x, 'BinWidth', 0.7, 'BinLimits', [-1 2]); ph('h_mk_w07', h);
h = histogram(x, 200); h.BinLimits = [-1 2]; ph('h_n200', h);
h = histogram(x, 200); h.BinLimits = [-1 2.01]; ph('h_n200_b', h);
h = histogram(x, 'BinWidth', 0.5); h.BinLimits = [-1 2]; ph('h_w05', h);
h = histogram(x, 'BinWidth', 0.7); h.BinLimits = [-1 2]; ph('h_w07', h);
h.BinLimits = [-1.5 2.5]; ph('h_w07_again', h);
h.BinLimitsMode = 'auto'; ph('h_w07_released', h);
h = histogram(x, [-4 -1 0 2 5]); h.BinLimits = [-1 2]; ph('h_edges', h);
h.BinLimitsMode = 'auto'; ph('h_edges_released', h);
h = histogram(x, [-4 -1 0 2 5]); h.BinLimitsMode = 'auto'; ph('h_edges_auto', h);
h = histogram(x, [-4 -3 -2 -1 0 1 2 3 4 5]); h.BinLimits = [-1 2]; ph('h_even_edges', h);
h = histogram(x); h.BinLimits = [-1 2]; ph('h_auto', h);
h = histogram(x, 'BinMethod', 'sqrt'); h.BinLimits = [-1 2]; ph('h_sqrt', h);
h.BinLimitsMode = 'auto'; ph('h_sqrt_released', h);
h = histogram(x, 'BinMethod', 'sturges'); h.BinLimits = [-1 2]; ph('h_sturges', h);
h = histogram(x, 'BinMethod', 'sqrt', 'BinLimits', [-1 2]); h.BinLimitsMode = 'auto'; ph('h_mk_sqrt_released', h);
h = histogram(x, 7); h.BinLimits = [-1 2]; ph('h_n7', h);
h = histogram(x, 10, 'BinLimits', [-1 2]); h.BinLimits = [0 1]; ph('h_n10_lim', h);
h = histogram(x, 200); h.BinLimits = [-1 2]; h.NumBins = 10; ph('h_n200_nb10', h);
h = histogram(x, 200); h.BinLimits = [-1 2]; h.BinLimitsMode = 'auto'; ph('h_n200_released', h);
h = histogram(x, 'NumBins', 50); h.BinLimits = [-1 2]; ph('h_nb50', h);
h = histogram(x); h.BinLimits = [-1 2]; h.BinMethod = 'sturges'; ph('h_auto_lim_sturges', h);
h = histogram(x); h.BinLimits = [-1 2]; h.BinWidth = 0.4; ph('h_auto_lim_w04', h);
h = histogram(x, 'BinLimits', [-1 2]); h.BinLimits = [0 1]; ph('h_mk_auto_lim', h);
h = histogram(x); h.BinEdges = [-1 0 0.5 2]; h.BinLimits = [-2 2]; ph('h_set_edges_lim', h);
h = histogram(x, 7); h.BinLimits = [-1 1e-6 - 1]; ph('h_tiny', h);
h = histogram(x, 'BinWidth', 0.25); h.BinLimits = [-1.1 2.05]; ph('h_w025', h);
h = histogram(z); h.BinLimits = [-3.5 4]; ph('h_z_auto', h);
h = histogram(z, 5); h.BinLimits = [-3.5 4]; ph('h_z_n5', h);
close(f);

function e = hce(varargin)
try
    [~, e] = histcounts(varargin{:});
catch err
    e = err.identifier;
end
end

function s = es(e)
if ischar(e)
    s = ['ERR ' e];
elseif numel(e) <= 12
    s = sprintf('n=%d [%s]', numel(e), strtrim(sprintf('%.17g ', e)));
else
    n = numel(e);
    k = [1 2 floor(n / 2) n - 1 n];
    s = sprintf('n=%d %s', n, strtrim(sprintf('%.17g ', e(k))));
end
end

function pe(key, e)
fprintf('CHK|%s|%s|exact\n', key, es(e));
end

function ph(key, h)
w = h.BinWidth;
if ischar(w)
    ws = w;
else
    ws = sprintf('%.17g', w);
end
fprintf('CHK|%s|%s|exact\n', key, es(h.BinEdges));
fprintf('CHK|%s_state|w=%s m=%s mode=%s lim=[%.17g %.17g] n=%d sum=%d|exact\n', key, ws, h.BinMethod, ...
    h.BinLimitsMode, h.BinLimits(1), h.BinLimits(2), h.NumBins, sum(h.Values));
end
