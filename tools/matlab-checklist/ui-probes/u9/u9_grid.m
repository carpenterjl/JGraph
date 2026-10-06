function u9_grid
% U9 probe: the size each kind asks for in a 'fit' cell of a grid, and where it is put in a
% fixed cell. Headless; the layout settles some time after drawnow, so every reading waits.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
warm = uigridlayout(uf, [1 1], 'RowHeight', {'fit'}, 'ColumnWidth', {'fit'}, 'Padding', 0); uiknob(warm); drawnow; pause(2); delete(warm);
makers = {
    'knob', @(g) uiknob(g)
    'knob FontSize 20', @(g) uiknob(g, 'FontSize', 20)
    'knob long labels', @(g) uiknob(g, 'MajorTickLabels', {'Minimum', 'Maximum', 'Middle', 'x', 'y', 'z'})
    'knob Limits 0 1000', @(g) uiknob(g, 'Limits', [0 1000])
    'dknob', @(g) uiknob(g, 'discrete')
    'dknob long items', @(g) uiknob(g, 'discrete', 'Items', {'Alpha', 'Beta', 'Gamma', 'Delta Epsilon'})
    'dknob 2 items', @(g) uiknob(g, 'discrete', 'Items', {'a', 'b'})
    'switch', @(g) uiswitch(g)
    'switch long', @(g) uiswitch(g, 'Items', {'Stopped', 'Running'})
    'switch vertical', @(g) uiswitch(g, 'Orientation', 'vertical')
    'switch FontSize 20', @(g) uiswitch(g, 'FontSize', 20)
    'rocker', @(g) uiswitch(g, 'rocker')
    'rocker horizontal', @(g) uiswitch(g, 'rocker', 'Orientation', 'horizontal')
    'toggle', @(g) uiswitch(g, 'toggle')
    'gauge', @(g) uigauge(g)
    'linear', @(g) uigauge(g, 'linear')
    'linear vertical', @(g) uigauge(g, 'linear', 'Orientation', 'vertical')
    'ninety', @(g) uigauge(g, 'ninetydegree')
    'semi', @(g) uigauge(g, 'semicircular')
    'lamp', @(g) uilamp(g)
    'datepicker', @(g) uidatepicker(g)
    'datepicker FontSize 20', @(g) uidatepicker(g, 'FontSize', 20)
    'colorpicker', @(g) uicolorpicker(g)
    'tree', @(g) uitree(g)
    'tree with nodes', @(g) withnodes(uitree(g))
    'checkbox tree', @(g) uitree(g, 'checkbox')
    'tree FontSize 20', @(g) uitree(g, 'FontSize', 20)
    };
for m = 1:size(makers, 1)
    g = uigridlayout(uf, [1 1], 'RowHeight', {'fit'}, 'ColumnWidth', {'fit'}, 'Padding', 0);
    h = makers{m, 2}(g);
    drawnow; pause(0.6);
    fprintf('FIT %s : Position=%s Outer=%s\n', makers{m, 1}, mat2str(h.Position, 8), mat2str(h.OuterPosition, 8));
    delete(g);
end
cells = {'knob', @(g) uiknob(g); 'dknob', @(g) uiknob(g, 'discrete'); 'switch', @(g) uiswitch(g); 'rocker', @(g) uiswitch(g, 'rocker'); ...
    'gauge', @(g) uigauge(g); 'linear', @(g) uigauge(g, 'linear'); 'lamp', @(g) uilamp(g); 'datepicker', @(g) uidatepicker(g); ...
    'colorpicker', @(g) uicolorpicker(g); 'tree', @(g) uitree(g)};
for m = 1:size(cells, 1)
    g = uigridlayout(uf, [1 1], 'RowHeight', {200}, 'ColumnWidth', {240}, 'Padding', 0);
    h = cells{m, 2}(g);
    drawnow; pause(0.6);
    fprintf('CELL240x200 %s : Position=%s Outer=%s\n', cells{m, 1}, mat2str(h.Position, 8), mat2str(h.OuterPosition, 8));
    delete(g);
end
delete(uf);
end

function t = withnodes(t)
a = uitreenode(t, 'Text', 'Alpha'); uitreenode(a, 'Text', 'Beta'); uitreenode(t, 'Text', 'Gamma Delta Epsilon');
end
