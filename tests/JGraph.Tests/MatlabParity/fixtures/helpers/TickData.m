classdef TickData < event.EventData
    % Event data carrying one number (V6.12, appendix A #106): what notify(obj, 'Changed',
    % TickData(5)) hands every listener, with EventName and Source filled in by notify.
    properties
        n
    end
    methods
        function d = TickData(n)
            d.n = n;
        end
    end
end
