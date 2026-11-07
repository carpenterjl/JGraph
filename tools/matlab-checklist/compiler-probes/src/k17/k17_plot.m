function n = k17_plot(visible)
%K17_PLOT Plots a line in a figure, invisible unless asked, and answers how many figures exist (stage K17).
if nargin < 1, visible = 'off'; end
f = figure('Visible', visible);
plot(1:10, (1:10) .^ 2);
title('k17\_plot');
n = numel(findall(0, 'Type', 'figure'));
if strcmpi(visible, 'off')
    close(f);
end
end
