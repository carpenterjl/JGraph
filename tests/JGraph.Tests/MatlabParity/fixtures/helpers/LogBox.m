classdef LogBox
    % A value class whose property accessors log every call, to record the order of getter, setter,
    % subscripts, end and right-hand side in a composite write. JGraph's parser refuses get.p/set.p
    % (appendix A #147), so only value_isolation_accessors.m constructs it.
    properties
        p = [1 2 3]
    end
    methods
        function v = get.p(obj)
            vlog('get');
            v = obj.p;
        end
        function obj = set.p(obj, v)
            vlog('set');
            obj.p = v;
        end
    end
end
