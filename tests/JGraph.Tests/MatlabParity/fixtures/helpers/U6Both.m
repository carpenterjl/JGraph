classdef U6Both < U6MixA & U6MixB
    % Two superclasses, one of whose constructors is called by name (u6_inherit.m).
    properties
        C = 'c'
    end
    methods
        function obj = U6Both()
            obj@U6MixB();
            vlog('Both');
        end
    end
end
