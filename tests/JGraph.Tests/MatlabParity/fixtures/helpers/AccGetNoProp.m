classdef AccGetNoProp
    % A get method for a property the class does not declare (property_accessors.m).
    properties
        p = 1
    end
    methods
        function v = get.zz(obj)
            v = obj.p;
        end
    end
end
