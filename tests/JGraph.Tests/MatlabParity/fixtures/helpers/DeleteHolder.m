classdef DeleteHolder < handle
    % A handle class with a destructor that logs its tag and a property that may hold another
    % destructor-bearing value (V10: the destructor runs before the properties are released).
    properties
        tag = ''
        inner = []
    end
    methods
        function obj = DeleteHolder(t)
            obj.tag = t;
        end
        function delete(obj)
            vlog(obj.tag);
        end
    end
end
