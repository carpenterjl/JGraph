classdef Trace < handle
    %TRACE A named run of samples: a handle, so scale changes the caller's object.
    %   The class is signal.Trace everywhere but its object header; its static reaches the
    %   sub-package by its full name, as members of a package must.
    properties
        Name = ''
        Data = []
    end
    properties (Dependent)
        Peak
    end
    methods
        function t = Trace(name, data)
            if nargin > 0, t.Name = name; end
            if nargin > 1, t.Data = data; end
        end
        function scale(t, k)
            t.Data = t.Data * k;
        end
        function p = get.Peak(t)
            p = max(t.Data);
        end
        function s = describe(t)
            s = sprintf('%s: %d samples, peak %g', t.Name, numel(t.Data), t.Peak);
        end
    end
    methods (Static)
        function t = fromFile(file)
            t = signal.io.readTrace(file);
        end
    end
end
