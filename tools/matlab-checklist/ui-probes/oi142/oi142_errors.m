% oi142_errors - open item 142, second pass: the refusals and warnings imshow's input parser gives
% that oi142_imshow did not reach, word for word, and two pictures: whether an axis label written
% after imshow is drawn (imshow sets the axes invisible and its labels visible), beside a plain axes
% made invisible. Headless; exportgraphics writes rt/*.png.
set(groot, 'DefaultFigureVisible', 'off');
fprintf('%s\n', version);
I = magic(4) / 16;
rgb = reshape(1:48, 4, 4, 3) / 48;
one('M1 imshow(I, [0 1], 3)', @() imshow(I, [0 1], 3));
one('M2 imshow(I, ones(2, 2))', @() imshow(I, ones(2, 2)));
one('M3 imshow(I, ''XData'')', @() imshow(I, 'XData'));
one('M4 imshow(I, ''In'', 1)', @() imshow(I, 'In', 1));
one('M5 imshow(I, ''XData'', [1 2], ''Bogus'', 1)', @() imshow(I, 'XData', [1 2], 'Bogus', 1));
one('M6 imshow(I, ''Interpolation'', ''cubic'')', @() imshow(I, 'Interpolation', 'cubic'));
one('M7 imshow(I, ''Border'', ''wide'')', @() imshow(I, 'Border', 'wide'));
one('M8 imshow(I, ''Colormap'', [1 2])', @() imshow(I, 'Colormap', [1 2]));
one('M9 imshow(I, ''XData'', 5)', @() imshow(I, 'XData', 5));
one('M10 imshow(I, ''InitialMagnification'', ''big'')', @() imshow(I, 'InitialMagnification', 'big'));
one('M11 imshow({1})', @() imshow({1}));
one('M12 imshow(int16(I), jet(8))', @() imshow(int16(magic(4)), jet(8)));
one('M13 imshow(int16 rgb)', @() imshow(int16(rgb * 100)));
one('M14 imshow(true(2, 2, 3))', @() imshow(true(2, 2, 3)));
one('M15 imshow(rgb, ''Colormap'', jet(8))', @() imshow(rgb, 'Colormap', jet(8)));
one('M16 imshow(''nofile.png'')', @() imshow('nofile.png'));
one('M17 imshow(I, [NaN 1])', @() imshow(I, [NaN 1]));
one('M18 imshow(I, ''Parent'', polaraxes)', @() imshow(I, 'Parent', polaraxes));
one('M19 imshow(I, ''xdata'', [1 2]) lower case', @() imshow(I, 'xdata', [1 2]));
one('M20 imshow(''peppers.png'', [0 1])', @() imshow('peppers.png', [0 1]));
one('M21 imshow(I, ''Reduce'', true)', @() imshow(I, 'Reduce', true));
one('M22 imshow(I, ''DisplayRange'', [])', @() imshow(I * 4, 'DisplayRange', []));
one('M23 imshow(I, "DisplayRange", [0 2]) string name', @() imshow(I, "DisplayRange", [0 2]));
one('M24 imshow(I, ''Colormap'', ''jet'')', @() imshow(I, 'Colormap', 'jet'));
one('M25 imshow(I, [0 1 2])', @() imshow(I, [0 1 2]));
one('M26 imshow(I, ''DisplayRange'', 5)', @() imshow(I, 'DisplayRange', 5));
one('M27 imshow(int8(I*100), [])', @() imshow(int8(I * 100), []));
one('M28 imshow(uint8 RGB, [])', @() imshow(uint8(rgb * 200), []));
one('M29 imshow(logical, [])', @() imshow(logical(eye(3)), []));
one('M30 imshow(I, gray(4)) indexed double 0.0625..1', @() imshow(I, gray(4)));

if ~exist('rt', 'dir'), mkdir('rt'); end
f = figure('Visible', 'off', 'Position', [100 100 400 300]);
imshow(kron(magic(4) / 16, ones(40)));
xlabel('XLABELTEXT');
ylabel('YLABELTEXT');
title('TITLETEXT');
fprintf('P1 imshow then labels: XLabel.Visible=%s YLabel.Visible=%s Title.Visible=%s\n', get(gca, 'XLabel').Visible, get(gca, 'YLabel').Visible, get(gca, 'Title').Visible);
try
    exportgraphics(f, fullfile('rt', 'p1_imshow_labels.png'), 'Resolution', 96);
    fprintf('P1 exported\n');
catch e
    fprintf('P1 export error: %s\n', e.message);
end
close(f);
f = figure('Visible', 'off', 'Position', [100 100 400 300]);
plot(1:10);
xlabel('XLABELTEXT');
title('TITLETEXT');
set(gca, 'Visible', 'off');
fprintf('P2 plain axes off: XLabel.Visible=%s Title.Visible=%s\n', get(gca, 'XLabel').Visible, get(gca, 'Title').Visible);
try
    exportgraphics(f, fullfile('rt', 'p2_axes_off_labels.png'), 'Resolution', 96);
    fprintf('P2 exported\n');
catch e
    fprintf('P2 export error: %s\n', e.message);
end
close(f);
fprintf('done\n');

function one(label, make)
fprintf('--- %s\n', label);
close all force;
lastwarn('');
try
    make();
    ax = gca;
    im = findobj(ax, 'Type', 'image');
    [msg, id] = lastwarn;
    fprintf('  ok: CLim=%s(%s) Colormap=%dx3 first=%s last=%s', mat2str(ax.CLim, 6), ax.CLimMode, size(ax.Colormap, 1), mat2str(ax.Colormap(1, :), 4), mat2str(ax.Colormap(end, :), 4));
    if ~isempty(im)
        fprintf(' CDataMapping=%s class=%s XData=%s', im(1).CDataMapping, class(im(1).CData), mat2str(im(1).XData, 6));
    end
    fprintf('\n');
    if ~isempty(msg)
        fprintf('  warning: %s | %s\n', id, msg);
    end
catch e
    fprintf('  error: %s | %s\n', e.identifier, strrep(e.message, newline, '\n'));
end
close all force;
end
