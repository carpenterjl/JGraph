classdef Mc196Named < handle
    % MC196NAMED starts with its own name.
    properties (GetAccess = ?Mc196Val, SetAccess = {?Mc196Val, ?Mc196Sub})
        Listed = 1
    end
    properties
        Ranged (1,1) double {mustBeInRange(Ranged, 0, 10)} = 5
        Typed string
        Checked {mustBeNumeric}
    end
    methods
        function obj = Mc196Named()
            % Mc196Named makes one.
            % More about making one.
        end
        function go(obj)
            % GO goes somewhere.
        end
    end
    methods (Access = {?Mc196Val})
        function listed(obj)
        end
    end
end
