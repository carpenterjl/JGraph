function r = k9t_opt(a, b, c)
%K9T_OPT Two optional inputs with defaults.
arguments
    a (1,1) double {mustBeReal}
    b (1,1) double {mustBeReal} = 10
    c (1,1) string = "c"
end
r = sprintf('%g %g %s', a, b, c);
end
