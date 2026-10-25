classdef k10val
    %K10VAL A value class, for mxGetProperty and mxSetProperty on a copy.
    properties
        Value = 0
    end
    methods
        function obj = k10val(v)
            if nargin > 0
                obj.Value = v;
            end
        end
    end
end
