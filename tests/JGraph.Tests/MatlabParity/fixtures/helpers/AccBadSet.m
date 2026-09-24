classdef AccBadSet
    % A value class whose set method returns nothing, which R2025b refuses at the write
    % (property_accessors.m).
    properties
        p = 1
    end
    methods
        function set.p(obj, v)
            vlog('set');
            obj.p = v;
        end
    end
end
