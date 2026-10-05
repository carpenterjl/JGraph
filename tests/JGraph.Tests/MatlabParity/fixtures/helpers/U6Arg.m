classdef U6Arg < U6Base
    % Hands its superclass constructor an argument it computed (u6_inherit.m).
    methods
        function obj = U6Arg(id)
            obj@U6Base(id * 2);
            vlog('Arg');
        end
    end
end
