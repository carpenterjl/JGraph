classdef OiPlainHandle < handle
    % Open items 27 and 29 (ADR 0216): a handle class with a hidden and a private property.
    properties
        A = 1
    end
    properties (Hidden)
        H = 'hid'
    end
    properties (Access = private)
        P = 3
    end
end
