classdef U6BothRev < U6MixA & U6MixB
    % Calls both superclass constructors, the second one first (u6_more.m).
    methods
        function obj = U6BothRev()
            obj@U6MixB();
            obj@U6MixA();
            vlog('BothRev');
        end
    end
end
