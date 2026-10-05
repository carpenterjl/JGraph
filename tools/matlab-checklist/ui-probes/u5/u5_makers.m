function makers = u5_makers()
% The batch-1 components of U5, by class word, each made in a parent p.
makers = {
    'Label',            @(p) uilabel(p)
    'Button',           @(p) uibutton(p)
    'StateButton',      @(p) uibutton(p, 'state')
    'EditField',        @(p) uieditfield(p)
    'NumericEditField', @(p) uieditfield(p, 'numeric')
    'TextArea',         @(p) uitextarea(p)
    'DropDown',         @(p) uidropdown(p)
    'ListBox',          @(p) uilistbox(p)
    'CheckBox',         @(p) uicheckbox(p)
    'RadioButton',      @(p) uiradiobutton(uibuttongroup(p))
    'ToggleButton',     @(p) uitogglebutton(uibuttongroup(p))
    'Slider',           @(p) uislider(p)
    'RangeSlider',      @(p) uislider(p, 'range')
    'Spinner',          @(p) uispinner(p)
    'Image',            @(p) uiimage(p)
    'Hyperlink',        @(p) uihyperlink(p)
    'GridLayout',       @(p) uigridlayout(p)
    };
end
