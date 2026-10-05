% record: -noFigureWindows
% U5 of the app-building plan (ADR 0202): the property surfaces of a uifigure's components and of
% uigridlayout. One battery of values is written to each property of a fresh component, and each
% line says what the property then reads as, or the identifier and sentence of the refusal. A
% property that behaves alike in every class that has it is tried on one of them.
uf = uifigure('Visible', 'off', 'Position', [100 100 560 420]);
battery = {'on', 'off', true, false, 1, 0, -1, 2.5, 50, [1 2], [10 20], [1 2 3], [0.2 0.4 0.6], [1 2 3 4], [5 6 7 8], ...
    'abc', "str", {'a', 'b'}, ["p"; "q"], {'a'; 'b'}, [], '', {}, NaN, Inf, -Inf, 'red', '#0f0', 'none', @sin, struct('a', 1), ...
    int8(3), single(2.5), 'left', 'center', 'right', 'top', 'bottom', 'bold', 'italic', 'auto', 'manual', 'fit', '1x', {'fit', '2x', 30}, ...
    'horizontal', 'vertical', ['ab'; 'cd'], 1 + 2i, "", {1, 2}, 'Option 2', 'Item 3', {'Item 1', 'Item 3'}, 2, 3, [2 4], ...
    '%.2f', 'text', 'html', 'latex', 'tex', uint8([10 20 30]), [0 1], [5 1], [-Inf Inf], [0 Inf], 'digits', 'vert', 'nor', 'ON'};
pairs = {
    'Button', 'HandleVisibility'
    'Button', 'BusyAction'
    'Button', 'Interruptible'
    'Button', 'Tag'
    'Button', 'Enable'
    'Button', 'Visible'
    'Button', 'Tooltip'
    'Button', 'Position'
    'Button', 'InnerPosition'
    'Button', 'OuterPosition'
    'Button', 'FontName'
    'Button', 'FontSize'
    'Button', 'FontWeight'
    'Button', 'FontAngle'
    'Button', 'FontColor'
    'Button', 'BackgroundColor'
    'Button', 'HorizontalAlignment'
    'Button', 'VerticalAlignment'
    'Button', 'Text'
    'Button', 'WordWrap'
    'Button', 'Interpreter'
    'Button', 'Icon'
    'Button', 'IconAlignment'
    'Button', 'ButtonPushedFcn'
    'Label', 'BackgroundColor'
    'StateButton', 'Value'
    'CheckBox', 'Value'
    'RadioButton', 'Value'
    'EditField', 'Value'
    'EditField', 'CharacterLimits'
    'EditField', 'InputType'
    'EditField', 'Editable'
    'EditField', 'Placeholder'
    'EditField', 'ValueChangingFcn'
    'NumericEditField', 'Value'
    'NumericEditField', 'Limits'
    'NumericEditField', 'LowerLimitInclusive'
    'NumericEditField', 'RoundFractionalValues'
    'NumericEditField', 'ValueDisplayFormat'
    'NumericEditField', 'AllowEmpty'
    'Spinner', 'Step'
    'Spinner', 'Value'
    'TextArea', 'Value'
    'DropDown', 'Value'
    'DropDown', 'ValueIndex'
    'DropDown', 'Items'
    'DropDown', 'ItemsData'
    'DropDown', 'Editable'
    'ListBox', 'Value'
    'ListBox', 'ValueIndex'
    'ListBox', 'Multiselect'
    'Slider', 'Value'
    'Slider', 'Limits'
    'Slider', 'Step'
    'Slider', 'StepMode'
    'Slider', 'MajorTicks'
    'Slider', 'MinorTicks'
    'Slider', 'MajorTickLabels'
    'Slider', 'MajorTicksMode'
    'Slider', 'Orientation'
    'Slider', 'Position'
    'RangeSlider', 'Value'
    'Image', 'ScaleMethod'
    'Image', 'ImageSource'
    'Image', 'URL'
    'Image', 'AltText'
    'Hyperlink', 'URL'
    'Hyperlink', 'VisitedColor'
    'GridLayout', 'ColumnWidth'
    'GridLayout', 'RowHeight'
    'GridLayout', 'ColumnSpacing'
    'GridLayout', 'Padding'
    'GridLayout', 'Scrollable'
    'GridLayout', 'BackgroundColor'
    'GridLayout', 'Position'
    };
for p = 1:size(pairs, 1)
    for k = 1:numel(battery)
        h = u5_make(pairs{p, 1}, uf);
        u5_try(sprintf('%s_%s_%02d', pairs{p, 1}, pairs{p, 2}, k), h, pairs{p, 2}, battery{k});
        delete(allchild(uf));
    end
end
delete(uf);
