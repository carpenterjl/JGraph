classdef Mc196Handle < handle
    % A handle class with events and an override of a handle method.
    properties (SetObservable)
        Level = 0
    end
    properties (NonCopyable)
        Tag
    end
    events
        Changed
    end
    events (ListenAccess = protected, NotifyAccess = private)
        Quiet
    end
    methods
        function obj = Mc196Handle()
        end
        function bump(obj)
            obj.Level = obj.Level + 1;
        end
        function delete(obj)
        end
    end
end
