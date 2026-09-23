classdef EventPair < handle
    % A handle class with two events and a counted property (V6.12, appendix A #106): what the
    % events_listeners fixture fires, counts and listens to.
    events
        Changed
        Other
    end
    properties
        count = 0
    end
    methods
        function fire(obj, name)
            notify(obj, name);
        end
        function bump(obj)
            obj.count = obj.count + 1;
            notify(obj, 'Changed');
        end
    end
end
