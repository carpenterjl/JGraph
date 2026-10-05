classdef U6Shape
    % The root of the U6 value hierarchy (u6_inherit.m).
    properties
        Name = 'shape'
        Sides = 0
    end
    properties (Constant)
        Kind = 'shape'
    end
    methods
        function obj = U6Shape(name, sides)
            vlog(['Shape:' class(obj) ':' num2str(nargin)]);
            if nargin > 0
                obj.Name = name;
            end
            if nargin > 1
                obj.Sides = sides;
            end
        end
        function a = area(obj)
            a = 0;
        end
        function s = describe(obj)
            s = sprintf('%s/%d/%g', obj.Name, obj.Sides, area(obj));
        end
        function s = viaDot(obj)
            s = obj.area();
        end
        function r = plus(a, b)
            r = a;
            r.Sides = a.Sides + b.Sides;
        end
        function s = whoami(obj)
            s = 'shape';
        end
    end
    methods (Static)
        function s = make()
            s = 'made by shape';
        end
    end
end
