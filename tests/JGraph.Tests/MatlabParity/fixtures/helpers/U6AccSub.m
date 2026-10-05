classdef U6AccSub < U6Acc
    properties
        Extra = 1
    end
    methods
        function obj = U6AccSub(w)
            if nargin > 0
                obj.W = w;
            end
        end
        function obj = grow(obj)
            obj.Count(end + 1) = 4;
            obj.Info.b = 2;
        end
        function r = stat(obj)
            r = obj.kind();
        end
        function h = hookHandle(obj)
            h = @hook;
        end
        function h = describeHandle(obj)
            h = @obj.runHook;
        end
    end
    methods (Access = protected)
        function r = hook(obj)
            r = ['sub hook<' hook@U6Acc(obj)];
        end
    end
    methods (Static)
        function r = kind()
            r = 'accsub';
        end
    end
end
