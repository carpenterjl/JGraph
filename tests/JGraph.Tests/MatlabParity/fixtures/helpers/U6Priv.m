classdef U6Priv
    % A private method a subclass also defines (u6_more.m).
    methods
        function r = run(obj)
            r = helper(obj);
        end
        function r = runDot(obj)
            r = obj.helper();
        end
    end
    methods (Access = private)
        function r = helper(obj)
            r = 'base';
        end
    end
end
