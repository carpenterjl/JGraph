function makers = u9_makers()
% The U9 objects by a key, each made in a parent figure p (classic or uifigure).
makers = {
    'Knob',           @(p) uiknob(p)
    'DiscreteKnob',   @(p) uiknob(p, 'discrete')
    'Switch',         @(p) uiswitch(p)
    'RockerSwitch',   @(p) uiswitch(p, 'rocker')
    'ToggleSwitch',   @(p) uiswitch(p, 'toggle')
    'Gauge',          @(p) uigauge(p)
    'LinearGauge',    @(p) uigauge(p, 'linear')
    'NinetyDegreeGauge', @(p) uigauge(p, 'ninetydegree')
    'SemicircularGauge', @(p) uigauge(p, 'semicircular')
    'Lamp',           @(p) uilamp(p)
    'DatePicker',     @(p) uidatepicker(p)
    'ColorPicker',    @(p) uicolorpicker(p)
    'Tree',           @(p) uitree(p)
    'CheckBoxTree',   @(p) uitree(p, 'checkbox')
    'TreeNode',       @(p) uitreenode(uitree(p))
    'SubNode',        @(p) uitreenode(uitreenode(uitree(p)))
    'CheckNode',      @(p) uitreenode(uitree(p, 'checkbox'))
    };
end
