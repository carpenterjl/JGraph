classdef U6SigImpl < U6Sig
    methods
        function a(obj)
            vlog('a');
        end
        function r = b(obj, x)
            r = x + 1;
        end
        function [r, s] = c(obj)
            r = 1;
            s = 2;
        end
        function d(obj)
            vlog('d');
        end
    end
    methods (Access = protected)
        function r = g(obj)
            r = 'g';
        end
    end
    methods (Static)
        function r = f()
            r = 'f';
        end
    end
end
