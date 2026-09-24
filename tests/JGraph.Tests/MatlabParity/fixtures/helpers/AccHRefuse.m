classdef AccHRefuse < handle
    % A handle class whose set method refuses a negative element (property_accessors.m).
    properties
        p = [1 2 3]
    end
    methods
        function set.p(obj, v)
            if any(v < 0)
                error('neg:bad', 'negative refused');
            end
            obj.p = v;
        end
    end
end
