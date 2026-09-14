classdef HelperBox
    % A value class fixtures share, living in fixtures\helpers\ where neither engine's enumeration
    % mistakes it for a fixture. p1_helpers.m proves a fixture reaches it on both engines.
    properties
        p = 1
    end
    methods
        function obj = HelperBox(v)
            if nargin > 0
                obj.p = v;
            end
        end
        function r = doubled(obj)
            r = 2 * obj.p;
        end
    end
end
