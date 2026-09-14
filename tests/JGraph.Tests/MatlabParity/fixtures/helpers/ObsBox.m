classdef ObsBox < handle
    % A handle class with an observable property. JGraph refuses SetObservable (appendix A #108), so
    % only value_isolation_observable.m constructs it.
    properties (SetObservable)
        data = [1 2 3]
    end
end
