classdef AccSetReads
    % set.p reads obj.p, which calls get.p: only the write is the storage's inside a setter
    % (property_accessors.m).
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
            old = obj.p;
            vlog(sprintf('old%d', numel(old)));
            obj.p = v;
        end
    end
end
