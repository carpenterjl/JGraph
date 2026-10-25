function r = k9nv(x, opts)
%K9NV A positional input and two name-value arguments.
arguments
    x (1,1) double
    opts.Scale (1,1) double = 1
    opts.Label (1,1) string = "none"
end
r = sprintf('%g %g %s', x, opts.Scale, opts.Label);
end
