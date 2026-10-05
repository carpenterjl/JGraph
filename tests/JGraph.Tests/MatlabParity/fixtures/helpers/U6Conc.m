classdef U6Conc < U6Abs
    properties
        Rate = 3
    end
    methods
        function r = compute(obj, x)
            r = obj.Rate * x + obj.Base;
        end
        function show(obj)
            vlog('show');
        end
    end
end
