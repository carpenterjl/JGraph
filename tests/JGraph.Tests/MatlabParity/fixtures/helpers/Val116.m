classdef Val116
    properties
        p = 1
        name = 'x'
    end
    methods
        function obj = Val116(v)
            global VAL116_CTOR
            if isempty(VAL116_CTOR), VAL116_CTOR = 0; end
            VAL116_CTOR = VAL116_CTOR + 1;
            if nargin > 0
                obj.p = v;
            end
        end
        function obj = bump(obj)
            for k = 1:numel(obj)
                obj(k).p = obj(k).p + 1;
            end
        end
        function t = total(obj)
            t = sum([obj.p]);
        end
    end
end
