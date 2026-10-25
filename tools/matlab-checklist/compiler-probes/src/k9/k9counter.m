classdef k9counter < handle
    %K9COUNTER A handle class: a count the client changes in place.
    properties
        Count (1,1) double = 0
        Label = 'counter'
    end
    methods
        function obj = k9counter(start)
            if nargin > 0
                obj.Count = start;
            end
        end
        function increment(obj, by)
            arguments
                obj
                by (1,1) double {mustBeReal}
            end
            obj.Count = obj.Count + by;
        end
        function c = current(obj)
            c = obj.Count;
        end
    end
end
