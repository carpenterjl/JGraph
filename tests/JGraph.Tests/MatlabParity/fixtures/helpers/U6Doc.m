classdef U6Doc < matlab.mixin.Copyable
    % copy, NonCopyable and Transient (u6_attrs.m).
    properties
        Title = 't'
        Data = [1 2 3]
    end
    properties (NonCopyable)
        Stamp = 0
    end
    properties (Transient)
        Cache = 'none'
    end
    properties (Transient, NonCopyable)
        Both = 'b'
    end
    properties
        Child
    end
end
