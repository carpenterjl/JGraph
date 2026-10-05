classdef (InferiorClasses = {?U6Shape}) U6Dom
    % Dominates U6Shape whichever side of an operator it is on (u6_attrs.m).
    methods
        function r = plus(a, b)
            r = ['dom:' class(a) '+' class(b)];
        end
    end
end
