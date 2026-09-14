classdef ValueReader
    % A value class whose method reads its receiver, for the receiver-scope case.
    properties
        p = 1
    end
    methods
        function r = read(obj, ~)
            r = obj.p;
        end
    end
end
