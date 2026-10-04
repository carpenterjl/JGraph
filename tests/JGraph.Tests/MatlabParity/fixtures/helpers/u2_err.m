function u2_err(name, fn)
% Runs fn for its effect: one CHK line saying it ran, or two for the error it raised (U2 fixtures).
try
    fn();
    fprintf('CHK|%s|ran|exact\n', name);
catch err
    fprintf('CHK|%s|%s|exact\n', name, err.identifier);
    fprintf('CHK|%s_msg|%s|exact\n', name, u2_text(err.message));
end
end
