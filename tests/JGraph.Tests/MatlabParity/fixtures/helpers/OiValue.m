classdef OiValue
    % Open items 14 and 29 (ADR 0216): a value class with hidden, private and constant properties.
    properties
        X = 1
        Y = 'two'
    end
    properties (Hidden)
        Z = 3
    end
    properties (Access = private)
        Q = 4
    end
    properties (Constant)
        K = 9
    end
end
