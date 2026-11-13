% record: -noFigureWindows
% Open item 145 (ADR 0343): every colormap generator R2025b ships computes its rows by its own source
% file's formula over m rows (jet.m, hot.m, colorcube.m, ...), or interpolates its 256-row table
% (parula, turbo, nebula), or is the lightness ramp of R2025b's built-in sky and abyss. Each is
% printed to 17 digits: whole at the small sizes, six rows and a sum at the large ones. The size
% argument is checked as the arguments block checks it, and with no size a generator is as long as
% the current figure's map. Probe cmap_probe (agent D's scratch).
close all force

names = {'parula', 'turbo', 'hsv', 'hot', 'cool', 'spring', 'summer', 'autumn', 'winter', ...
    'gray', 'bone', 'copper', 'pink', 'jet', 'colorcube', 'prism', 'flag', 'white', 'sky', ...
    'abyss', 'nebula'};
for i = 1:numel(names)
    for n = [0 1 2 3 8 9 10]
        whole(names{i}, n, 'exact');
    end
    for n = [27 64 100 256 257]
        sampled(names{i}, n, 'exact');
    end
end

% lines repeats the default colour order, which is R2025b's newer palette there and the classic
% one here (ADR 0226, open item 105).
whole('lines', 0, 'exact');
whole('lines', 3, 'div=ADR0226');
sampled('lines', 256, 'div=ADR0226');

% The size argument.
u9b_chk('jet_negative', @() jet(-1));
u9b_chk('jet_fraction', @() jet(2.5));
u9b_chk('jet_vector', @() jet([1 2]));
u9b_chk('jet_nan', @() jet(NaN));
u9b_chk('jet_inf', @() jet(Inf));
u9b_chk('jet_logical', @() size(jet(true)));
u9b_chk('jet_int8', @() size(jet(int8(4))));
u9b_chk('hot_zero_size', @() size(hot(0)));

% No size: as long as the current figure's map, 256 with no figure.
u9b_chk('bare_no_figure', @() size(jet));
f = figure('Visible', 'off');
u9b_chk('bare_fresh_figure', @() size(hot));
colormap(f, hot(8));
u9b_chk('bare_after_figure_map', @() size(jet));
u9b_chk('bare_colorcube_after_figure_map', @() size(colorcube));
u9b_chk('bare_sky_after_figure_map', @() size(sky));
colormap(jet(5));
u9b_chk('bare_after_map', @() size(parula));
u9b_chk('read_after_map', @() isequal(colormap, jet(5)));
close(f);
f = figure('Visible', 'off');
colormap('hot');
u9b_chk('read_named', @() isequal(colormap, hot(256)));
colormap('nebula');
u9b_chk('read_named_nebula', @() isequal(colormap, nebula(256)));
close(f);

function whole(name, n, rule)
m = feval(name, n);
fprintf('CHK|%s_%d|%s %s|%s\n', name, n, mat2str(size(m)), strtrim(sprintf('%.17g ', m)), rule);
end

function sampled(name, n, rule)
m = feval(name, n);
rows = unique([1 2 floor(n / 3) floor(n / 2) + 1 n - 1 n]);
fprintf('CHK|%s_%d|%s %s|%s\n', name, n, mat2str(size(m)), strtrim(sprintf('%.17g ', m(rows, :))), rule);
sumrule = 'abs=1e-6';
if startsWith(rule, 'div')
    sumrule = rule;
end
fprintf('CHK|%s_%d_sum|%.10g|%s\n', name, n, sum(m(:)), sumrule);
end
