classdef k9opt < handle
    %K9OPT A handle class whose method takes one optional input besides the object.
    properties
        Total (1,1) double {mustBeReal} = 0
    end
    properties (Dependent)
        Twice
    end
    properties (Hidden)
        Secret = 1
    end
    methods
        function obj = k9opt()
        end
        function bump(obj, by)
            arguments
                obj
                by (1,1) double {mustBeReal} = 1
            end
            obj.Total = obj.Total + by;
        end
        function v = get.Twice(obj)
            v = 2 * obj.Total;
        end
        function r = lt(a, b)
            r = a.Total < b.Total;
        end
    end
    methods (Access = private)
        function hidden(obj) %#ok<MANU>
        end
    end
end
