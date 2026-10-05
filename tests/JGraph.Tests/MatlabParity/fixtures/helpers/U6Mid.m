classdef U6Mid < U6Base
    % Calls no superclass constructor, so MATLAB calls it with no arguments (u6_inherit.m).
    properties
        M = 'm'
    end
    methods
        function obj = U6Mid()
            vlog('Mid');
        end
        function delete(obj)
            vlog('delMid');
        end
    end
end
