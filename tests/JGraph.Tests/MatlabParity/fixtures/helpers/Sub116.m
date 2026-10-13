classdef Sub116 < Val116
    properties
        q = 7
    end
    methods
        function obj = Sub116(v)
            if nargin == 0
                v = 0;
            end
            obj = obj@Val116(v);
        end
    end
end
