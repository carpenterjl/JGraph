function hellofeval(varargin)
%HELLOFEVAL Calls a function by name only; no static reference to it.
fname = ['mu', 'lti'];
try
    [s, p, m] = feval(fname, 2, 3, 4);
    fprintf('feval ok: s=%g p=%g msg=%s\n', s, p, m);
catch e
    fprintf('feval failed: id=%s msg=%s\n', e.identifier, e.message);
    exit(4);
end
if nargin > 0
    code = str2double(varargin{1});
    fprintf('exiting with %d\n', code);
    exit(code);
end
end
