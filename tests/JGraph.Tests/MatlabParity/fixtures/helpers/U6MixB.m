classdef U6MixB < handle
    properties
        B = 'b'
    end
    methods
        function obj = U6MixB()
            vlog('MixB');
        end
        function r = fromB(obj)
            r = ['B:' obj.B];
        end
    end
end
