classdef k9point
    %K9POINT A value class: two coordinates, a method, a static method.
    properties
        X (1,1) double = 0
        Y (1,1) double = 0
    end
    properties (SetAccess = private)
        Tag (1,1) string = "pt"
    end
    methods
        function obj = k9point(x, y)
            if nargin > 0
                obj.X = x;
                obj.Y = y;
            end
        end
        function n = norm(obj)
            n = hypot(obj.X, obj.Y);
        end
        function p = scaled(obj, k)
            arguments
                obj (1,1) k9point
                k (1,1) double {mustBeReal}
            end
            p = k9point(obj.X * k, obj.Y * k);
        end
        function [a, b] = parts(obj)
            a = obj.X;
            b = obj.Y;
        end
    end
    methods (Static)
        function p = origin()
            p = k9point(0, 0);
        end
    end
end
