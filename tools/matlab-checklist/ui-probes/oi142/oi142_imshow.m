% oi142_imshow - open item 142: what R2025b's imshow does to the axes and figure it draws in.
% Headless. imshow is base MATLAB in R2025b (toolbox/matlab/images): it makes the image with the
% high-level image call (so the limits are the pixel edges, ADR 0241), then sets YDir, TickDir, the
% grids, DataAspectRatio, PlotBoxAspectRatioMode and Visible, the colormap and CLim, and may resize
% the figure for its magnification. This records each over the forms: the classes, the display
% range, a map, true colour, the options, a named axes, subplot, hold, what the figure's NextPlot
% becomes, the output, and the refusals.
set(groot, 'DefaultFigureVisible', 'off');
fprintf('%s\n', version);
s = settings;
fprintf('settings: InitialMagnificationStyle=%s InitialMagnification=%s BorderStyle=%s ShowAxes=%s\n', ...
    v2s(s.matlab.imshow.InitialMagnificationStyle.ActiveValue), v2s(s.matlab.imshow.InitialMagnification.ActiveValue), ...
    v2s(s.matlab.imshow.BorderStyle.ActiveValue), v2s(s.matlab.imshow.ShowAxes.ActiveValue));
fprintf('DefaultImageInterpolation=%s\n', get(groot, 'DefaultImageInterpolation'));

I = magic(4) / 16;
one('B1 imshow(magic(4), [])', @() imshow(magic(4), []));
one('B2 imshow(rand(4, 5)) double', @() imshow(reshape(1:20, 4, 5) / 20));
one('B3 imshow(uint8)', @() imshow(uint8(magic(4) * 10)));
one('B4 imshow(uint16)', @() imshow(uint16(magic(4) * 1000)));
one('B5 imshow(int16)', @() imshow(int16(magic(4) * 100 - 800)));
one('B6 imshow(logical)', @() imshow(logical(eye(4))));
one('B7 imshow(single)', @() imshow(single(I)));
one('B8 imshow(rgb double)', @() imshow(reshape(1:48, 4, 4, 3) / 48));
one('B9 imshow(rgb uint8)', @() imshow(uint8(reshape(1:48, 4, 4, 3) * 5)));
one('B10 imshow(X, map) double indexed', @() imshow(magic(4) / 2, jet(8)));
one('B11 imshow(uint8 X, map)', @() imshow(uint8(mod(magic(4), 8)), jet(8)));
one('B12 imshow(I, [0.2 0.8])', @() imshow(I, [0.2 0.8]));
one('B13 imshow(I, ''DisplayRange'', [0.2 0.8])', @() imshow(I, 'DisplayRange', [0.2 0.8]));
one('B14 imshow(I, ''XData'', [10 20], ''YData'', [5 8])', @() imshow(I, 'XData', [10 20], 'YData', [5 8]));
one('B15 imshow(I, ''Colormap'', jet(16))', @() imshow(I, 'Colormap', jet(16)));
one('B16 imshow(I, ''Interpolation'', ''bilinear'')', @() imshow(I, 'Interpolation', 'bilinear'));
one('B17 imshow(I, ''Border'', ''tight'')', @() imshow(I, 'Border', 'tight'));
one('B18 imshow(I, ''InitialMagnification'', ''fit'')', @() imshow(I, 'InitialMagnification', 'fit'));
one('B19 imshow(I, ''InitialMagnification'', 200)', @() imshow(I, 'InitialMagnification', 200));
one('B20 imshow(I, ''Border'', ''tight'', ''InitialMagnification'', ''fit'')', @() imshow(I, 'Border', 'tight', 'InitialMagnification', 'fit'));
one('B21 two axes, imshow(I, ''Parent'', first)', @() parent_named(I));
one('B22 subplot(1, 2, 1); imshow(I)', @() in_subplot(I));
one('B23 plot(1:10); hold on; imshow(I)', @() held(I));
one('B24 plot(1:10) with grid; imshow(I) (not held)', @() replaced(I));
one('B25 h = imshow(I)', @() with_output(I));
one('B26 imshow(I); plot(1:3)', @() then_plot(I));
one('B27 imshow(ones(3) * 5, []) constant', @() imshow(ones(3) * 5, []));
one('B28 imshow(rgb, [])', @() imshow(reshape(1:48, 4, 4, 3) / 48, []));
one('B29 imshow(''peppers.png'')', @() imshow('peppers.png'));
one('B30 imshow(I) twice', @() twice(I));
one('B31 imshow(int8)', @() imshow(int8(magic(4) - 8)));
one('B32 imshow(I, []) int16', @() imshow(int16(magic(4) * 100 - 800), []));
one('B33 imshow(complex)', @() imshow(I + 1i));
one('B34 imshow(I, [5 1])', @() imshow(I, [5 1]));
one('B35 imshow(I, 3)', @() imshow(I, 3));
one('B36 imshow()', @() imshow());
one('B37 imshow(I, ''Bogus'', 1)', @() imshow(I, 'Bogus', 1));
one('B38 imshow(1:4, 1:4, I)', @() imshow(1:4, 1:4, I));
one('B39 imshow(I, ''Parent'', 5)', @() imshow(I, 'Parent', 5));
one('B40 imshow(I) then title(''x'')', @() then_title(I));
one('B41 imshow(zeros(0, 3))', @() imshow(zeros(0, 3)));
one('B42 imshow(I) on a figure with NextPlot new', @() nextplot_new(I));
one('B43 imshow(uint32)', @() imshow(uint32(magic(4))));
one('B44 imshow(rand(4, 4, 2))', @() imshow(rand(4, 4, 2)));
one('B45 imshow(I, ''XData'', [10 20])', @() imshow(I, 'XData', [10 20]));
one('B46 imshow(I, ''Colormap'', jet(16)) then colormap read of figure', @() imshow(I, 'Colormap', jet(16)));
one('B47 imshow(I, ''disp'', [0 2]) abbreviation', @() imshow(I, 'disp', [0 2]));
one('B48 imshow(rgb, jet(8))', @() imshow(reshape(1:48, 4, 4, 3) / 48, jet(8)));
one('B49 axes in a uipanel; imshow(I, ''Parent'', ax)', @() in_panel(I));
one('B50 imshow(I) into a uiaxes', @() in_uiaxes(I));
fprintf('done\n');

function one(label, make)
fprintf('--- %s\n', label);
close all force;
f = figure('Visible', 'off');
f0 = f.Position;
try
    out = make();
    f = gcf;
    ax = gca;
    ims = findobj(f, 'Type', 'image');
    fprintf('  fig: n=%d Position=%s (was %s) NextPlot=%s Colormap=%s axes=%d\n', numel(findobj(groot, 'Type', 'figure')), ...
        mat2str(f.Position), mat2str(f0), f.NextPlot, mapdesc(f.Colormap), numel(findobj(f, 'Type', 'axes')));
    fprintf('  axes: XLim=%s(%s) YLim=%s(%s) YDir=%s TickDir=%s(%s) XGrid=%s YGrid=%s DAR=%s(%s) PBARMode=%s Visible=%s\n', ...
        mat2str(ax.XLim, 6), ax.XLimMode, mat2str(ax.YLim, 6), ax.YLimMode, ax.YDir, ax.TickDir, ax.TickDirMode, ...
        ax.XGrid, ax.YGrid, mat2str(ax.DataAspectRatio, 4), ax.DataAspectRatioMode, ax.PlotBoxAspectRatioMode, ax.Visible);
    fprintf('  axes: CLim=%s(%s) Colormap=%s Position=%s Units=%s Box=%s Layer=%s NextPlot=%s Children=%d Title.Visible=%s XLabel.Visible=%s\n', ...
        mat2str(ax.CLim, 6), ax.CLimMode, mapdesc(ax.Colormap), mat2str(round(ax.Position, 4)), ax.Units, ax.Box, ax.Layer, ax.NextPlot, ...
        numel(ax.Children), ax.Title.Visible, ax.XLabel.Visible);
    for k = 1:numel(ims)
        im = ims(k);
        fprintf('  image%d: class=%s size=%s CDataMapping=%s XData=%s YData=%s Interpolation=%s BusyAction=%s Interruptible=%s\n', ...
            k, class(im.CData), mat2str(size(im.CData)), im.CDataMapping, mat2str(im.XData, 6), mat2str(im.YData, 6), ...
            im.Interpolation, im.BusyAction, im.Interruptible);
    end
    if ~isempty(out)
        fprintf('  out: %s\n', out);
    end
catch e
    fprintf('  error: %s | %s\n', e.identifier, e.message);
end
close all force;
end

function d = mapdesc(m)
d = sprintf('%dx%d first=%s last=%s', size(m, 1), size(m, 2), mat2str(m(1, :), 4), mat2str(m(end, :), 4));
end

function out = parent_named(I)
a1 = subplot(1, 2, 1);
subplot(1, 2, 2);
plot(1:3);
imshow(I, 'Parent', a1);
out = sprintf('current is the second: %d; first: XLim=%s(%s) DAR=%s Visible=%s Position=%s', ...
    isequal(gca, a1) == 0, mat2str(a1.XLim), a1.XLimMode, mat2str(a1.DataAspectRatio), a1.Visible, mat2str(round(a1.Position, 4)));
end

function out = in_subplot(I)
subplot(1, 2, 1);
imshow(I);
out = '';
end

function out = held(I)
plot(1:10);
hold on;
imshow(I);
out = sprintf('children types: %s', strjoin(get(gca().Children, 'Type')', ' '));
end

function out = replaced(I)
plot(1:10);
grid on;
set(gca, 'Tag', 'mine', 'FontSize', 14);
ax0 = gca;
imshow(I);
out = sprintf('same axes: %d Tag=%s FontSize=%g children=%d', isequal(ax0, gca), get(gca, 'Tag'), get(gca, 'FontSize'), numel(gca().Children));
end

function out = with_output(I)
h = imshow(I);
out = sprintf('class=%s Type=%s', class(h), h.Type);
end

function out = then_plot(I)
imshow(I);
ax0 = gca;
plot(1:3);
out = sprintf('same axes after plot: %d; XLim=%s(%s) DARMode=%s Visible=%s YDir=%s children=%d Position=%s', ...
    isequal(ax0, gca), mat2str(gca().XLim), gca().XLimMode, gca().DataAspectRatioMode, gca().Visible, gca().YDir, ...
    numel(gca().Children), mat2str(round(gca().Position, 4)));
end

function out = twice(I)
imshow(I);
imshow(I * 0.5);
out = sprintf('images in figure: %d', numel(findobj(gcf, 'Type', 'image')));
end

function out = then_title(I)
imshow(I);
title('x');
out = sprintf('title: %s visible=%s', gca().Title.String, gca().Title.Visible);
end

function out = nextplot_new(I)
f = gcf;
f.NextPlot = 'new';
imshow(I);
out = sprintf('new figure: %d', ~isequal(gcf, f));
end

function out = in_panel(I)
p = uipanel(gcf, 'Position', [0.1 0.1 0.5 0.5]);
ax = axes(p);
imshow(I, 'Parent', ax);
out = sprintf('panel axes: XLim=%s(%s) Visible=%s Position=%s', mat2str(ax.XLim), ax.XLimMode, ax.Visible, mat2str(round(ax.Position, 4)));
end

function out = in_uiaxes(I)
uf = uifigure('Visible', 'off');
ax = uiaxes(uf);
imshow(I, 'Parent', ax);
out = sprintf('uiaxes: XLim=%s(%s) DAR=%s(%s) Visible=%s CLim=%s(%s) YDir=%s', mat2str(ax.XLim), ax.XLimMode, ...
    mat2str(ax.DataAspectRatio), ax.DataAspectRatioMode, ax.Visible, mat2str(ax.CLim), ax.CLimMode, ax.YDir);
delete(uf);
end

function s = v2s(x)
if ischar(x)
    s = ['''' x ''''];
elseif isstring(x)
    s = char(x);
elseif isnumeric(x) || islogical(x)
    s = mat2str(x);
else
    s = ['<' class(x) '>'];
end
end
