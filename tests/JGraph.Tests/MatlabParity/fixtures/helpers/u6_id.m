function u6_id(label, fn)
% One CHK line: the identifier of the error fn raises, or that it ran (U6 fixtures). For refusals
% whose sentence is this build's own.
try
    fn();
    r = 'ran';
catch err
    r = err.identifier;
end
fprintf('CHK|%s|%s|exact\n', label, r);
end
