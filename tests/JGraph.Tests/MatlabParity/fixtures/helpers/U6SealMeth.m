classdef U6SealMeth
    methods (Sealed)
        function r = locked(obj)
            r = 'locked';
        end
    end
end
