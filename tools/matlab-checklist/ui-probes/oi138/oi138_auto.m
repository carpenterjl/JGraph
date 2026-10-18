% oi138_auto - open item 138, part B: the automatic limits of an axes holding an image, alone and
% with other objects, after axis auto. An image alone reads tight to its pixel edges (oi138_image
% A15, A18, A21, A24); this records what a line, a scatter, a text or a second image beside it does.
set(groot, 'DefaultFigureVisible', 'off');
fprintf('%s\n', version);

one('B1 image + line inside', @() with_line([1 3], [1 2]));
one('B2 image + line to 4.7', @() with_line([1 4.7], [1 2]));
one('B3 image + line from 0.9 to 4.2', @() with_line([0.9 4.2], [1 2]));
one('B4 image + line 0:8', @() with_line(0:8, 0:8));
one('B5 image + line y to 6', @() with_line([1 2], [0 6]));
one('B6 image + line to 5.2', @() with_line([1 5.2], [1 2]));
one('B7 image + line to 7', @() with_line([1 7], [1 2]));
one('B8 image + line to 12', @() with_line([1 12], [1 2]));
one('B9 image + line from -2', @() with_line([-2 2], [1 2]));
one('B10 image + scatter at 4.6', @() with(@() scatter(4.6, 2)));
one('B11 image + text at 6', @() with(@() text(6, 2, 'x')));
one('B12 two images', @() with(@() image([6 9], [1 4], magic(4))));
one('B13 image + patch to 5', @() with(@() patch([1 5 5 1], [1 1 2 2], 'r')));
one('B14 image [2 5] alone, xlim auto', @() then(@() image([2 5], [1 4], magic(4)), @() xlim('auto')));
one('B15 line alone 0.5:4.5', @() plot(0.5:4.5, 0.5:4.5));
one('B16 line alone 1:4.7', @() plot([1 4.7], [1 2]));
one('B17 image + line 1 to 4.7, tight', @() then(@() with_line([1 4.7], [1 2]), @() axis('tight')));
one('B18 image + line, padded', @() then(@() with_line([1 4.7], [1 2]), @() set(gca, 'XLimitMethod', 'padded')));
one('B19 image + line, tickaligned explicit', @() then(@() with_line([1 4.7], [1 2]), @() set(gca, 'XLimitMethod', 'tickaligned')));
one('B20 image + line 1 to 4.55', @() with_line([1 4.55], [1 2]));
one('B21 image + line 1 to 4.6', @() with_line([1 4.6], [1 2]));
one('B22 image + line 1 to 4.8', @() with_line([1 4.8], [1 2]));
one('B23 image + line 1 to 4.9', @() with_line([1 4.9], [1 2]));
one('B24 image + line 1 to 5', @() with_line([1 5], [1 2]));
one('B25 image + line 1 to 5.5', @() with_line([1 5.5], [1 2]));
one('B26 image + line 1 to 6', @() with_line([1 6], [1 2]));
one('B27 image + line 1 to 6.5', @() with_line([1 6.5], [1 2]));
one('B28 image + line 1 to 9', @() with_line([1 9], [1 2]));
one('B29 image + line 1 to 10', @() with_line([1 10], [1 2]));
one('B30 image + line 1 to 4.51', @() with_line([1 4.51], [1 2]));
fprintf('done\n');

function one(label, make)
fprintf('--- %s\n', label);
f = figure('Visible', 'off');
try
    make();
    ax = gca;
    fprintf('  XLim=%s(%s) YLim=%s(%s) XTick=%s XLimitMethod=%s\n', mat2str(ax.XLim, 6), ax.XLimMode, mat2str(ax.YLim, 6), ax.YLimMode, mat2str(ax.XTick, 6), ax.XLimitMethod);
catch e
    fprintf('  error: %s | %s\n', e.identifier, e.message);
end
delete(f);
end

function with_line(x, y)
with(@() plot(x, y));
end

function with(make)
imagesc(magic(4));
hold on;
make();
axis auto;
end

function then(a, b)
a();
b();
end
