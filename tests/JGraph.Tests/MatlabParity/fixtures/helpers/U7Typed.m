classdef U7Typed < handle
    % Properties typed with graphics classes, as App Designer writes them (U7 fixtures).
    properties
        Fig     matlab.ui.Figure
        Btn     matlab.ui.control.Button
        Ax      matlab.ui.control.UIAxes
        AnyAx   matlab.graphics.axis.Axes
        Pan     matlab.ui.container.Panel
        Any     matlab.graphics.Graphics
        Ln      matlab.graphics.chart.primitive.Line
        Num     double
    end
end
