classdef U6Cube < U6Square
    % A subclass with no constructor of its own (u6_inherit.m).
    properties
        Depth = 2
    end
    methods
        function a = area(obj)
            a = 6 * area@U6Square(obj);
        end
    end
end
