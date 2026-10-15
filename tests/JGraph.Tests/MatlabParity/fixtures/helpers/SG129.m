classdef SG129 < matlab.mixin.SetGet
    % Open item 129: a SetGet class, whose arrays take set and get.
    properties
        p = 1
        name = 'x'
    end
    methods
        function obj = SG129(v)
            if nargin > 0
                obj.p = v;
            end
        end
    end
end
