classdef Series
    %SERIES Samples with a weighted total, defined by its @Series folder: total and smooth are
    %declared here and live in files of their own, describe is a method file declared nowhere,
    %and weight in private/ is reached by the method files alone.
    properties
        Values = []
    end
    methods
        function s = Series(values)
            if nargin > 0, s.Values = values(:).'; end
        end
        r = total(s)
        r = smooth(s, width)
    end
    methods (Static)
        function s = ofTrace(t)
            s = Series(t.Data);
        end
    end
end
