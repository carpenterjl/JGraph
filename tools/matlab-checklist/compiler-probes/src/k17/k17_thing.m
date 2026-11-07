classdef k17_thing
    %K17_THING A value object with one property, for what an object becomes in a Variant (stage K17).
    properties
        Value
    end
    methods
        function obj = k17_thing(v)
            obj.Value = v;
        end
    end
end
