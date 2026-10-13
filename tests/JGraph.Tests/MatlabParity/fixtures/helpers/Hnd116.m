classdef Hnd116 < handle
    properties
        p = 1
        name = 'x'
    end
    methods
        function obj = Hnd116(v)
            if nargin > 0
                obj.p = v;
            end
        end
        function bump(obj)
            for k = 1:numel(obj)
                obj(k).p = obj(k).p + 1;
            end
        end
    end
end
