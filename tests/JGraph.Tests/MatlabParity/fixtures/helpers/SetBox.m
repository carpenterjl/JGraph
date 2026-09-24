classdef SetBox
    % A value class with a property set method (appendix A #27; value_isolation_accessors.m and
    % property_accessors.m construct it).
    properties
        p
    end
    methods
        function obj = set.p(obj, v)
            obj.p = v;
        end
    end
end
