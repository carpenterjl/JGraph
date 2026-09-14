classdef DepBox
    % A value class with a Dependent property read through get.q.
    properties
        p = [1 1 1]
    end
    properties (Dependent)
        q
    end
    methods
        function v = get.q(obj)
            v = obj.p;
        end
    end
end
