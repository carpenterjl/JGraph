classdef (Sealed = true, Hidden, Abstract = false) U6Attr
    % Attribute forms: a value, a bare name, a false (u6_attrs.m).
    properties (Constant = true, Hidden = false)
        K = 3
    end
    properties (Dependent = false, Transient = false, Access = public)
        P = 1
    end
    methods (Static = true, Sealed = false, Hidden = false, Access = public)
        function r = s()
            r = 's';
        end
    end
    methods (Static = false)
        function r = m(obj)
            r = obj.P + U6Attr.K;
        end
    end
end
