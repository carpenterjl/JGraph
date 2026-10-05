% record: -noFigureWindows
% U8 of the app-building plan (ADR 0206): the property surfaces of uitable, uitabgroup, uitab,
% uimenu, uicontextmenu, uitoolbar, uipushtool and uitoggletool. One battery of values is written
% to each property of a fresh object, and each line says what the property then reads as, or the
% identifier and sentence of the refusal. A property that behaves alike in every class that has it
% is tried on one of them. R2025b answers alike in a classic figure and in a uifigure; the objects
% here are made in a uifigure, and the last block tries a few in a classic one.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
T = table([1; 2], {'a'; 'b'}, 'VariableNames', {'N', 'S'});
battery = {'on', 'off', true, false, 1, 0, -1, 2.5, 50, [1 2], [10 20], [1 2 3], [0.2 0.4 0.6], [1 2 3 4], [5 6 7 8], ...
    'abc', "str", {'a', 'b'}, ["p"; "q"], {'a'; 'b'}, [], '', {}, NaN, Inf, -Inf, 'red', '#0f0', 'none', @sin, struct('a', 1), ...
    int8(3), single(2.5), 'left', 'center', 'right', 'top', 'bottom', 'bold', 'italic', 'auto', 'fit', '1x', {'fit', '2x', 30}, ...
    ['ab'; 'cd'], "", {1, 2}, 2, 3, [2 4], magic(3), {1, 'a'; 2, 'b'}, T, [true false; false true], ...
    'numeric', 'char', 'logical', {'numeric', 'char'}, {'numeric', {'x', 'y'}}, 'numbered', {'auto', 50}, {50 60}, ...
    [true false], {'bank', 'short'}, 'cell', 'row', 'column', 0.5 * ones(4, 4, 3), uint8(zeros(4, 4, 3)), 'pixels', 'normalized', ...
    'A', [1 1], [2 1; 1 2], uint8([10 20 30]), {'1x', '2x'}, 'longG', 'ON', 'r', 'TOP'};
pairs = {
    'Table', 'Data'
    'Table', 'ColumnName'
    'Table', 'RowName'
    'Table', 'ColumnWidth'
    'Table', 'ColumnEditable'
    'Table', 'ColumnSortable'
    'Table', 'ColumnFormat'
    'Table', 'ColumnRearrangeable'
    'Table', 'BackgroundColor'
    'Table', 'ForegroundColor'
    'Table', 'RowStriping'
    'Table', 'SelectionType'
    'Table', 'Selection'
    'Table', 'Multiselect'
    'Table', 'FontName'
    'Table', 'FontSize'
    'Table', 'FontUnits'
    'Table', 'FontWeight'
    'Table', 'FontAngle'
    'Table', 'Units'
    'Table', 'Position'
    'Table', 'InnerPosition'
    'Table', 'OuterPosition'
    'Table', 'Enable'
    'Table', 'Visible'
    'Table', 'Tooltip'
    'Table', 'Tag'
    'Table', 'HandleVisibility'
    'Table', 'BusyAction'
    'Table', 'Interruptible'
    'Table', 'CellEditCallback'
    'Table', 'KeyPressFcn'
    'Table', 'ButtonDownFcn'
    'Table', 'DisplayData'
    'Table', 'Extent'
    'TabGroup', 'TabLocation'
    'TabGroup', 'Tooltip'
    'TabGroup', 'Visible'
    'TabGroup', 'Units'
    'TabGroup', 'Position'
    'TabGroup', 'InnerPosition'
    'TabGroup', 'OuterPosition'
    'TabGroup', 'AutoResizeChildren'
    'TabGroup', 'SelectionChangedFcn'
    'TabGroup', 'SizeChangedFcn'
    'TabGroup', 'Tag'
    'Tab', 'Title'
    'Tab', 'BackgroundColor'
    'Tab', 'ForegroundColor'
    'Tab', 'Tooltip'
    'Tab', 'Units'
    'Tab', 'Position'
    'Tab', 'Scrollable'
    'Tab', 'AutoResizeChildren'
    'Tab', 'Tag'
    'Menu', 'Text'
    'Menu', 'Accelerator'
    'Menu', 'Checked'
    'Menu', 'Separator'
    'Menu', 'Enable'
    'Menu', 'Visible'
    'Menu', 'ForegroundColor'
    'Menu', 'Tooltip'
    'Menu', 'Position'
    'Menu', 'Clipping'
    'Menu', 'MenuSelectedFcn'
    'Menu', 'Tag'
    'Menu', 'HandleVisibility'
    'Menu', 'BusyAction'
    'Menu', 'Interruptible'
    'ContextMenu', 'ContextMenuOpeningFcn'
    'ContextMenu', 'Clipping'
    'ContextMenu', 'Tag'
    'Toolbar', 'BackgroundColor'
    'Toolbar', 'Visible'
    'Toolbar', 'Tag'
    'PushTool', 'Tooltip'
    'PushTool', 'CData'
    'PushTool', 'Icon'
    'PushTool', 'Enable'
    'PushTool', 'Separator'
    'PushTool', 'Visible'
    'PushTool', 'ClickedCallback'
    'PushTool', 'Tag'
    'ToggleTool', 'State'
    'ToggleTool', 'OnCallback'
    };
for p = 1:size(pairs, 1)
    for k = 1:numel(battery)
        h = u8_make(pairs{p, 1}, uf);
        u5_try(sprintf('%s_%s_%02d', pairs{p, 1}, pairs{p, 2}, k), h, pairs{p, 2}, battery{k});
        delete(allchild(uf));
    end
end
delete(uf);

% In a classic figure: the properties whose defaults differ there, and a few more.
f = figure('Visible', 'off', 'Position', [100 100 560 420]);
classic = {
    'Table', 'FontSize'
    'Table', 'FontUnits'
    'Table', 'Units'
    'Table', 'Position'
    'Table', 'Data'
    'TabGroup', 'Units'
    'TabGroup', 'Position'
    'TabGroup', 'TabLocation'
    'Menu', 'Text'
    'Menu', 'Position'
    'ToggleTool', 'State'
    };
for p = 1:size(classic, 1)
    for k = 1:numel(battery)
        h = u8_make(classic{p, 1}, f);
        u5_try(sprintf('F_%s_%s_%02d', classic{p, 1}, classic{p, 2}, k), h, classic{p, 2}, battery{k});
        delete(allchild(f));
    end
end
delete(f);
