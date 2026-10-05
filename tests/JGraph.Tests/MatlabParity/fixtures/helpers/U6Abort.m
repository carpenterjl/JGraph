classdef U6Abort < handle
    % AbortSet: a write of the value a property already holds does nothing (u6_attrs.m).
    properties (AbortSet, SetObservable)
        P = 1
    end
    properties (SetObservable)
        Q = 1
    end
    methods
        function set.P(obj, v)
            vlog('setP');
            obj.P = v;
        end
    end
end
