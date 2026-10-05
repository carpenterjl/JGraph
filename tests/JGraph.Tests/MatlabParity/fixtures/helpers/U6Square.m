classdef U6Square < U6Shape
    % A subclass that calls its superclass constructor and two superclass methods (u6_inherit.m).
    properties
        Side = 1
    end
    methods
        function obj = U6Square(side)
            obj = obj@U6Shape('square', 4);
            vlog(['Square:' num2str(nargin)]);
            if nargin > 0
                obj.Side = side;
            end
        end
        function a = area(obj)
            a = obj.Side ^ 2;
        end
        function s = describe(obj)
            s = ['sq:' describe@U6Shape(obj)];
        end
        function s = whoami(obj)
            s = ['square<' whoami@U6Shape(obj)];
        end
    end
end
