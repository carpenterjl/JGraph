% ex9_guide - the U11 window check (ADR 0210): research C's hand-built GUIDE pair runs under
% gui_mainfcn, shown, and a figure R2025b's savefig wrote opens with its axes, lines, labels,
% legend, image and colour bar. Both files are the parity fixtures' helpers (saved hidden, so
% each is shown here).
here = fileparts(mfilename('fullpath'));
helpers = fullfile(here, '..', '..', '..', '..', '..', '..', 'tests', 'JGraph.Tests', 'MatlabParity', 'fixtures', 'helpers');
addpath(helpers);
h = myguide('Visible', 'on');
hs = guidata(h);
fprintf('GUIDE pair: %s, handles %s\n', get(h, 'Name'), strjoin(fieldnames(hs)', ','));
p = openfig(fullfile(helpers, 'u11_plots.fig'), 'visible'); % the helpers were saved hidden
fprintf('R2025b figure: %s, %d axes\n', p.Name, numel(findobj(p, 'Type', 'axes')));
