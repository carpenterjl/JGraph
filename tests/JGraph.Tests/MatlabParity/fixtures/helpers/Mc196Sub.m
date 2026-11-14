classdef Mc196Sub < Mc196Val
    % A value subclass: overrides one method, adds one, adds a property.
    properties
        Extra = 'e'
    end
    methods
        function r = first(obj, x, y)
            r = 0;
        end
        function own(obj)
        end
    end
end
