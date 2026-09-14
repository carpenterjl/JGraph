classdef HandleHolder < handle
    % A handle class holding a value, whose method writes into it.
    properties
        data
    end
    methods
        function z = bump(obj)
            obj.data(1) = 7;
            z = 0;
        end
    end
end
