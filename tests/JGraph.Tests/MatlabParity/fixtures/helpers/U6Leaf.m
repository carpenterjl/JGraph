classdef U6Leaf < U6Mid
    % Two levels down, with no destructor of its own (u6_inherit.m).
    properties
        L = 'l'
    end
    methods
        function obj = U6Leaf(l)
            obj@U6Mid();
            vlog('Leaf');
            if nargin > 0
                obj.L = l;
            end
        end
        function r = tag(obj)
            r = ['leaf>' tag@U6Base(obj)];
        end
        function fireFromLeaf(obj)
            notify(obj, 'Ping');
        end
    end
end
