classdef ObsPair < handle
    % A handle class with three observable properties and one plain one (V6.12, appendix A #108):
    % what the events_listeners fixture's PreSet and PostSet lines write to.
    properties (SetObservable)
        a = [1 2 3]
        b = 0
        s = struct('v', [1 2 3])
    end
    properties
        plain = 0
    end
    methods
        function setA(obj, v)
            obj.a = v;
        end
    end
end
