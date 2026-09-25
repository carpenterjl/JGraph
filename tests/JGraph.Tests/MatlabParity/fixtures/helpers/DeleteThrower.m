classdef DeleteThrower < handle
    % A handle class whose destructor errors (V10: a warning, MATLAB:class:DestructorError).
    methods
        function delete(~)
            error('lt:dtor', 'destructor failed');
        end
    end
end
