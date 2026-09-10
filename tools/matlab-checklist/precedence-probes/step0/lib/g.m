function r = g(x, varargin)
r = struct();
try, r.helper = evalin('caller', 'helper()'); catch e, r.helper = ['ERR: ' e.message]; end
try, r.v = evalin('caller', 'v'); catch e, r.v = ['ERR: ' e.message]; end
try, assignin('caller', 'seen', x); r.assign = 'ok'; catch e, r.assign = ['ERR: ' e.message]; end
st = dbstack('-completenames');
r.stack = strjoin(arrayfun(@(s) sprintf('%s@%s', s.name, s.file), st, 'UniformOutput', false), ' <- ');
end
