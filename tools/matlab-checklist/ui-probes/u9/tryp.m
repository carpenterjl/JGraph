function tryp(label, fn)
% Runs fn and prints its result, or the error it raised, on one line.
try
    v = fn();
    fprintf('%s : %s\n', label, v2s(v));
catch e
    fprintf('%s : ERR %s | %s\n', label, e.identifier, oneline(e.message));
end
end
