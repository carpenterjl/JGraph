classdef OiShape < handle
    % Open item 14 (ADR 0216): a handle class with a constructor, instance, static and private methods.
    properties
        R = 1
    end
    methods
        function obj = OiShape(r)
            if nargin > 0
                obj.R = r;
            end
        end
        function a = area(obj)
            a = pi * obj.R^2;
        end
        function grow(obj, k)
            obj.R = obj.R * k;
        end
        function longer_method_name(obj) %#ok<MANU>
        end
    end
    methods (Static)
        function c = unit()
            c = OiShape(1);
        end
    end
    methods (Access = private)
        function hidden_one(obj) %#ok<MANU>
        end
    end
end
