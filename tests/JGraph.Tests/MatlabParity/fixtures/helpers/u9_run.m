function u9_run(name, fn)
% Runs fn for its effect: one CHK line saying it ran, with the identifier of any warning, or two
% for the error it raised (U9 fixtures).
lastwarn('');
try
    fn();
    [~, wid] = lastwarn;
    if isempty(wid)
        fprintf('CHK|%s|ran|exact\n', name);
    else
        fprintf('CHK|%s|ran WARN %s|exact\n', name, wid);
    end
catch err
    fprintf('CHK|%s|%s|exact\n', name, err.identifier);
    fprintf('CHK|%s_msg|%s|exact\n', name, u2_text(err.message));
end
end
