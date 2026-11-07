classdef AccSeries
    %ACCSERIES The acceptance set's class folder (packages and class folders plan, stage P6): a
    %value class whose method total is declared here and defined in its own file, which calls the
    %folder's private weight.
    properties
        Values (1,:) double = []
    end
    methods
        function o = AccSeries(v)
            if nargin > 0
                o.Values = v;
            end
        end
        r = total(o)
    end
end
