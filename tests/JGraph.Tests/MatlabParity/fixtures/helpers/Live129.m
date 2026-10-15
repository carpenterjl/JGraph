classdef Live129 < handle
    % Open item 129: a handle with a destructor and nothing else. While one lives, a temporary
    % nobody binds is checked at its statement's end, which once ended a column of listeners.
    methods
        function delete(~)
        end
    end
end
