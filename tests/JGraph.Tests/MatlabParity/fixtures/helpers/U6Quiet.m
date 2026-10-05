classdef U6Quiet < handle
    events (Hidden)
        Whisper
    end
    events
        Shout
    end
    methods
        function go(obj)
            notify(obj, 'Whisper');
            notify(obj, 'Shout');
        end
    end
end
