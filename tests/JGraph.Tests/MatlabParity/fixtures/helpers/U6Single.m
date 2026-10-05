classdef U6Single < handle
    % A private constructor behind a static method (u6_access.m).
    properties
        V = 1
    end
    methods (Access = private)
        function obj = U6Single()
        end
    end
    methods (Static)
        function obj = instance()
            persistent one
            if isempty(one) || ~isvalid(one)
                one = U6Single();
            end
            obj = one;
        end
    end
end
