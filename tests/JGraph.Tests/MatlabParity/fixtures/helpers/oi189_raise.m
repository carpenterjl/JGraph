function oi189_raise(name, fn, opt)
% Runs fn, which raises a warning, and prints what came of it (open item 189): the error's class
% and identifier if the warning became one, or that it stayed a warning, whether it was shown and
% what lastwarn says. opt 'stack' adds the top frame's name, 'nomsg' leaves out the message.
if nargin < 3
    opt = '';
end
lastwarn('', '');
try
    t = evalc('fn()');
    [m, i] = lastwarn;
    shown = ~isempty(m) && contains(t, ['Warning: ' m]);
    fprintf('CHK|%s|warned id=%s shown=%d|exact\n', name, i, shown);
catch err
    [m, i] = lastwarn;
    fprintf('CHK|%s|error %s id=%s lastwarn=%s|exact\n', name, class(err), err.identifier, i);
    if strcmp(opt, 'stack')
        fprintf('CHK|%s_top|%s|exact\n', name, err.stack(1).name);
    end
    m = err.message;
end
if ~strcmp(opt, 'nomsg')
    fprintf('CHK|%s_msg|%s|exact\n', name, strrep(regexprep(strtrim(m), '\s+', ' '), '|', '/'));
end
end
