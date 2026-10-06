function h = u9_make(kind, parent)
% One object of U9 by its class word, in a figure of either kind (U9 fixtures). A node gets a tree
% of its own, because that is the only parent it takes.
switch kind
    case 'Knob',              h = uiknob(parent);
    case 'DiscreteKnob',      h = uiknob(parent, 'discrete');
    case 'Switch',            h = uiswitch(parent);
    case 'RockerSwitch',      h = uiswitch(parent, 'rocker');
    case 'ToggleSwitch',      h = uiswitch(parent, 'toggle');
    case 'Gauge',             h = uigauge(parent);
    case 'LinearGauge',       h = uigauge(parent, 'linear');
    case 'NinetyDegreeGauge', h = uigauge(parent, 'ninetydegree');
    case 'SemicircularGauge', h = uigauge(parent, 'semicircular');
    case 'Lamp',              h = uilamp(parent);
    case 'DatePicker',        h = uidatepicker(parent);
    case 'ColorPicker',       h = uicolorpicker(parent);
    case 'Tree',              h = uitree(parent);
    case 'CheckBoxTree',      h = uitree(parent, 'checkbox');
    case 'TreeNode',          h = uitreenode(uitree(parent));
end
end
