% oi139_helper - writes rt/oi139_matlab_tree.fig: one figure hgsave'd by R2025b and stripped to
% its struct tree alone (hgS_070000), holding a titled and subtitled axes with two lines and a
% titled legend, and an image axes with a labelled colour bar. The parity fixture
% oi_image_limits_fig_leftovers opens the copy in fixtures/helpers in both engines: hgsave's
% tree carries no subtitle, legend title or colour bar label (oi139_shapes), so both read none.
here = fileparts(mfilename('fullpath'));
rt = fullfile(here, 'rt');
if ~isfolder(rt), mkdir(rt); end
warning('off', 'MATLAB:hgsave:HgsaveToBeRemoved');
set(groot, 'DefaultFigureVisible', 'off');
f = figure('Name', 'leftovers', 'NumberTitle', 'off', 'Position', [100 100 640 360], 'Tag', 'leftovers');
ax1 = subplot(1, 2, 1);
plot(ax1, 1:5, [1 4 2 5 3], 'r-o', 'Tag', 'first');
hold(ax1, 'on');
plot(ax1, 1:5, [2 2 3 3 4], 'b--', 'Tag', 'second');
title(ax1, 'Main');
subtitle(ax1, 'Below the main');
xlabel(ax1, 'X');
lg = legend(ax1, 'a', 'b', 'Location', 'northwest');
title(lg, 'Series');
set(ax1, 'Tag', 'left');
ax2 = subplot(1, 2, 2);
imagesc(ax2, magic(4));
cb = colorbar(ax2);
cb.Label.String = 'Heat';
set(ax2, 'Tag', 'right');
full = fullfile(rt, 'oi139_matlab_full.fig');
only = fullfile(rt, 'oi139_matlab_tree.fig');
hgsave(f, full);
s = load(full, '-mat', 'hgS_070000');
hgS_070000 = s.hgS_070000; %#ok<NASGU>
save(only, 'hgS_070000', '-mat');
delete(f);
h = openfig(only, 'invisible');
L = findobj(h, 'Tag', 'left');
fprintf('title=%s subtitle=%s legend title=%s\n', L.Title.String, L.Subtitle.String, findobj(h, 'Type', 'legend').Title.String);
fprintf('colorbar label=%s\n', findobj(h, 'Type', 'colorbar').Label.String);
d = dir(only);
fprintf('wrote %s (%d bytes)\n', only, d.bytes);
delete(h);
