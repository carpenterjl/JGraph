classdef Lv324Cls
    properties
        A = 1
        B
    end
    properties (Dependent)
        C
    end
    methods
        function v = get.C(obj)
            v = obj.A + 1;
        end
        function obj = set.B(obj, v)
            obj.B = v;
        end
    end
end
