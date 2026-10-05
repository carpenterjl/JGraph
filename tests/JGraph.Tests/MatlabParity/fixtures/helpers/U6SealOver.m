classdef U6SealOver < U6SealMeth
    % Overrides a Sealed method (u6_inherit.m).
    methods
        function r = locked(obj)
            r = 'picked';
        end
    end
end
