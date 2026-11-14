classdef Mc196Val
    % A value class with property blocks of mixed attributes.
    properties
        A = 1
        B
        C (1,1) double
        D (1,:) char {mustBeNonempty} = 'x'
    end
    properties (SetAccess = private)
        Priv = 'p'
    end
    properties (Constant)
        K = 42
    end
    properties (Dependent)
        Dep
    end
    properties (Hidden, Transient)
        Hid
    end
    properties (Access = protected)
        Prot = []
    end
    properties (GetAccess = public, SetAccess = immutable, AbortSet)
        Imm = int8(3)
    end
    properties (Access = private)
        PrivX = 0
    end
    methods
        function obj = Mc196Val(a)
            if nargin > 0
                obj.A = a;
            end
        end
        function r = first(obj, x, y)
            r = obj.A + x + y;
        end
        function [p, q] = second(obj)
            p = obj.A; q = 2;
        end
        function v = get.Dep(obj)
            v = obj.A * 2;
        end
        function varargout = third(obj, varargin)
            varargout = varargin;
        end
    end
    methods (Static)
        function r = make(n)
            r = Mc196Val(n);
        end
    end
    methods (Access = private)
        function helper(obj)
        end
    end
    methods (Hidden, Sealed)
        function r = secret(obj)
            r = 1;
        end
    end
end
