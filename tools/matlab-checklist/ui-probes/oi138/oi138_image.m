% oi138_image - open item 138: what R2025b's image family does to the axes it draws in. Headless.
% After image(C) or imagesc(C) the axes' XLim and YLim are the pixel edges with their modes manual
% (measured once in batch 27); this records the rule over the forms: C alone, x and y given (two
% ends, a scalar, a decreasing pair, a non-uniform vector, one column), a 1-by-1 and a true-colour
% C, the low-level form, the trailing clims, a named axes, hold on over a plot, the automatic
% limits asked for again afterwards, a plot added after, axis tight/auto/image, and imshow.
set(groot, 'DefaultFigureVisible', 'off');
fprintf('%s\n', version);

one('A1 imagesc(magic(4))', @() imagesc(magic(4)));
one('A2 image(magic(4))', @() image(magic(4)));
one('A3 image([2 5], [10 40], magic(4))', @() image([2 5], [10 40], magic(4)));
one('A4 image(3, 7, magic(4)) scalar x y', @() image(3, 7, magic(4)));
one('A5 image(7) 1-by-1', @() image(7));
one('A6 imagesc(ones(3, 5))', @() imagesc(ones(3, 5)));
one('A7 image([5 2], [1 4], magic(4)) decreasing x', @() image([5 2], [1 4], magic(4)));
one('A8 image([1 2 10], [1 4], magic(4)) non-uniform x', @() image([1 2 10], [1 4], magic(4)));
one('A9 image([2 5], [1 4], (1:4)'') one column', @() image([2 5], [1 4], (1:4)'));
one('A10 image(rand(4, 4, 3)) true colour', @() image(rand(4, 4, 3)));
one('A11 image(''CData'', magic(4)) low level', @() image('CData', magic(4)));
one('A12 imagesc(magic(4), [0 20])', @() imagesc(magic(4), [0 20]));
one('A13 imagesc(ax, magic(4)) named axes', @() imagesc(gca, magic(4)));
one('A14 plot then hold on then imagesc', @() hold_then(@() imagesc(magic(4))));
one('A15 imagesc then xlim auto', @() then(@() imagesc(magic(4)), @() xlim('auto')));
one('A16 imagesc then hold on plot(0:8) then axis auto', @() then(@() imagesc(magic(4)), @() addplot_auto()));
one('A17 imagesc then hold on plot(0:8)', @() then(@() imagesc(magic(4)), @() addplot()));
one('A18 imagesc then axis tight', @() then(@() imagesc(magic(4)), @() axis('tight')));
one('A19 imagesc then axis image', @() then(@() imagesc(magic(4)), @() axis('image')));
one('A20 image([]) empty', @() image([]));
one('A21 imagesc then set XLimMode auto', @() then(@() imagesc(magic(4)), @() set(gca, 'XLimMode', 'auto', 'YLimMode', 'auto')));
one('A22 imagesc(1:5, 1:3, ones(3, 5)) vectors', @() imagesc(1:5, 1:3, ones(3, 5)));
one('A23 image(magic(4)) then CData 2-by-7', @() then(@() image(magic(4)), @() set(findobj(gca, 'Type', 'image'), 'CData', ones(2, 7))));
one('A24 imagesc into held empty axes', @() then(@() hold(gca, 'on'), @() imagesc(magic(4))));
one('A25 imagesc(''XData'', [2 5], ''CData'', magic(4)) low level with XData', @() image('XData', [2 5], 'CData', magic(4)));
one('A26 imshow(magic(4), [])', @() imshow(magic(4), []));
one('A27 imagesc(magic(4)) in a uiaxes', @() in_uiaxes());
one('A28 imagesc then ylabel then xlim manual read', @() then(@() imagesc(magic(4)), @() ylabel('y')));
fprintf('done\n');

function one(label, make)
fprintf('--- %s\n', label);
f = figure('Visible', 'off');
try
    make();
    ax = findobj(f, '-isa', 'matlab.graphics.axis.Axes', '-or', '-isa', 'matlab.ui.control.UIAxes');
    if isempty(ax), ax = gca; end
    ax = ax(1);
    im = findobj(ax, 'Type', 'image');
    fprintf('  XLim=%s(%s) YLim=%s(%s) YDir=%s Layer=%s DAR=%s(%s) PBAR=%s View=%s CLim=%s(%s) Visible=%s\n', ...
        mat2str(ax.XLim, 6), ax.XLimMode, mat2str(ax.YLim, 6), ax.YLimMode, ax.YDir, ax.Layer, ...
        mat2str(ax.DataAspectRatio, 4), ax.DataAspectRatioMode, ax.PlotBoxAspectRatioMode, mat2str(ax.View), mat2str(ax.CLim, 6), ax.CLimMode, ax.Visible);
    if ~isempty(im)
        fprintf('  image: XData=%s(%s) YData=%s(%s) size=%s CDataMapping=%s\n', mat2str(im(1).XData, 6), im(1).XDataMode, mat2str(im(1).YData, 6), im(1).YDataMode, mat2str(size(im(1).CData)), im(1).CDataMapping);
    end
catch e
    fprintf('  error: %s | %s\n', e.identifier, e.message);
end
delete(f);
end

function hold_then(make)
plot(1:10, (1:10) * 2);
hold on;
make();
end

function then(a, b)
a();
b();
end

function addplot()
hold on;
plot(0:8, 0:8);
end

function addplot_auto()
hold on;
plot(0:8, 0:8);
axis auto;
end

function in_uiaxes()
f = gcf;
ax = uiaxes(f);
imagesc(ax, magic(4));
end
