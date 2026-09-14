classdef LogHBox < handle
    % The handle form of LogBox; see LogBox.m.
    properties
        p = [1 2 3]
    end
    methods
        function v = get.p(obj)
            vlog('get');
            v = obj.p;
        end
        function set.p(obj, v)
            vlog('set');
            obj.p = v;
        end
    end
end
