% record: -noFigureWindows
% U9 of the app-building plan (ADR 0207): the property surfaces of uiknob (both styles), uiswitch,
% uigauge (all four), uilamp, uidatepicker, uicolorpicker, uitree (both kinds) and uitreenode. One
% battery of values is written to each property of a fresh object, and each line says what the
% property then reads as, or the identifier and sentence of the refusal. A property that behaves
% alike in every class that has it is tried on one of them. R2025b answers alike in a classic figure
% and in a uifigure; the objects here are made in a uifigure.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
d1 = datetime(2024, 1, 15);
d2 = datetime(2024, 3, 5, 14, 30, 0);
battery = {'on', 'off', true, false, 1, 0, -1, 2.5, 50, [1 2], [10 20], [1 2 3], [0.2 0.4 0.6], [1 2 3 4], [5 6 7 8], ...
    'abc', "str", {'a', 'b'}, ["p"; "q"], {'a'; 'b'}, [], '', {}, NaN, Inf, -Inf, 'red', '#0f0', 'none', @sin, struct('a', 1), ...
    int8(3), single(2.5), 'left', 'center', 'right', 'top', 'bottom', 'bold', 'italic', 'auto', 'manual', ...
    ['ab'; 'cd'], "", {1, 2}, 2, 3, [2 4], magic(3), {1, 'a'; 2, 'b'}, [true false; false true], ...
    'numeric', {'a', 'b', 'c'}, {'Off', 'On'}, {'On', 'Off'}, 'Off', 'On', 'off ', 'Low', 'High', 'Medium', 100, 101, 0.5, [0 1], [0 100], [100 0], [50 50], ...
    'clockwise', 'counterclockwise', 'north', 'south', 'east', 'west', 'northwest', 'northeast', 'southwest', 'southeast', ...
    'horizontal', 'vertical', 'HORIZONTAL', 'horiz', [1 0 0; 0 1 0], {'red', 'green'}, [0 50; 50 100], [0 50; 60 100], [50 0], 'r', 'g', 'y', ...
    d1, d2, NaT, [d1 d2], [d2 d1], [d1; d2], datetime(2024, 1, [1 2 3]), 'dd/MM/uuuu', 'MM/dd/uuuu', 'uuuu', 'dd-MMM-uuuu', 'yyyy-MM-dd', 'dd-MM-yy', 'HH:mm', 'bogus', ...
    'Monday', 'monday', {'Monday', 'Sunday'}, ["Saturday" "Sunday"], [1 7], 1:7, 8, [1 1], 'Tree Node', [1 0 0 0.5], '15-Jan-2024', "15-Jan-2024", 20240115, ...
    'pixels', 'normalized', 'tex', 'latex', 'html', 'A', 'ab', [2 1; 1 2], uint8([10 20 30]), 'ON', 'TOP', 'checkbox', 'discrete', 'linear', 'rocker'};
pairs = {
    'Knob', 'Value'
    'Knob', 'Limits'
    'Knob', 'MajorTicks'
    'Knob', 'MinorTicks'
    'Knob', 'MajorTickLabels'
    'Knob', 'MajorTicksMode'
    'Knob', 'MinorTicksMode'
    'Knob', 'MajorTickLabelsMode'
    'Knob', 'ValueChangedFcn'
    'Knob', 'ValueChangingFcn'
    'Knob', 'FontName'
    'Knob', 'FontSize'
    'Knob', 'FontWeight'
    'Knob', 'FontColor'
    'Knob', 'Tooltip'
    'Knob', 'Position'
    'Knob', 'InnerPosition'
    'Knob', 'Enable'
    'Knob', 'Visible'
    'Knob', 'Tag'
    'Knob', 'HandleVisibility'
    'Knob', 'BusyAction'
    'Knob', 'Interruptible'
    'DiscreteKnob', 'Value'
    'DiscreteKnob', 'ValueIndex'
    'DiscreteKnob', 'Items'
    'DiscreteKnob', 'ItemsData'
    'DiscreteKnob', 'ValueChangedFcn'
    'Switch', 'Value'
    'Switch', 'ValueIndex'
    'Switch', 'Items'
    'Switch', 'ItemsData'
    'Switch', 'Orientation'
    'RockerSwitch', 'Orientation'
    'ToggleSwitch', 'Value'
    'Gauge', 'Value'
    'Gauge', 'Limits'
    'Gauge', 'ScaleColors'
    'Gauge', 'ScaleColorLimits'
    'Gauge', 'ScaleDirection'
    'Gauge', 'BackgroundColor'
    'Gauge', 'MajorTicks'
    'Gauge', 'MajorTickLabels'
    'Gauge', 'FontSize'
    'LinearGauge', 'Orientation'
    'LinearGauge', 'Value'
    'NinetyDegreeGauge', 'Orientation'
    'NinetyDegreeGauge', 'ScaleDirection'
    'SemicircularGauge', 'Orientation'
    'Lamp', 'Color'
    'Lamp', 'Tooltip'
    'Lamp', 'Visible'
    'Lamp', 'Enable'
    'Lamp', 'Position'
    'Lamp', 'Tag'
    'DatePicker', 'DisplayFormat'
    'DatePicker', 'Value'
    'DatePicker', 'DisabledDates'
    'DatePicker', 'DisabledDaysOfWeek'
    'DatePicker', 'Limits'
    'DatePicker', 'Editable'
    'DatePicker', 'Placeholder'
    'DatePicker', 'BackgroundColor'
    'DatePicker', 'FontColor'
    'DatePicker', 'ValueChangedFcn'
    'ColorPicker', 'Value'
    'ColorPicker', 'Icon'
    'ColorPicker', 'BackgroundColor'
    'ColorPicker', 'ValueChangedFcn'
    'Tree', 'Multiselect'
    'Tree', 'Editable'
    'Tree', 'BackgroundColor'
    'Tree', 'FontName'
    'Tree', 'FontWeight'
    'Tree', 'FontAngle'
    'Tree', 'Position'
    'Tree', 'Tooltip'
    'Tree', 'SelectionChangedFcn'
    'Tree', 'ClickedFcn'
    'Tree', 'NodeTextChangedFcn'
    'CheckBoxTree', 'Editable'
    'CheckBoxTree', 'CheckedNodesChangedFcn'
    'TreeNode', 'Text'
    'TreeNode', 'NodeData'
    'TreeNode', 'Icon'
    'TreeNode', 'Tag'
    'TreeNode', 'HandleVisibility'
    'TreeNode', 'BusyAction'
    'TreeNode', 'Interruptible'
    'TreeNode', 'CreateFcn'
    };
for p = 1:size(pairs, 1)
    for k = 1:numel(battery)
        h = u9_make(pairs{p, 1}, uf);
        u9_try(sprintf('%s_%s_%03d', pairs{p, 1}, pairs{p, 2}, k), h, pairs{p, 2}, battery{k});
        delete(allchild(uf));
    end
end
delete(uf);
