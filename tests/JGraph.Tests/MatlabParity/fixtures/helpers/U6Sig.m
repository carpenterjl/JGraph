classdef (Abstract) U6Sig
    % Every way an abstract method's signature is written (u6_more.m).
    methods (Abstract)
        a(obj)
        r = b(obj, x)
        [r, s] = c(obj)
        d
    end
    methods (Abstract, Access = protected)
        g(obj)
    end
    methods (Abstract, Static)
        r = f()
    end
    methods
        function r = runE(obj)
            r = g(obj);
        end
    end
end
