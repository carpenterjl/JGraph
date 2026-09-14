classdef SetBox
    % A value class with a property set method. JGraph's parser refuses set.p (appendix A #27), so
    % only value_isolation_accessors.m constructs it; that fixture's run is recorded as failing.
    properties
        p
    end
    methods
        function obj = set.p(obj, v)
            obj.p = v;
        end
    end
end
