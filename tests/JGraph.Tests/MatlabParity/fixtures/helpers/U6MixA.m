classdef U6MixA < handle
    properties
        A = 'a'
    end
    methods
        function obj = U6MixA()
            vlog('MixA');
        end
        function r = fromA(obj)
            r = ['A:' obj.A];
        end
    end
end
