function u2_chk(name, fn)
% One CHK line for what fn answers, or two for the error it raises (U2 fixtures).
try
    v = fn();
    fprintf('CHK|%s|%s|exact\n', name, u2_text(v));
catch err
    fprintf('CHK|%s|%s|exact\n', name, err.identifier);
    fprintf('CHK|%s_msg|%s|exact\n', name, u2_text(err.message));
end
end
