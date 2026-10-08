classdef OiCopyKid < OiPlainHandle & matlab.mixin.Copyable
    % Open items 14, 27 and 29 (ADR 0216): two superclasses, one of them a built-in mixin.
    properties
        B = 2
    end
end
