classdef k10obj < handle
    %K10OBJ A handle class, for mxGetProperty and mxSetProperty and passing an object back in.
    properties
        Value = 0
        Label = 'object'
    end
    properties (Access = private)
        Hidden = 1
    end
    methods
        function obj = k10obj(v)
            if nargin > 0
                obj.Value = v;
            end
        end
    end
end
