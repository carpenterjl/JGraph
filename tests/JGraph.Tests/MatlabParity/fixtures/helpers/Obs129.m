classdef Obs129 < handle
    % Open item 129: a handle class with an observable property, a plain one and an event.
    properties (SetObservable)
        p = 1
    end
    properties
        q = 2
    end
    events
        Changed
    end
    methods
        function obj = Obs129(v)
            if nargin > 0
                obj.p = v;
            end
        end
        function fire(obj)
            notify(obj, 'Changed');
        end
    end
end
