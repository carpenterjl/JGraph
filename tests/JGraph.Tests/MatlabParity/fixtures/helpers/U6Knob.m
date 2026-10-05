classdef U6Knob < matlab.mixin.SetGet
    % set and get by property name (u6_attrs.m).
    properties
        Level = 1
        Label = 'k'
    end
    properties (SetAccess = private)
        Locked = 0
    end
    properties (Access = private)
        Inner = 2
    end
end
