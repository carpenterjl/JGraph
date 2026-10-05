classdef U6SigPart < U6Sig
    % Everything but the static method (u6_more.m).
    methods
        function a(obj)
        end
        function r = b(obj, x)
            r = x;
        end
        function [r, s] = c(obj)
            r = 1;
            s = 2;
        end
        function d(obj)
        end
    end
    methods (Access = protected)
        function r = g(obj)
            r = 'g';
        end
    end
end
