classdef k11cls
    %K11CLS A class file given to a C library build.
    properties
        Value = 1
    end
    methods
        function obj = k11cls(v)
            if nargin > 0
                obj.Value = v;
            end
        end
    end
end
