classdef A3Class < handle
    properties
        P = 1
    end
    methods
        function r = m1(obj, a)
            r = obj.P;
        end
        function r = m2(obj)
            r = 2;
        end
        function r = m3(obj, a)
            disp(obj.P + a)
        end
        function set.P(obj, v)
            obj.P = v;
        end
    end
    methods (Static)
        function r = s1(a, b)
            r = a;
        end
    end
end
