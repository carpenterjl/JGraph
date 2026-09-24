classdef AccDepDefault
    % A Dependent property with a default value, which R2025b ignores (property_accessors.m).
    properties
        p = 1
    end
    properties (Dependent)
        q = 5
    end
    methods
        function v = get.q(obj)
            v = obj.p * 2;
        end
    end
end
