function u5_run(label, fn)
% Runs fn for its effect and prints one CHK line: that it ran, or the identifier and sentence of
% the error it raised, with the identifier of any warning after it (U5 fixtures).
lastwarn('');
try
    fn();
    r = 'ran';
catch err
    r = [err.identifier ' :: ' u2_text(err.message)];
end
[~, wid] = lastwarn;
if ~isempty(wid)
    r = [r ' WARN ' wid];
end
fprintf('CHK|%s|%s|exact\n', label, r);
end
