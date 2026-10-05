function n = u7_figs()
% How many figures there are, hidden ones included (U7 fixtures).
n = numel(findall(groot, 'Type', 'figure'));
end
