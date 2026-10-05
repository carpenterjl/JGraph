classdef U6Var < U6Shape
    % Hands every argument it was given to its superclass (u6_more.m).
    methods
        function obj = U6Var(varargin)
            obj = obj@U6Shape(varargin{:});
        end
    end
end
