classdef U6DeepDoc < U6Doc
    % Customises the copy through copyElement (u6_attrs.m).
    methods (Access = protected)
        function cp = copyElement(obj)
            cp = copyElement@matlab.mixin.Copyable(obj);
            vlog('copyElement');
            cp.Title = [obj.Title ' (copy)'];
        end
    end
end
