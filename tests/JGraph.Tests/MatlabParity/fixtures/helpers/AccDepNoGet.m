classdef AccDepNoGet
    % A Dependent property with neither a get nor a set method (property_accessors.m).
    properties
        p = 1
    end
    properties (Dependent)
        r
    end
end
