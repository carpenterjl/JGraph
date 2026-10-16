% record: -noFigureWindows
% Open item 133 (ADR 0235), in the MATLAB dialect: the plot box and tight inset a script reads before
% the first frame under turned tick labels - category counts, label lengths, manual angles, letters,
% a title, axis labels, a larger font, a taller and a wider figure, a plain numeric plot - in pixels
% of the figure to a ten-thousandth. Lines marked div=ADR0235 are where this build's text widths
% miss R2025b's by a pixel (ADR 0156).
u9b_chk('bx_4_3', @() boxdigits(4, 3, []));
u9b_chk('bx_4_6', @() boxdigits(4, 6, []));
u9b_chk('bx_4_9', @() boxdigits(4, 9, []));
u9b_chk('bx_4_12', @() boxdigits(4, 12, []));
u9b_chk('bx_8_3', @() boxdigits(8, 3, []));
u9b_chk('bx_8_6', @() boxdigits(8, 6, []));
u9b_chk('bx_8_9', @() boxdigits(8, 9, []));
u9b_chkdiv('bx_8_12', @() oi_try( @() boxdigits(8, 12, [])), 'ADR0235');
u9b_chk('bx_12_3', @() boxdigits(12, 3, []));
u9b_chk('bx_12_6', @() boxdigits(12, 6, []));
u9b_chk('bx_12_9', @() boxdigits(12, 9, []));
u9b_chkdiv('bx_12_12', @() oi_try( @() boxdigits(12, 12, [])), 'ADR0235');
u9b_chk('bx_20_3', @() boxdigits(20, 3, []));
u9b_chk('bx_20_6', @() boxdigits(20, 6, []));
u9b_chk('bx_20_9', @() boxdigits(20, 9, []));
u9b_chkdiv('bx_20_12', @() oi_try( @() boxdigits(20, 12, [])), 'ADR0235');
u9b_chk('bx_30_3', @() boxdigits(30, 3, []));
u9b_chk('bx_30_6', @() boxdigits(30, 6, []));
u9b_chk('bx_30_9', @() boxdigits(30, 9, []));
u9b_chkdiv('bx_30_12', @() oi_try( @() boxdigits(30, 12, [])), 'ADR0235');
u9b_chk('ba_0', @() boxdigits(12, 9, 0));
u9b_chk('ba_15', @() boxdigits(12, 9, 15));
u9b_chk('ba_30', @() boxdigits(12, 9, 30));
u9b_chk('ba_45', @() boxdigits(12, 9, 45));
u9b_chk('ba_60', @() boxdigits(12, 9, 60));
u9b_chk('ba_75', @() boxdigits(12, 9, 75));
u9b_chk('ba_90', @() boxdigits(12, 9, 90));
u9b_chkdiv('bc_letters', @() oi_try( @() boxcase('letters')), 'ADR0235');
u9b_chkdiv('bc_title', @() oi_try( @() boxcase('title')), 'ADR0235');
u9b_chkdiv('bc_xlabel', @() oi_try( @() boxcase('xlabel')), 'ADR0235');
u9b_chk('bc_ylabel', @() boxcase('ylabel'));
u9b_chk('bc_both', @() boxcase('both'));
u9b_chk('bc_numeric', @() boxcase('numeric'));
u9b_chkdiv('bc_font14', @() oi_try( @() boxcase('font14')), 'ADR0235');
u9b_chk('bc_tall', @() boxsize([100 100 560 600]));
u9b_chkdiv('bc_wide', @() oi_try( @() boxsize([100 100 900 420])), 'ADR0235');
u9b_chk('bc_plain', @() boxplain());
u9b_chk('bc_after_xlabel', @() boxlater());



function out = px(ax)
% The angle, the plot box and the tight inset in pixels of the 560-by-420 figure, to a ten-thousandth.
f = ax.Parent;
s = f.Position([3 4 3 4]);
out = {ax.XTickLabelRotation, round(ax.InnerPosition .* s * 1e4) / 1e4, round(ax.TightInset .* s * 1e4) / 1e4};
end

function out = boxdigits(n, L, ang)
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
ax = axes(f);
names = arrayfun(@(k) sprintf(['%0' num2str(L) 'd'], k), 1:n, 'UniformOutput', false);
bar(ax, categorical(names, names), 1:n);
if ~isempty(ang), ax.XTickLabelRotation = ang; end
out = px(ax);
close(f);
end

function out = boxcase(kind)
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
ax = axes(f);
names = arrayfun(@(j) sprintf('label%02dWW', j), 1:20, 'UniformOutput', false);
switch kind
    case 'numeric'
        plot(ax, 1:20, 1:20); ax.XTickLabelRotation = 45;
    case 'font14'
        bar(ax, categorical(names, names), 1:20); ax.FontSize = 14;
    otherwise
        bar(ax, categorical(names, names), 1:20);
end
if any(strcmp(kind, {'title', 'both'})), title(ax, 'Title'); end
if any(strcmp(kind, {'xlabel', 'both'})), xlabel(ax, 'X label'); end
if any(strcmp(kind, {'ylabel', 'both'})), ylabel(ax, 'Y label'); end
out = px(ax);
close(f);
end

function out = boxsize(pos)
f = figure('Visible', 'off', 'Position', pos);
ax = axes(f);
names = arrayfun(@(k) sprintf('%09d', k), 1:20, 'UniformOutput', false);
bar(ax, categorical(names, names), 1:20);
out = px(ax);
close(f);
end

function out = boxplain()
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
ax = axes(f);
plot(ax, 1:10, (1:10) .^ 2);
out = px(ax);
close(f);
end

function out = boxlater()
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
ax = axes(f);
names = arrayfun(@(k) sprintf('%09d', k), 1:20, 'UniformOutput', false);
bar(ax, categorical(names, names), 1:20);
before = px(ax);
xlabel(ax, 'later');
after = px(ax);
out = {before{2}(2), after{2}(2), after{3}(2)};
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
