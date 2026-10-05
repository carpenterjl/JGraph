classdef U6Acc
    % Accessors, validators, a default from a local function, a protected hook and a private
    % helper, for a subclass to inherit (u6_more.m).
    properties
        W (1,1) double {mustBePositive} = u6acc_default()
        Tag = 'acc'
    end
    properties (Dependent)
        Twice
    end
    properties (SetAccess = protected)
        Count = [1 2 3]
        Info = struct('a', 1)
    end
    methods
        function v = get.Twice(obj)
            v = 2 * obj.W;
        end
        function obj = set.Twice(obj, v)
            obj.W = v / 2;
        end
        function obj = set.Tag(obj, v)
            vlog(['setTag:' v]);
            obj.Tag = v;
        end
        function r = localSees(obj)
            r = u6acc_default() + 1;
        end
        function r = runHook(obj)
            r = hook(obj);
        end
    end
    methods (Access = protected)
        function r = hook(obj)
            r = 'base hook';
        end
    end
end

function d = u6acc_default()
d = 7;
end
