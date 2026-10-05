function r = u7_id(fn)
% The identifier of the error fn raises, or that it ran (U7 fixtures).
try
    fn();
    r = 'ran';
catch err
    r = err.identifier;
end
end
