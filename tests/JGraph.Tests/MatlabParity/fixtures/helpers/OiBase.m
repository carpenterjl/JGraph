classdef OiBase
    % Open item 14 (ADR 0216): a value class whose methods have several outputs, varargin and ~.
    properties
        V = 1
    end
    methods
        function [a, b] = two_out(obj, x, varargin) %#ok<INUSD>
            a = obj.V; b = x;
        end
        function skip_arg(~, y) %#ok<INUSD>
        end
        function out = base_only(obj)
            out = obj.V;
        end
    end
    methods (Static)
        function r = make(n, m) %#ok<INUSD>
            r = n;
        end
    end
end
