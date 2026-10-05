function h = u5_make(kind, parent)
% One uifigure component by its class word, in a parent (U5 fixtures). A radio or toggle button
% gets a button group of its own, because that is the only parent it takes.
switch kind
    case 'Label',            h = uilabel(parent);
    case 'Button',           h = uibutton(parent);
    case 'StateButton',      h = uibutton(parent, 'state');
    case 'EditField',        h = uieditfield(parent);
    case 'NumericEditField', h = uieditfield(parent, 'numeric');
    case 'TextArea',         h = uitextarea(parent);
    case 'DropDown',         h = uidropdown(parent);
    case 'ListBox',          h = uilistbox(parent);
    case 'CheckBox',         h = uicheckbox(parent);
    case 'RadioButton',      h = uiradiobutton(uibuttongroup(parent));
    case 'ToggleButton',     h = uitogglebutton(uibuttongroup(parent));
    case 'Slider',           h = uislider(parent);
    case 'RangeSlider',      h = uislider(parent, 'range');
    case 'Spinner',          h = uispinner(parent);
    case 'Image',            h = uiimage(parent);
    case 'Hyperlink',        h = uihyperlink(parent);
    case 'GridLayout',       h = uigridlayout(parent);
end
end
