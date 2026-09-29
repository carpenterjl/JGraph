function s = dv_err(f)
% DV_ERR  Call f for no output. Return "identifier ## message" of the error it throws, the message on
%   one line with any hyperlink reduced to its text (ix_flat); or 'none'.
try
    f();
    s = 'none';
catch e
    s = [e.identifier ' ## ' ix_flat(e.message)];
end
end
