classdef U6VBox
    % A value class with restricted properties (u6_access.m).
    properties (SetAccess = private)
        N = 0
    end
    properties (Access = private)
        P = 1
    end
    methods
        function obj = inc(obj)
            obj.N = obj.N + 1;
            obj.P = obj.P * 2;
        end
        function r = total(obj)
            r = obj.N + obj.P;
        end
    end
end
