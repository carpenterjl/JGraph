classdef AccHBadSet < handle
    % A handle class whose set method returns the object, which R2025b allows and ignores
    % (property_accessors.m).
    properties
        p = 1
    end
    methods
        function obj = set.p(obj, v)
            vlog('set');
            obj.p = v;
        end
    end
end
