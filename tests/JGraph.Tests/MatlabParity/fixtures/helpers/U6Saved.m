classdef U6Saved
    % A Transient property is not saved (u6_attrs.m).
    properties
        Keep = 1
    end
    properties (Transient)
        Temp = 'default'
    end
end
