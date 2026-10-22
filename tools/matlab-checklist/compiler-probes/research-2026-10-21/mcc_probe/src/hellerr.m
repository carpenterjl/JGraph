function hellerr(varargin)
%HELLERR Print to stdout and stderr, then throw an uncaught error.
fprintf('to stdout\n');
fprintf(2, 'to stderr\n');
warning('jg:probe', 'a warning from hellerr');
if nargin > 0 && strcmp(varargin{1}, 'ok')
    return
end
error('jg:boom', 'uncaught failure with arg count %d', nargin);
end
