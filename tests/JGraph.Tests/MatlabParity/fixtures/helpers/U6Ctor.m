classdef U6Ctor < handle
    % Calls handle's constructor by name (u6_more.m).
    properties
        V = 0
    end
    methods
        function obj = U6Ctor(v)
            obj@handle();
            if nargin > 0
                obj.V = v;
            end
        end
    end
end
