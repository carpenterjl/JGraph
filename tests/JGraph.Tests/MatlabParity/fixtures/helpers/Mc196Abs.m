classdef (Abstract) Mc196Abs < handle
    % An abstract handle class.
    % Its second comment line.
    %
    % After a blank comment line.
    properties (Abstract)
        Shape % the shape's name
    end
    methods (Abstract)
        r = area(obj)
    end
    methods
        function r = twice(obj)
            % Twice the area.
            % Second line of twice.
            r = 2 * obj.area();
        end
    end
end
