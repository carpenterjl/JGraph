classdef DeleteLogger2 < handle
    methods
        function delete(obj)
            u7_note(sprintf('delete valid=%d', isvalid(obj)));
        end
    end
end
