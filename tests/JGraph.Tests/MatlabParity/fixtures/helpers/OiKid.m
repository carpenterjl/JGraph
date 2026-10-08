classdef OiKid < OiBase
    % Open item 14 (ADR 0216): inherits OiBase's methods, overrides one, adds one.
    methods
        function obj = OiKid()
            obj.V = 2;
        end
        function out = base_only(obj)
            out = obj.V + 1;
        end
        function kid_only(obj) %#ok<MANU>
        end
    end
end
