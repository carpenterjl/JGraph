classdef OiGetters
    % Open item 29 (ADR 0216): one Dependent property that answers and one whose getter refuses.
    properties
        R = 2
    end
    properties (Dependent)
        D
        Bad
    end
    methods
        function v = get.D(obj)
            v = obj.R * 10;
        end
        function v = get.Bad(obj) %#ok<STOUT,MANU>
            error('OiGetters:bad', 'the getter refuses');
        end
    end
end
