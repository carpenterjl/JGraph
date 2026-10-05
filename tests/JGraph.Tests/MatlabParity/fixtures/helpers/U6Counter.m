classdef U6Counter < handle
    % Function handles to methods, made inside the class (u6_handles.m).
    properties
        Count = 0
        Fn
    end
    methods
        function obj = U6Counter()
            obj.Fn = @step;
        end
        function h = bare(obj)
            h = @add;
        end
        function h = dotted(obj)
            h = @obj.add;
        end
        function h = anon(obj)
            h = @(n) add(obj, n);
        end
        function h = anonDot(obj)
            h = @(n) obj.add(n);
        end
        function h = privBare(obj)
            h = @step;
        end
        function h = privDot(obj)
            h = @obj.step;
        end
        function h = privAnon(obj)
            h = @() step(obj);
        end
        function h = stat(obj)
            h = @U6Counter.twice;
        end
        function h = wrapped(obj)
            h = u6counter_wrap(obj, @step);
        end
        function add(obj, n)
            obj.Count = obj.Count + n;
        end
        function runFn(obj)
            f = obj.Fn;
            f(obj);
        end
        function onEvent(obj, src, evt)
            obj.Count = obj.Count + 100;
        end
        function h = cb(obj)
            h = @obj.onEvent;
        end
        function h = cbAnon(obj)
            h = @(src, evt) onEvent(obj, src, evt);
        end
    end
    methods (Access = private)
        function step(obj)
            obj.Count = obj.Count + 1;
        end
    end
    methods (Static)
        function r = twice(x)
            r = 2 * x;
        end
    end
end

function h = u6counter_wrap(obj, f)
h = @(src, evt) f(obj);
end
