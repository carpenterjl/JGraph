classdef (Abstract) U6Abs
    % An abstract class: one abstract property, two abstract methods (u6_attrs.m).
    properties (Abstract)
        Rate
    end
    properties
        Base = 10
    end
    methods (Abstract)
        r = compute(obj, x)
        show(obj)
    end
    methods
        function r = twice(obj, x)
            r = 2 * compute(obj, x);
        end
    end
end
