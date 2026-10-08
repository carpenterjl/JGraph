% u11_lte - the struct tree of the one GUIDE figure R2025b ships (saved by R2024a), and an HG1-era
% axes with a title and labels saved by hgsave. Headless.
lte = fullfile(matlabroot, 'toolbox', 'lte', 'lte', 'saLteDLConformanceTestBenchGUI.fig');
u11dump(lte);
here = fileparts(mfilename('fullpath'));
f = figure('Visible', 'off');
ax = axes(f);
plot(ax, 1:3, [3 1 2]);
title(ax, 'T'); xlabel(ax, 'XL'); ylabel(ax, 'YL'); zlabel(ax, 'ZL');
xlim(ax, [0 4]); grid(ax, 'on'); set(ax, 'XScale', 'log');
legend(ax, 'only');
warning('off', 'MATLAB:hgsave:HgsaveToBeRemoved');
hgsave(f, fullfile(here, 'figs', 'u11_labels7.fig'));
savefig(f, fullfile(here, 'figs', 'u11_labels.fig'));
delete(f);
u11dump(fullfile(here, 'figs', 'u11_labels7.fig'));
